using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInventory))]
public class PlayerItemInteractor : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField] private string actionMapName = "Player";
    [SerializeField] private string interactActionName = "Interact";
    [SerializeField] private string dropActionName = "Drop";
    [SerializeField] private string storeActionName = "Store";
    [SerializeField] private string nextSlotActionName = "Next";
    [SerializeField] private string previousSlotActionName = "Previous";
    [SerializeField] private Key fallbackInteractKey = Key.E;
    [SerializeField] private Key fallbackDropKey = Key.G;
    [SerializeField] private Key fallbackStoreKey = Key.F;

    [Header("Hotbar")]
    [SerializeField] private bool useNumberHotbarKeys = true;
    [SerializeField, Range(1, 10)] private int numberHotbarSlots = 10;

    [Header("Pickup")]
    [SerializeField] private float pickupDistance = 3f;
    [SerializeField] private LayerMask pickupMask = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Held Item")]
    [SerializeField] private Transform viewTransform;
    [SerializeField] private string holdPointName = "HoldPoint";
    [SerializeField] private Vector3 holdPointLocalPosition = new Vector3(0.35f, -0.2f, 0.55f);
    [SerializeField] private Vector3 holdPointLocalEulerAngles;
    [SerializeField] private float dropForwardSpeed = 3f;
    [SerializeField] private float dropUpSpeed = 1f;
    [SerializeField, Min(0f)] private float throwSpread = 2f;
    [SerializeField] private Vector2 throwSpinRange = new Vector2(4f, 9f);
    [SerializeField] private float ignorePlayerCollisionAfterDrop = 0.2f;

    [Header("Inventory")]
    [SerializeField] private PlayerInventory inventory;

    private CharacterController characterController;
    private Collider[] playerColliders;
    private Transform holdPoint;
    private WorldItem heldItem;

    private InputAction interactAction;
    private InputAction dropAction;
    private InputAction storeAction;
    private InputAction nextSlotAction;
    private InputAction previousSlotAction;

    private bool interactActionEnabledByThis;
    private bool dropActionEnabledByThis;
    private bool storeActionEnabledByThis;
    private bool nextSlotActionEnabledByThis;
    private bool previousSlotActionEnabledByThis;

    private void Awake()
    {
        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        characterController = GetComponent<CharacterController>();
        playerColliders = GetComponentsInChildren<Collider>(true);

        ResolveViewTransform();
        EnsureHoldPoint();
    }

    private void OnEnable()
    {
        ResolveInputActions();
    }

    private void OnDisable()
    {
        if (heldItem != null)
        {
            DropHeldItem();
        }

        DisableIfOwned(interactAction, interactActionEnabledByThis);
        DisableIfOwned(dropAction, dropActionEnabledByThis);
        DisableIfOwned(storeAction, storeActionEnabledByThis);
        DisableIfOwned(nextSlotAction, nextSlotActionEnabledByThis);
        DisableIfOwned(previousSlotAction, previousSlotActionEnabledByThis);
    }

    private void Update()
    {
        if (viewTransform == null || holdPoint == null)
        {
            ResolveViewTransform();
            EnsureHoldPoint();
        }

        if (HandleNumberHotbarInput())
        {
            return;
        }

        if (inventory != null)
        {
            if (WasPressed(nextSlotAction, Key.None))
            {
                inventory.SelectNext();
            }

            if (WasPressed(previousSlotAction, Key.None))
            {
                inventory.SelectPrevious();
            }
        }

        bool interactPressed = WasPressed(interactAction, fallbackInteractKey);
        bool dropPressed = WasPressed(dropAction, fallbackDropKey);
        bool storePressed = WasPressed(storeAction, fallbackStoreKey);

        if (heldItem == null)
        {
            if (!interactPressed)
            {
                return;
            }

            if (TryPickUpFocusedItem())
            {
                return;
            }

            TryTakeSelectedItemToHand();
            return;
        }

        if (dropPressed)
        {
            DropHeldItem();
            return;
        }

        if (storePressed)
        {
            TryStoreHeldItem();
        }
    }

    private void ResolveInputActions()
    {
        InputActionAsset actionAsset = InputSystem.actions;

        BindAction(actionAsset, interactActionName, ref interactAction, ref interactActionEnabledByThis);
        BindAction(actionAsset, dropActionName, ref dropAction, ref dropActionEnabledByThis);
        BindAction(actionAsset, storeActionName, ref storeAction, ref storeActionEnabledByThis);
        BindAction(actionAsset, nextSlotActionName, ref nextSlotAction, ref nextSlotActionEnabledByThis);
        BindAction(actionAsset, previousSlotActionName, ref previousSlotAction, ref previousSlotActionEnabledByThis);
    }

    private void BindAction(InputActionAsset asset, string actionName, ref InputAction action, ref bool enabledByThis)
    {
        enabledByThis = false;
        action = FindAction(asset, actionName);

        if (action == null || action.enabled)
        {
            return;
        }

        action.Enable();
        enabledByThis = true;
    }

    private InputAction FindAction(InputActionAsset asset, string actionName)
    {
        if (asset == null || string.IsNullOrWhiteSpace(actionName))
        {
            return null;
        }

        InputAction byPath = asset.FindAction(actionMapName + "/" + actionName, false);
        if (byPath != null)
        {
            return byPath;
        }

        return asset.FindAction(actionName, false);
    }

    private static void DisableIfOwned(InputAction action, bool enabledByThis)
    {
        if (!enabledByThis || action == null)
        {
            return;
        }

        action.Disable();
    }

    private bool TryPickUpFocusedItem()
    {
        if (viewTransform == null || holdPoint == null)
        {
            return false;
        }

        Ray ray = new Ray(viewTransform.position, viewTransform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, pickupDistance, pickupMask, triggerInteraction);
        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null || hitCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            WorldItem worldItem = hitCollider.GetComponentInParent<WorldItem>();
            if (worldItem == null || !worldItem.CanPickUp)
            {
                continue;
            }

            if (!worldItem.TryPickUp(holdPoint))
            {
                continue;
            }

            heldItem = worldItem;
            return true;
        }

        return false;
    }

    private bool TryStoreHeldItem()
    {
        if (heldItem == null || inventory == null)
        {
            return false;
        }

        if (!heldItem.TryStoreToInventory(inventory, inventory.SelectedSlotIndex))
        {
            return false;
        }

        heldItem = null;
        return true;
    }

    private bool TryTakeSelectedItemToHand(bool allowFallbackSelection = true)
    {
        if (inventory == null || holdPoint == null)
        {
            return false;
        }

        if (!inventory.TryTakeFromSelected(1, out ItemDefinition itemDefinition))
        {
            if (!allowFallbackSelection)
            {
                return false;
            }

            if (!inventory.TrySelectFirstNonEmpty())
            {
                return false;
            }

            if (!inventory.TryTakeFromSelected(1, out itemDefinition))
            {
                return false;
            }
        }

        if (itemDefinition == null || itemDefinition.WorldPrefab == null)
        {
            if (itemDefinition != null)
            {
                inventory.TryAdd(itemDefinition, 1);
            }

            return false;
        }

        GameObject instance = Instantiate(itemDefinition.WorldPrefab, holdPoint.position, holdPoint.rotation);
        WorldItem worldItem = instance.GetComponent<WorldItem>();
        if (worldItem == null)
        {
            worldItem = instance.AddComponent<WorldItem>();
        }

        worldItem.Configure(itemDefinition, 1);

        if (!worldItem.TryPickUp(holdPoint))
        {
            Destroy(instance);
            inventory.TryAdd(itemDefinition, 1);
            return false;
        }

        heldItem = worldItem;
        return true;
    }

    private void DropHeldItem()
    {
        if (heldItem == null)
        {
            return;
        }

        Vector3 throwVelocity = Vector3.zero;
        Vector3 throwDirection = viewTransform != null ? viewTransform.forward : transform.forward;

        if (throwSpread > 0.001f)
        {
            float pitch = Random.Range(-throwSpread, throwSpread);
            float yaw = Random.Range(-throwSpread, throwSpread);
            throwDirection = Quaternion.Euler(pitch, yaw, 0f) * throwDirection;
        }

        if (characterController != null)
        {
            throwVelocity += characterController.velocity;
        }

        throwVelocity += throwDirection * dropForwardSpeed;
        throwVelocity += Vector3.up * dropUpSpeed;

        float spinMin = Mathf.Min(throwSpinRange.x, throwSpinRange.y);
        float spinMax = Mathf.Max(throwSpinRange.x, throwSpinRange.y);
        float spinMagnitude = spinMax > 0f ? Random.Range(spinMin, spinMax) : 0f;
        Vector3 throwAngularVelocity = spinMagnitude > 0f ? Random.onUnitSphere * spinMagnitude : Vector3.zero;

        heldItem.Drop(throwVelocity, throwAngularVelocity, playerColliders, ignorePlayerCollisionAfterDrop);
        heldItem = null;
    }

    private void ResolveViewTransform()
    {
        if (viewTransform != null)
        {
            return;
        }

        Camera playerCamera = GetComponentInChildren<Camera>(true);
        if (playerCamera != null)
        {
            viewTransform = playerCamera.transform;
        }
    }

    private void EnsureHoldPoint()
    {
        if (viewTransform == null)
        {
            return;
        }

        string safeName = string.IsNullOrWhiteSpace(holdPointName) ? "HoldPoint" : holdPointName;
        holdPoint = viewTransform.Find(safeName);

        if (holdPoint == null)
        {
            GameObject newHoldPoint = new GameObject(safeName);
            holdPoint = newHoldPoint.transform;
            holdPoint.SetParent(viewTransform, false);
        }

        holdPoint.localPosition = holdPointLocalPosition;
        holdPoint.localRotation = Quaternion.Euler(holdPointLocalEulerAngles);
    }

    private static bool WasPressed(InputAction action, Key fallbackKey)
    {
        if (action != null)
        {
            return action.WasPressedThisFrame();
        }

        if (fallbackKey == Key.None || Keyboard.current == null)
        {
            return false;
        }

        return Keyboard.current[fallbackKey].wasPressedThisFrame;
    }

    private bool HandleNumberHotbarInput()
    {
        if (!useNumberHotbarKeys || inventory == null)
        {
            return false;
        }

        if (!TryGetPressedNumberSlot(out int pressedSlot))
        {
            return false;
        }

        if (pressedSlot < 0 || pressedSlot >= inventory.Capacity || pressedSlot >= numberHotbarSlots)
        {
            return true;
        }

        bool sameSlot = pressedSlot == inventory.SelectedSlotIndex;

        if (!sameSlot)
        {
            inventory.SelectSlot(pressedSlot);

            if (heldItem != null)
            {
                TryStoreHeldItem();
                return true;
            }

            TryTakeSelectedItemToHand(false);
            return true;
        }

        if (heldItem != null)
        {
            TryStoreHeldItem();
            return true;
        }

        TryTakeSelectedItemToHand(false);
        return true;
    }

    private static bool TryGetPressedNumberSlot(out int slotIndex)
    {
        slotIndex = -1;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        if (keyboard.digit1Key.wasPressedThisFrame) slotIndex = 0;
        else if (keyboard.digit2Key.wasPressedThisFrame) slotIndex = 1;
        else if (keyboard.digit3Key.wasPressedThisFrame) slotIndex = 2;
        else if (keyboard.digit4Key.wasPressedThisFrame) slotIndex = 3;
        else if (keyboard.digit5Key.wasPressedThisFrame) slotIndex = 4;
        else if (keyboard.digit6Key.wasPressedThisFrame) slotIndex = 5;
        else if (keyboard.digit7Key.wasPressedThisFrame) slotIndex = 6;
        else if (keyboard.digit8Key.wasPressedThisFrame) slotIndex = 7;
        else if (keyboard.digit9Key.wasPressedThisFrame) slotIndex = 8;
        else if (keyboard.digit0Key.wasPressedThisFrame) slotIndex = 9;

        return slotIndex >= 0;
    }
}
