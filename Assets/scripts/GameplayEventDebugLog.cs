using System.Collections.Generic;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Rolling log of high-level gameplay events for the F1 <see cref="AssistantDebugUI"/> overlay.
    /// Call <see cref="Push"/> from session flow, hallway, export, etc.
    /// </summary>
    public static class GameplayEventDebugLog
    {
        public const int MaxLines = 40;
        private static readonly List<string> Lines = new List<string>(MaxLines);

        public static void Push(string category, string detail)
        {
            if (string.IsNullOrEmpty(category))
                category = "?";
            if (detail == null)
                detail = "";

            string t = Time.unscaledTime.ToString("F1");
            string line = $"[{t}s] {category}: {detail}";
            Lines.Add(line);
            while (Lines.Count > MaxLines)
                Lines.RemoveAt(0);
        }

        public static int Count => Lines.Count;

        public static string GetLine(int index)
        {
            if (index < 0 || index >= Lines.Count)
                return string.Empty;
            return Lines[index];
        }

        public static void Clear() => Lines.Clear();
    }
}
