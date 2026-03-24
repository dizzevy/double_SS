using UnityEngine;

[DisallowMultipleComponent]
public class PortalIdleMotion : MonoBehaviour
{
    [Header("Motion")]
    [SerializeField] private Vector3 rotationAxis = Vector3.up;
    [SerializeField] private float rotationSpeed = 24f;
    [SerializeField] private float bobAmplitude = 0.08f;
    [SerializeField] private float bobFrequency = 1.1f;
    [SerializeField] private float pulseAmount = 0.06f;
    [SerializeField] private float pulseFrequency = 1.5f;

    [Header("Shader Seed")]
    [SerializeField] private bool randomizeSeedOnStart = true;
    [SerializeField] private float seed;
    [SerializeField] private Renderer[] targetRenderers;

    private static readonly int SeedId = Shader.PropertyToID("_Seed");

    private MaterialPropertyBlock propertyBlock;
    private Vector3 baseLocalPosition;
    private Vector3 baseLocalScale;
    private bool initialized;

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        Initialize();
        ApplySeed();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            CollectRenderersIfNeeded();
            ApplySeed();
        }
    }

    private void Initialize()
    {
        if (initialized)
        {
            return;
        }

        baseLocalPosition = transform.localPosition;
        baseLocalScale = transform.localScale;
        CollectRenderersIfNeeded();

        if (Application.isPlaying && randomizeSeedOnStart && Mathf.Approximately(seed, 0f))
        {
            seed = Random.Range(0.01f, 100f);
        }

        initialized = true;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        float t = Time.time + seed;

        if (rotationAxis.sqrMagnitude > 0.0001f && Mathf.Abs(rotationSpeed) > 0.0001f)
        {
            transform.Rotate(rotationAxis.normalized, rotationSpeed * dt, Space.Self);
        }

        float bobOffset = bobAmplitude * Mathf.Sin(t * bobFrequency);
        transform.localPosition = baseLocalPosition + Vector3.up * bobOffset;

        float pulse = 1f + pulseAmount * Mathf.Sin(t * pulseFrequency);
        transform.localScale = baseLocalScale * pulse;
    }

    private void CollectRenderersIfNeeded()
    {
        if (targetRenderers != null && targetRenderers.Length > 0)
        {
            return;
        }

        targetRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void ApplySeed()
    {
        if (targetRenderers == null)
        {
            return;
        }

        EnsurePropertyBlock();

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer targetRenderer = targetRenderers[i];
            if (targetRenderer == null)
            {
                continue;
            }

            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(SeedId, seed);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    private void EnsurePropertyBlock()
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }
    }
}
