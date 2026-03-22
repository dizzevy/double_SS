using UnityEngine;

public class HeadLookTargetFollower : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform headBone;
    [SerializeField] private Transform lookTarget;

    [Header("Mode")]
    [SerializeField] private bool useHumanoidIK = true;

    [Header("Humanoid IK")]
    [SerializeField, Range(0f, 1f)] private float ikBodyWeight;
    [SerializeField, Range(0f, 1f)] private float ikHeadWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float ikEyesWeight;
    [SerializeField, Range(0f, 1f)] private float ikClampWeight = 0.5f;

    [Header("Limits")]
    [SerializeField] private float maxYaw = 60f;
    [SerializeField] private float maxPitchUp = 35f;
    [SerializeField] private float maxPitchDown = 25f;

    [Header("Blend")]
    [SerializeField, Range(0f, 1f)] private float weight = 1f;
    [SerializeField] private float smoothSpeed = 12f;

    [Header("Tuning")]
    [SerializeField] private bool invertPitch;
    [SerializeField] private Vector3 localRotationOffset;

    private Transform headParent;
    private float smoothedWeight;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (headBone == null)
        {
            Transform autoHead = FindHeadBoneFromAnimator();
            if (autoHead != null)
            {
                headBone = autoHead;
            }
        }

        if (headBone != null)
        {
            headParent = headBone.parent;
        }
    }

    private void Update()
    {
        float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
        smoothedWeight = Mathf.Lerp(smoothedWeight, weight, t);
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (!CanUseHumanoidIK() || lookTarget == null)
        {
            return;
        }

        animator.SetLookAtWeight(smoothedWeight, ikBodyWeight, ikHeadWeight, ikEyesWeight, ikClampWeight);
        animator.SetLookAtPosition(lookTarget.position);
    }

    private void LateUpdate()
    {
        if (CanUseHumanoidIK())
        {
            return;
        }

        if (headBone == null || headParent == null || lookTarget == null)
        {
            return;
        }

        Vector3 toTargetWorld = lookTarget.position - headBone.position;
        if (toTargetWorld.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 toTargetLocal = headParent.InverseTransformDirection(toTargetWorld.normalized);

        float yaw = Mathf.Atan2(toTargetLocal.x, toTargetLocal.z) * Mathf.Rad2Deg;
        float pitch = -Mathf.Atan2(toTargetLocal.y, new Vector2(toTargetLocal.x, toTargetLocal.z).magnitude) * Mathf.Rad2Deg;
        if (invertPitch)
        {
            pitch = -pitch;
        }

        yaw = Mathf.Clamp(yaw, -maxYaw, maxYaw);
        pitch = Mathf.Clamp(pitch, -maxPitchUp, maxPitchDown);

        Quaternion animationLocalRotation = headBone.localRotation;
        Quaternion lookOffset = Quaternion.Euler(pitch, yaw, 0f) * Quaternion.Euler(localRotationOffset);
        Quaternion desiredLocalRotation = animationLocalRotation * lookOffset;
        Quaternion blendedRotation = Quaternion.Slerp(animationLocalRotation, desiredLocalRotation, smoothedWeight);

        headBone.localRotation = blendedRotation;
    }

    private bool CanUseHumanoidIK()
    {
        return useHumanoidIK
            && animator != null
            && animator.avatar != null
            && animator.avatar.isValid
            && animator.isHuman;
    }

    private Transform FindHeadBoneFromAnimator()
    {
        if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.isHuman)
        {
            return null;
        }

        return animator.GetBoneTransform(HumanBodyBones.Head);
    }
}
