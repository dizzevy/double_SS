using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class WorldItem : MonoBehaviour
{
    [Header("Item")]
    [SerializeField] private ItemDefinition definition;
    [SerializeField] private string itemIdOverride;
    [SerializeField] private string displayNameOverride;
    [SerializeField, Min(1)] private int quantity = 1;

    [Header("Hold Pose")]
    [SerializeField] private bool overrideHoldPose;
    [SerializeField] private Vector3 holdLocalPosition;
    [SerializeField] private Vector3 holdLocalEulerAngles;

    [Header("Per-Item Hold Tuning")]
    [SerializeField] private Vector3 holdLocalPositionOffset;
    [SerializeField] private Vector3 holdLocalEulerOffset;

    [Header("Physics")]
    [SerializeField] private bool autoCreateRigidbody = true;
    [SerializeField] private string colliderProxyName = "ColliderProxy";
    [SerializeField] private bool forceConvexMeshColliders = true;
    [SerializeField] private bool disableCollidersWhileHeld = true;
    [SerializeField] private bool includeInactiveGeometry;
    [SerializeField, Min(0.5f)] private float autoBoundsMergeDistanceFactor = 3f;
    [SerializeField, Min(1f)] private float autoBoundsOutlierVolumeFactor = 12f;
    [SerializeField, Min(0f)] private float fallbackLinearDamping = 1.8f;
    [SerializeField, Min(0f)] private float fallbackAngularDamping = 2.4f;
    [SerializeField, Min(0.5f)] private float maxThrowSpeed = 10f;
    [SerializeField, Min(0.5f)] private float maxThrowAngularSpeed = 20f;
    [SerializeField, Min(1f)] private float maxColliderToVisualVolumeFactor = 20f;
    [SerializeField, Min(1f)] private float maxColliderToVisualExtentFactor = 4f;

    [Header("Settling")]
    [SerializeField] private bool autoSleepWhenStill = true;
    [SerializeField, Min(0f)] private float settleLinearSpeed = 0.05f;
    [SerializeField, Min(0f)] private float settleAngularSpeed = 1f;
    [SerializeField, Min(0f)] private float settleDelay = 0.35f;
    [SerializeField, Min(0f)] private float pickupCooldownAfterDrop = 0.12f;
    [SerializeField, Min(0f)] private float dropSurfaceLift = 0.03f;

    private Rigidbody body;
    private Collider[] worldColliders;
    private Transform colliderProxy;
    private Transform holdAnchor;
    private RigidbodyConstraints cachedConstraints;
    private bool isHeld;
    private float pickupAllowedAt;
    private float settleTimer;
    private Coroutine collisionIgnoreRoutine;
    private bool canUseDynamicRigidbody = true;

    public ItemDefinition Definition => definition;
    public int Quantity => Mathf.Max(1, quantity);
    public bool IsHeld => isHeld;
    public bool CanPickUp => !isHeld && Time.time >= pickupAllowedAt;

    public string ItemId
    {
        get
        {
            if (definition != null)
            {
                return definition.ItemId;
            }

            if (!string.IsNullOrWhiteSpace(itemIdOverride))
            {
                return ItemDefinition.SanitizeId(itemIdOverride);
            }

            return ItemDefinition.SanitizeId(name);
        }
    }

    public string DisplayName
    {
        get
        {
            if (definition != null)
            {
                return definition.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(displayNameOverride))
            {
                return displayNameOverride;
            }

            return name;
        }
    }

    public Vector3 HoldLocalPosition => ResolveHoldLocalPosition();
    public Vector3 HoldLocalEulerAngles => ResolveHoldLocalRotation().eulerAngles;

    private void Awake()
    {
        quantity = Mathf.Max(1, quantity);
        ResolveDefinition();
        EnsurePhysicsComponents();
        EnsureColliderSetup();
        CacheColliders();
        ApplyDampingDefaults();
    }

    private void OnValidate()
    {
        quantity = Mathf.Max(1, quantity);
        fallbackLinearDamping = Mathf.Max(0f, fallbackLinearDamping);
        fallbackAngularDamping = Mathf.Max(0f, fallbackAngularDamping);
        settleLinearSpeed = Mathf.Max(0f, settleLinearSpeed);
        settleAngularSpeed = Mathf.Max(0f, settleAngularSpeed);
        settleDelay = Mathf.Max(0f, settleDelay);
        pickupCooldownAfterDrop = Mathf.Max(0f, pickupCooldownAfterDrop);
        dropSurfaceLift = Mathf.Max(0f, dropSurfaceLift);
        autoBoundsMergeDistanceFactor = Mathf.Max(0.5f, autoBoundsMergeDistanceFactor);
        autoBoundsOutlierVolumeFactor = Mathf.Max(1f, autoBoundsOutlierVolumeFactor);
        maxThrowSpeed = Mathf.Max(0.5f, maxThrowSpeed);
        maxThrowAngularSpeed = Mathf.Max(0.5f, maxThrowAngularSpeed);
        maxColliderToVisualVolumeFactor = Mathf.Max(1f, maxColliderToVisualVolumeFactor);
        maxColliderToVisualExtentFactor = Mathf.Max(1f, maxColliderToVisualExtentFactor);

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EnsurePhysicsComponents();
            EnsureColliderSetup();
            CacheColliders();
        }
#endif
    }

    private void FixedUpdate()
    {
        if (!autoSleepWhenStill || isHeld || body == null || body.isKinematic)
        {
            return;
        }

        if (body.IsSleeping())
        {
            settleTimer = 0f;
            return;
        }

        bool nearStill = body.linearVelocity.sqrMagnitude <= settleLinearSpeed * settleLinearSpeed
            && body.angularVelocity.sqrMagnitude <= settleAngularSpeed * settleAngularSpeed;

        if (!nearStill)
        {
            settleTimer = 0f;
            return;
        }

        settleTimer += Time.fixedDeltaTime;
        if (settleTimer < settleDelay)
        {
            return;
        }

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.Sleep();
        settleTimer = 0f;
    }

    private void LateUpdate()
    {
        if (!isHeld)
        {
            return;
        }

        SnapToHoldPose();
    }

    public bool TryPickUp(Transform holdParent)
    {
        if (!CanPickUp || holdParent == null)
        {
            return false;
        }

        ResolveDefinition();
        EnsurePhysicsComponents();
        EnsureColliderSetup();
        CacheColliders();

        if (collisionIgnoreRoutine != null)
        {
            StopCoroutine(collisionIgnoreRoutine);
            collisionIgnoreRoutine = null;
        }

        if (body != null)
        {
            cachedConstraints = body.constraints;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
            body.detectCollisions = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.constraints = RigidbodyConstraints.FreezeAll;
        }

        if (disableCollidersWhileHeld)
        {
            SetCollidersEnabled(false);
        }

        holdAnchor = holdParent;
        transform.SetParent(holdAnchor, false);
        SnapToHoldPose();

        isHeld = true;
        settleTimer = 0f;
        return true;
    }

    public void Drop(Vector3 throwVelocity, Vector3 throwAngularVelocity, Collider[] playerColliders, float ignoreCollisionSeconds)
    {
        if (!isHeld)
        {
            return;
        }

        SnapToHoldPose();
        transform.SetParent(null, true);

        EnsureColliderSetup();

        if (disableCollidersWhileHeld)
        {
            SetCollidersEnabled(true);
        }

        CacheColliders();

        float safeLift = Mathf.Max(dropSurfaceLift, ComputeSafeDropLift());
        if (safeLift > 0f)
        {
            transform.position += Vector3.up * safeLift;
        }

        Physics.SyncTransforms();

        if (body != null)
        {
            body.constraints = cachedConstraints;
            body.detectCollisions = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            ApplyDampingDefaults();

            if (canUseDynamicRigidbody)
            {
                body.isKinematic = false;
                body.useGravity = true;
                body.linearVelocity = Vector3.ClampMagnitude(throwVelocity, maxThrowSpeed);
                body.angularVelocity = Vector3.ClampMagnitude(throwAngularVelocity, maxThrowAngularSpeed);
            }
            else
            {
                body.isKinematic = true;
                body.useGravity = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                TrySnapKinematicDropToGround();
            }

            body.WakeUp();
        }

        isHeld = false;
        holdAnchor = null;
        pickupAllowedAt = Time.time + pickupCooldownAfterDrop;

        if (ignoreCollisionSeconds > 0f && playerColliders != null && playerColliders.Length > 0)
        {
            if (collisionIgnoreRoutine != null)
            {
                StopCoroutine(collisionIgnoreRoutine);
            }

            collisionIgnoreRoutine = StartCoroutine(IgnoreCollisionsForSeconds(playerColliders, ignoreCollisionSeconds));
        }
    }

    public bool TryStoreToInventory(PlayerInventory inventory, int preferredSlotIndex = -1)
    {
        if (inventory == null || definition == null)
        {
            return false;
        }

        bool added = preferredSlotIndex >= 0
            ? inventory.TryAddWithPreferredSlot(definition, Quantity, preferredSlotIndex)
            : inventory.TryAdd(definition, Quantity);

        if (!added)
        {
            return false;
        }

        Destroy(gameObject);
        return true;
    }

    public void Configure(ItemDefinition newDefinition, int newQuantity)
    {
        definition = newDefinition;
        quantity = Mathf.Max(1, newQuantity);

        if (definition != null)
        {
            itemIdOverride = definition.ItemId;
        }

        ApplyDampingDefaults();
    }

    public void SetItemId(string itemId)
    {
        itemIdOverride = ItemDefinition.SanitizeId(itemId);

        if (definition == null)
        {
            definition = ItemDefinitionRegistry.GetById(itemIdOverride);
        }
    }

    private void ResolveDefinition()
    {
        if (definition != null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(itemIdOverride))
        {
            itemIdOverride = ItemDefinition.SanitizeId(name);
        }

        definition = ItemDefinitionRegistry.GetById(itemIdOverride);
    }

    private void EnsurePhysicsComponents()
    {
        body = GetComponent<Rigidbody>();

        if (body == null && autoCreateRigidbody)
        {
            body = gameObject.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    private void EnsureColliderSetup()
    {
        ResolveProxyReferenceIfMissing();
        SanitizeMeshColliders();
        DisableLegacyProxyObjects();
        SetProxyActive(false);
        EnableNonProxyColliders();
        DisableOutlierCollidersByVisualBounds();
        UpdateDynamicPhysicsSupportFlag();
        ApplyBodySafetyMode();
    }

    private void EnsureFallbackColliderIfNone()
    {
        // Intentionally empty: in strict mode we never create colliders automatically.
    }

    private void DisableOutlierCollidersByVisualBounds()
    {
        if (!TryGetVisualBounds(out Bounds visualBounds))
        {
            return;
        }

        float visualVolume = Mathf.Max(EstimateVolume(visualBounds.size), 0.000001f);
        float visualExtent = Mathf.Max(visualBounds.extents.magnitude, 0.01f);

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        int validColliderCount = 0;
        int outlierCount = 0;
        Collider smallestOutlier = null;
        float smallestOutlierVolume = float.PositiveInfinity;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider colliderComponent = colliders[i];
            if (colliderComponent == null || !colliderComponent.enabled || colliderComponent.isTrigger || IsProxyCollider(colliderComponent))
            {
                continue;
            }

            validColliderCount++;

            Bounds colliderBounds = colliderComponent.bounds;
            float colliderVolume = EstimateVolume(colliderBounds.size);
            float colliderExtent = colliderBounds.extents.magnitude;

            bool volumeOutlier = colliderVolume > visualVolume * maxColliderToVisualVolumeFactor;
            bool extentOutlier = colliderExtent > visualExtent * maxColliderToVisualExtentFactor;

            if (volumeOutlier || extentOutlier)
            {
                colliderComponent.enabled = false;
                outlierCount++;

                if (colliderVolume < smallestOutlierVolume)
                {
                    smallestOutlierVolume = colliderVolume;
                    smallestOutlier = colliderComponent;
                }
            }
        }

        if (validColliderCount > 0 && outlierCount >= validColliderCount && smallestOutlier != null)
        {
            smallestOutlier.enabled = true;
        }
    }

    private bool TryGetVisualBounds(out Bounds bounds)
    {
        bounds = default;
        Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactiveGeometry);
        bool hasBounds = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer rendererComponent = renderers[i];
            if (rendererComponent == null)
            {
                continue;
            }

            if (!includeInactiveGeometry && (!rendererComponent.enabled || !rendererComponent.gameObject.activeInHierarchy))
            {
                continue;
            }

            if (colliderProxy != null && rendererComponent.transform.IsChildOf(colliderProxy))
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = rendererComponent.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(rendererComponent.bounds);
            }
        }

        return hasBounds;
    }

    private bool HasAnyPrimitiveCollider()
    {
        ResolveProxyReferenceIfMissing();

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider colliderComponent = colliders[i];
            if (colliderComponent == null || !colliderComponent.enabled || colliderComponent.isTrigger || IsProxyCollider(colliderComponent))
            {
                continue;
            }

            if (colliderComponent is BoxCollider || colliderComponent is SphereCollider || colliderComponent is CapsuleCollider)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasAnyMeshCollider()
    {
        ResolveProxyReferenceIfMissing();

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider colliderComponent = colliders[i];
            if (colliderComponent == null || !colliderComponent.enabled || colliderComponent.isTrigger || IsProxyCollider(colliderComponent))
            {
                continue;
            }

            if (colliderComponent is MeshCollider)
            {
                return true;
            }
        }

        return false;
    }

    private void SanitizeMeshColliders()
    {
        MeshCollider[] meshColliders = GetComponentsInChildren<MeshCollider>(true);
        for (int i = 0; i < meshColliders.Length; i++)
        {
            MeshCollider meshCollider = meshColliders[i];
            if (meshCollider == null || IsProxyCollider(meshCollider))
            {
                continue;
            }

            if (meshCollider.sharedMesh == null)
            {
                meshCollider.enabled = false;
                continue;
            }

            if (!meshCollider.convex && forceConvexMeshColliders)
            {
                TrySetMeshColliderConvex(meshCollider);
            }
        }
    }

    private static void TrySetMeshColliderConvex(MeshCollider meshCollider)
    {
        if (meshCollider == null)
        {
            return;
        }

        try
        {
            meshCollider.convex = true;
        }
        catch
        {
            meshCollider.convex = false;
        }
    }

    private bool HasAnyNonProxyCollider()
    {
        ResolveProxyReferenceIfMissing();

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider colliderComponent = colliders[i];
            if (colliderComponent == null || !colliderComponent.enabled || colliderComponent.isTrigger || IsProxyCollider(colliderComponent))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void EnsureColliderProxy()
    {
        string safeName = string.IsNullOrWhiteSpace(colliderProxyName) ? "ColliderProxy" : colliderProxyName;
        Transform proxy = transform.Find(safeName);

        if (proxy == null)
        {
            GameObject proxyObject = new GameObject(safeName);
            proxy = proxyObject.transform;
            proxy.SetParent(transform, false);
        }

        colliderProxy = proxy;
        colliderProxy.gameObject.SetActive(true);
        colliderProxy.localPosition = Vector3.zero;
        colliderProxy.localRotation = Quaternion.identity;
        colliderProxy.localScale = Vector3.one;

        BoxCollider proxyCollider = colliderProxy.GetComponent<BoxCollider>();
        if (proxyCollider == null)
        {
            proxyCollider = colliderProxy.gameObject.AddComponent<BoxCollider>();
        }

        if (!TryCalculateGeometryBounds(out Bounds localBounds))
        {
            proxyCollider.center = Vector3.zero;
            proxyCollider.size = Vector3.one * 0.4f;
            return;
        }

        proxyCollider.center = localBounds.center;
        proxyCollider.size = new Vector3(
            Mathf.Max(0.05f, localBounds.size.x),
            Mathf.Max(0.05f, localBounds.size.y),
            Mathf.Max(0.05f, localBounds.size.z));
    }

    private bool TryCalculateGeometryBounds(out Bounds rootSpaceBounds)
    {
        Matrix4x4 worldToRoot = transform.worldToLocalMatrix;
        rootSpaceBounds = new Bounds(Vector3.zero, Vector3.zero);
        List<Bounds> candidates = new List<Bounds>(8);

        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(includeInactiveGeometry);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                continue;
            }

            MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                continue;
            }

            if (!includeInactiveGeometry && (!meshRenderer.enabled || !meshRenderer.gameObject.activeInHierarchy))
            {
                continue;
            }

            if (colliderProxy != null && meshFilter.transform.IsChildOf(colliderProxy))
            {
                continue;
            }

            Matrix4x4 meshToRoot = worldToRoot * meshFilter.transform.localToWorldMatrix;
            if (TryBuildTransformedBounds(meshFilter.sharedMesh.bounds, meshToRoot, out Bounds transformed))
            {
                candidates.Add(transformed);
            }
        }

        SkinnedMeshRenderer[] skinned = GetComponentsInChildren<SkinnedMeshRenderer>(includeInactiveGeometry);
        for (int i = 0; i < skinned.Length; i++)
        {
            SkinnedMeshRenderer skinnedRenderer = skinned[i];
            if (skinnedRenderer == null)
            {
                continue;
            }

            if (!includeInactiveGeometry && (!skinnedRenderer.enabled || !skinnedRenderer.gameObject.activeInHierarchy))
            {
                continue;
            }

            if (colliderProxy != null && skinnedRenderer.transform.IsChildOf(colliderProxy))
            {
                continue;
            }

            Matrix4x4 meshToRoot = worldToRoot * skinnedRenderer.transform.localToWorldMatrix;
            if (TryBuildTransformedBounds(skinnedRenderer.localBounds, meshToRoot, out Bounds transformed))
            {
                candidates.Add(transformed);
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        int primaryIndex = 0;
        float primaryScore = float.PositiveInfinity;

        for (int i = 0; i < candidates.Count; i++)
        {
            Bounds candidate = candidates[i];
            float centerDistance = candidate.center.magnitude;
            float sizeMagnitude = candidate.size.magnitude;
            float score = centerDistance + sizeMagnitude * 0.2f;

            if (score >= primaryScore)
            {
                continue;
            }

            primaryScore = score;
            primaryIndex = i;
        }

        Bounds primary = candidates[primaryIndex];
        rootSpaceBounds = primary;

        float primaryVolume = EstimateVolume(primary.size);
        float primaryRadius = Mathf.Max(primary.extents.magnitude, 0.01f);
        float mergeDistance = primaryRadius * autoBoundsMergeDistanceFactor;

        for (int i = 0; i < candidates.Count; i++)
        {
            if (i == primaryIndex)
            {
                continue;
            }

            Bounds candidate = candidates[i];
            float centerDistance = Vector3.Distance(candidate.center, primary.center);
            if (centerDistance > mergeDistance)
            {
                continue;
            }

            float candidateVolume = EstimateVolume(candidate.size);
            if (primaryVolume > 0.000001f && candidateVolume > primaryVolume * autoBoundsOutlierVolumeFactor)
            {
                continue;
            }

            rootSpaceBounds.Encapsulate(candidate.min);
            rootSpaceBounds.Encapsulate(candidate.max);
        }

        return true;
    }

    private static bool TryBuildTransformedBounds(Bounds source, Matrix4x4 matrix, out Bounds result)
    {
        result = new Bounds(Vector3.zero, Vector3.zero);
        Vector3 extents = source.extents;
        Vector3 center = source.center;
        bool hasBounds = false;

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    Vector3 transformed = matrix.MultiplyPoint3x4(point);

                    if (!hasBounds)
                    {
                        result = new Bounds(transformed, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        result.Encapsulate(transformed);
                    }
                }
            }
        }

        return hasBounds;
    }

    private static float EstimateVolume(Vector3 size)
    {
        return Mathf.Abs(size.x * size.y * size.z);
    }

    private void DisableNonProxyColliders()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider colliderComponent = colliders[i];
            if (colliderComponent == null)
            {
                continue;
            }

            bool isProxyCollider = colliderProxy != null && colliderComponent.transform.IsChildOf(colliderProxy);
            colliderComponent.enabled = isProxyCollider;
        }
    }

    private void EnableNonProxyColliders()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider colliderComponent = colliders[i];
            if (colliderComponent == null)
            {
                continue;
            }

            bool isProxyCollider = colliderProxy != null && colliderComponent.transform.IsChildOf(colliderProxy);
            if (!isProxyCollider)
            {
                colliderComponent.enabled = true;
            }
        }
    }

    private void SetProxyActive(bool active)
    {
        ResolveProxyReferenceIfMissing();

        if (colliderProxy == null)
        {
            return;
        }

        if (colliderProxy.gameObject.activeSelf == active)
        {
            return;
        }

        colliderProxy.gameObject.SetActive(active);
    }

    private void CacheColliders()
    {
        ResolveProxyReferenceIfMissing();

        Collider[] allColliders = GetComponentsInChildren<Collider>(true);
        List<Collider> filtered = new List<Collider>(allColliders.Length);

        for (int i = 0; i < allColliders.Length; i++)
        {
            Collider colliderComponent = allColliders[i];
            if (colliderComponent == null || !colliderComponent.enabled || colliderComponent.isTrigger)
            {
                continue;
            }

            if (IsProxyCollider(colliderComponent))
            {
                continue;
            }

            filtered.Add(colliderComponent);
        }

        worldColliders = filtered.ToArray();
        UpdateDynamicPhysicsSupportFlag();
    }

    private void UpdateDynamicPhysicsSupportFlag()
    {
        bool hasPhysicalCollider = false;
        bool hasDynamicSafeCollider = false;
        bool hasConcaveMeshCollider = false;

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider colliderComponent = colliders[i];
            if (colliderComponent == null || !colliderComponent.enabled || colliderComponent.isTrigger || IsProxyCollider(colliderComponent))
            {
                continue;
            }

            hasPhysicalCollider = true;

            if (colliderComponent is MeshCollider meshCollider)
            {
                if (meshCollider.sharedMesh == null)
                {
                    continue;
                }

                if (meshCollider.convex)
                {
                    hasDynamicSafeCollider = true;
                }
                else
                {
                    hasConcaveMeshCollider = true;
                }

                continue;
            }

            hasDynamicSafeCollider = true;
        }

        canUseDynamicRigidbody = hasPhysicalCollider && hasDynamicSafeCollider && !hasConcaveMeshCollider;
    }

    private void ApplyBodySafetyMode()
    {
        if (body == null)
        {
            return;
        }

        if (isHeld)
        {
            return;
        }

        if (canUseDynamicRigidbody)
        {
            body.isKinematic = false;
            body.useGravity = true;
            body.detectCollisions = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            if (body.interpolation == RigidbodyInterpolation.None)
            {
                body.interpolation = RigidbodyInterpolation.Interpolate;
            }

            body.WakeUp();
            return;
        }

        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    private void ApplyDampingDefaults()
    {
        if (body == null)
        {
            return;
        }

        if (definition != null)
        {
            body.linearDamping = definition.LinearDamping;
            body.angularDamping = definition.AngularDamping;
            return;
        }

        body.linearDamping = fallbackLinearDamping;
        body.angularDamping = fallbackAngularDamping;
    }

    private IEnumerator IgnoreCollisionsForSeconds(Collider[] otherColliders, float duration)
    {
        SetCollisionIgnore(otherColliders, true);
        yield return new WaitForSeconds(duration);
        SetCollisionIgnore(otherColliders, false);
        collisionIgnoreRoutine = null;
    }

    private void SetCollisionIgnore(Collider[] otherColliders, bool ignored)
    {
        if (worldColliders == null || otherColliders == null)
        {
            return;
        }

        for (int i = 0; i < worldColliders.Length; i++)
        {
            Collider own = worldColliders[i];
            if (!IsValidSceneCollider(own))
            {
                continue;
            }

            for (int j = 0; j < otherColliders.Length; j++)
            {
                Collider other = otherColliders[j];
                if (!IsValidSceneCollider(other) || other == own)
                {
                    continue;
                }

                Physics.IgnoreCollision(own, other, ignored);
            }
        }
    }

    private static bool IsValidSceneCollider(Collider colliderComponent)
    {
        if (colliderComponent == null)
        {
            return false;
        }

        if (!colliderComponent.enabled)
        {
            return false;
        }

        GameObject go = colliderComponent.gameObject;
        if (go == null)
        {
            return false;
        }

        if (!go.activeInHierarchy)
        {
            return false;
        }

        Scene scene = go.scene;
        return scene.IsValid() && scene.isLoaded;
    }

    private void SetCollidersEnabled(bool enabled)
    {
        if (worldColliders == null)
        {
            return;
        }

        for (int i = 0; i < worldColliders.Length; i++)
        {
            Collider colliderComponent = worldColliders[i];
            if (colliderComponent == null)
            {
                continue;
            }

            colliderComponent.enabled = enabled;
        }
    }

    private float ComputeSafeDropLift()
    {
        if (worldColliders == null || worldColliders.Length == 0)
        {
            return dropSurfaceLift;
        }

        bool hasBounds = false;
        Bounds bounds = default;

        for (int i = 0; i < worldColliders.Length; i++)
        {
            Collider colliderComponent = worldColliders[i];
            if (colliderComponent == null || !colliderComponent.enabled || colliderComponent.isTrigger)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = colliderComponent.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(colliderComponent.bounds);
            }
        }

        if (!hasBounds)
        {
            return dropSurfaceLift;
        }

        float suggestedLift = Mathf.Clamp(bounds.extents.y * 0.2f, 0.03f, 0.25f);
        return Mathf.Max(dropSurfaceLift, suggestedLift);
    }

    private void TrySnapKinematicDropToGround()
    {
        float halfHeight = 0.05f;
        if (worldColliders != null && worldColliders.Length > 0)
        {
            bool hasBounds = false;
            Bounds bounds = default;

            for (int i = 0; i < worldColliders.Length; i++)
            {
                Collider colliderComponent = worldColliders[i];
                if (colliderComponent == null || !colliderComponent.enabled || colliderComponent.isTrigger)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = colliderComponent.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(colliderComponent.bounds);
                }
            }

            if (hasBounds)
            {
                halfHeight = Mathf.Max(halfHeight, bounds.extents.y);
            }
        }

        Vector3 origin = transform.position + Vector3.up * Mathf.Max(0.2f, halfHeight + 0.2f);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null || IsOwnCollider(hitCollider))
            {
                continue;
            }

            float lift = Mathf.Max(dropSurfaceLift, 0.02f);
            transform.position = hits[i].point + Vector3.up * (halfHeight + lift);
            Physics.SyncTransforms();
            return;
        }
    }

    private bool IsOwnCollider(Collider colliderComponent)
    {
        if (colliderComponent == null)
        {
            return false;
        }

        return colliderComponent.transform.IsChildOf(transform);
    }

    private bool IsProxyCollider(Collider colliderComponent)
    {
        ResolveProxyReferenceIfMissing();

        if (colliderComponent == null || colliderProxy == null)
        {
            return false;
        }

        return colliderComponent.transform.IsChildOf(colliderProxy);
    }

    private void ResolveProxyReferenceIfMissing()
    {
        if (colliderProxy != null)
        {
            return;
        }

        string safeName = string.IsNullOrWhiteSpace(colliderProxyName) ? "ColliderProxy" : colliderProxyName;
        Transform existingProxy = transform.Find(safeName);
        if (existingProxy != null)
        {
            colliderProxy = existingProxy;
        }
    }

    private void DisableLegacyProxyObjects()
    {
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || candidate == transform)
            {
                continue;
            }

            bool nameMatches = string.Equals(candidate.name, "ColliderProxy", System.StringComparison.Ordinal)
                || string.Equals(candidate.name, colliderProxyName, System.StringComparison.Ordinal)
                || candidate.name.StartsWith("ColliderProxy", System.StringComparison.Ordinal);

            if (!nameMatches)
            {
                continue;
            }

            if (candidate.GetComponent<Collider>() == null)
            {
                continue;
            }

            if (candidate.GetComponentInChildren<Renderer>(true) != null)
            {
                continue;
            }

            candidate.gameObject.SetActive(false);
        }
    }

    private void SnapToHoldPose()
    {
        if (holdAnchor == null)
        {
            return;
        }

        if (transform.parent != holdAnchor)
        {
            transform.SetParent(holdAnchor, false);
        }

        transform.localPosition = ResolveHoldLocalPosition();
        transform.localRotation = ResolveHoldLocalRotation();
    }

    private Vector3 ResolveHoldLocalPosition()
    {
        Vector3 basePosition = overrideHoldPose || definition == null
            ? holdLocalPosition
            : definition.HoldLocalPosition;

        return basePosition + holdLocalPositionOffset;
    }

    private Quaternion ResolveHoldLocalRotation()
    {
        Vector3 baseEuler = overrideHoldPose || definition == null
            ? holdLocalEulerAngles
            : definition.HoldLocalEulerAngles;

        Quaternion baseRotation = Quaternion.Euler(baseEuler);
        Quaternion offsetRotation = Quaternion.Euler(holdLocalEulerOffset);
        return baseRotation * offsetRotation;
    }
}
