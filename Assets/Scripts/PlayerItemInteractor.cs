using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInventory))]
public class PlayerItemInteractor : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField] private string actionMapName = "Player";
    [SerializeField] private string interactActionName = "Interact";
    [SerializeField] private string dropActionName = "Drop";
    [SerializeField] private string nextSlotActionName = "Next";
    [SerializeField] private string previousSlotActionName = "Previous";
    [SerializeField] private Key fallbackInteractKey = Key.E;
    [SerializeField] private Key fallbackDropKey = Key.G;

    [Header("Hotbar")]
    [SerializeField] private bool useNumberHotbarKeys = true;
    [SerializeField, Range(1, 10)] private int numberHotbarSlots = 10;
    [SerializeField, Min(1)] private int pickupHotbarPrioritySlots = 10;

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
    private ItemDefinition heldDefinition;
    private int heldFromSlotIndex = -1;
    private bool hotbarSelectionActive = true;

    private InputAction interactAction;
    private InputAction dropAction;
    private InputAction nextSlotAction;
    private InputAction previousSlotAction;

    private bool interactActionEnabledByThis;
    private bool dropActionEnabledByThis;
    private bool nextSlotActionEnabledByThis;
    private bool previousSlotActionEnabledByThis;

    public bool IsHotbarSelectionActive => hotbarSelectionActive;
    public event System.Action HotbarSelectionStateChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CleanupItemGhostsBeforeSceneLoad()
    {
        EnsureRuntimeCleanup();
    }

    private void Awake()
    {
        EnsureRuntimeCleanup();

        if (inventory == null)
        {
            inventory = GetComponent<PlayerInventory>();
        }

        characterController = GetComponent<CharacterController>();
        playerColliders = GetComponentsInChildren<Collider>(true);

        ResolveViewTransform();
        EnsureHoldPoint();
    }

    private static void EnsureRuntimeCleanup()
    {
        CleanupLegacyRuntimeTemplates();
        CleanupStraySceneItemGhosts();
    }

    private static void CleanupLegacyRuntimeTemplates()
    {
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        if (allObjects == null)
        {
            return;
        }

        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];
            if (go == null)
            {
                continue;
            }

            bool legacyTemplate = go.name.StartsWith("RuntimeItemTemplate_", System.StringComparison.Ordinal)
                || go.name.StartsWith("RuntimeItemVisual_", System.StringComparison.Ordinal);

            bool hiddenWorldItemGhost = go.hideFlags != HideFlags.None && go.GetComponent<WorldItem>() != null;
            if (!legacyTemplate && !hiddenWorldItemGhost)
            {
                continue;
            }

            Object.Destroy(go);
        }
    }

    private static void CleanupStraySceneItemGhosts()
    {
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        if (allObjects == null)
        {
            return;
        }

        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];
            if (go == null)
            {
                continue;
            }

            var scene = go.scene;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                continue;
            }

            bool hiddenInHierarchy = (go.hideFlags & HideFlags.HideInHierarchy) != 0;
            bool dontSave = (go.hideFlags & HideFlags.DontSave) != 0
                || (go.hideFlags & HideFlags.DontSaveInBuild) != 0
                || (go.hideFlags & HideFlags.DontSaveInEditor) != 0;
            bool hiddenGhostFlag = hiddenInHierarchy || dontSave;

            bool legacyTemplate = go.name.StartsWith("RuntimeItemTemplate_", System.StringComparison.Ordinal)
                || go.name.StartsWith("RuntimeItemVisual_", System.StringComparison.Ordinal);

            if (!legacyTemplate && !hiddenGhostFlag)
            {
                continue;
            }

            if (!legacyTemplate && go.transform.parent != null)
            {
                continue;
            }

            if (go.GetComponentInParent<PlayerItemInteractor>(true) != null)
            {
                continue;
            }

            if (go.GetComponentInParent<WorldItem>(true) != null)
            {
                continue;
            }

            Rigidbody rigidbodyComponent = go.GetComponentInChildren<Rigidbody>(true);
            Collider colliderComponent = go.GetComponentInChildren<Collider>(true);
            if (rigidbodyComponent == null || colliderComponent == null)
            {
                continue;
            }

            if (go.hideFlags != HideFlags.None)
            {
                go.hideFlags = HideFlags.None;
            }

            Object.Destroy(go);
        }
    }

    private void OnEnable()
    {
        ResolveInputActions();
    }

    private void OnDisable()
    {
        if (heldItem != null)
        {
            UnequipHeldItemVisual();
        }

        DisableIfOwned(interactAction, interactActionEnabledByThis);
        DisableIfOwned(dropAction, dropActionEnabledByThis);
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
            bool changedSelection = false;

            if (WasPressed(nextSlotAction, Key.None))
            {
                changedSelection = inventory.SelectNext() || changedSelection;
            }

            if (WasPressed(previousSlotAction, Key.None))
            {
                changedSelection = inventory.SelectPrevious() || changedSelection;
            }

            if (changedSelection)
            {
                SetHotbarSelectionActive(true);

                if (heldItem != null)
                {
                    UnequipHeldItemVisual();
                }

                TryEquipSelectedFromHotbar();
            }
        }

        bool interactPressed = WasPressed(interactAction, fallbackInteractKey);
        bool dropPressed = WasPressed(dropAction, fallbackDropKey);

        if (heldItem == null)
        {
            if (interactPressed)
            {
                if (TryCollectFocusedItemToInventory(out bool addedToSelectedHotbar)
                    && addedToSelectedHotbar
                    && hotbarSelectionActive)
                {
                    TryEquipSelectedFromHotbar();
                }
            }

            return;
        }

        if (dropPressed)
        {
            DropHeldItem();
            return;
        }
    }

    private void ResolveInputActions()
    {
        InputActionAsset actionAsset = InputSystem.actions;

        BindAction(actionAsset, interactActionName, ref interactAction, ref interactActionEnabledByThis);
        BindAction(actionAsset, dropActionName, ref dropAction, ref dropActionEnabledByThis);
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

    private bool TryCollectFocusedItemToInventory(out bool addedToSelectedHotbar)
    {
        addedToSelectedHotbar = false;

        if (viewTransform == null || inventory == null)
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

            ItemDefinition definition = worldItem.Definition;
            if (definition == null)
            {
                definition = ItemDefinitionRegistry.GetById(worldItem.ItemId);
            }

            if (definition == null)
            {
                continue;
            }

            int prioritySlots = Mathf.Clamp(pickupHotbarPrioritySlots, 1, inventory.Capacity);
            int selectedIndex = inventory.SelectedSlotIndex;
            int hotbarCount = Mathf.Clamp(numberHotbarSlots, 1, inventory.Capacity);
            bool selectedIsHotbar = hotbarSelectionActive && selectedIndex >= 0 && selectedIndex < hotbarCount;
            int preferredHotbarSlot = selectedIsHotbar ? selectedIndex : -1;

            if (!inventory.TryAddWithHotbarPriority(
                    definition,
                    worldItem.Quantity,
                    prioritySlots,
                    preferredHotbarSlot,
                    out bool addedToPreferredHotbarSlot))
            {
                continue;
            }

            addedToSelectedHotbar = selectedIsHotbar && addedToPreferredHotbarSlot;

            Destroy(worldItem.gameObject);
            return true;
        }

        return false;
    }

    public bool TryEquipSelectedFromHotbar()
    {
        if (inventory == null)
        {
            return false;
        }

        int hotbarCount = Mathf.Clamp(numberHotbarSlots, 1, inventory.Capacity);
        if (inventory.SelectedSlotIndex < 0 || inventory.SelectedSlotIndex >= hotbarCount)
        {
            return false;
        }

        if (!hotbarSelectionActive)
        {
            return false;
        }

        int selectedIndex = inventory.SelectedSlotIndex;
        if (selectedIndex < 0 || selectedIndex >= inventory.Slots.Count)
        {
            return false;
        }

        PlayerInventory.Slot selectedSlot = inventory.Slots[selectedIndex];
        if (selectedSlot.IsEmpty)
        {
            if (heldItem != null)
            {
                UnequipHeldItemVisual();
            }

            return false;
        }

        if (heldItem != null && heldFromSlotIndex == selectedIndex && heldDefinition == selectedSlot.item)
        {
            return true;
        }

        if (heldItem != null)
        {
            UnequipHeldItemVisual();
        }

        return TryTakeSelectedItemToHand(false);
    }

    public bool TrySelectHotbarSlotAndEquip(int slotIndex)
    {
        if (inventory == null)
        {
            return false;
        }

        int hotbarCount = Mathf.Clamp(numberHotbarSlots, 1, inventory.Capacity);
        if (slotIndex < 0 || slotIndex >= hotbarCount)
        {
            return false;
        }

        bool sameSlot = inventory.SelectedSlotIndex == slotIndex;
        if (sameSlot)
        {
            if (hotbarSelectionActive)
            {
                SetHotbarSelectionActive(false);

                if (heldItem != null)
                {
                    UnequipHeldItemVisual();
                }

                return true;
            }

            SetHotbarSelectionActive(true);
            return TryEquipSelectedFromHotbar();
        }

        SetHotbarSelectionActive(true);

        if (inventory.SelectedSlotIndex != slotIndex)
        {
            inventory.SelectSlot(slotIndex);
        }

        if (heldItem != null)
        {
            UnequipHeldItemVisual();
        }

        return TryEquipSelectedFromHotbar();
    }

    public bool TryDropInventorySlotToWorld(int slotIndex, int amount)
    {
        if (inventory == null || amount <= 0)
        {
            return false;
        }

        ResolveViewTransform();
        EnsureHoldPoint();

        if (holdPoint == null)
        {
            return false;
        }

        if (slotIndex < 0 || slotIndex >= inventory.Slots.Count)
        {
            return false;
        }

        PlayerInventory.Slot slot = inventory.Slots[slotIndex];
        if (slot.IsEmpty || slot.item == null)
        {
            return false;
        }

        ItemDefinition itemDefinition = slot.item;
        int dropAmount = Mathf.Clamp(amount, 1, slot.amount);

        GameObject sourcePrefab = itemDefinition.WorldPrefab;
        if (sourcePrefab == null)
        {
            sourcePrefab = FindActiveWorldVisual(itemDefinition);
        }

        if (sourcePrefab == null)
        {
            return false;
        }

        GameObject instance = Instantiate(sourcePrefab, holdPoint.position, holdPoint.rotation);
        if (instance == null)
        {
            return false;
        }

        if (!instance.activeSelf)
        {
            instance.SetActive(true);
        }

        instance.name = string.IsNullOrWhiteSpace(itemDefinition.DisplayName)
            ? itemDefinition.ItemId
            : itemDefinition.DisplayName;

        WorldItem worldItem = instance.GetComponent<WorldItem>();
        if (worldItem == null)
        {
            worldItem = instance.AddComponent<WorldItem>();
        }

        worldItem.Configure(itemDefinition, dropAmount);

        if (!worldItem.TryPickUp(holdPoint))
        {
            Destroy(instance);
            return false;
        }

        if (!inventory.TryTakeFromSlot(slotIndex, dropAmount, itemDefinition, out _))
        {
            Destroy(instance);
            return false;
        }

        BuildThrowKinematics(out Vector3 throwVelocity, out Vector3 throwAngularVelocity);
        worldItem.Drop(throwVelocity, throwAngularVelocity, playerColliders, ignorePlayerCollisionAfterDrop);

        RefreshHeldVisualAfterSlotMutation(slotIndex);
        return true;
    }

    private bool TryTakeSelectedItemToHand(bool allowFallbackSelection = true)
    {
        if (inventory == null || holdPoint == null)
        {
            return false;
        }

        int selectedIndex = inventory.SelectedSlotIndex;
        if (selectedIndex < 0 || selectedIndex >= inventory.Slots.Count)
        {
            return false;
        }

        PlayerInventory.Slot selectedSlot = inventory.Slots[selectedIndex];
        if (selectedSlot.IsEmpty)
        {
            if (!allowFallbackSelection)
            {
                return false;
            }

            if (!inventory.TrySelectFirstNonEmpty())
            {
                return false;
            }

            selectedIndex = inventory.SelectedSlotIndex;
            if (selectedIndex < 0 || selectedIndex >= inventory.Slots.Count)
            {
                return false;
            }

            selectedSlot = inventory.Slots[selectedIndex];
            if (selectedSlot.IsEmpty)
            {
                return false;
            }
        }

        ItemDefinition itemDefinition = selectedSlot.item;
        if (itemDefinition == null)
        {
            return false;
        }

        GameObject sourcePrefab = itemDefinition.WorldPrefab;
        if (sourcePrefab == null)
        {
            sourcePrefab = FindActiveWorldVisual(itemDefinition);
        }

        if (sourcePrefab == null)
        {
            return false;
        }

        if (heldItem != null)
        {
            UnequipHeldItemVisual();
        }

        GameObject instance = Instantiate(sourcePrefab, holdPoint.position, holdPoint.rotation);
        if (instance == null)
        {
            return false;
        }

        if (!instance.activeSelf)
        {
            instance.SetActive(true);
        }

        instance.name = string.IsNullOrWhiteSpace(itemDefinition.DisplayName)
            ? itemDefinition.ItemId
            : itemDefinition.DisplayName;

        WorldItem worldItem = instance.GetComponent<WorldItem>();
        if (worldItem == null)
        {
            worldItem = instance.AddComponent<WorldItem>();
        }

        int heldAmount = Mathf.Max(1, selectedSlot.amount);
        worldItem.Configure(itemDefinition, heldAmount);

        if (!worldItem.TryPickUp(holdPoint))
        {
            Destroy(instance);
            return false;
        }

        heldItem = worldItem;
        heldDefinition = itemDefinition;
        heldFromSlotIndex = selectedIndex;
        return true;
    }

    private void DropHeldItem()
    {
        if (heldItem == null)
        {
            return;
        }

        if (!TryGetHeldDropAmount(out int dropAmount))
        {
            UnequipHeldItemVisual();
            return;
        }

        if (!ConsumeHeldItemFromInventory(dropAmount))
        {
            UnequipHeldItemVisual();
            return;
        }

        heldItem.Configure(heldDefinition, dropAmount);

        BuildThrowKinematics(out Vector3 throwVelocity, out Vector3 throwAngularVelocity);

        heldItem.Drop(throwVelocity, throwAngularVelocity, playerColliders, ignorePlayerCollisionAfterDrop);
        heldItem = null;
        heldDefinition = null;
        heldFromSlotIndex = -1;
    }

    private void BuildThrowKinematics(out Vector3 throwVelocity, out Vector3 throwAngularVelocity)
    {
        throwVelocity = Vector3.zero;
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
        throwAngularVelocity = spinMagnitude > 0f ? Random.onUnitSphere * spinMagnitude : Vector3.zero;
    }

    private void UnequipHeldItemVisual()
    {
        if (heldItem != null)
        {
            Destroy(heldItem.gameObject);
        }

        heldItem = null;
        heldDefinition = null;
        heldFromSlotIndex = -1;
    }

    private bool TryGetHeldDropAmount(out int dropAmount)
    {
        dropAmount = 0;

        if (inventory == null || heldDefinition == null)
        {
            return false;
        }

        if (TryGetStackAmountFromSlot(heldFromSlotIndex, heldDefinition, out dropAmount))
        {
            return true;
        }

        int selectedIndex = inventory.SelectedSlotIndex;
        if (TryGetStackAmountFromSlot(selectedIndex, heldDefinition, out dropAmount))
        {
            heldFromSlotIndex = selectedIndex;
            return true;
        }

        IReadOnlyList<PlayerInventory.Slot> slots = inventory.Slots;
        for (int i = 0; i < slots.Count; i++)
        {
            PlayerInventory.Slot slot = slots[i];
            if (slot.IsEmpty || slot.item != heldDefinition || slot.amount <= 0)
            {
                continue;
            }

            heldFromSlotIndex = i;
            dropAmount = slot.amount;
            return true;
        }

        return false;
    }

    private bool TryGetStackAmountFromSlot(int slotIndex, ItemDefinition expectedItem, out int amount)
    {
        amount = 0;

        if (inventory == null || expectedItem == null)
        {
            return false;
        }

        if (slotIndex < 0 || slotIndex >= inventory.Slots.Count)
        {
            return false;
        }

        PlayerInventory.Slot slot = inventory.Slots[slotIndex];
        if (slot.IsEmpty || slot.item != expectedItem || slot.amount <= 0)
        {
            return false;
        }

        amount = slot.amount;
        return true;
    }

    private bool ConsumeHeldItemFromInventory(int amount)
    {
        if (inventory == null || heldDefinition == null || amount <= 0)
        {
            return false;
        }

        if (heldFromSlotIndex >= 0
            && heldFromSlotIndex < inventory.Capacity
            && inventory.TryTakeFromSlot(heldFromSlotIndex, amount, heldDefinition, out _))
        {
            return true;
        }

        int selectedIndex = inventory.SelectedSlotIndex;
        if (selectedIndex >= 0
            && selectedIndex < inventory.Capacity
            && inventory.TryTakeFromSlot(selectedIndex, amount, heldDefinition, out _))
        {
            heldFromSlotIndex = selectedIndex;
            return true;
        }

        if (inventory.TryTakeFirstMatching(heldDefinition, amount, out int takenFromSlotIndex))
        {
            heldFromSlotIndex = takenFromSlotIndex;
            return true;
        }

        return false;
    }

    private void RefreshHeldVisualAfterSlotMutation(int slotIndex)
    {
        if (heldItem == null || inventory == null)
        {
            return;
        }

        if (slotIndex != heldFromSlotIndex)
        {
            return;
        }

        bool hasMatchingItemInSlot = heldFromSlotIndex >= 0 && heldFromSlotIndex < inventory.Slots.Count;
        if (hasMatchingItemInSlot)
        {
            PlayerInventory.Slot slot = inventory.Slots[heldFromSlotIndex];
            hasMatchingItemInSlot = !slot.IsEmpty && slot.item == heldDefinition;
        }

        if (hasMatchingItemInSlot)
        {
            PlayerInventory.Slot slot = inventory.Slots[heldFromSlotIndex];
            heldItem.Configure(heldDefinition, Mathf.Max(1, slot.amount));
            return;
        }

        UnequipHeldItemVisual();

        if (hotbarSelectionActive)
        {
            TryEquipSelectedFromHotbar();
        }
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

    private static GameObject FindActiveWorldVisual(ItemDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        WorldItem[] worldItems = Object.FindObjectsByType<WorldItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (worldItems == null || worldItems.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < worldItems.Length; i++)
        {
            WorldItem worldItem = worldItems[i];
            if (worldItem == null)
            {
                continue;
            }

            bool definitionMatch = worldItem.Definition == definition;
            bool itemIdMatch = !string.IsNullOrWhiteSpace(definition.ItemId) && worldItem.ItemId == definition.ItemId;
            if (!definitionMatch && !itemIdMatch)
            {
                continue;
            }

            GameObject go = worldItem.gameObject;
            if (go == null || !go.activeInHierarchy)
            {
                continue;
            }

            return go;
        }

        return null;
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

        TrySelectHotbarSlotAndEquip(pressedSlot);
        return true;
    }

    private void SetHotbarSelectionActive(bool isActive)
    {
        if (hotbarSelectionActive == isActive)
        {
            return;
        }

        hotbarSelectionActive = isActive;
        HotbarSelectionStateChanged?.Invoke();
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
