using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AlgorithmicGallery.Corruption;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AlgorithmicGallery.Corruption.Editor
{
    /// <summary>
    /// Full-sweep validator that runs headless in batch mode without entering Play Mode.
    /// </summary>
    public static class CurationViewportBatchValidator
    {
        private const string ScenePath = "Assets/AssetCuration.unity";
        private static readonly string ReportPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs/curation-full-sweep-report.txt");

        private static ValidationSession _session;
        private static bool _running;

        public static void RunFullSweepValidation()
        {
            if (_running)
            {
                Debug.LogWarning("[CurationBatchValidator] Validation already running.");
                return;
            }

            _running = true;
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                _session = new ValidationSession();
                EditorApplication.update += OnEditorUpdate;
                Debug.Log("[CurationBatchValidator] Started full-sweep validation.");
            }
            catch (Exception ex)
            {
                WriteReport($"[CurationBatchValidator] FAILED EARLY: {ex.Message}");
                Exit(1);
            }
        }

        private static void OnEditorUpdate()
        {
            if (!_running || _session == null)
                return;

            _session.Tick();
            if (!_session.IsComplete)
                return;

            WriteReport(_session.BuildReport());
            Exit(_session.ExitCode);
        }

        private static void Exit(int code)
        {
            EditorApplication.update -= OnEditorUpdate;
            _running = false;
            _session = null;
            Debug.Log($"[CurationBatchValidator] Exiting with code {code}.");
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }

        private static void WriteReport(string report)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? "Logs");
                File.WriteAllText(ReportPath, report);
                Debug.Log($"[CurationBatchValidator] Report written: {ReportPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CurationBatchValidator] Failed to write report: {ex.Message}");
            }
        }

        private sealed class ValidationSession
        {
            private const float PerPropLoadTimeoutSeconds = 20f;
            private const float SessionTimeoutSeconds = 7200f;
            private const float AlignmentTolerance = 0.02f;
            private const float ViewportMargin = 0.02f;

            private readonly List<string> _failures = new List<string>();
            private readonly float _sessionStartedAt = Time.realtimeSinceStartup;
            private readonly CuratedPropManifest _manifest;
            private readonly IReadOnlyList<PropEntry> _props;

            private CurationViewport _viewport;
            private int _index;
            private PropEntry _activeProp;
            private CoroutinePump _activeLoad;
            private float _activePropStartedAt;
            private bool _turntableDisabled;
            private int _validatedCount;

            public bool IsComplete { get; private set; }
            public int ExitCode { get; private set; } = 1;

            public ValidationSession()
            {
                _manifest = CuratedPropManifest.LoadFromStreamingAssets();
                _props = _manifest?.All;
                if (_props == null || _props.Count == 0)
                {
                    _failures.Add("manifest-load-failed-or-empty");
                    IsComplete = true;
                    ExitCode = 1;
                }
            }

            public void Tick()
            {
                if (IsComplete)
                    return;

                if (Time.realtimeSinceStartup - _sessionStartedAt > SessionTimeoutSeconds)
                {
                    _failures.Add("session-timeout");
                    Finish(1);
                    return;
                }

                if (_viewport == null)
                {
                    _viewport = UnityEngine.Object.FindFirstObjectByType<CurationViewport>();
                    if (_viewport == null)
                    {
                        var host = new GameObject("CurationViewport_BatchValidatorHost");
                        _viewport = host.AddComponent<CurationViewport>();
                    }
                    _viewport.InitializeForDiagnosticsIfNeeded();
                    return;
                }

                if (!_turntableDisabled && _viewport.TurntableOn)
                {
                    _viewport.ToggleTurntable();
                    _turntableDisabled = true;
                }

                if (_activeLoad == null)
                {
                    if (_index >= _props.Count)
                    {
                        Finish(_failures.Count == 0 ? 0 : 1);
                        return;
                    }

                    _activeProp = _props[_index];
                    if (_activeProp == null || string.IsNullOrWhiteSpace(_activeProp.GlbPath))
                    {
                        _failures.Add($"invalid-entry id='{_activeProp?.Id ?? "null"}' reason=missing-prop-or-glb-path");
                        Advance();
                        return;
                    }

                    IEnumerator loadRoutine = _viewport.CreateLoadPropCoroutineForDiagnostics(_activeProp);
                    if (loadRoutine == null)
                    {
                        _failures.Add($"id='{_activeProp.Id}' reason=load-routine-null");
                        Advance();
                        return;
                    }
                    _activeLoad = new CoroutinePump(loadRoutine);
                    _activePropStartedAt = Time.realtimeSinceStartup;
                    return;
                }

                if (Time.realtimeSinceStartup - _activePropStartedAt > PerPropLoadTimeoutSeconds)
                {
                    _failures.Add($"id='{_activeProp.Id}' reason=load-timeout");
                    Advance();
                    return;
                }

                bool keepRunning = _activeLoad.StepOneTick();
                if (keepRunning)
                    return;

                ValidateLoadedProp();
                Advance();
            }

            public string BuildReport()
            {
                var sb = new StringBuilder();
                sb.AppendLine("[CurationBatchValidator] Full sweep report");
                sb.AppendLine($"ValidatedProps={_validatedCount}");
                sb.AppendLine($"FailureCount={_failures.Count}");
                sb.AppendLine($"Result={(ExitCode == 0 ? "PASS" : "FAIL")}");
                if (_failures.Count > 0)
                {
                    sb.AppendLine("Failures:");
                    for (int i = 0; i < _failures.Count; i++)
                        sb.AppendLine($"- {_failures[i]}");
                }
                return sb.ToString();
            }

            private void ValidateLoadedProp()
            {
                if (!_viewport.TryGetLoadedPropBounds(out Bounds bounds))
                {
                    _failures.Add($"id='{_activeProp.Id}' reason=missing-bounds-after-load");
                    return;
                }

                if (!_viewport.TryGetPedestalTopWorldYForDiagnostics(out float pedestalTopY))
                {
                    _failures.Add($"id='{_activeProp.Id}' reason=pedestal-top-unresolved");
                    return;
                }

                float expectedBottom = pedestalTopY + _viewport.PropPedestalClearance;
                float alignmentDelta = Mathf.Abs(bounds.min.y - expectedBottom);
                if (alignmentDelta > AlignmentTolerance)
                    _failures.Add($"id='{_activeProp.Id}' reason=bad-alignment minY={bounds.min.y:F4} expected={expectedBottom:F4} delta={alignmentDelta:F4}");

                float distance = _viewport.CurrentCameraDistance;
                if (distance < _viewport.MinFramingDistance - 0.0001f || distance > _viewport.MaxFramingDistance + 0.0001f)
                    _failures.Add($"id='{_activeProp.Id}' reason=distance-out-of-range dist={distance:F4} min={_viewport.MinFramingDistance:F4} max={_viewport.MaxFramingDistance:F4}");

                Camera cam = _viewport.ViewCamera;
                if (cam == null)
                {
                    _failures.Add($"id='{_activeProp.Id}' reason=missing-view-camera");
                    return;
                }

                if (!AreBoundsCornersInsideViewport(cam, bounds, ViewportMargin))
                    _failures.Add($"id='{_activeProp.Id}' reason=not-fully-framed");
            }

            private void Advance()
            {
                _validatedCount++;
                _index++;
                _activeProp = null;
                _activeLoad = null;
            }

            private void Finish(int exitCode)
            {
                ExitCode = exitCode;
                IsComplete = true;
            }

            private static bool AreBoundsCornersInsideViewport(Camera camera, Bounds bounds, float margin)
            {
                Vector3 c = bounds.center;
                Vector3 e = bounds.extents;
                Vector3[] corners =
                {
                    c + new Vector3( e.x,  e.y,  e.z),
                    c + new Vector3( e.x,  e.y, -e.z),
                    c + new Vector3( e.x, -e.y,  e.z),
                    c + new Vector3( e.x, -e.y, -e.z),
                    c + new Vector3(-e.x,  e.y,  e.z),
                    c + new Vector3(-e.x,  e.y, -e.z),
                    c + new Vector3(-e.x, -e.y,  e.z),
                    c + new Vector3(-e.x, -e.y, -e.z),
                };

                float min = margin;
                float max = 1f - margin;
                for (int i = 0; i < corners.Length; i++)
                {
                    Vector3 vp = camera.WorldToViewportPoint(corners[i]);
                    if (vp.z <= 0f || vp.x < min || vp.x > max || vp.y < min || vp.y > max)
                        return false;
                }

                return true;
            }

            private sealed class CoroutinePump
            {
                private readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();
                private object _pendingYield;
                private bool _waitOneFrame;

                public CoroutinePump(IEnumerator root)
                {
                    _stack.Push(root);
                }

                public bool StepOneTick()
                {
                    if (_stack.Count == 0)
                        return false;

                    if (_waitOneFrame)
                    {
                        _waitOneFrame = false;
                        return true;
                    }

                    if (_pendingYield is CustomYieldInstruction customYield)
                    {
                        if (customYield.keepWaiting)
                            return true;
                        _pendingYield = null;
                    }

                    int guard = 64;
                    while (guard-- > 0 && _stack.Count > 0)
                    {
                        IEnumerator top = _stack.Peek();
                        if (!top.MoveNext())
                        {
                            _stack.Pop();
                            continue;
                        }

                        object yielded = top.Current;
                        if (yielded == null)
                        {
                            _waitOneFrame = true;
                            return true;
                        }

                        if (yielded is IEnumerator nested)
                        {
                            _stack.Push(nested);
                            continue;
                        }

                        if (yielded is CustomYieldInstruction c)
                        {
                            _pendingYield = c;
                            return true;
                        }

                        // Unknown yield objects are treated as frame waits.
                        _waitOneFrame = true;
                        return true;
                    }

                    return _stack.Count > 0;
                }
            }
        }
    }
}
