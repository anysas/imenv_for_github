using UnityEngine;

public class cubeAppear : MonoBehaviour
{
    [Header("Visual")]
    public MeshRenderer meshRenderer; // the cube visual

    [Header("Next Behaviour")]
    public placeBlock placeBlockScript;

    private bool activated = false;

    void Start()
    {
        // ensure it starts invisible
        if (meshRenderer != null)
            meshRenderer.enabled = false;
    }

    void Update()
    {
        if (activated) return;

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.transform == transform)
                {
                    Activate();
                }
            }
        }
    }

    void Activate()
    {
        activated = true;

        // reveal cube
        if (meshRenderer != null)
            meshRenderer.enabled = true;

        // enable next system
        if (placeBlockScript != null)
            placeBlockScript.enabled = true;
    }
}