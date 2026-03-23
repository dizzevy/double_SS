using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerFirstPersonVisibility : MonoBehaviour
{
    [SerializeField] private Camera firstPersonCamera;
    [SerializeField] private Transform visualsRoot;
    [SerializeField] private string hiddenLayerName = "LocalPlayerBody";

    private readonly List<Transform> visualNodes = new List<Transform>();
    private bool applied;

    private void Awake()
    {
        ResolveReferences();
        Apply();
    }

    private void LateUpdate()
    {
        if (applied)
        {
            return;
        }

        Apply();
    }

    private void ResolveReferences()
    {
        if (firstPersonCamera == null)
        {
            firstPersonCamera = GetComponentInChildren<Camera>(true);
        }

        if (visualsRoot == null)
        {
            Transform candidate = transform.Find("Visuals");
            if (candidate != null)
            {
                visualsRoot = candidate;
            }
        }
    }

    private void Apply()
    {
        if (applied || firstPersonCamera == null || visualsRoot == null)
        {
            return;
        }

        if (!firstPersonCamera.isActiveAndEnabled)
        {
            return;
        }

        int layer = LayerMask.NameToLayer(hiddenLayerName);
        if (layer < 0)
        {
            Debug.LogWarning("Layer '" + hiddenLayerName + "' not found. Create it in TagManager.", this);
            return;
        }

        visualNodes.Clear();
        CollectVisualNodes(visualsRoot, visualNodes);

        for (int i = 0; i < visualNodes.Count; i++)
        {
            visualNodes[i].gameObject.layer = layer;
        }

        int hiddenMask = 1 << layer;
        firstPersonCamera.cullingMask &= ~hiddenMask;
        applied = true;
    }

    private static void CollectVisualNodes(Transform root, List<Transform> output)
    {
        output.Add(root);

        for (int i = 0; i < root.childCount; i++)
        {
            CollectVisualNodes(root.GetChild(i), output);
        }
    }
}
