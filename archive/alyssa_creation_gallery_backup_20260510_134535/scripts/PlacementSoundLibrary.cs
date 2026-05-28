using System.Collections.Generic;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Procedural placement sound library. Each emotional tag maps to distinct tone
    /// parameters so the player can *hear* the register of what they (or the assistant) places.
    ///
    /// Assistant placements receive a subtle detune (±30 cents) so they feel slightly wrong —
    /// present but off, like a pitch shifted echo of the player's own sound vocabulary.
    ///
    /// No .wav files required — all clips are synthesized at runtime from the same
    /// CreateToneClip / CreateSweepClip pattern as SandboxSfx.
    /// </summary>
    public static class PlacementSoundLibrary
    {
        // Assistant placements can fire in tight bursts (reactive + autonomous). Stacking many
        // short one-shots on a single AudioSource clips and reads as harsh digital clicks.
        private const float AssistantPlacementSoundMinInterval = 0.12f;
        private static float _lastAssistantPlacementSoundUnscaledTime = -1000f;
        private static AudioClip _playerMarkerClip;
        private static AudioClip _assistantMarkerClip;

        private static readonly Dictionary<string, AudioClip> _cache = new();

        // ── Tag → tone parameters ──────────────────────────────────────────────
        // (freq Hz, duration s, amplitude, sweep end Hz or 0 for pure tone, second harmonic Hz or 0)
        private static readonly Dictionary<string, (float freq, float freq2, float freqSweepEnd, float duration, float amplitude, ToneShape shape)> TAG_PARAMS =
            new Dictionary<string, (float, float, float, float, float, ToneShape)>
        {
            // Warm, low, slow attack — the player hears something lived-in
            ["intimate"]      = (220f,  330f, 0f,    0.22f, 0.30f, ToneShape.SlowSwell),
            // Sharp metallic click — cold, immediate, no sustain
            ["clinical"]      = (1400f, 0f,   0f,    0.055f, 0.28f, ToneShape.SharpDecay),
            // Tonal bell swell with gentle harmonic — warm but brief
            ["nostalgic"]     = (440f,  550f, 0f,    0.24f, 0.26f, ToneShape.BellSwell),
            // Low dissonant pair — unsettling sub-pulse
            ["threatening"]   = (58f,   87f,  0f,    0.18f, 0.32f, ToneShape.LowPulse),
            // Soft three-tone chord, slow fade — overtly comforting
            ["comforting"]    = (330f,  495f, 0f,    0.22f, 0.24f, ToneShape.SoftPad),
            // Slow melancholy drop
            ["melancholy"]    = (340f,  0f,   220f,  0.20f, 0.25f, ToneShape.Sweep),
            // Brief stamp — bureaucratic, impersonal
            ["bureaucratic"]  = (820f,  0f,   0f,    0.048f, 0.22f, ToneShape.SharpDecay),
            // Hollow low note — space that has been emptied
            ["abandoned"]     = (160f,  0f,   0f,    0.18f, 0.22f, ToneShape.SlowSwell),
            // Decayed creak sweep — going nowhere slowly
            ["decayed"]       = (110f,  0f,   95f,   0.16f, 0.20f, ToneShape.Sweep),
            // Thin ascending — not here, not there
            ["liminal"]       = (660f,  0f,   880f,  0.14f, 0.20f, ToneShape.Sweep),
            // Resonant sacred bell
            ["sacred"]        = (396f,  594f, 0f,    0.26f, 0.24f, ToneShape.BellSwell),
            // Mundane tap — present but unremarkable
            ["mundane"]       = (520f,  0f,   0f,    0.065f, 0.22f, ToneShape.SharpDecay),
            // Similar to clinical but slightly warmer
            ["institutional"] = (1100f, 0f,   0f,    0.062f, 0.24f, ToneShape.SharpDecay),
            // Slightly warmer than mundane — objects with human history
            ["domestic"]      = (380f,  0f,   0f,    0.14f,  0.24f, ToneShape.SoftPad),
            // Thin public-space ring
            ["public"]        = (740f,  0f,   0f,    0.07f,  0.20f, ToneShape.SharpDecay),
            // Personal — intermediate warmth
            ["personal"]      = (290f,  0f,   0f,    0.16f,  0.24f, ToneShape.SlowSwell),
        };

        private enum ToneShape { SoftPad, SlowSwell, SharpDecay, BellSwell, LowPulse, Sweep }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Play a placement sound appropriate for the prop's emotional tags on the given AudioSource.
        /// </summary>
        /// <param name="src">AudioSource to play on (must not be null).</param>
        /// <param name="emotionalTags">The prop's emotional_tags array (first recognized tag wins).</param>
        /// <param name="isAssistant">True = apply a ±30-cent detune so assistant placements sound "off".</param>
        public static void PlayPlacement(AudioSource src, string[] emotionalTags, bool isAssistant)
        {
            if (src == null) return;

            if (isAssistant)
            {
                float now = Time.unscaledTime;
                if (now - _lastAssistantPlacementSoundUnscaledTime < AssistantPlacementSoundMinInterval)
                    return;
                _lastAssistantPlacementSoundUnscaledTime = now;
            }

            string tag = FirstRecognizedTag(emotionalTags);
            AudioClip clip = GetOrCreateClip(tag);

            float prevPitch = src.pitch;
            if (isAssistant)
            {
                // Detune randomly up or down by 30 cents (twelfth-root of two per cent)
                float cents = Random.value < 0.5f ? 30f : -30f;
                src.pitch = Mathf.Pow(2f, cents / 1200f);
            }
            else
            {
                src.pitch = Random.Range(0.97f, 1.03f);
            }

            // Distinct tag layer.
            src.PlayOneShot(clip, isAssistant ? 0.14f : 0.27f);
            // Distinct role marker layer to clearly separate player vs machine placements.
            src.PlayOneShot(isAssistant ? GetAssistantMarkerClip() : GetPlayerMarkerClip(), isAssistant ? 0.24f : 0.18f);
            src.pitch = prevPitch; // restore after scheduling — PlayOneShot captures pitch at call time
        }

        // ── Internal helpers ───────────────────────────────────────────────────

        private static string FirstRecognizedTag(string[] tags)
        {
            if (tags != null)
            {
                foreach (var t in tags)
                    if (!string.IsNullOrEmpty(t) && TAG_PARAMS.ContainsKey(t))
                        return t;
            }
            return "mundane"; // fallback
        }

        private static AudioClip GetOrCreateClip(string tag)
        {
            if (_cache.TryGetValue(tag, out var cached))
                return cached;

            var p = TAG_PARAMS.TryGetValue(tag, out var prm) ? prm : TAG_PARAMS["mundane"];
            AudioClip clip = BuildClip(tag, p.freq, p.freq2, p.freqSweepEnd, p.duration, p.amplitude, p.shape);
            _cache[tag] = clip;
            return clip;
        }

        private static AudioClip BuildClip(string name, float freq, float freq2, float freqSweepEnd,
                                           float duration, float amplitude, ToneShape shape)
        {
            int sampleRate = 44100;
            int n = Mathf.Max(1, Mathf.RoundToInt(sampleRate * duration));
            float[] data = new float[n];

            float phase1 = 0f, phase2 = 0f;

            // Keep fades shorter than half the clip so in/out regions never fight on tiny buffers.
            int edgeFade = Mathf.Clamp(n / 8, 1, Mathf.Min(96, Mathf.Max(1, n / 2)));

            for (int i = 0; i < n; i++)
            {
                float k = i / (float)(n - 1); // 0..1 normalized time

                // Envelope
                float env = shape switch
                {
                    ToneShape.SlowSwell    => Mathf.Pow(Mathf.Sin(k * Mathf.PI), 1.6f),
                    ToneShape.SharpDecay   => Mathf.Pow(1f - k, 2.2f),
                    ToneShape.BellSwell    => Mathf.Pow(Mathf.Sin(k * Mathf.PI), 0.7f),
                    ToneShape.LowPulse     => Mathf.Sin(k * Mathf.PI) * Mathf.Abs(Mathf.Sin(k * Mathf.PI * 3.7f)),
                    ToneShape.SoftPad      => Mathf.Pow(Mathf.Sin(k * Mathf.PI), 2.5f),
                    ToneShape.Sweep        => Mathf.Sin(k * Mathf.PI),
                    _                      => Mathf.Sin(k * Mathf.PI),
                };

                // Primary tone — optionally sweep frequency
                float f1 = freqSweepEnd > 0f ? Mathf.Lerp(freq, freqSweepEnd, k) : freq;
                phase1 += (2f * Mathf.PI * f1) / sampleRate;
                float sample = Mathf.Sin(phase1) * amplitude * env;

                // Optional second harmonic
                if (freq2 > 0f)
                {
                    phase2 += (2f * Mathf.PI * freq2) / sampleRate;
                    sample += Mathf.Sin(phase2) * amplitude * 0.45f * env;
                    sample *= 0.7f; // keep total under 1 with two partials
                }

                // Short edge fades keep abrupt SharpDecay / short clips from popping when layered.
                float edge = 1f;
                if (i < edgeFade)
                    edge *= (i + 1) / (float)edgeFade;
                if (i >= n - edgeFade)
                    edge *= (n - i) / (float)edgeFade;
                sample *= edge;

                data[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            var clip = AudioClip.Create($"sfx_place_{name}", n, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip GetPlayerMarkerClip()
        {
            if (_playerMarkerClip != null)
                return _playerMarkerClip;
            _playerMarkerClip = BuildClip(
                "player_marker",
                freq: 360f,
                freq2: 540f,
                freqSweepEnd: 0f,
                duration: 0.12f,
                amplitude: 0.22f,
                shape: ToneShape.SoftPad
            );
            return _playerMarkerClip;
        }

        private static AudioClip GetAssistantMarkerClip()
        {
            if (_assistantMarkerClip != null)
                return _assistantMarkerClip;
            _assistantMarkerClip = BuildClip(
                "assistant_marker",
                freq: 980f,
                freq2: 1220f,
                freqSweepEnd: 760f,
                duration: 0.10f,
                amplitude: 0.25f,
                shape: ToneShape.SharpDecay
            );
            return _assistantMarkerClip;
        }
    }
}
