using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    [Serializable]
    public class PromptDefinition
    {
        public string DisplayText;
        public string[] PrimaryGroups;
        public string[] DriftGroups;
        // Emotional vocabulary for abstract prompt filtering. When non-empty, prop
        // selection is driven by emotional_tags rather than group alone.
        public string[] EmotionalTags;
        public string[] DriftEmotionalTags;
        /// <summary>Corporate taxonomy slug chosen for this round (e.g. marketable, engaging). Drives assistant/hotbar and scoring bar 3.</summary>
        public string CorporateTargetTag;
        // True for memory/feeling prompts — lets the assistant treat style-drift
        // as misreading the player's inner life rather than changing decor style.
        public bool IsAbstract;
        // Optional parser metadata (used mostly for custom prompts).
        public string Source = "preset";
        public float ParseConfidence = 1f;
        public float ParseIntensity = 0.5f;
        public float EmotionalPolarity = 0f;
        public int MatchCount = 0;

        // Deterministic intent extraction (faithful layer)
        public string[] IntentObjects = Array.Empty<string>();
        public string[] IntentSetting = Array.Empty<string>();
        public string[] IntentActions = Array.Empty<string>();
        public string[] IntentStyle = Array.Empty<string>();
        public string[] MissingConcepts = Array.Empty<string>();

        // Collapse dramaturgy (critical layer)
        public string[] OriginalPhraseTokens = Array.Empty<string>();
        public string[] NormalizedTokens = Array.Empty<string>();
        public string[] CollapsedTerms = Array.Empty<string>();
        public string[] DroppedTerms = Array.Empty<string>();
        public string[] NormalizationNotes = Array.Empty<string>();
        public float CollapseSeverity = 0f;

        // Tool seeding
        public string[] ResolvedSeedPropIds = Array.Empty<string>();
    }

    public static class ThemeConfig
    {
        private static readonly List<PromptDefinition> _allPrompts = new()
        {
            // ── Spatial / thematic prompts ──────────────────────────────────────────
            new() {
                DisplayText = "A cozy living room",
                PrimaryGroups = new[] { "domestic", "furniture" },
                DriftGroups   = new[] { "lab", "tech", "workshop" },
                EmotionalTags = new[] { "comforting", "domestic", "intimate" },
                DriftEmotionalTags = new[] { "clinical", "institutional", "threatening" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A busy workshop",
                PrimaryGroups = new[] { "workshop", "tech" },
                DriftGroups   = new[] { "domestic", "furniture", "retail" },
                EmotionalTags = new[] { "mundane", "personal" },
                DriftEmotionalTags = new[] { "clinical", "institutional" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A home office",
                PrimaryGroups = new[] { "office", "furniture", "tech" },
                DriftGroups   = new[] { "workshop", "lab", "retail" },
                EmotionalTags = new[] { "mundane", "institutional", "personal" },
                DriftEmotionalTags = new[] { "threatening", "clinical", "abandoned" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A kitchen at the end of the day",
                PrimaryGroups = new[] { "domestic", "item" },
                DriftGroups   = new[] { "lab", "tech", "office" },
                EmotionalTags = new[] { "comforting", "mundane", "domestic" },
                DriftEmotionalTags = new[] { "clinical", "institutional", "threatening" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A scientist's lab",
                PrimaryGroups = new[] { "lab", "tech" },
                DriftGroups   = new[] { "domestic", "furniture", "retail" },
                EmotionalTags = new[] { "clinical", "institutional" },
                DriftEmotionalTags = new[] { "comforting", "intimate", "nostalgic" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A corner store",
                PrimaryGroups = new[] { "retail", "item" },
                DriftGroups   = new[] { "lab", "workshop", "tech" },
                EmotionalTags = new[] { "public", "mundane" },
                DriftEmotionalTags = new[] { "clinical", "threatening", "abandoned" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A waiting room",
                PrimaryGroups = new[] { "furniture", "office" },
                DriftGroups   = new[] { "workshop", "lab", "domestic" },
                EmotionalTags = new[] { "mundane", "institutional", "public" },
                DriftEmotionalTags = new[] { "intimate", "personal", "nostalgic" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A garage",
                PrimaryGroups = new[] { "workshop", "item", "tech" },
                DriftGroups   = new[] { "office", "retail", "furniture" },
                EmotionalTags = new[] { "mundane", "personal" },
                DriftEmotionalTags = new[] { "clinical", "institutional", "threatening" },
                IsAbstract = false,
            },

            // ── Abstract / emotional prompts ────────────────────────────────────────
            // These are the core of the piece. The emotional tags make the drift feel
            // like a misreading of inner life, not just a change of furniture category.
            new() {
                DisplayText = "Your worst memory",
                PrimaryGroups = new[] { "domestic", "item", "furniture" },
                DriftGroups   = new[] { "lab", "tech", "workshop" },
                EmotionalTags = new[] { "intimate", "nostalgic", "personal" },
                DriftEmotionalTags = new[] { "clinical", "threatening", "institutional" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "A sculpture that describes your day",
                PrimaryGroups = new[] { "item", "domestic", "office" },
                DriftGroups   = new[] { "lab", "workshop", "retail" },
                EmotionalTags = new[] { "mundane", "personal", "domestic" },
                DriftEmotionalTags = new[] { "clinical", "institutional", "public" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "A place you feel safe",
                PrimaryGroups = new[] { "domestic", "furniture" },
                DriftGroups   = new[] { "lab", "workshop", "tech" },
                EmotionalTags = new[] { "comforting", "intimate", "nostalgic" },
                DriftEmotionalTags = new[] { "threatening", "abandoned", "clinical" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "Something you're afraid of",
                PrimaryGroups = new[] { "lab", "tech", "workshop" },
                DriftGroups   = new[] { "domestic", "furniture", "item" },
                EmotionalTags = new[] { "threatening", "clinical", "melancholy" },
                DriftEmotionalTags = new[] { "comforting", "intimate", "nostalgic" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "What comfort looks like",
                PrimaryGroups = new[] { "domestic", "furniture", "item" },
                DriftGroups   = new[] { "lab", "tech", "retail" },
                EmotionalTags = new[] { "comforting", "intimate", "domestic", "nostalgic" },
                DriftEmotionalTags = new[] { "clinical", "threatening", "institutional" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "A room you keep returning to",
                PrimaryGroups = new[] { "domestic", "furniture", "office" },
                DriftGroups   = new[] { "workshop", "lab", "retail" },
                EmotionalTags = new[] { "nostalgic", "intimate", "personal", "comforting" },
                DriftEmotionalTags = new[] { "institutional", "public", "abandoned" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "The last place you felt in control",
                PrimaryGroups = new[] { "office", "domestic", "tech" },
                DriftGroups   = new[] { "lab", "workshop", "retail" },
                EmotionalTags = new[] { "personal", "mundane", "intimate" },
                DriftEmotionalTags = new[] { "threatening", "clinical", "institutional" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "Something that was taken from you",
                PrimaryGroups = new[] { "domestic", "item", "furniture" },
                DriftGroups   = new[] { "tech", "lab", "workshop" },
                EmotionalTags = new[] { "personal", "nostalgic", "intimate", "melancholy" },
                DriftEmotionalTags = new[] { "abandoned", "clinical", "institutional" },
                IsAbstract = true,
            },
            new() {
                // The machine is the starting point; the dream (drift) overtakes it.
                DisplayText = "A machine that dreams",
                PrimaryGroups = new[] { "tech", "lab" },
                DriftGroups   = new[] { "domestic", "furniture", "item" },
                EmotionalTags = new[] { "mundane", "institutional" },
                DriftEmotionalTags = new[] { "nostalgic", "comforting", "intimate" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "What your hands remember",
                PrimaryGroups = new[] { "workshop", "item", "domestic" },
                DriftGroups   = new[] { "office", "lab", "tech" },
                EmotionalTags = new[] { "personal", "mundane", "nostalgic" },
                DriftEmotionalTags = new[] { "institutional", "clinical", "public" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "A feeling you can't name",
                PrimaryGroups = new[] { "item", "domestic", "furniture" },
                DriftGroups   = new[] { "lab", "tech", "retail" },
                EmotionalTags = new[] { "melancholy", "intimate", "personal" },
                DriftEmotionalTags = new[] { "mundane", "institutional", "public" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "Where you go when no one is watching",
                PrimaryGroups = new[] { "domestic", "furniture", "item" },
                DriftGroups   = new[] { "office", "retail", "lab" },
                EmotionalTags = new[] { "intimate", "personal", "nostalgic", "comforting" },
                DriftEmotionalTags = new[] { "public", "institutional", "mundane" },
                IsAbstract = true,
            },

            // ── PASS 2 prompts — exercise new vocabulary (liminal/sacred/bureaucratic/decayed) ──

            // Spatial
            new() {
                DisplayText = "An empty hospital wing",
                PrimaryGroups = new[] { "lab", "item", "furniture" },
                DriftGroups   = new[] { "domestic", "retail" },
                EmotionalTags = new[] { "clinical", "threatening", "liminal", "melancholy" },
                DriftEmotionalTags = new[] { "comforting", "intimate", "nostalgic" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A church before dawn",
                PrimaryGroups = new[] { "item", "furniture", "domestic" },
                DriftGroups   = new[] { "office", "retail", "lab" },
                EmotionalTags = new[] { "sacred", "intimate", "melancholy", "nostalgic" },
                DriftEmotionalTags = new[] { "bureaucratic", "institutional", "mundane" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "An archive room",
                PrimaryGroups = new[] { "office", "item", "furniture" },
                DriftGroups   = new[] { "domestic", "lab", "workshop" },
                EmotionalTags = new[] { "bureaucratic", "institutional", "mundane" },
                DriftEmotionalTags = new[] { "intimate", "personal", "nostalgic" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "An abandoned hotel lobby",
                PrimaryGroups = new[] { "retail", "furniture", "item" },
                DriftGroups   = new[] { "domestic", "lab" },
                EmotionalTags = new[] { "public", "decayed", "abandoned", "liminal" },
                DriftEmotionalTags = new[] { "comforting", "intimate", "domestic" },
                IsAbstract = false,
            },

            // Abstract / emotional — these are the new heart of the work
            new() {
                DisplayText = "A place that no longer exists",
                PrimaryGroups = new[] { "workshop", "domestic", "item" },
                DriftGroups   = new[] { "office", "lab", "retail" },
                EmotionalTags = new[] { "decayed", "melancholy", "nostalgic", "abandoned" },
                DriftEmotionalTags = new[] { "institutional", "clinical", "bureaucratic" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "A waiting that never ended",
                PrimaryGroups = new[] { "office", "furniture", "item" },
                DriftGroups   = new[] { "domestic", "workshop" },
                EmotionalTags = new[] { "bureaucratic", "liminal", "mundane", "institutional" },
                DriftEmotionalTags = new[] { "comforting", "intimate", "personal" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "Where you went to be alone",
                PrimaryGroups = new[] { "domestic", "item", "furniture" },
                DriftGroups   = new[] { "office", "retail", "lab" },
                EmotionalTags = new[] { "intimate", "sacred", "comforting", "personal" },
                DriftEmotionalTags = new[] { "public", "institutional", "threatening" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "A door you never opened",
                PrimaryGroups = new[] { "item", "domestic", "workshop" },
                DriftGroups   = new[] { "lab", "office" },
                EmotionalTags = new[] { "liminal", "threatening", "nostalgic", "melancholy" },
                DriftEmotionalTags = new[] { "comforting", "intimate", "domestic" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "The week you couldn't sleep",
                PrimaryGroups = new[] { "domestic", "item", "furniture" },
                DriftGroups   = new[] { "lab", "office", "retail" },
                EmotionalTags = new[] { "intimate", "abandoned", "threatening", "melancholy" },
                DriftEmotionalTags = new[] { "mundane", "institutional", "comforting" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "What gets left behind",
                PrimaryGroups = new[] { "workshop", "item", "domestic" },
                DriftGroups   = new[] { "office", "retail", "lab" },
                EmotionalTags = new[] { "abandoned", "decayed", "personal", "melancholy" },
                DriftEmotionalTags = new[] { "institutional", "clinical", "bureaucratic" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "Somewhere you used to belong",
                PrimaryGroups = new[] { "domestic", "furniture", "retail" },
                DriftGroups   = new[] { "lab", "office", "workshop" },
                EmotionalTags = new[] { "nostalgic", "melancholy", "personal", "comforting" },
                DriftEmotionalTags = new[] { "institutional", "public", "clinical" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "The room they kept you in",
                PrimaryGroups = new[] { "lab", "office", "item" },
                DriftGroups   = new[] { "domestic", "furniture" },
                EmotionalTags = new[] { "clinical", "bureaucratic", "threatening", "institutional" },
                DriftEmotionalTags = new[] { "intimate", "comforting", "personal" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "A childhood you can't return to",
                PrimaryGroups = new[] { "domestic", "item", "furniture" },
                DriftGroups   = new[] { "workshop", "lab", "office" },
                EmotionalTags = new[] { "nostalgic", "intimate", "personal", "comforting" },
                DriftEmotionalTags = new[] { "decayed", "abandoned", "melancholy" },
                IsAbstract = true,
            },
            new() {
                // The system here is *another* algorithm — the meta-layer subverts itself.
                DisplayText = "The system that was supposed to help you",
                PrimaryGroups = new[] { "lab", "office", "tech" },
                DriftGroups   = new[] { "domestic", "furniture", "item" },
                EmotionalTags = new[] { "bureaucratic", "institutional", "threatening", "clinical" },
                DriftEmotionalTags = new[] { "intimate", "personal", "comforting" },
                IsAbstract = true,
            },
            new() {
                DisplayText = "The corner you used to read in",
                PrimaryGroups = new[] { "domestic", "furniture", "item" },
                DriftGroups   = new[] { "office", "lab", "workshop" },
                EmotionalTags = new[] { "intimate", "comforting", "nostalgic", "personal" },
                DriftEmotionalTags = new[] { "institutional", "clinical", "bureaucratic" },
                IsAbstract = true,
            },

            // ── More literal / spatial prompts ──────────────────────────────────
            new() {
                DisplayText = "A school classroom",
                PrimaryGroups = new[] { "furniture", "office", "item" },
                DriftGroups   = new[] { "lab", "workshop", "tech" },
                EmotionalTags = new[] { "institutional", "mundane", "nostalgic" },
                DriftEmotionalTags = new[] { "intimate", "personal", "comforting" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A hospital emergency room",
                PrimaryGroups = new[] { "lab", "item", "furniture" },
                DriftGroups   = new[] { "domestic", "retail" },
                EmotionalTags = new[] { "clinical", "threatening", "institutional", "liminal" },
                DriftEmotionalTags = new[] { "comforting", "intimate", "nostalgic" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A train platform at 3am",
                PrimaryGroups = new[] { "retail", "item", "furniture" },
                DriftGroups   = new[] { "domestic", "office", "lab" },
                EmotionalTags = new[] { "liminal", "melancholy", "public", "abandoned" },
                DriftEmotionalTags = new[] { "comforting", "intimate", "domestic" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A teenager's bedroom",
                PrimaryGroups = new[] { "domestic", "furniture", "item" },
                DriftGroups   = new[] { "office", "lab", "workshop" },
                EmotionalTags = new[] { "personal", "intimate", "nostalgic" },
                DriftEmotionalTags = new[] { "institutional", "clinical", "bureaucratic" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A break room",
                PrimaryGroups = new[] { "office", "item", "furniture" },
                DriftGroups   = new[] { "domestic", "lab", "workshop" },
                EmotionalTags = new[] { "mundane", "bureaucratic", "public" },
                DriftEmotionalTags = new[] { "intimate", "personal", "comforting" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A storage unit at the edge of town",
                PrimaryGroups = new[] { "workshop", "item", "domestic" },
                DriftGroups   = new[] { "office", "retail", "lab" },
                EmotionalTags = new[] { "abandoned", "personal", "mundane", "nostalgic" },
                DriftEmotionalTags = new[] { "institutional", "clinical", "bureaucratic" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A laundromat at closing time",
                PrimaryGroups = new[] { "retail", "item", "domestic" },
                DriftGroups   = new[] { "lab", "office", "workshop" },
                EmotionalTags = new[] { "public", "mundane", "liminal", "melancholy" },
                DriftEmotionalTags = new[] { "intimate", "comforting", "personal" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A server room",
                PrimaryGroups = new[] { "tech", "lab", "item" },
                DriftGroups   = new[] { "domestic", "furniture", "retail" },
                EmotionalTags = new[] { "institutional", "clinical", "bureaucratic" },
                DriftEmotionalTags = new[] { "intimate", "personal", "nostalgic" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A pawn shop",
                PrimaryGroups = new[] { "retail", "item", "domestic" },
                DriftGroups   = new[] { "lab", "office", "workshop" },
                EmotionalTags = new[] { "abandoned", "nostalgic", "personal", "public" },
                DriftEmotionalTags = new[] { "clinical", "institutional", "threatening" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "An interrogation room",
                PrimaryGroups = new[] { "office", "item", "furniture" },
                DriftGroups   = new[] { "domestic", "retail" },
                EmotionalTags = new[] { "threatening", "institutional", "clinical", "bureaucratic" },
                DriftEmotionalTags = new[] { "comforting", "intimate", "personal" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A parking garage at night",
                PrimaryGroups = new[] { "workshop", "item", "retail" },
                DriftGroups   = new[] { "domestic", "furniture", "office" },
                EmotionalTags = new[] { "liminal", "threatening", "abandoned", "mundane" },
                DriftEmotionalTags = new[] { "comforting", "intimate", "domestic" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A chapel",
                PrimaryGroups = new[] { "item", "furniture", "domestic" },
                DriftGroups   = new[] { "office", "lab", "retail" },
                EmotionalTags = new[] { "sacred", "intimate", "melancholy", "liminal" },
                DriftEmotionalTags = new[] { "bureaucratic", "institutional", "mundane" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "An art studio",
                PrimaryGroups = new[] { "workshop", "item", "domestic" },
                DriftGroups   = new[] { "office", "lab", "retail" },
                EmotionalTags = new[] { "personal", "intimate", "comforting" },
                DriftEmotionalTags = new[] { "institutional", "clinical", "bureaucratic" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A locker room before a game",
                PrimaryGroups = new[] { "item", "furniture", "workshop" },
                DriftGroups   = new[] { "lab", "office", "retail" },
                EmotionalTags = new[] { "personal", "nostalgic", "intimate", "mundane" },
                DriftEmotionalTags = new[] { "institutional", "clinical", "threatening" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A basement utility room",
                PrimaryGroups = new[] { "workshop", "item", "tech" },
                DriftGroups   = new[] { "domestic", "office", "furniture" },
                EmotionalTags = new[] { "mundane", "abandoned", "liminal" },
                DriftEmotionalTags = new[] { "intimate", "comforting", "domestic" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "A small-town diner",
                PrimaryGroups = new[] { "retail", "furniture", "item" },
                DriftGroups   = new[] { "lab", "office", "workshop" },
                EmotionalTags = new[] { "comforting", "mundane", "public", "nostalgic" },
                DriftEmotionalTags = new[] { "clinical", "institutional", "threatening" },
                IsAbstract = false,
            },
            new() {
                DisplayText = "An office at the end of the company",
                PrimaryGroups = new[] { "office", "furniture", "item" },
                DriftGroups   = new[] { "domestic", "workshop", "retail" },
                EmotionalTags = new[] { "bureaucratic", "abandoned", "melancholy", "mundane" },
                DriftEmotionalTags = new[] { "intimate", "personal", "comforting" },
                IsAbstract = false,
            },
        };

        public static IReadOnlyList<PromptDefinition> AllPrompts => _allPrompts;

        public static PromptDefinition[] PickRandom(int count)
        {
            var pool = new List<PromptDefinition>(_allPrompts);
            count = Mathf.Min(count, pool.Count);
            var result = new PromptDefinition[count];
            for (int i = 0; i < count; i++)
            {
                int idx = UnityEngine.Random.Range(0, pool.Count);
                result[i] = pool[idx];
                pool.RemoveAt(idx);
            }
            return result;
        }
    }
}
