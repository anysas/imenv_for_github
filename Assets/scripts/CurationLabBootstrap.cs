using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    // ─────────────────────────────────────────────────────────────────────────
    // CurationLabBootstrap
    //
    // Drop this on a single empty GameObject in a new scene.
    // It adds CurationManager, CurationViewport, and CurationUI automatically.
    //
    // Setup:
    //   1. File > New Scene (Empty or Basic URP)
    //   2. Create empty GameObject named "CurationLab"
    //   3. Add CurationLabBootstrap component
    //   4. Hit Play
    //
    // Controls (keyboard):
    //   →/← or D/A  next/prev prop
    //   X or Delete  delete prop
    //   K           keep + advance (mark reviewed, no changes)
    //   S           save edits + advance
    //   R           toggle turntable
    //   F           focus camera on prop
    //   +/-         nudge scale by 0.05
    //   Right drag  orbit camera
    //   Scroll      zoom
    // ─────────────────────────────────────────────────────────────────────────
    [DisallowMultipleComponent]
    public class CurationLabBootstrap : MonoBehaviour
    {
        void Awake()
        {
            // The three components must exist on the same GameObject because
            // CurationUI uses [RequireComponent] to find them via GetComponent.
            if (GetComponent<CurationManager>()  == null) gameObject.AddComponent<CurationManager>();
            if (GetComponent<CurationViewport>() == null) gameObject.AddComponent<CurationViewport>();
            if (GetComponent<CurationUI>()       == null) gameObject.AddComponent<CurationUI>();

            // Make sure audio doesn't interfere with an otherwise silent tool scene
            var listener = FindFirstObjectByType<AudioListener>();
            if (listener == null)
            {
                var go = new GameObject("AudioListener");
                go.AddComponent<AudioListener>();
                go.transform.SetParent(transform);
            }

            Debug.Log("CurationLab: bootstrapped.  StreamingAssets path = " +
                      Application.streamingAssetsPath);
        }
    }
}
