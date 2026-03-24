using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInventory))]
public class PlayerInventoryUi : MonoBehaviour
{
    private sealed class SlotWidgets
    {
        public int slotIndex;
        public bool isHotbar;
        public Image background;
        public RawImage icon;
        public Text indexText;
        public Text itemText;
        public Text amountText;
    }

    [Header("Hotbar")]
    [SerializeField, Min(1)] private int hotbarSlots = 10;
    [SerializeField, Min(0)] private int additionalInventorySlots = 10;
    [SerializeField] private Vector2 hotbarSlotSize = new Vector2(76f, 76f);
    [SerializeField, Min(0f)] private float hotbarSpacing = 8f;

    [Header("Inventory Window")]
    [SerializeField] private Vector2 inventorySlotSize = new Vector2(92f, 92f);
    [SerializeField, Min(0f)] private float inventorySlotSpacing = 10f;
    [SerializeField, Min(0f)] private float inventoryRowSpacing = 14f;

    [Header("Input")]
    [SerializeField] private bool allowToggle = true;
    [SerializeField] private Key toggleKey = Key.Tab;
    [SerializeField] private Key closeKey = Key.Escape;

    [Header("Palette")]
    [SerializeField] private Color panelColor = new Color(0.08f, 0.095f, 0.125f, 0.95f);
    [SerializeField] private Color dimColor = new Color(0f, 0f, 0f, 0.58f);
    [SerializeField] private Color slotEmptyColor = new Color(0.14f, 0.16f, 0.2f, 0.95f);
    [SerializeField] private Color slotFilledColor = new Color(0.23f, 0.25f, 0.31f, 0.97f);
    [SerializeField] private Color slotSelectedColor = new Color(0.59f, 0.63f, 0.69f, 0.98f);
    [SerializeField] private Color borderColor = new Color(0.32f, 0.37f, 0.46f, 0.8f);
    [SerializeField] private Color accentStripColor = new Color(0.42f, 0.46f, 0.54f, 0.92f);
    [SerializeField] private Color textColor = new Color(0.94f, 0.96f, 1f, 1f);
    [SerializeField] private Color subTextColor = new Color(0.66f, 0.72f, 0.82f, 1f);

    private const int IconSize = 128;
    private const int PreviewLayer = 31;

    private static Font cachedFont;

    private PlayerInventory inventory;
    private PlayerLook playerLook;
    private PlayerMotor playerMotor;
    private PlayerItemInteractor playerInteractor;

    private RectTransform rootRect;
    private RectTransform dimRect;
    private RectTransform windowRect;
    private RectTransform hotbarRect;
    private Text selectedItemText;

    private readonly List<SlotWidgets> hotbarWidgets = new List<SlotWidgets>();
    private readonly List<SlotWidgets> gridWidgets = new List<SlotWidgets>();
    private readonly Dictionary<ItemDefinition, Texture2D> iconCache = new Dictionary<ItemDefinition, Texture2D>();

    private Camera iconCamera;
    private Light iconLight;
    private RenderTexture iconRenderTexture;

    private int dragFromIndex = -1;
    private RectTransform dragGhostRect;
    private RawImage dragGhostIcon;
    private Text dragGhostText;

    private bool inventorySubscribed;
    private bool interactorSubscribed;
    private bool controlsCaptured;
    private bool inventoryOpen;
    private bool dragDroppedOnSlot;
    private bool splitAdjustActive;
    private int splitFromIndex = -1;
    private int splitAmount;
    private int splitHoveredSlot = -1;

    private bool savedLookEnabled;
    private bool savedMotorEnabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToPlayerInventories()
    {
        PlayerInventory[] inventories = Object.FindObjectsByType<PlayerInventory>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (inventories == null || inventories.Length == 0)
        {
            return;
        }

        for (int i = 0; i < inventories.Length; i++)
        {
            PlayerInventory targetInventory = inventories[i];
            if (targetInventory == null || targetInventory.GetComponent<PlayerItemInteractor>() == null)
            {
                continue;
            }

            if (targetInventory.GetComponent<PlayerInventoryUi>() != null)
            {
                continue;
            }

            targetInventory.gameObject.AddComponent<PlayerInventoryUi>();
        }
    }

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        playerLook = GetComponent<PlayerLook>();
        playerMotor = GetComponent<PlayerMotor>();
        playerInteractor = GetComponent<PlayerItemInteractor>();

        if (inventory != null)
        {
            int minimumSlots = Mathf.Max(hotbarSlots + additionalInventorySlots, 20);
            inventory.EnsureMinimumCapacity(minimumSlots);
        }

        BuildUi();
        HookInventoryEvents(true);
        HookInteractorEvents(true);
        RefreshAll();
        SetInventoryOpen(false, true);
    }

    private void OnEnable()
    {
        HookInventoryEvents(true);
        HookInteractorEvents(true);
        RefreshAll();
    }

    private void OnDisable()
    {
        HookInventoryEvents(false);
        HookInteractorEvents(false);
        RestoreControlsIfNeeded();
    }

    private void OnDestroy()
    {
        if (rootRect != null)
        {
            Destroy(rootRect.gameObject);
        }

        if (iconRenderTexture != null)
        {
            iconRenderTexture.Release();
            Destroy(iconRenderTexture);
        }

        if (iconCamera != null)
        {
            Destroy(iconCamera.gameObject);
        }

        if (iconLight != null)
        {
            Destroy(iconLight.gameObject);
        }

        foreach (KeyValuePair<ItemDefinition, Texture2D> entry in iconCache)
        {
            if (entry.Value != null)
            {
                Destroy(entry.Value);
            }
        }

        iconCache.Clear();
    }

    private void Update()
    {
        UpdateSplitAdjustMode();

        if (!allowToggle)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (toggleKey != Key.None && keyboard[toggleKey].wasPressedThisFrame)
        {
            SetInventoryOpen(!inventoryOpen);
            return;
        }

        if (inventoryOpen && closeKey != Key.None && keyboard[closeKey].wasPressedThisFrame)
        {
            SetInventoryOpen(false);
        }
    }

    private void HookInventoryEvents(bool subscribe)
    {
        if (inventory == null)
        {
            return;
        }

        if (subscribe)
        {
            if (inventorySubscribed)
            {
                return;
            }

            inventory.InventoryChanged += RefreshAll;
            inventory.SelectedSlotChanged += OnSelectedSlotChanged;
            inventorySubscribed = true;
            return;
        }

        if (!inventorySubscribed)
        {
            return;
        }

        inventory.InventoryChanged -= RefreshAll;
        inventory.SelectedSlotChanged -= OnSelectedSlotChanged;
        inventorySubscribed = false;
    }

    private void HookInteractorEvents(bool subscribe)
    {
        if (playerInteractor == null)
        {
            return;
        }

        if (subscribe)
        {
            if (interactorSubscribed)
            {
                return;
            }

            playerInteractor.HotbarSelectionStateChanged += RefreshAll;
            interactorSubscribed = true;
            return;
        }

        if (!interactorSubscribed)
        {
            return;
        }

        playerInteractor.HotbarSelectionStateChanged -= RefreshAll;
        interactorSubscribed = false;
    }

    private void OnSelectedSlotChanged(int _, PlayerInventory.Slot __)
    {
        RefreshAll();
    }

    private void BuildUi()
    {
        if (rootRect != null)
        {
            return;
        }

        EnsureEventSystem();

        GameObject canvasObject = new GameObject("InventoryUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        rootRect = canvasObject.GetComponent<RectTransform>();

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = false;
        canvas.sortingOrder = 350;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildWindow();
        BuildHotbar();
        BuildDragGhost();
    }

    private void BuildWindow()
    {
        dimRect = CreateRect("Dim", rootRect);
        StretchFull(dimRect);

        Image dimImage = dimRect.gameObject.AddComponent<Image>();
        dimImage.color = dimColor;
        dimRect.gameObject.SetActive(false);

        RectTransform shadowRect = CreateRect("WindowShadow", dimRect);
        shadowRect.anchorMin = new Vector2(0.5f, 0.5f);
        shadowRect.anchorMax = new Vector2(0.5f, 0.5f);
        shadowRect.pivot = new Vector2(0.5f, 0.5f);
        shadowRect.anchoredPosition = new Vector2(0f, -6f);

        Image shadowImage = shadowRect.gameObject.AddComponent<Image>();
        shadowImage.color = new Color(0f, 0f, 0f, 0.38f);

        windowRect = CreateRect("InventoryWindow", dimRect);
        windowRect.anchorMin = new Vector2(0.5f, 0.5f);
        windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.pivot = new Vector2(0.5f, 0.5f);
        windowRect.anchoredPosition = Vector2.zero;

        int slotCount = inventory != null ? inventory.Capacity : 20;
        int firstRowCount = Mathf.Clamp(Mathf.Min(hotbarSlots, slotCount), 0, slotCount);
        int secondRowCount = Mathf.Max(0, slotCount - firstRowCount);

        float firstRowWidth = ComputeRowWidth(firstRowCount, inventorySlotSize.x, inventorySlotSpacing);
        float secondRowWidth = ComputeRowWidth(secondRowCount, inventorySlotSize.x, inventorySlotSpacing);
        float rowsWidth = Mathf.Max(520f, firstRowWidth, secondRowWidth);
        float rowsHeight = inventorySlotSize.y + (secondRowCount > 0 ? inventorySlotSize.y + inventoryRowSpacing : 0f);

        float width = rowsWidth + 104f;
        float height = rowsHeight + 214f;
        windowRect.sizeDelta = new Vector2(width, height);
        shadowRect.sizeDelta = new Vector2(width + 8f, height + 8f);

        Image panelImage = windowRect.gameObject.AddComponent<Image>();
        panelImage.color = panelColor;

        Outline panelOutline = windowRect.gameObject.AddComponent<Outline>();
        panelOutline.effectColor = borderColor;
        panelOutline.effectDistance = new Vector2(1f, -1f);

        RectTransform stripRect = CreateRect("AccentStrip", windowRect);
        stripRect.anchorMin = new Vector2(0f, 1f);
        stripRect.anchorMax = new Vector2(1f, 1f);
        stripRect.pivot = new Vector2(0.5f, 1f);
        stripRect.sizeDelta = new Vector2(0f, 6f);
        stripRect.anchoredPosition = Vector2.zero;

        Image stripImage = stripRect.gameObject.AddComponent<Image>();
        stripImage.color = accentStripColor;

        RectTransform titleRect = CreateRect("Title", windowRect);
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = new Vector2(-38f, 46f);
        titleRect.anchoredPosition = new Vector2(0f, -18f);

        Text titleText = CreateText(titleRect, 30, FontStyle.Bold, TextAnchor.UpperLeft);
        titleText.text = "INVENTORY";
        titleText.color = textColor;

        RectTransform hintRect = CreateRect("Hint", windowRect);
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.sizeDelta = new Vector2(-38f, 34f);
        hintRect.anchoredPosition = new Vector2(0f, 12f);

        Text hintText = CreateText(hintRect, 16, FontStyle.Normal, TextAnchor.MiddleLeft);
        hintText.text = "Tab / Esc - close   |   Hold RMB + wheel - split amount   |   Drag outside window - drop stack";
        hintText.color = subTextColor;

        RectTransform rowsRect = CreateRect("Rows", windowRect);
        rowsRect.anchorMin = new Vector2(0.5f, 0.5f);
        rowsRect.anchorMax = new Vector2(0.5f, 0.5f);
        rowsRect.pivot = new Vector2(0.5f, 0.5f);
        rowsRect.sizeDelta = new Vector2(rowsWidth + 16f, rowsHeight + 16f);
        rowsRect.anchoredPosition = new Vector2(0f, -6f);

        Image rowsBg = rowsRect.gameObject.AddComponent<Image>();
        rowsBg.color = new Color(0.04f, 0.05f, 0.075f, 0.76f);

        RectTransform firstRowRect = CreateRect("Row_1_10", rowsRect);
        firstRowRect.anchorMin = new Vector2(0.5f, 1f);
        firstRowRect.anchorMax = new Vector2(0.5f, 1f);
        firstRowRect.pivot = new Vector2(0.5f, 1f);
        firstRowRect.sizeDelta = new Vector2(firstRowWidth, inventorySlotSize.y);
        firstRowRect.anchoredPosition = new Vector2(0f, -8f);

        HorizontalLayoutGroup firstRowLayout = firstRowRect.gameObject.AddComponent<HorizontalLayoutGroup>();
        firstRowLayout.childAlignment = TextAnchor.MiddleCenter;
        firstRowLayout.childControlWidth = false;
        firstRowLayout.childControlHeight = false;
        firstRowLayout.childForceExpandWidth = false;
        firstRowLayout.childForceExpandHeight = false;
        firstRowLayout.spacing = inventorySlotSpacing;

        RectTransform secondRowRect = null;

        if (secondRowCount > 0)
        {
            secondRowRect = CreateRect("Row_" + (firstRowCount + 1) + "_" + slotCount, rowsRect);
            secondRowRect.anchorMin = new Vector2(0.5f, 1f);
            secondRowRect.anchorMax = new Vector2(0.5f, 1f);
            secondRowRect.pivot = new Vector2(0.5f, 1f);
            secondRowRect.sizeDelta = new Vector2(secondRowWidth, inventorySlotSize.y);
            secondRowRect.anchoredPosition = new Vector2(0f, -8f - inventorySlotSize.y - inventoryRowSpacing);

            HorizontalLayoutGroup secondRowLayout = secondRowRect.gameObject.AddComponent<HorizontalLayoutGroup>();
            secondRowLayout.childAlignment = TextAnchor.MiddleCenter;
            secondRowLayout.childControlWidth = false;
            secondRowLayout.childControlHeight = false;
            secondRowLayout.childForceExpandWidth = false;
            secondRowLayout.childForceExpandHeight = false;
            secondRowLayout.spacing = inventorySlotSpacing;
        }

        gridWidgets.Clear();

        for (int i = 0; i < firstRowCount; i++)
        {
            gridWidgets.Add(CreateSlot(firstRowRect, inventorySlotSize, i, false));
        }

        for (int i = firstRowCount; i < slotCount; i++)
        {
            if (secondRowRect == null)
            {
                break;
            }

            gridWidgets.Add(CreateSlot(secondRowRect, inventorySlotSize, i, false));
        }
    }

    private void BuildHotbar()
    {
        RectTransform hotbarFrame = CreateRect("HotbarFrame", rootRect);
        hotbarFrame.anchorMin = new Vector2(0.5f, 0f);
        hotbarFrame.anchorMax = new Vector2(0.5f, 0f);
        hotbarFrame.pivot = new Vector2(0.5f, 0f);
        hotbarFrame.anchoredPosition = new Vector2(0f, 18f);

        float frameWidth = ComputeRowWidth(hotbarSlots, hotbarSlotSize.x, hotbarSpacing) + 30f;
        float frameHeight = hotbarSlotSize.y + 20f;
        hotbarFrame.sizeDelta = new Vector2(frameWidth, frameHeight);

        Image frameImage = hotbarFrame.gameObject.AddComponent<Image>();
        frameImage.color = new Color(0.05f, 0.06f, 0.085f, 0.74f);

        Outline frameOutline = hotbarFrame.gameObject.AddComponent<Outline>();
        frameOutline.effectColor = borderColor;
        frameOutline.effectDistance = new Vector2(1f, -1f);

        hotbarRect = CreateRect("Hotbar", hotbarFrame);
        hotbarRect.anchorMin = new Vector2(0.5f, 0.5f);
        hotbarRect.anchorMax = new Vector2(0.5f, 0.5f);
        hotbarRect.pivot = new Vector2(0.5f, 0.5f);
        hotbarRect.anchoredPosition = Vector2.zero;
        hotbarRect.sizeDelta = new Vector2(frameWidth - 14f, frameHeight - 8f);

        HorizontalLayoutGroup layout = hotbarRect.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.spacing = hotbarSpacing;
        layout.padding = new RectOffset(6, 6, 4, 4);

        hotbarWidgets.Clear();
        for (int i = 0; i < hotbarSlots; i++)
        {
            hotbarWidgets.Add(CreateSlot(hotbarRect, hotbarSlotSize, i, true));
        }

        RectTransform selectedRect = CreateRect("SelectedItem", rootRect);
        selectedRect.anchorMin = new Vector2(0.5f, 0f);
        selectedRect.anchorMax = new Vector2(0.5f, 0f);
        selectedRect.pivot = new Vector2(0.5f, 0f);
        selectedRect.sizeDelta = new Vector2(760f, 34f);
        selectedRect.anchoredPosition = new Vector2(0f, hotbarSlotSize.y + 54f);

        selectedItemText = CreateText(selectedRect, 20, FontStyle.Bold, TextAnchor.MiddleCenter);
        selectedItemText.color = textColor;

        Shadow selectedShadow = selectedRect.gameObject.AddComponent<Shadow>();
        selectedShadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        selectedShadow.effectDistance = new Vector2(1f, -1f);
    }

    private void BuildDragGhost()
    {
        dragGhostRect = CreateRect("DragGhost", rootRect);
        dragGhostRect.anchorMin = new Vector2(0f, 0f);
        dragGhostRect.anchorMax = new Vector2(0f, 0f);
        dragGhostRect.pivot = new Vector2(0.5f, 0.5f);
        dragGhostRect.sizeDelta = new Vector2(86f, 86f);

        Image background = dragGhostRect.gameObject.AddComponent<Image>();
        background.color = new Color(0.18f, 0.2f, 0.26f, 0.92f);
        background.raycastTarget = false;

        Outline outline = dragGhostRect.gameObject.AddComponent<Outline>();
        outline.effectDistance = new Vector2(1f, -1f);
        outline.effectColor = new Color(0f, 0f, 0f, 0.6f);

        RectTransform iconRect = CreateRect("Icon", dragGhostRect);
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = new Vector2(54f, 54f);
        iconRect.anchoredPosition = new Vector2(0f, 2f);

        dragGhostIcon = iconRect.gameObject.AddComponent<RawImage>();
        dragGhostIcon.raycastTarget = false;

        RectTransform amountRect = CreateRect("Amount", dragGhostRect);
        amountRect.anchorMin = new Vector2(1f, 0f);
        amountRect.anchorMax = new Vector2(1f, 0f);
        amountRect.pivot = new Vector2(1f, 0f);
        amountRect.sizeDelta = new Vector2(70f, 18f);
        amountRect.anchoredPosition = new Vector2(-5f, 4f);

        dragGhostText = CreateText(amountRect, 14, FontStyle.Bold, TextAnchor.LowerRight);
        dragGhostText.color = textColor;

        dragGhostRect.gameObject.SetActive(false);
    }

    private SlotWidgets CreateSlot(RectTransform parent, Vector2 size, int index, bool isHotbar)
    {
        RectTransform slotRect = CreateRect("Slot_" + index, parent);
        slotRect.sizeDelta = size;

        LayoutElement element = slotRect.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = size.x;
        element.preferredHeight = size.y;

        Image background = slotRect.gameObject.AddComponent<Image>();
        background.color = slotEmptyColor;

        Outline outline = slotRect.gameObject.AddComponent<Outline>();
        outline.effectDistance = new Vector2(1f, -1f);
        outline.effectColor = new Color(0f, 0f, 0f, 0.56f);

        AddSlotEventTriggers(slotRect.gameObject, index, isHotbar);

        RectTransform indexRect = CreateRect("Index", slotRect);
        indexRect.anchorMin = new Vector2(0f, 1f);
        indexRect.anchorMax = new Vector2(0f, 1f);
        indexRect.pivot = new Vector2(0f, 1f);
        indexRect.sizeDelta = new Vector2(size.x - 8f, 18f);
        indexRect.anchoredPosition = new Vector2(5f, -3f);

        Text indexText = CreateText(indexRect, isHotbar ? 13 : 12, FontStyle.Bold, TextAnchor.UpperLeft);
        indexText.color = subTextColor;
        indexText.text = BuildSlotIndexLabel(index);

        RectTransform iconRect = CreateRect("Icon", slotRect);
        iconRect.anchorMin = new Vector2(0.5f, 0.54f);
        iconRect.anchorMax = new Vector2(0.5f, 0.54f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = isHotbar ? new Vector2(46f, 46f) : new Vector2(58f, 58f);

        RawImage icon = iconRect.gameObject.AddComponent<RawImage>();
        icon.raycastTarget = false;
        icon.enabled = false;

        RectTransform itemRect = CreateRect("Item", slotRect);
        itemRect.anchorMin = new Vector2(0f, 0f);
        itemRect.anchorMax = new Vector2(1f, 0f);
        itemRect.pivot = new Vector2(0.5f, 0f);
        itemRect.sizeDelta = new Vector2(-10f, 18f);
        itemRect.anchoredPosition = new Vector2(0f, 4f);

        Text itemText = CreateText(itemRect, isHotbar ? 12 : 11, FontStyle.Bold, TextAnchor.LowerLeft);
        itemText.color = textColor;
        itemText.horizontalOverflow = HorizontalWrapMode.Overflow;
        itemText.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform amountRect = CreateRect("Amount", slotRect);
        amountRect.anchorMin = new Vector2(1f, 0f);
        amountRect.anchorMax = new Vector2(1f, 0f);
        amountRect.pivot = new Vector2(1f, 0f);
        amountRect.sizeDelta = new Vector2(size.x - 8f, 18f);
        amountRect.anchoredPosition = new Vector2(-4f, 3f);

        Text amountText = CreateText(amountRect, isHotbar ? 14 : 13, FontStyle.Bold, TextAnchor.LowerRight);
        amountText.color = textColor;

        return new SlotWidgets
        {
            slotIndex = index,
            isHotbar = isHotbar,
            background = background,
            icon = icon,
            indexText = indexText,
            itemText = itemText,
            amountText = amountText
        };
    }

    private void AddSlotEventTriggers(GameObject target, int slotIndex, bool isHotbar)
    {
        EventTrigger trigger = target.AddComponent<EventTrigger>();
        trigger.triggers = new List<EventTrigger.Entry>();

        AddEventEntry(trigger, EventTriggerType.PointerClick, data =>
        {
            HandleSlotClick(slotIndex, isHotbar, data as PointerEventData);
        });

        AddEventEntry(trigger, EventTriggerType.PointerDown, data =>
        {
            HandleSlotPointerDown(slotIndex, data as PointerEventData);
        });

        AddEventEntry(trigger, EventTriggerType.PointerEnter, _ =>
        {
            HandleSlotPointerEnter(slotIndex);
        });

        AddEventEntry(trigger, EventTriggerType.PointerExit, _ =>
        {
            HandleSlotPointerExit(slotIndex);
        });

        AddEventEntry(trigger, EventTriggerType.BeginDrag, data =>
        {
            BeginSlotDrag(slotIndex, data as PointerEventData);
        });

        AddEventEntry(trigger, EventTriggerType.Drag, data =>
        {
            DragSlot(data as PointerEventData);
        });

        AddEventEntry(trigger, EventTriggerType.EndDrag, data =>
        {
            EndSlotDrag(data as PointerEventData);
        });

        AddEventEntry(trigger, EventTriggerType.Drop, data =>
        {
            DropOnSlot(slotIndex, data as PointerEventData);
        });
    }

    private static void AddEventEntry(EventTrigger trigger, EventTriggerType eventType, UnityAction<BaseEventData> callback)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry
        {
            eventID = eventType
        };

        entry.callback.AddListener(callback);
        trigger.triggers.Add(entry);
    }

    private void RefreshAll()
    {
        if (inventory == null)
        {
            return;
        }

        IReadOnlyList<PlayerInventory.Slot> slots = inventory.Slots;
        int selectedIndex = inventory.SelectedSlotIndex;
        bool hotbarSelectionActive = playerInteractor == null || playerInteractor.IsHotbarSelectionActive;

        for (int i = 0; i < hotbarWidgets.Count; i++)
        {
            PlayerInventory.Slot slot = i < slots.Count ? slots[i] : default;
            bool isSelected = i == selectedIndex && hotbarSelectionActive;
            ApplySlotVisual(hotbarWidgets[i], slot, isSelected);
        }

        for (int i = 0; i < gridWidgets.Count; i++)
        {
            SlotWidgets widgets = gridWidgets[i];
            int slotIndex = widgets.slotIndex;
            PlayerInventory.Slot slot = slotIndex < slots.Count ? slots[slotIndex] : default;

            bool isSelected = slotIndex == selectedIndex;
            if (isSelected && slotIndex < hotbarSlots && !hotbarSelectionActive)
            {
                isSelected = false;
            }

            ApplySlotVisual(widgets, slot, isSelected);
        }

        if (selectedItemText == null)
        {
            return;
        }

        bool hasActiveSelection = selectedIndex >= 0
            && selectedIndex < slots.Count
            && (selectedIndex >= hotbarSlots || hotbarSelectionActive);

        if (!hasActiveSelection)
        {
            selectedItemText.text = "Selected: Empty";
            return;
        }

        PlayerInventory.Slot selected = slots[selectedIndex];
        if (selected.IsEmpty)
        {
            selectedItemText.text = "Selected: Empty";
            return;
        }

        selectedItemText.text = "Selected: " + selected.item.DisplayName + " x" + selected.amount;
    }

    private void ApplySlotVisual(SlotWidgets widgets, PlayerInventory.Slot slot, bool selected)
    {
        if (widgets == null)
        {
            return;
        }

        bool empty = slot.IsEmpty;

        if (widgets.background != null)
        {
            widgets.background.color = selected
                ? slotSelectedColor
                : (empty ? slotEmptyColor : slotFilledColor);
        }

        if (widgets.indexText != null)
        {
            widgets.indexText.text = BuildSlotIndexLabel(widgets.slotIndex);
            widgets.indexText.color = selected ? new Color(0.12f, 0.12f, 0.14f, 1f) : subTextColor;
        }

        if (widgets.icon != null)
        {
            Texture2D iconTexture = empty ? null : GetOrCreateIcon(slot.item);
            widgets.icon.texture = iconTexture;
            widgets.icon.enabled = iconTexture != null;
            widgets.icon.color = Color.white;
        }

        if (widgets.itemText != null)
        {
            if (empty)
            {
                widgets.itemText.text = string.Empty;
            }
            else if (widgets.isHotbar)
            {
                widgets.itemText.text = widgets.icon != null && widgets.icon.enabled ? string.Empty : BuildShortItemLabel(slot.item);
            }
            else
            {
                widgets.itemText.text = slot.item.DisplayName;
            }

            widgets.itemText.color = selected ? new Color(0.12f, 0.12f, 0.14f, 1f) : textColor;
        }

        if (widgets.amountText != null)
        {
            widgets.amountText.text = empty || slot.amount <= 1 ? string.Empty : slot.amount.ToString();
            widgets.amountText.color = selected ? new Color(0.12f, 0.12f, 0.14f, 1f) : textColor;
        }
    }

    public void HandleSlotClick(int slotIndex, bool isHotbar, PointerEventData eventData)
    {
        if (inventory == null || eventData == null)
        {
            return;
        }

        if (slotIndex < 0 || slotIndex >= inventory.Capacity)
        {
            return;
        }

        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        if (!isHotbar)
        {
            inventory.SelectSlot(slotIndex);
            return;
        }

        if (playerInteractor != null)
        {
            playerInteractor.TrySelectHotbarSlotAndEquip(slotIndex);
            return;
        }

        inventory.SelectSlot(slotIndex);
    }

    public void HandleSlotPointerDown(int slotIndex, PointerEventData eventData)
    {
        if (!inventoryOpen || inventory == null || eventData == null)
        {
            return;
        }

        if (eventData.button != PointerEventData.InputButton.Right)
        {
            return;
        }

        BeginSplitAdjust(slotIndex, eventData.position);
    }

    public void HandleSlotPointerEnter(int slotIndex)
    {
        if (!splitAdjustActive)
        {
            return;
        }

        splitHoveredSlot = slotIndex;
    }

    public void HandleSlotPointerExit(int slotIndex)
    {
        if (!splitAdjustActive)
        {
            return;
        }

        if (splitHoveredSlot == slotIndex)
        {
            splitHoveredSlot = -1;
        }
    }

    public void BeginSlotDrag(int slotIndex, PointerEventData eventData)
    {
        if (!inventoryOpen || inventory == null || eventData == null || splitAdjustActive)
        {
            return;
        }

        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        if (slotIndex < 0 || slotIndex >= inventory.Capacity)
        {
            return;
        }

        PlayerInventory.Slot slot = inventory.Slots[slotIndex];
        if (slot.IsEmpty)
        {
            return;
        }

        dragFromIndex = slotIndex;
        dragDroppedOnSlot = false;

        if (dragGhostRect != null)
        {
            dragGhostRect.gameObject.SetActive(true);
            dragGhostRect.position = eventData.position;

            if (dragGhostIcon != null)
            {
                dragGhostIcon.texture = GetOrCreateIcon(slot.item);
                dragGhostIcon.enabled = dragGhostIcon.texture != null;
            }

            if (dragGhostText != null)
            {
                dragGhostText.text = slot.amount > 1 ? slot.amount.ToString() : string.Empty;
            }
        }
    }

    public void DragSlot(PointerEventData eventData)
    {
        if (dragFromIndex < 0 || dragGhostRect == null || eventData == null)
        {
            return;
        }

        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        dragGhostRect.position = eventData.position;
    }

    public void DropOnSlot(int targetSlotIndex, PointerEventData eventData)
    {
        if (dragFromIndex < 0 || inventory == null || eventData == null)
        {
            return;
        }

        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        if (targetSlotIndex < 0 || targetSlotIndex >= inventory.Capacity)
        {
            return;
        }

        dragDroppedOnSlot = true;
        inventory.TryMoveSlot(dragFromIndex, targetSlotIndex);
    }

    public void EndSlotDrag(PointerEventData eventData)
    {
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        int sourceSlotIndex = dragFromIndex;
        bool droppedOnSlot = dragDroppedOnSlot;

        if (dragGhostRect != null)
        {
            dragGhostRect.gameObject.SetActive(false);
        }

        dragFromIndex = -1;
        dragDroppedOnSlot = false;

        if (inventory == null || sourceSlotIndex < 0 || sourceSlotIndex >= inventory.Slots.Count)
        {
            return;
        }

        if (droppedOnSlot || !inventoryOpen || !IsOutsideInventoryWindow(eventData))
        {
            return;
        }

        TryDropSlotStackToWorld(sourceSlotIndex);
    }

    private void BeginSplitAdjust(int slotIndex, Vector2 pointerPosition)
    {
        if (inventory == null || slotIndex < 0 || slotIndex >= inventory.Slots.Count)
        {
            return;
        }

        PlayerInventory.Slot source = inventory.Slots[slotIndex];
        if (source.IsEmpty || source.item == null || source.amount <= 1)
        {
            return;
        }

        splitAdjustActive = true;
        splitFromIndex = slotIndex;
        splitHoveredSlot = slotIndex;
        splitAmount = Mathf.Clamp(source.amount / 2, 1, source.amount - 1);

        if (dragGhostRect != null)
        {
            dragGhostRect.gameObject.SetActive(true);
            dragGhostRect.position = pointerPosition;

            if (dragGhostIcon != null)
            {
                dragGhostIcon.texture = GetOrCreateIcon(source.item);
                dragGhostIcon.enabled = dragGhostIcon.texture != null;
            }

            if (dragGhostText != null)
            {
                dragGhostText.text = splitAmount.ToString();
            }
        }
    }

    private void UpdateSplitAdjustMode()
    {
        if (!splitAdjustActive)
        {
            return;
        }

        if (!inventoryOpen || inventory == null || Mouse.current == null)
        {
            CancelSplitAdjust();
            return;
        }

        if (splitFromIndex < 0 || splitFromIndex >= inventory.Slots.Count)
        {
            CancelSplitAdjust();
            return;
        }

        PlayerInventory.Slot source = inventory.Slots[splitFromIndex];
        if (source.IsEmpty || source.item == null || source.amount <= 1)
        {
            CancelSplitAdjust();
            return;
        }

        int maxSplitAmount = source.amount - 1;
        splitAmount = Mathf.Clamp(splitAmount, 1, maxSplitAmount);

        if (dragGhostRect != null)
        {
            dragGhostRect.position = Mouse.current.position.ReadValue();
        }

        float scrollY = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scrollY) > 0.01f)
        {
            int step = scrollY > 0f ? Mathf.CeilToInt(scrollY / 120f) : Mathf.FloorToInt(scrollY / 120f);
            if (step == 0)
            {
                step = scrollY > 0f ? 1 : -1;
            }

            int nextAmount = Mathf.Clamp(splitAmount + step, 1, maxSplitAmount);
            if (nextAmount != splitAmount)
            {
                splitAmount = nextAmount;

                if (dragGhostText != null)
                {
                    dragGhostText.text = splitAmount.ToString();
                }
            }
        }

        if (!Mouse.current.rightButton.isPressed)
        {
            CommitSplitAdjust();
        }
    }

    private void CommitSplitAdjust()
    {
        if (inventory == null)
        {
            CancelSplitAdjust();
            return;
        }

        int sourceSlot = splitFromIndex;
        int hoveredSlot = splitHoveredSlot;
        int amount = splitAmount;

        CancelSplitAdjust();

        if (sourceSlot < 0 || sourceSlot >= inventory.Slots.Count)
        {
            return;
        }

        PlayerInventory.Slot source = inventory.Slots[sourceSlot];
        if (source.IsEmpty || source.item == null || source.amount <= 1)
        {
            return;
        }

        int clampedAmount = Mathf.Clamp(amount, 1, source.amount - 1);

        bool aimedAtOtherSlot = hoveredSlot >= 0
            && hoveredSlot < inventory.Slots.Count
            && hoveredSlot != sourceSlot;

        if (aimedAtOtherSlot)
        {
            inventory.TrySplitSlot(sourceSlot, hoveredSlot, clampedAmount);
            return;
        }

        int targetSlot = FindSplitTargetSlot(sourceSlot, source.item);
        if (targetSlot < 0)
        {
            return;
        }

        inventory.TrySplitSlot(sourceSlot, targetSlot, clampedAmount);
    }

    private void CancelSplitAdjust()
    {
        splitAdjustActive = false;
        splitFromIndex = -1;
        splitHoveredSlot = -1;
        splitAmount = 0;

        if (dragGhostRect != null)
        {
            dragGhostRect.gameObject.SetActive(false);
        }
    }

    private int FindSplitTargetSlot(int sourceSlotIndex, ItemDefinition item)
    {
        if (inventory == null || item == null)
        {
            return -1;
        }

        IReadOnlyList<PlayerInventory.Slot> slots = inventory.Slots;

        for (int i = 0; i < slots.Count; i++)
        {
            if (i == sourceSlotIndex)
            {
                continue;
            }

            if (slots[i].IsEmpty)
            {
                return i;
            }
        }

        return -1;
    }

    private bool IsOutsideInventoryWindow(PointerEventData eventData)
    {
        if (windowRect == null || eventData == null)
        {
            return false;
        }

        return !RectTransformUtility.RectangleContainsScreenPoint(windowRect, eventData.position, eventData.pressEventCamera);
    }

    private void TryDropSlotStackToWorld(int slotIndex)
    {
        if (inventory == null || playerInteractor == null)
        {
            return;
        }

        if (slotIndex < 0 || slotIndex >= inventory.Slots.Count)
        {
            return;
        }

        PlayerInventory.Slot slot = inventory.Slots[slotIndex];
        if (slot.IsEmpty || slot.item == null)
        {
            return;
        }

        playerInteractor.TryDropInventorySlotToWorld(slotIndex, slot.amount);
    }

    private void SetInventoryOpen(bool open, bool force = false)
    {
        if (!force && inventoryOpen == open)
        {
            return;
        }

        inventoryOpen = open;

        if (dimRect != null)
        {
            dimRect.gameObject.SetActive(open);
        }

        if (open)
        {
            CaptureAndDisableControls();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        CancelSplitAdjust();

        RestoreControlsIfNeeded();
    }

    private void CaptureAndDisableControls()
    {
        if (controlsCaptured)
        {
            return;
        }

        controlsCaptured = true;

        savedLookEnabled = playerLook != null && playerLook.enabled;
        savedMotorEnabled = playerMotor != null && playerMotor.enabled;

        if (playerLook != null)
        {
            playerLook.enabled = false;
        }

        if (playerMotor != null)
        {
            playerMotor.enabled = false;
        }
    }

    private void RestoreControlsIfNeeded()
    {
        if (!controlsCaptured)
        {
            return;
        }

        if (playerLook != null)
        {
            playerLook.enabled = savedLookEnabled;
        }

        if (playerMotor != null)
        {
            playerMotor.enabled = savedMotorEnabled;
        }

        controlsCaptured = false;

        if (playerLook != null && playerLook.enabled)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private Texture2D GetOrCreateIcon(ItemDefinition item)
    {
        if (item == null)
        {
            return null;
        }

        if (iconCache.TryGetValue(item, out Texture2D cached) && cached != null)
        {
            return cached;
        }

        GameObject sourcePrefab = item.WorldPrefab;
        if (sourcePrefab == null)
        {
            sourcePrefab = TryFindWorldVisual(item);
        }

        Texture2D generated = null;
        try
        {
            generated = CreateIconFromPrefab(sourcePrefab);

            if (generated == null)
            {
                GameObject fallbackVisual = TryFindWorldVisual(item);
                if (fallbackVisual != null && fallbackVisual != sourcePrefab)
                {
                    generated = CreateIconFromPrefab(fallbackVisual);
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Inventory icon generation skipped for '" + item.DisplayName + "': " + exception.Message);
            generated = null;
        }

        if (generated != null)
        {
            iconCache[item] = generated;
        }
        else
        {
            generated = CreateFallbackIcon(item);
            if (generated != null)
            {
                iconCache[item] = generated;
            }
            else
            {
                iconCache.Remove(item);
            }
        }

        return generated;
    }

    private static Texture2D CreateFallbackIcon(ItemDefinition item)
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "FallbackIcon_" + (item != null ? item.ItemId : "item")
        };

        Color top = new Color(0.22f, 0.26f, 0.34f, 1f);
        Color bottom = new Color(0.12f, 0.14f, 0.2f, 1f);

        for (int y = 0; y < size; y++)
        {
            float t = y / (float)(size - 1);
            Color row = Color.Lerp(bottom, top, t);
            for (int x = 0; x < size; x++)
            {
                texture.SetPixel(x, y, row);
            }
        }

        string label = BuildShortItemLabel(item);
        DrawLabelBlocks(texture, label);
        texture.Apply(false, false);
        return texture;
    }

    private static void DrawLabelBlocks(Texture2D texture, string label)
    {
        if (texture == null || string.IsNullOrEmpty(label))
        {
            return;
        }

        int size = texture.width;
        int blockSize = Mathf.Max(4, size / 10);
        int spacing = Mathf.Max(2, blockSize / 4);
        int letters = Mathf.Min(2, label.Length);

        int totalWidth = letters * blockSize + (letters - 1) * spacing;
        int startX = (size - totalWidth) / 2;
        int startY = size / 2 - blockSize / 2;

        for (int i = 0; i < letters; i++)
        {
            int charCode = char.ToUpperInvariant(label[i]);
            Color c = new Color(
                0.5f + (charCode % 37) / 120f,
                0.55f + (charCode % 23) / 120f,
                0.65f + (charCode % 17) / 120f,
                1f);

            int x0 = startX + i * (blockSize + spacing);
            int x1 = Mathf.Min(size - 1, x0 + blockSize);
            int y0 = Mathf.Max(0, startY);
            int y1 = Mathf.Min(size - 1, startY + blockSize);

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    texture.SetPixel(x, y, c);
                }
            }
        }
    }

    private static GameObject TryFindWorldVisual(ItemDefinition item)
    {
        if (item == null)
        {
            return null;
        }

        WorldItem[] worldItems = Object.FindObjectsByType<WorldItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (worldItems == null || worldItems.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < worldItems.Length; i++)
        {
            WorldItem worldItem = worldItems[i];
            if (worldItem == null || worldItem.Definition != item)
            {
                continue;
            }

            return worldItem.gameObject;
        }

        return null;
    }

    private Texture2D CreateIconFromPrefab(GameObject sourcePrefab)
    {
        if (sourcePrefab == null)
        {
            return null;
        }

        EnsureIconPreviewRig();
        if (iconCamera == null || iconRenderTexture == null)
        {
            return null;
        }

        UnityEngine.Object clonedObject = Object.Instantiate((UnityEngine.Object)sourcePrefab);
        GameObject instance = clonedObject as GameObject;
        if (instance == null && clonedObject is Component component)
        {
            instance = component.gameObject;
        }

        if (instance == null)
        {
            if (clonedObject != null)
            {
                Destroy(clonedObject);
            }

            return null;
        }

        instance.hideFlags = HideFlags.HideAndDontSave;
        instance.transform.position = new Vector3(-9999f, -9999f, -9999f);
        instance.transform.rotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        SetLayerRecursively(instance.transform, PreviewLayer);
        DisableBehavioursForPreview(instance);

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            Destroy(instance);
            return null;
        }

        if (!TryCalculateRendererBounds(renderers, out Bounds bounds))
        {
            Destroy(instance);
            return null;
        }

        float rawRadius = Mathf.Max(bounds.extents.magnitude, 0.01f);
        float desiredRadius = 0.55f;
        float normalizeScale = desiredRadius / rawRadius;
        if (Mathf.Abs(normalizeScale - 1f) > 0.02f)
        {
            instance.transform.localScale *= normalizeScale;
            renderers = instance.GetComponentsInChildren<Renderer>(true);

            if (!TryCalculateRendererBounds(renderers, out bounds))
            {
                Destroy(instance);
                return null;
            }
        }

        Vector3 center = bounds.center;
        float radius = Mathf.Max(bounds.extents.magnitude, 0.15f);
        float distance = radius * 2.15f;

        iconCamera.transform.position = center + new Vector3(distance * 0.35f, distance * 0.2f, -distance);
        iconCamera.transform.LookAt(center);
        iconCamera.nearClipPlane = 0.01f;
        iconCamera.farClipPlane = Mathf.Max(12f, distance * 8f);

        iconLight.transform.position = center + new Vector3(-distance, distance * 1.1f, -distance * 0.3f);
        iconLight.transform.rotation = Quaternion.LookRotation(center - iconLight.transform.position, Vector3.up);

        RenderTexture previous = RenderTexture.active;
        iconCamera.targetTexture = iconRenderTexture;
        RenderTexture.active = iconRenderTexture;
        iconCamera.Render();

        Texture2D texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.ReadPixels(new Rect(0f, 0f, IconSize, IconSize), 0, 0, false);
        texture.Apply(false, false);

        RenderTexture.active = previous;
        iconCamera.targetTexture = null;

        Destroy(instance);
        return texture;
    }

    private void EnsureIconPreviewRig()
    {
        if (iconRenderTexture == null)
        {
            iconRenderTexture = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32)
            {
                name = "ItemIconRT",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            iconRenderTexture.Create();
        }

        if (iconCamera == null)
        {
            GameObject cameraObject = new GameObject("ItemIconCamera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;

            iconCamera = cameraObject.AddComponent<Camera>();
            iconCamera.enabled = false;
            iconCamera.clearFlags = CameraClearFlags.SolidColor;
            iconCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            iconCamera.cullingMask = 1 << PreviewLayer;
            iconCamera.allowHDR = false;
            iconCamera.allowMSAA = false;
            iconCamera.fieldOfView = 32f;
        }

        if (iconLight == null)
        {
            GameObject lightObject = new GameObject("ItemIconLight");
            lightObject.hideFlags = HideFlags.HideAndDontSave;

            iconLight = lightObject.AddComponent<Light>();
            iconLight.type = LightType.Directional;
            iconLight.color = Color.white;
            iconLight.intensity = 1.15f;
            iconLight.shadows = LightShadows.None;
            iconLight.cullingMask = 1 << PreviewLayer;
        }
    }

    private static void DisableBehavioursForPreview(GameObject root)
    {
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            behaviour.enabled = false;
        }

        ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] != null)
            {
                particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }

    private static bool TryCalculateRendererBounds(Renderer[] renderers, out Bounds bounds)
    {
        bounds = default;
        if (renderers == null || renderers.Length == 0)
        {
            return false;
        }

        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer rendererComponent = renderers[i];
            if (rendererComponent == null)
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

    private static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null)
        {
            return;
        }

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
        {
            SetLayerRecursively(root.GetChild(i), layer);
        }
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystemObject.hideFlags = HideFlags.None;
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Text CreateText(RectTransform rect, int fontSize, FontStyle style, TextAnchor anchor)
    {
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = ResolveFont();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static Font ResolveFont()
    {
        if (cachedFont != null)
        {
            return cachedFont;
        }

        cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (cachedFont == null)
        {
            cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        return cachedFont;
    }

    private static string BuildSlotIndexLabel(int slotIndex)
    {
        int number = slotIndex + 1;
        if (number == 10)
        {
            return "0";
        }

        return number.ToString();
    }

    private static float ComputeRowWidth(int slotCount, float slotWidth, float spacing)
    {
        if (slotCount <= 0)
        {
            return 0f;
        }

        return slotCount * slotWidth + (slotCount - 1) * spacing;
    }

    private static string BuildShortItemLabel(ItemDefinition item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        string display = string.IsNullOrWhiteSpace(item.DisplayName) ? item.name : item.DisplayName;
        string[] words = display.Split(' ');

        if (words.Length >= 2 && words[0].Length > 0 && words[1].Length > 0)
        {
            char a = char.ToUpperInvariant(words[0][0]);
            char b = char.ToUpperInvariant(words[1][0]);
            return string.Concat(a, b);
        }

        string trimmed = display.Trim();
        if (trimmed.Length <= 3)
        {
            return trimmed.ToUpperInvariant();
        }

        return trimmed.Substring(0, 3).ToUpperInvariant();
    }
}
