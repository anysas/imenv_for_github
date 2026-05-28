using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Reductively parses a user's typed phrase into a PromptDefinition.
    /// The reductiveness IS the misreading: the player wrote a sentence,
    /// the system "understood" three tags. This is what the work critiques.
    ///
    /// Always returns SOMETHING — falls back to mundane/personal/domestic
    /// for unrecognized text rather than rejecting input.
    /// </summary>
    public static class PromptParser
    {
        private static VocabularyDictionary _vocab;
        private static VocabularyDictionary Vocab => _vocab ??= VocabularyDictionary.Instance;
        private const float DICT_DAMPING = 0.65f;
        public class ParseResult
        {
            public PromptDefinition Prompt;
            public List<string> MatchedEmotionalTags;   // ordered by score
            public string OriginalText;
            public float Confidence;                    // 0..1 parser confidence
            public float Intensity;                     // 0..1 emotional intensity estimate
            public float EmotionalPolarity;             // -1..1 (negative to positive)
            public int MatchCount;                      // regex match hit count
            public int WordCount;
            public List<string> IntentObjects;
            public List<string> IntentSetting;
            public List<string> IntentActions;
            public List<string> IntentStyle;
            public List<string> NormalizedTokens;
            public List<string> CollapsedTerms;
            public List<string> DroppedTerms;
            public List<string> MissingConcepts;
            public List<string> NormalizationNotes;
            public float CollapseSeverity;              // 0..1 how aggressively text was flattened
        }

        // --- Keyword table: regex → emotional tags it triggers ----------------
        // Each row biases the parser toward a slice of the emotional vocabulary.
        // Multiple matches stack; final tag set is sorted by hit count, top 3 win.
        private static readonly (string pattern, string[] emotional)[] KEYWORDS = new (string, string[])[]
        {
            // Personal / intimate
            (@"\b(my|me|mine|myself|i)\b",                                  new[]{"personal"}),
            (@"\b(alone|private|secret|hidden|quiet|silent|silence)\b",     new[]{"intimate","personal"}),
            (@"\b(love|loved|loving|beloved)\b",                            new[]{"comforting","intimate"}),
            (@"\b(safe|safety|sanctuary|haven|shelter)\b",                  new[]{"comforting","intimate"}),

            // Family / childhood / domestic
            (@"\b(mother|mom|mama|mum|father|dad|papa|parent|parents)\b",   new[]{"personal","nostalgic","intimate"}),
            (@"\b(grandmother|grandma|granny|grandfather|grandpa|gran)\b",  new[]{"personal","nostalgic","intimate"}),
            (@"\b(family|sibling|brother|sister|son|daughter|aunt|uncle)\b",new[]{"personal","domestic","nostalgic"}),
            (@"\b(child|childhood|kid|baby|infant|toddler|teen)\b",         new[]{"personal","nostalgic","intimate"}),
            (@"\b(home|house|kitchen|bedroom|nursery|attic|basement)\b",    new[]{"domestic","intimate"}),
            (@"\b(living\s*room|dining|hearth|fireplace)\b",                new[]{"comforting","domestic","intimate"}),
            (@"\b(bed|crib|pillow|blanket|quilt)\b",                        new[]{"intimate","comforting"}),

            // Memory / nostalgia
            (@"\b(remember|remembering|memory|memories|recollection)\b",    new[]{"nostalgic","personal"}),
            (@"\b(used\s*to|once|long\s*ago|back\s*when|years\s*ago)\b",    new[]{"nostalgic","melancholy"}),
            (@"\b(past|before|old|ancient|vintage|antique)\b",              new[]{"nostalgic"}),
            (@"\b(photograph|photo|picture|portrait|album|letter)\b",       new[]{"nostalgic","personal"}),

            // Loss / melancholy / abandonment
            (@"\b(gone|missing|absent|left|leaving|abandoned|forgotten)\b", new[]{"abandoned","melancholy"}),
            (@"\b(empty|hollow|barren|deserted|vacant|silent)\b",           new[]{"abandoned","melancholy","liminal"}),
            (@"\b(death|died|dying|grave|funeral|coffin|mourning)\b",       new[]{"melancholy","sacred"}),
            (@"\b(grief|sorrow|loss|losing|broken|ended|over)\b",           new[]{"melancholy","abandoned"}),
            (@"\b(no\s*longer|never\s*again|gone\s*forever)\b",             new[]{"melancholy","decayed","nostalgic"}),

            // Fear / threatening
            (@"\b(afraid|fear|scared|scary|terrified|terror|horror)\b",     new[]{"threatening"}),
            (@"\b(nightmare|dread|panic|anxious|anxiety)\b",                new[]{"threatening","melancholy"}),
            (@"\b(danger|dangerous|trapped|watch|watched|watching|stalker|hunted|surveilled)\b", new[]{"threatening"}),
            (@"\b(violence|violent|hurt|pain|wound|blood)\b",               new[]{"threatening","clinical"}),

            // Clinical / medical / institutional
            (@"\b(hospital|doctor|nurse|clinic|ward|medical|medicine)\b",   new[]{"clinical","institutional"}),
            (@"\b(sick|illness|disease|surgery|operation|exam)\b",          new[]{"clinical","threatening"}),
            (@"\b(needle|syringe|scalpel|gurney|stretcher)\b",              new[]{"clinical","threatening"}),
            (@"\b(machine|robot|algorithm|ai|artificial|computer|system)\b",new[]{"institutional","mundane"}),

            // Sacred / ritual
            (@"\b(church|chapel|cathedral|temple|altar|shrine|pew)\b",      new[]{"sacred"}),
            (@"\b(prayer|prayed|pray|worship|holy|sacred|divine|god)\b",    new[]{"sacred","intimate"}),
            (@"\b(soul|faith|spirit|spiritual|ritual|ceremony)\b",          new[]{"sacred","intimate"}),
            (@"\b(candle|incense|votive)\b",                                new[]{"sacred","intimate","comforting"}),

            // Bureaucratic
            (@"\b(office|workplace|cubicle|desk|meeting|boss|coworker)\b",  new[]{"bureaucratic","mundane","institutional"}),
            (@"\b(form|forms|paperwork|document|file|files|folder)\b",      new[]{"bureaucratic","institutional"}),
            (@"\b(rule|rules|law|laws|policy|regulation|government)\b",     new[]{"institutional","bureaucratic"}),
            (@"\b(application|approval|denial|rejected|processed)\b",       new[]{"bureaucratic","institutional"}),

            // Liminal / threshold
            (@"\b(door|doors|doorway|doorframe|threshold|gateway)\b",       new[]{"liminal"}),
            (@"\b(hallway|hall|corridor|stairs|stairway|passage)\b",        new[]{"liminal"}),
            (@"\b(between|leaving|departure|goodbye|farewell)\b",           new[]{"liminal","melancholy"}),
            (@"\b(waiting|wait|paused|pause|in-between|liminal)\b",         new[]{"liminal","mundane"}),
            (@"\b(window|curtain|blind|blinds)\b",                          new[]{"liminal","intimate"}),

            // Decayed / abandoned
            (@"\b(rust|rusted|rusty|rotting|rotten|decayed|decay)\b",       new[]{"decayed","abandoned"}),
            (@"\b(ruin|ruins|crumbling|collapsed|collapsing|crumble)\b",    new[]{"decayed","abandoned","melancholy"}),
            (@"\b(burnt|burned|charred|wreckage|wrecked|destroyed)\b",      new[]{"decayed","abandoned"}),
            (@"\b(mold|moldy|moss|mossy|weathered|worn)\b",                 new[]{"decayed","abandoned","nostalgic"}),

            // Public / institutional
            (@"\b(city|street|sidewalk|station|stop|terminal)\b",           new[]{"public","mundane","liminal"}),
            (@"\b(store|shop|mall|market|register|cashier)\b",              new[]{"public","mundane","retail"}),
            (@"\b(public|crowded|crowd|stranger|strangers)\b",              new[]{"public","mundane"}),

            // Comforting / warmth
            (@"\b(warm|warmth|cozy|comfort|comforting|comfortable)\b",      new[]{"comforting","intimate"}),
            (@"\b(fire|firelight|flame|glow|glowing|hearth)\b",             new[]{"comforting","nostalgic","intimate"}),
            (@"\b(soft|gentle|tender|kind|kindness)\b",                     new[]{"comforting","intimate"}),
            (@"\b(reading|book|books|library|story)\b",                     new[]{"comforting","intimate","nostalgic"}),

            // Workshop / making / hands
            (@"\b(workshop|garage|tools|craft|making|building|wood)\b",     new[]{"mundane","personal"}),
            (@"\b(hands|fingers|making|crafting|repair)\b",                 new[]{"personal","mundane"}),
        };

        private static readonly (Regex re, string[] tags)[] COMPILED =
            KEYWORDS.Select(k => (new Regex(k.pattern, RegexOptions.IgnoreCase), k.emotional)).ToArray();

        // Tag → group bias: which manifest groups should anchor a prompt
        // when the dominant emotional tag is X.
        private static readonly Dictionary<string, string[]> TAG_TO_PRIMARY_GROUPS = new()
        {
            ["intimate"]      = new[]{"domestic","item","furniture"},
            ["nostalgic"]     = new[]{"domestic","item","furniture"},
            ["personal"]      = new[]{"item","domestic","furniture"},
            ["comforting"]    = new[]{"domestic","furniture","item"},
            ["domestic"]      = new[]{"domestic","furniture"},
            ["clinical"]      = new[]{"lab","tech","item"},
            ["institutional"] = new[]{"office","lab","tech"},
            ["bureaucratic"]  = new[]{"office","item","furniture"},
            ["threatening"]   = new[]{"lab","workshop","item"},
            ["melancholy"]    = new[]{"item","domestic","furniture"},
            ["abandoned"]     = new[]{"workshop","item","domestic"},
            ["decayed"]       = new[]{"workshop","item","domestic"},
            ["sacred"]        = new[]{"item","furniture","domestic"},
            ["liminal"]       = new[]{"item","furniture","domestic"},
            ["public"]        = new[]{"retail","item","office"},
            ["mundane"]       = new[]{"domestic","item","office"},
        };

        // Tag → drift register: what does the algorithm push the player TOWARD
        // when their input is X-coded? Roughly: opposite emotional valence.
        private static readonly Dictionary<string, string[]> TAG_TO_DRIFT_TAGS = new()
        {
            ["intimate"]      = new[]{"institutional","clinical","bureaucratic"},
            ["nostalgic"]     = new[]{"institutional","clinical","public"},
            ["personal"]      = new[]{"public","institutional","bureaucratic"},
            ["comforting"]    = new[]{"clinical","threatening","institutional"},
            ["domestic"]      = new[]{"institutional","clinical","bureaucratic"},
            ["clinical"]      = new[]{"comforting","intimate","nostalgic"},
            ["institutional"] = new[]{"intimate","comforting","personal"},
            ["bureaucratic"]  = new[]{"intimate","personal","comforting"},
            ["threatening"]   = new[]{"comforting","intimate","nostalgic"},
            ["melancholy"]    = new[]{"institutional","mundane","public"},
            ["abandoned"]     = new[]{"institutional","public","mundane"},
            ["decayed"]       = new[]{"institutional","clinical","bureaucratic"},
            ["sacred"]        = new[]{"bureaucratic","institutional","mundane"},
            ["liminal"]       = new[]{"comforting","intimate","domestic"},
            ["public"]        = new[]{"intimate","personal","sacred"},
            ["mundane"]       = new[]{"threatening","clinical","melancholy"},
        };

        private static readonly Dictionary<string, string[]> TAG_TO_DRIFT_GROUPS = new()
        {
            ["intimate"]      = new[]{"office","lab","retail"},
            ["nostalgic"]     = new[]{"office","lab"},
            ["personal"]      = new[]{"office","retail","lab"},
            ["comforting"]    = new[]{"lab","tech","workshop"},
            ["domestic"]      = new[]{"lab","tech","office"},
            ["clinical"]      = new[]{"domestic","furniture","item"},
            ["institutional"] = new[]{"domestic","furniture"},
            ["bureaucratic"]  = new[]{"domestic","furniture"},
            ["threatening"]   = new[]{"domestic","furniture","item"},
            ["melancholy"]    = new[]{"office","retail","lab"},
            ["abandoned"]     = new[]{"office","retail","lab"},
            ["decayed"]       = new[]{"office","lab"},
            ["sacred"]        = new[]{"office","retail"},
            ["liminal"]       = new[]{"domestic","furniture"},
            ["public"]        = new[]{"domestic","item"},
            ["mundane"]       = new[]{"lab","workshop"},
        };

        private static readonly Regex INTENSIFIERS =
            new(@"\b(very|really|extremely|deeply|incredibly|so|absolutely|totally)\b", RegexOptions.IgnoreCase);
        private static readonly Regex HEDGES =
            new(@"\b(maybe|kind of|sort of|somewhat|a bit|not sure|i guess|perhaps)\b", RegexOptions.IgnoreCase);
        private static readonly Regex TOKENIZER =
            new(@"[a-zA-Z']+", RegexOptions.Compiled);

        private static readonly HashSet<string> POSITIVE_TAGS = new()
        {
            "comforting", "intimate", "nostalgic", "sacred", "domestic", "personal"
        };
        private static readonly HashSet<string> NEGATIVE_TAGS = new()
        {
            "threatening", "clinical", "institutional", "bureaucratic", "abandoned", "decayed", "melancholy"
        };

        private static readonly HashSet<string> STOPWORDS = new()
        {
            "a","an","and","the","to","of","for","in","on","at","by","from","with","without","into","out","my","me","i","we","our","your",
            "this","that","it","is","are","was","were","be","been","being","do","did","does","have","has","had","as","or","if","but"
        };

        private static readonly Dictionary<string, string[]> OBJECT_SYNONYMS = new()
        {
            ["chair"] = new[] {"chair","stool","seat","bench","armchair"},
            ["table"] = new[] {"table","desk","counter","workbench"},
            ["bed"] = new[] {"bed","mattress","cot","crib"},
            ["lamp"] = new[] {"lamp","light","lantern","torch"},
            ["book"] = new[] {"book","books","novel","journal","notebook"},
            ["computer"] = new[] {"computer","pc","terminal","monitor","keyboard"},
            ["tool"] = new[] {"tool","hammer","saw","wrench","drill"},
            ["medical"] = new[] {"syringe","needle","scalpel","gurney","stretcher","hospital"},
            ["kitchenware"] = new[] {"plate","cup","mug","pan","pot","fork","knife"},
            ["storage"] = new[] {"shelf","cabinet","drawer","box","crate","locker"}
        };

        private static readonly Dictionary<string, string[]> SETTING_SYNONYMS = new()
        {
            ["home"] = new[] {"home","house","living","bedroom","kitchen","domestic"},
            ["office"] = new[] {"office","workplace","cubicle","meeting"},
            ["workshop"] = new[] {"workshop","garage","factory","industrial"},
            ["lab"] = new[] {"lab","clinic","hospital","medical","ward"},
            ["retail"] = new[] {"store","shop","mall","market","retail"},
            ["public"] = new[] {"street","station","city","terminal","public"},
            ["sacred"] = new[] {"church","chapel","temple","altar","shrine"},
            ["liminal"] = new[] {"hallway","corridor","threshold","doorway","passage"}
        };

        private static readonly string[] ACTION_VERBS =
        {
            "build","create","remember","hide","wait","repair","work","pray","escape","sleep","study","cook","heal","protect","search","mourn"
        };

        private static readonly string[] STYLE_WORDS =
        {
            "cozy","cold","warm","clean","messy","clinical","nostalgic","sacred","abandoned","decayed","bright","dark","quiet","crowded","safe","threatening"
        };

        /// <summary>
        /// Parse the user's typed text into a synthesized PromptDefinition.
        /// Returns ParseResult with the prompt + which tags were matched (for UI display).
        /// </summary>
        public static ParseResult Parse(string userText)
        {
            string text = userText ?? "";
            int wordCount = TOKENIZER.Matches(text).Count;
            List<string> originalTokens = Tokenize(text);
            List<string> normalizedTokens = NormalizeTokens(originalTokens);
            List<string> droppedTokens = originalTokens
                .Where(t => !normalizedTokens.Contains(NormalizeToken(t)))
                .Distinct()
                .ToList();

            List<string> intentObjects = ExtractBySynonyms(normalizedTokens, OBJECT_SYNONYMS);
            List<string> intentSetting = ExtractBySynonyms(normalizedTokens, SETTING_SYNONYMS);
            List<string> intentActions = ExtractFromWordList(normalizedTokens, ACTION_VERBS);
            List<string> intentStyle = ExtractFromWordList(normalizedTokens, STYLE_WORDS);

            // --- Pass 0: Phrase pre-scan (multi-word expressions) ---
            var phraseMatches = Vocab.MatchPhrases(text.ToLowerInvariant());
            var phraseTagScores = new Dictionary<string, float>();
            foreach (var (phrase, entry) in phraseMatches)
            {
                foreach (var tag in entry.Emotional)
                    phraseTagScores[tag] = phraseTagScores.GetValueOrDefault(tag) + entry.Weight * DICT_DAMPING;
                if (entry.Object != null && !intentObjects.Contains(entry.Object))
                    intentObjects.Add(entry.Object);
                if (entry.Setting != null && !intentSetting.Contains(entry.Setting))
                    intentSetting.Add(entry.Setting);
                if (entry.Action != null && !intentActions.Contains(entry.Action))
                    intentActions.Add(entry.Action);
                if (entry.Style != null && !intentStyle.Contains(entry.Style))
                    intentStyle.Add(entry.Style);
            }

            // --- Pass 1: Regex keyword scoring (the crude reading — this IS the art) ---
            var tagScores = new Dictionary<string, float>();
            int totalMatches = 0;
            foreach (var (re, tags) in COMPILED)
            {
                int matches = re.Matches(text).Count;
                if (matches == 0) continue;
                totalMatches += matches;
                foreach (var tag in tags)
                {
                    if (!tagScores.ContainsKey(tag)) tagScores[tag] = 0;
                    tagScores[tag] += matches;
                }
            }

            // --- Pass 2: Dictionary supplement (fills gaps in regex coverage) ---
            foreach (var token in normalizedTokens)
            {
                if (Vocab.TryGetWord(token, out var entry))
                {
                    foreach (var tag in entry.Emotional)
                        tagScores[tag] = tagScores.GetValueOrDefault(tag) + entry.Weight * DICT_DAMPING;
                    totalMatches++;
                    if (entry.Object != null && !intentObjects.Contains(entry.Object))
                        intentObjects.Add(entry.Object);
                    if (entry.Setting != null && !intentSetting.Contains(entry.Setting))
                        intentSetting.Add(entry.Setting);
                    if (entry.Action != null && !intentActions.Contains(entry.Action))
                        intentActions.Add(entry.Action);
                    if (entry.Style != null && !intentStyle.Contains(entry.Style))
                        intentStyle.Add(entry.Style);
                }
            }

            // Merge phrase scores into main tag scores
            foreach (var kv in phraseTagScores)
                tagScores[kv.Key] = tagScores.GetValueOrDefault(kv.Key) + kv.Value;

            // Top 4 tags by score; tie-break alphabetical for determinism.
            var topTags = tagScores
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Take(4)
                .Select(kv => kv.Key)
                .ToList();

            // Fallback: nothing matched → "mundane" + "personal" (the most reductive read possible)
            if (topTags.Count == 0)
                topTags = new List<string> { "mundane", "personal", "liminal" };

            float intensity = ComputeIntensity(text, totalMatches, wordCount);
            float confidence = ComputeConfidence(topTags.Count, totalMatches, wordCount, intensity);
            float polarity = ComputePolarity(topTags, tagScores);
            List<string> collapsedTerms = BuildCollapsedTerms(
                topTags,
                intentSetting,
                intentObjects,
                intentStyle
            );
            List<string> missingConcepts = ComputeMissingConcepts(
                intentObjects,
                intentSetting,
                intentActions,
                normalizedTokens
            );
            List<string> normalizationNotes = BuildNormalizationNotes(
                intentObjects,
                intentSetting,
                intentActions,
                intentStyle,
                droppedTokens,
                topTags
            );
            float collapseSeverity = ComputeCollapseSeverity(
                originalTokens,
                normalizedTokens,
                collapsedTerms
            );

            string[] primaryGroups = BuildWeightedGroups(
                topTags,
                tagScores,
                TAG_TO_PRIMARY_GROUPS,
                fallback: new[] { "domestic", "item", "furniture" },
                maxCount: intensity >= 0.75f ? 4 : 3
            );

            primaryGroups = MergeSettingGroups(intentSetting, primaryGroups, maxCount: 4);

            string[] driftGroups = BuildWeightedGroups(
                topTags,
                tagScores,
                TAG_TO_DRIFT_GROUPS,
                fallback: new[] { "office", "lab", "retail" },
                maxCount: intensity >= 0.75f ? 4 : 3
            );

            string[] driftEmotionalTags = BuildWeightedDriftTags(
                topTags,
                tagScores,
                polarity,
                maxCount: 4
            );

            var prompt = new PromptDefinition
            {
                DisplayText = userText, // The end card shows the user's literal phrase
                PrimaryGroups = primaryGroups,
                DriftGroups = driftGroups,
                EmotionalTags = topTags.ToArray(),
                DriftEmotionalTags = driftEmotionalTags,
                IsAbstract = true, // Custom input is always treated as abstract
                Source = "custom",
                ParseConfidence = confidence,
                ParseIntensity = intensity,
                EmotionalPolarity = polarity,
                MatchCount = totalMatches,
                IntentObjects = intentObjects.ToArray(),
                IntentSetting = intentSetting.ToArray(),
                IntentActions = intentActions.ToArray(),
                IntentStyle = intentStyle.ToArray(),
                MissingConcepts = missingConcepts.ToArray(),
                OriginalPhraseTokens = originalTokens.ToArray(),
                NormalizedTokens = normalizedTokens.ToArray(),
                CollapsedTerms = collapsedTerms.ToArray(),
                DroppedTerms = droppedTokens.ToArray(),
                NormalizationNotes = normalizationNotes.ToArray(),
                CollapseSeverity = collapseSeverity,
            };

            return new ParseResult
            {
                Prompt = prompt,
                MatchedEmotionalTags = topTags,
                OriginalText = userText,
                Confidence = confidence,
                Intensity = intensity,
                EmotionalPolarity = polarity,
                MatchCount = totalMatches,
                WordCount = wordCount,
                IntentObjects = intentObjects,
                IntentSetting = intentSetting,
                IntentActions = intentActions,
                IntentStyle = intentStyle,
                NormalizedTokens = normalizedTokens,
                CollapsedTerms = collapsedTerms,
                DroppedTerms = droppedTokens,
                MissingConcepts = missingConcepts,
                NormalizationNotes = normalizationNotes,
                CollapseSeverity = collapseSeverity,
            };
        }

        private static List<string> Tokenize(string text)
        {
            return TOKENIZER.Matches(text ?? string.Empty)
                .Select(m => m.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
        }

        private static string NormalizeToken(string token)
        {
            return (token ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static List<string> NormalizeTokens(List<string> tokens)
        {
            var normalized = tokens
                .Select(NormalizeToken)
                .Where(t => t.Length > 1 && !STOPWORDS.Contains(t))
                .Distinct()
                .ToList();
            return normalized;
        }

        private static List<string> ExtractBySynonyms(
            List<string> normalizedTokens,
            Dictionary<string, string[]> lexicon)
        {
            var outList = new List<string>();
            foreach (var kv in lexicon)
            {
                if (kv.Value.Any(s => normalizedTokens.Contains(s)))
                    outList.Add(kv.Key);
            }
            return outList;
        }

        private static List<string> ExtractFromWordList(List<string> normalizedTokens, IEnumerable<string> words)
        {
            return words.Where(normalizedTokens.Contains).Distinct().ToList();
        }

        private static List<string> BuildCollapsedTerms(
            List<string> topTags,
            List<string> setting,
            List<string> objects,
            List<string> style)
        {
            var output = new List<string>();
            output.AddRange(topTags.Take(3));
            output.AddRange(setting.Take(2));
            output.AddRange(objects.Take(2));
            output.AddRange(style.Take(2));
            return output.Distinct().Take(8).ToList();
        }

        private static List<string> ComputeMissingConcepts(
            List<string> objects,
            List<string> setting,
            List<string> actions,
            List<string> normalizedTokens)
        {
            var missing = new List<string>();
            if (objects.Count == 0) missing.Add("objects_unspecified");
            if (setting.Count == 0) missing.Add("setting_unspecified");
            if (actions.Count == 0) missing.Add("action_unspecified");
            if (normalizedTokens.Count > 0 && normalizedTokens.Count < 3) missing.Add("low_prompt_detail");
            return missing;
        }

        private static List<string> BuildNormalizationNotes(
            List<string> objects,
            List<string> setting,
            List<string> actions,
            List<string> style,
            List<string> droppedTokens,
            List<string> emotionalTags)
        {
            var notes = new List<string>();
            notes.Add($"mapped_objects:{objects.Count}");
            notes.Add($"mapped_setting:{setting.Count}");
            notes.Add($"mapped_actions:{actions.Count}");
            notes.Add($"mapped_style:{style.Count}");
            notes.Add($"dropped_tokens:{droppedTokens.Count}");
            notes.Add($"emotional_register:{string.Join("|", emotionalTags.Take(3))}");
            return notes;
        }

        private static float ComputeCollapseSeverity(
            List<string> originalTokens,
            List<string> normalizedTokens,
            List<string> collapsedTerms)
        {
            if (originalTokens.Count == 0) return 0.8f;
            float keptRatio = normalizedTokens.Count / Mathf.Max(1f, originalTokens.Count);
            float outputRatio = collapsedTerms.Count / Mathf.Max(1f, normalizedTokens.Count);
            float severity = 1f - Mathf.Clamp01(keptRatio * 0.75f + outputRatio * 0.25f);
            return Mathf.Clamp01(severity);
        }

        private static string[] MergeSettingGroups(List<string> settings, string[] baseGroups, int maxCount)
        {
            var merged = new List<string>();
            foreach (var s in settings ?? new List<string>())
            {
                switch (s)
                {
                    case "home": merged.AddRange(new[] { "domestic", "furniture", "item" }); break;
                    case "office": merged.AddRange(new[] { "office", "furniture", "tech" }); break;
                    case "workshop": merged.AddRange(new[] { "workshop", "item", "tech" }); break;
                    case "lab": merged.AddRange(new[] { "lab", "tech", "item" }); break;
                    case "retail": merged.AddRange(new[] { "retail", "item", "office" }); break;
                    case "public": merged.AddRange(new[] { "retail", "office", "item" }); break;
                    case "sacred": merged.AddRange(new[] { "domestic", "item", "furniture" }); break;
                    case "liminal": merged.AddRange(new[] { "furniture", "item", "office" }); break;
                }
            }

            merged.AddRange(baseGroups ?? Array.Empty<string>());
            return merged
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct()
                .Take(Mathf.Max(1, maxCount))
                .ToArray();
        }

        private static float ComputeIntensity(string text, int totalMatches, int wordCount)
        {
            int exclamations = text.Count(c => c == '!');
            int intensifiers = INTENSIFIERS.Matches(text).Count;
            int hedges = HEDGES.Matches(text).Count;

            float score = 0.32f;
            score += Mathf.Clamp01(totalMatches / 10f) * 0.38f;
            score += Mathf.Clamp(intensifiers * 0.08f + exclamations * 0.05f, 0f, 0.22f);
            score += Mathf.Clamp01((wordCount - 10) / 20f) * 0.08f;
            score -= Mathf.Clamp(hedges * 0.06f, 0f, 0.2f);

            return Mathf.Clamp01(score);
        }

        private static float ComputeConfidence(int topTagCount, int totalMatches, int wordCount, float intensity)
        {
            float score = 0.2f;
            score += Mathf.Clamp01(totalMatches / 8f) * 0.45f;
            score += Mathf.Clamp01(topTagCount / 3f) * 0.2f;
            score += Mathf.Clamp01(wordCount / 20f) * 0.1f;
            score += intensity * 0.15f;
            return Mathf.Clamp01(score);
        }

        private static float ComputePolarity(List<string> tags, Dictionary<string, float> tagScores)
        {
            float positive = 0f;
            float negative = 0f;

            foreach (var tag in tags)
            {
                float weight = tagScores.TryGetValue(tag, out var s) ? s : 1f;
                if (POSITIVE_TAGS.Contains(tag)) positive += weight;
                if (NEGATIVE_TAGS.Contains(tag)) negative += weight;
            }

            float total = positive + negative;
            if (total <= 0.0001f) return 0f;
            return Mathf.Clamp((positive - negative) / total, -1f, 1f);
        }

        private static string[] BuildWeightedGroups(
            List<string> topTags,
            Dictionary<string, float> tagScores,
            Dictionary<string, string[]> map,
            string[] fallback,
            int maxCount)
        {
            var scores = new Dictionary<string, float>();

            for (int i = 0; i < topTags.Count; i++)
            {
                string tag = topTags[i];
                if (!map.TryGetValue(tag, out var groups) || groups == null || groups.Length == 0)
                    continue;

                float tagWeight = tagScores.TryGetValue(tag, out var score) ? score : 1f;
                float rankWeight = Mathf.Lerp(1f, 0.55f, topTags.Count > 1 ? i / (float)(topTags.Count - 1) : 0f);

                for (int g = 0; g < groups.Length; g++)
                {
                    string group = groups[g];
                    float positionWeight = Mathf.Lerp(1f, 0.72f, groups.Length > 1 ? g / (float)(groups.Length - 1) : 0f);
                    float weight = tagWeight * rankWeight * positionWeight;
                    scores[group] = scores.TryGetValue(group, out var existing) ? existing + weight : weight;
                }
            }

            var ordered = scores
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Take(Mathf.Max(1, maxCount))
                .Select(kv => kv.Key)
                .ToArray();

            return ordered.Length > 0 ? ordered : fallback;
        }

        private static string[] BuildWeightedDriftTags(
            List<string> topTags,
            Dictionary<string, float> tagScores,
            float polarity,
            int maxCount)
        {
            var scores = new Dictionary<string, float>();

            for (int i = 0; i < topTags.Count; i++)
            {
                string tag = topTags[i];
                if (!TAG_TO_DRIFT_TAGS.TryGetValue(tag, out var driftTags) || driftTags == null || driftTags.Length == 0)
                    continue;

                float tagWeight = tagScores.TryGetValue(tag, out var score) ? score : 1f;
                float rankWeight = Mathf.Lerp(1f, 0.6f, topTags.Count > 1 ? i / (float)(topTags.Count - 1) : 0f);

                foreach (var drift in driftTags)
                {
                    float polarityBias = 1f;
                    if (polarity > 0.2f && NEGATIVE_TAGS.Contains(drift)) polarityBias = 1.25f;
                    if (polarity < -0.2f && POSITIVE_TAGS.Contains(drift)) polarityBias = 1.25f;
                    if (Mathf.Abs(polarity) < 0.15f && drift == "institutional") polarityBias = 1.12f;

                    float weight = tagWeight * rankWeight * polarityBias;
                    scores[drift] = scores.TryGetValue(drift, out var existing) ? existing + weight : weight;
                }
            }

            var ordered = scores
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Take(Mathf.Max(1, maxCount))
                .Select(kv => kv.Key)
                .ToArray();

            return ordered.Length > 0 ? ordered : new[] { "institutional", "clinical", "mundane" };
        }
    }
}
