using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInventory))]
[RequireComponent(typeof(PlayerItemInteractor))]
public class PlayerMotor : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference sprintAction;
    [SerializeField] private InputActionReference crouchAction;

    [Header("Speed")]
    [SerializeField] private float walkSpeed = 4.8f;
    [SerializeField] private float sprintSpeed = 7.2f;
    [SerializeField] private float crouchSpeed = 3.0f;

    [Header("Acceleration")]
    [SerializeField] private float groundAcceleration = 28f;
    [SerializeField] private float groundDeceleration = 34f;
    [SerializeField] private float airAcceleration = 8f;

    [Header("Jump/Gravity")]
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = 24f;

    [Header("Crouch")]
    [SerializeField] private float standingHeight = 1.8f;
    [SerializeField] private float crouchHeight = 1.2f;
    [SerializeField] private float crouchLerpSpeed = 14f;

    [Header("View")]
    [SerializeField] private Transform viewTarget;
    [SerializeField] private float standingViewHeight = 1.62f;
    [SerializeField] private float crouchViewHeight = 1.05f;
    [SerializeField] private float viewHeightLerpSpeed = 12f;

    [Header("Slide")]
    [SerializeField] private float slideStartBoost = 1.2f;
    [SerializeField] private float slideDuration = 0.7f;
    [SerializeField] private float slideFriction = 10f;

    [Header("Physics Interaction")]
    [SerializeField] private float bodyPushVelocity = 1.4f;

    private CharacterController characterController;

    private Vector3 horizontalVelocity;
    private float verticalVelocity;

    private bool isCrouching;
    private bool isSliding;
    private float slideTimer;
    private Vector3 slideVelocity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        SetControllerHeight(standingHeight);
        EnsureGameplayComponents();
    }

    private void OnEnable()
    {
        moveAction?.action.Enable();
        jumpAction?.action.Enable();
        sprintAction?.action.Enable();
        crouchAction?.action.Enable();
    }

    private void OnDisable()
    {
        moveAction?.action.Disable();
        jumpAction?.action.Disable();
        sprintAction?.action.Disable();
        crouchAction?.action.Disable();
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        Vector2 moveInput = moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;
        bool jumpPressed = jumpAction != null && jumpAction.action.WasPressedThisFrame();
        bool sprintHeld = sprintAction != null && sprintAction.action.IsPressed();
        bool crouchHeld = crouchAction != null && crouchAction.action.IsPressed();

        bool grounded = characterController.isGrounded;
        if (grounded && verticalVelocity < -2f)
        {
            verticalVelocity = -2f;
        }

        TryStartSlide(grounded, crouchHeld, sprintHeld, moveInput);

        bool wantsCrouch = crouchHeld || isSliding;
        if (!wantsCrouch && !CanStandUp())
        {
            wantsCrouch = true;
        }

        isCrouching = wantsCrouch;

        float targetSpeed = isCrouching ? crouchSpeed : (sprintHeld ? sprintSpeed : walkSpeed);

        Vector3 wishDirection = transform.right * moveInput.x + transform.forward * moveInput.y;
        if (wishDirection.sqrMagnitude > 1f)
        {
            wishDirection.Normalize();
        }

        if (isSliding)
        {
            HandleSlide(wishDirection, dt);
        }
        else
        {
            HandleGroundAirMove(wishDirection, targetSpeed, grounded, dt);

            if (jumpPressed && grounded)
            {
                verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
            }
        }

        verticalVelocity -= gravity * dt;

        Vector3 finalVelocity = horizontalVelocity + Vector3.up * verticalVelocity;
        characterController.Move(finalVelocity * dt);

        UpdateCrouchHeight(dt);
        UpdateViewHeight(dt);
    }

    private void EnsureGameplayComponents()
    {
        if (GetComponent<PlayerInventory>() == null)
        {
            gameObject.AddComponent<PlayerInventory>();
        }

        if (GetComponent<PlayerItemInteractor>() == null)
        {
            gameObject.AddComponent<PlayerItemInteractor>();
        }
    }

    private void TryStartSlide(bool grounded, bool crouchHeld, bool sprintHeld, Vector2 moveInput)
    {
        if (isSliding)
        {
            return;
        }

        if (!grounded || !crouchHeld || !sprintHeld)
        {
            return;
        }

        if (moveInput.y <= 0.2f)
        {
            return;
        }

        if (horizontalVelocity.magnitude <= sprintSpeed * 0.85f)
        {
            return;
        }

        isSliding = true;
        slideTimer = slideDuration;

        Vector3 direction = horizontalVelocity.sqrMagnitude > 0.01f
            ? horizontalVelocity.normalized
            : transform.forward;

        slideVelocity = direction * (sprintSpeed * slideStartBoost);
    }

    private void HandleSlide(Vector3 wishDirection, float dt)
    {
        slideTimer -= dt;
        slideVelocity = Vector3.MoveTowards(slideVelocity, Vector3.zero, slideFriction * dt);

        Vector3 steer = wishDirection * (airAcceleration * 0.5f * dt);
        slideVelocity = Vector3.ClampMagnitude(slideVelocity + steer, sprintSpeed * slideStartBoost);

        horizontalVelocity = slideVelocity;

        if (slideTimer <= 0f || slideVelocity.magnitude < crouchSpeed)
        {
            isSliding = false;
        }
    }

    private void HandleGroundAirMove(Vector3 wishDirection, float targetSpeed, bool grounded, float dt)
    {
        Vector3 desiredVelocity = wishDirection * targetSpeed;

        if (grounded)
        {
            float acceleration = desiredVelocity.sqrMagnitude > 0.01f ? groundAcceleration : groundDeceleration;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, desiredVelocity, acceleration * dt);
            return;
        }

        Vector3 delta = desiredVelocity - horizontalVelocity;
        Vector3 additional = Vector3.ClampMagnitude(delta, airAcceleration * dt);
        horizontalVelocity += additional;
    }

    private void UpdateCrouchHeight(float dt)
    {
        float targetHeight = isCrouching ? crouchHeight : standingHeight;
        float nextHeight = Mathf.Lerp(characterController.height, targetHeight, crouchLerpSpeed * dt);
        SetControllerHeight(nextHeight);
    }

    private void SetControllerHeight(float height)
    {
        float minHeight = characterController.radius * 2f + 0.01f;
        float clampedHeight = Mathf.Max(height, minHeight);

        characterController.height = clampedHeight;
        characterController.center = new Vector3(0f, clampedHeight * 0.5f, 0f);
    }

    private void UpdateViewHeight(float dt)
    {
        if (viewTarget == null)
        {
            return;
        }

        Vector3 localPosition = viewTarget.localPosition;
        float targetY = isCrouching ? crouchViewHeight : standingViewHeight;
        localPosition.y = Mathf.Lerp(localPosition.y, targetY, viewHeightLerpSpeed * dt);
        viewTarget.localPosition = localPosition;
    }

    private bool CanStandUp()
    {
        float radius = Mathf.Max(characterController.radius - 0.01f, 0.01f);
        Vector3 capsuleBottom = transform.position + Vector3.up * radius;
        float topY = Mathf.Max(standingHeight - radius, radius + 0.01f);
        Vector3 capsuleTop = transform.position + Vector3.up * topY;

        int layerMask = ~(1 << gameObject.layer);
        return !Physics.CheckCapsule(capsuleBottom, capsuleTop, radius, layerMask, QueryTriggerInteraction.Ignore);
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (bodyPushVelocity <= 0f)
        {
            return;
        }

        Rigidbody hitBody = hit.collider.attachedRigidbody;
        if (hitBody == null || hitBody.isKinematic)
        {
            return;
        }

        Vector3 pushDirection = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        if (pushDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        hitBody.AddForce(pushDirection.normalized * bodyPushVelocity, ForceMode.VelocityChange);
    }
}
