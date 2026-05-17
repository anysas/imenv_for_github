using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    public class VocabEntry
    {
        public string[] Emotional = Array.Empty<string>();
        public string Object;
        public string Setting;
        public string Action;
        public string Style;
        public float Weight = 0.8f;
    }

    public class VocabularyDictionary
    {
        private Dictionary<string, VocabEntry> _words = new();
        private List<(string phrase, VocabEntry entry)> _phrases = new();
        private static VocabularyDictionary _instance;

        public int WordCount => _words.Count;
        public int PhraseCount => _phrases.Count;

        public static VocabularyDictionary Instance => _instance ??= Load();

        public bool TryGetWord(string token, out VocabEntry entry)
        {
            return _words.TryGetValue(token, out entry);
        }

        public List<(string phrase, VocabEntry entry)> MatchPhrases(string lowercaseText)
        {
            var matches = new List<(string, VocabEntry)>();
            var consumed = new HashSet<int>();

            foreach (var (phrase, entry) in _phrases)
            {
                int idx = 0;
                while (idx <= lowercaseText.Length - phrase.Length)
                {
                    int pos = lowercaseText.IndexOf(phrase, idx, StringComparison.Ordinal);
                    if (pos < 0) break;

                    bool startOk = pos == 0 || !char.IsLetterOrDigit(lowercaseText[pos - 1]);
                    int end = pos + phrase.Length;
                    bool endOk = end >= lowercaseText.Length || !char.IsLetterOrDigit(lowercaseText[end]);

                    if (startOk && endOk && !IsConsumed(consumed, pos, end))
                    {
                        matches.Add((phrase, entry));
                        for (int i = pos; i < end; i++) consumed.Add(i);
                    }
                    idx = pos + 1;
                }
            }
            return matches;
        }

        private static bool IsConsumed(HashSet<int> consumed, int start, int end)
        {
            for (int i = start; i < end; i++)
                if (consumed.Contains(i)) return true;
            return false;
        }

        private static VocabularyDictionary Load()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "vocabulary.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"VocabularyDictionary: file not found at {path} — parser will rely on regex only");
                return new VocabularyDictionary();
            }

            string json = File.ReadAllText(path);
            var root = JObject.Parse(json);
            var dict = new VocabularyDictionary();

            var wordsObj = root["words"] as JObject;
            if (wordsObj != null)
            {
                foreach (var kv in wordsObj)
                {
                    var entry = ParseEntry(kv.Value as JObject);
                    if (entry != null)
                        dict._words[kv.Key.ToLowerInvariant()] = entry;
                }
            }

            var phrasesObj = root["phrases"] as JObject;
            if (phrasesObj != null)
            {
                foreach (var kv in phrasesObj)
                {
                    var entry = ParseEntry(kv.Value as JObject);
                    if (entry != null)
                        dict._phrases.Add((kv.Key.ToLowerInvariant(), entry));
                }
                dict._phrases.Sort((a, b) => b.phrase.Length.CompareTo(a.phrase.Length));
            }

            Debug.Log($"VocabularyDictionary: loaded {dict._words.Count} words, {dict._phrases.Count} phrases");
            return dict;
        }

        private static VocabEntry ParseEntry(JObject obj)
        {
            if (obj == null) return null;

            var entry = new VocabEntry();

            var emo = obj["emotional"] as JArray;
            if (emo != null)
                entry.Emotional = emo.Select(t => t.ToString()).ToArray();

            entry.Object = obj["object"]?.ToString();
            entry.Setting = obj["setting"]?.ToString();
            entry.Action = obj["action"]?.ToString();
            entry.Style = obj["style"]?.ToString();
            entry.Weight = obj["weight"]?.ToObject<float>() ?? 0.8f;

            return entry;
        }
    }
}
