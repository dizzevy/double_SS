using UnityEngine;

[DisallowMultipleComponent]
public class FirstPersonCameraCollision : MonoBehaviour
{
    [SerializeField] private Transform pivot;
    [SerializeField] private Camera collisionCamera;
    [SerializeField] private LayerMask obstacleMask = ~0;
    [SerializeField, Min(0.01f)] private float collisionRadius = 0.12f;
    [SerializeField, Min(0f)] private float wallOffset = 0.02f;
    [SerializeField] private bool includeNearClipPadding = true;
    [SerializeField, Min(0f)] private float smoothing = 20f;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    private Vector3 desiredLocalPosition;
    private Transform actorRoot;

    private void Awake()
    {
        if (pivot == null)
        {
            pivot = transform.parent;
        }

        if (collisionCamera == null)
        {
            collisionCamera = GetComponent<Camera>();
        }

        desiredLocalPosition = transform.localPosition;
        actorRoot = transform.root;
    }

    private void LateUpdate()
    {
        if (pivot == null)
        {
            return;
        }

        Vector3 from = pivot.position;
        Vector3 desiredWorld = pivot.TransformPoint(desiredLocalPosition);
        Vector3 toDesired = desiredWorld - from;
        float desiredDistance = toDesired.magnitude;

        if (desiredDistance <= 0.0001f)
        {
            return;
        }

        Vector3 direction = toDesired / desiredDistance;
        float safeDistance = desiredDistance;

        RaycastHit[] hits = Physics.SphereCastAll(
            from,
            collisionRadius,
            direction,
            desiredDistance,
            obstacleMask,
            triggerInteraction);

        if (hits != null && hits.Length > 0)
        {
            float nearestDistance = float.PositiveInfinity;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hitCollider = hits[i].collider;
                if (hitCollider == null)
                {
                    continue;
                }

                if (actorRoot != null && hitCollider.transform.IsChildOf(actorRoot))
                {
                    continue;
                }

                if (hits[i].distance < nearestDistance)
                {
                    nearestDistance = hits[i].distance;
                }
            }

            if (!float.IsInfinity(nearestDistance))
            {
                safeDistance = Mathf.Max(nearestDistance - wallOffset, 0f);
            }
        }

        float clipPadding = 0f;
        if (includeNearClipPadding && collisionCamera != null)
        {
            clipPadding = collisionCamera.nearClipPlane;
        }

        safeDistance = Mathf.Max(safeDistance - clipPadding, 0f);
        Vector3 targetWorld = from + direction * safeDistance;

        if (smoothing <= 0f)
        {
            transform.position = targetWorld;
            return;
        }

        float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetWorld, t);
    }
}
