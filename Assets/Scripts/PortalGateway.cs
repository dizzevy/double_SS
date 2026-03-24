using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class PortalGateway : MonoBehaviour
{
    [Header("Destination")]
    [SerializeField] private Transform destination;
    [SerializeField] private Vector3 destinationOffset = new Vector3(0f, 0f, 1.8f);
    [SerializeField] private bool alignToDestinationYaw = true;
    [SerializeField] private bool preserveRelativeYaw = true;

    [Header("Filter")]
    [SerializeField] private LayerMask teleportLayers = ~0;
    [SerializeField] private bool characterControllerOnly = true;
    [SerializeField] private bool requireCharacterControllerOrRigidbody = true;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 0.35f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private static readonly Dictionary<Transform, float> CooldownByActor = new Dictionary<Transform, float>();
    private static readonly List<Transform> CooldownCleanup = new List<Transform>();

    private void Reset()
    {
        EnsurePortalCollider();
        EnsureKinematicBody();
    }

    private void Awake()
    {
        EnsurePortalCollider();
        EnsureKinematicBody();
    }

    private void OnValidate()
    {
        cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
        EnsurePortalCollider();
    }

    private void OnTriggerEnter(Collider other)
    {
        TryTeleport(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryTeleport(other);
    }

    private void TryTeleport(Collider other)
    {
        if (destination == null || other == null)
        {
            return;
        }

        Transform actor = ResolveActorRoot(other);
        if (actor == null || actor == transform || actor.IsChildOf(transform))
        {
            return;
        }

        if (!IsLayerAllowed(actor.gameObject.layer))
        {
            return;
        }

        CharacterController controller = actor.GetComponent<CharacterController>();
        Rigidbody rigidbodyComponent = actor.GetComponent<Rigidbody>();

        if (characterControllerOnly && controller == null)
        {
            return;
        }

        if (requireCharacterControllerOrRigidbody && controller == null && rigidbodyComponent == null)
        {
            return;
        }

        CleanupCooldownEntries();
        if (CooldownByActor.TryGetValue(actor, out float blockedUntil) && Time.time < blockedUntil)
        {
            return;
        }

        TeleportActor(actor, controller, rigidbodyComponent);
        CooldownByActor[actor] = Time.time + cooldownSeconds;
    }

    private void TeleportActor(Transform actor, CharacterController controller, Rigidbody rigidbodyComponent)
    {
        Vector3 targetPosition = destination.TransformPoint(destinationOffset);

        Quaternion targetRotation = actor.rotation;
        Quaternion velocityRotation = Quaternion.identity;

        if (alignToDestinationYaw)
        {
            float sourceYaw = transform.eulerAngles.y;
            float destinationYaw = destination.eulerAngles.y;
            float yawOffset = preserveRelativeYaw
                ? Mathf.DeltaAngle(sourceYaw, destinationYaw)
                : Mathf.DeltaAngle(actor.eulerAngles.y, destinationYaw);

            velocityRotation = Quaternion.AngleAxis(yawOffset, Vector3.up);
            targetRotation = velocityRotation * actor.rotation;
        }

        bool wasControllerEnabled = controller != null && controller.enabled;
        if (wasControllerEnabled)
        {
            controller.enabled = false;
        }

        if (rigidbodyComponent != null)
        {
            Vector3 linearVelocity = rigidbodyComponent.linearVelocity;
            Vector3 angularVelocity = rigidbodyComponent.angularVelocity;

            rigidbodyComponent.position = targetPosition;
            rigidbodyComponent.rotation = targetRotation;
            rigidbodyComponent.linearVelocity = velocityRotation * linearVelocity;
            rigidbodyComponent.angularVelocity = velocityRotation * angularVelocity;
            rigidbodyComponent.WakeUp();
        }
        else
        {
            actor.SetPositionAndRotation(targetPosition, targetRotation);
        }

        Physics.SyncTransforms();

        if (wasControllerEnabled)
        {
            controller.enabled = true;
        }
    }

    private Transform ResolveActorRoot(Collider other)
    {
        if (other.attachedRigidbody != null)
        {
            return other.attachedRigidbody.transform;
        }

        CharacterController controller = other.GetComponentInParent<CharacterController>();
        if (controller != null)
        {
            return controller.transform;
        }

        return other.transform.root;
    }

    private bool IsLayerAllowed(int layer)
    {
        return (teleportLayers.value & (1 << layer)) != 0;
    }

    private void CleanupCooldownEntries()
    {
        if (CooldownByActor.Count == 0)
        {
            return;
        }

        CooldownCleanup.Clear();

        foreach (KeyValuePair<Transform, float> pair in CooldownByActor)
        {
            if (pair.Key == null || pair.Value <= Time.time)
            {
                CooldownCleanup.Add(pair.Key);
            }
        }

        for (int i = 0; i < CooldownCleanup.Count; i++)
        {
            CooldownByActor.Remove(CooldownCleanup[i]);
        }
    }

    private void EnsurePortalCollider()
    {
        Collider portalCollider = GetComponent<Collider>();
        if (portalCollider == null)
        {
            return;
        }

        portalCollider.isTrigger = true;

        SphereCollider sphereCollider = portalCollider as SphereCollider;
        if (sphereCollider != null && sphereCollider.radius < 0.55f)
        {
            sphereCollider.radius = 0.55f;
        }
    }

    private void EnsureKinematicBody()
    {
        Rigidbody rigidbodyComponent = GetComponent<Rigidbody>();
        if (rigidbodyComponent == null)
        {
            rigidbodyComponent = gameObject.AddComponent<Rigidbody>();
        }

        rigidbodyComponent.useGravity = false;
        rigidbodyComponent.isKinematic = true;
        rigidbodyComponent.constraints = RigidbodyConstraints.FreezeAll;
        rigidbodyComponent.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbodyComponent.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos)
        {
            return;
        }

        Gizmos.color = new Color(0.08f, 0.08f, 0.08f, 0.8f);
        Collider portalCollider = GetComponent<Collider>();
        if (portalCollider != null)
        {
            Gizmos.DrawWireCube(portalCollider.bounds.center, portalCollider.bounds.size);
        }

        if (destination == null)
        {
            return;
        }

        Vector3 exitPoint = destination.TransformPoint(destinationOffset);
        Gizmos.color = new Color(0f, 0f, 0f, 0.85f);
        Gizmos.DrawLine(transform.position, exitPoint);
        Gizmos.DrawWireSphere(exitPoint, 0.2f);
    }
}
