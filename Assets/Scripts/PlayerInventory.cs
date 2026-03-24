using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerInventory : MonoBehaviour
{
    [Serializable]
    public struct Slot
    {
        public ItemDefinition item;
        public int amount;

        public bool IsEmpty => item == null || amount <= 0;
    }

    [Header("Inventory")]
    [SerializeField, Min(1)] private int capacity = 20;
    [SerializeField] private List<Slot> slots = new List<Slot>();
    [SerializeField, Min(0)] private int selectedSlotIndex;

    public int Capacity => capacity;
    public int SelectedSlotIndex => selectedSlotIndex;
    public IReadOnlyList<Slot> Slots => slots;

    public event Action InventoryChanged;
    public event Action<int, Slot> SelectedSlotChanged;

    private void Awake()
    {
        EnsureCapacity();
        ClampSelection();
    }

    public bool EnsureMinimumCapacity(int minimumCapacity)
    {
        int targetCapacity = Mathf.Max(1, minimumCapacity);
        if (capacity >= targetCapacity)
        {
            EnsureCapacity();
            ClampSelection();
            return false;
        }

        capacity = targetCapacity;
        EnsureCapacity();
        ClampSelection();
        NotifyChanged();
        return true;
    }

    private void OnValidate()
    {
        capacity = Mathf.Max(1, capacity);
        EnsureCapacity();
        ClampSelection();
    }

    public bool TryAdd(ItemDefinition item, int amount)
    {
        if (item == null || amount <= 0)
        {
            return false;
        }

        EnsureCapacity();

        int remaining = amount;
        int maxStack = item.MaxStack;

        for (int i = 0; i < slots.Count && remaining > 0; i++)
        {
            Slot slot = slots[i];
            if (slot.item != item || slot.amount >= maxStack)
            {
                continue;
            }

            int freeSpace = maxStack - slot.amount;
            int toInsert = Mathf.Min(freeSpace, remaining);
            remaining -= toInsert;
        }

        for (int i = 0; i < slots.Count && remaining > 0; i++)
        {
            Slot slot = slots[i];
            if (!slot.IsEmpty)
            {
                continue;
            }

            int toInsert = Mathf.Min(maxStack, remaining);
            remaining -= toInsert;
        }

        if (remaining > 0)
        {
            return false;
        }

        remaining = amount;

        for (int i = 0; i < slots.Count && remaining > 0; i++)
        {
            Slot slot = slots[i];
            if (slot.item != item || slot.amount >= maxStack)
            {
                continue;
            }

            int freeSpace = maxStack - slot.amount;
            int toInsert = Mathf.Min(freeSpace, remaining);
            slot.amount += toInsert;
            slots[i] = slot;
            remaining -= toInsert;
        }

        for (int i = 0; i < slots.Count && remaining > 0; i++)
        {
            Slot slot = slots[i];
            if (!slot.IsEmpty)
            {
                continue;
            }

            int toInsert = Mathf.Min(maxStack, remaining);
            slot.item = item;
            slot.amount = toInsert;
            slots[i] = slot;
            remaining -= toInsert;
        }

        NotifyChanged();
        return true;
    }

    public bool TryAddWithHotbarPriority(ItemDefinition item, int amount, int hotbarSlotCount)
    {
        return TryAddWithHotbarPriority(item, amount, hotbarSlotCount, -1, out _);
    }

    public bool TryAddWithHotbarPriority(
        ItemDefinition item,
        int amount,
        int hotbarSlotCount,
        int preferredHotbarSlotIndex,
        out bool addedToPreferredHotbarSlot)
    {
        addedToPreferredHotbarSlot = false;

        if (item == null || amount <= 0)
        {
            return false;
        }

        EnsureCapacity();

        int splitIndex = Mathf.Clamp(hotbarSlotCount, 0, slots.Count);
        bool usePreferredSlot = preferredHotbarSlotIndex >= 0 && preferredHotbarSlotIndex < splitIndex;

        int remaining = amount;

        if (usePreferredSlot)
        {
            remaining = SimulateAddToSingleSlot(item, remaining, preferredHotbarSlotIndex, out _);
        }

        remaining = SimulateAddToRange(item, remaining, 0, splitIndex, usePreferredSlot ? preferredHotbarSlotIndex : -1);
        remaining = SimulateAddToRange(item, remaining, splitIndex, slots.Count, -1);

        if (remaining > 0)
        {
            return false;
        }

        remaining = amount;

        int addedToPreferredCount = 0;
        if (usePreferredSlot)
        {
            remaining = ApplyAddToSingleSlot(item, remaining, preferredHotbarSlotIndex, out addedToPreferredCount);
        }

        remaining = ApplyAddToRange(item, remaining, 0, splitIndex, usePreferredSlot ? preferredHotbarSlotIndex : -1);
        remaining = ApplyAddToRange(item, remaining, splitIndex, slots.Count, -1);

        addedToPreferredHotbarSlot = addedToPreferredCount > 0;

        NotifyChanged();
        return remaining <= 0;
    }

    public bool TryAddWithPreferredSlot(ItemDefinition item, int amount, int preferredSlotIndex)
    {
        if (item == null || amount <= 0)
        {
            return false;
        }

        EnsureCapacity();

        if (preferredSlotIndex >= 0 && preferredSlotIndex < slots.Count)
        {
            Slot preferredSlot = slots[preferredSlotIndex];
            bool canUsePreferred = preferredSlot.IsEmpty || preferredSlot.item == item;

            if (canUsePreferred)
            {
                int currentAmount = preferredSlot.IsEmpty ? 0 : preferredSlot.amount;
                int nextAmount = currentAmount + amount;

                if (nextAmount <= item.MaxStack)
                {
                    preferredSlot.item = item;
                    preferredSlot.amount = nextAmount;
                    slots[preferredSlotIndex] = preferredSlot;
                    NotifyChanged();
                    return true;
                }
            }
        }

        return TryAdd(item, amount);
    }

    public bool TryTakeFromSelected(int amount, out ItemDefinition item)
    {
        item = null;
        if (amount <= 0)
        {
            return false;
        }

        EnsureCapacity();
        ClampSelection();

        Slot selected = slots[selectedSlotIndex];
        if (selected.IsEmpty || selected.amount < amount)
        {
            return false;
        }

        selected.amount -= amount;
        item = selected.item;

        if (selected.amount <= 0)
        {
            selected.item = null;
            selected.amount = 0;
        }

        slots[selectedSlotIndex] = selected;
        NotifyChanged();
        return true;
    }

    public bool TryTakeFromSlot(int slotIndex, int amount, out ItemDefinition item)
    {
        return TryTakeFromSlot(slotIndex, amount, null, out item);
    }

    public bool TryTakeFromSlot(int slotIndex, int amount, ItemDefinition expectedItem, out ItemDefinition item)
    {
        item = null;
        if (amount <= 0)
        {
            return false;
        }

        EnsureCapacity();

        if (slotIndex < 0 || slotIndex >= slots.Count)
        {
            return false;
        }

        Slot slot = slots[slotIndex];
        if (slot.IsEmpty || slot.amount < amount)
        {
            return false;
        }

        if (expectedItem != null && slot.item != expectedItem)
        {
            return false;
        }

        slot.amount -= amount;
        item = slot.item;

        if (slot.amount <= 0)
        {
            slot.item = null;
            slot.amount = 0;
        }

        slots[slotIndex] = slot;
        NotifyChanged();
        return true;
    }

    public bool TryTakeFirstMatching(ItemDefinition item, int amount, out int takenFromSlotIndex)
    {
        takenFromSlotIndex = -1;
        if (item == null || amount <= 0)
        {
            return false;
        }

        EnsureCapacity();

        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];
            if (slot.item != item || slot.amount < amount)
            {
                continue;
            }

            if (!TryTakeFromSlot(i, amount, item, out _))
            {
                continue;
            }

            takenFromSlotIndex = i;
            return true;
        }

        return false;
    }

    public bool SelectNext()
    {
        return SelectByOffset(1);
    }

    public bool SelectPrevious()
    {
        return SelectByOffset(-1);
    }

    public bool SelectSlot(int index)
    {
        if (index < 0 || index >= slots.Count)
        {
            return false;
        }

        if (selectedSlotIndex == index)
        {
            return false;
        }

        selectedSlotIndex = index;
        SelectedSlotChanged?.Invoke(selectedSlotIndex, slots[selectedSlotIndex]);
        return true;
    }

    public bool TrySelectFirstNonEmpty()
    {
        EnsureCapacity();

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsEmpty)
            {
                continue;
            }

            SelectSlot(i);
            return true;
        }

        return false;
    }

    public bool TryMoveSlot(int fromIndex, int toIndex)
    {
        EnsureCapacity();

        if (fromIndex < 0 || fromIndex >= slots.Count || toIndex < 0 || toIndex >= slots.Count)
        {
            return false;
        }

        if (fromIndex == toIndex)
        {
            return false;
        }

        Slot from = slots[fromIndex];
        Slot to = slots[toIndex];

        if (from.IsEmpty)
        {
            return false;
        }

        if (to.IsEmpty)
        {
            slots[toIndex] = from;
            slots[fromIndex] = new Slot();
            NotifyChanged();
            return true;
        }

        if (to.item == from.item)
        {
            int maxStack = Mathf.Max(1, to.item.MaxStack);
            if (to.amount < maxStack)
            {
                int transfer = Mathf.Min(maxStack - to.amount, from.amount);
                to.amount += transfer;
                from.amount -= transfer;

                slots[toIndex] = to;
                slots[fromIndex] = from.amount > 0 ? from : new Slot();
                NotifyChanged();
                return true;
            }
        }

        slots[toIndex] = from;
        slots[fromIndex] = to;
        NotifyChanged();
        return true;
    }

    public bool TrySplitSlot(int fromIndex, int toIndex, int amount)
    {
        EnsureCapacity();

        if (fromIndex < 0 || fromIndex >= slots.Count || toIndex < 0 || toIndex >= slots.Count)
        {
            return false;
        }

        if (fromIndex == toIndex || amount <= 0)
        {
            return false;
        }

        Slot from = slots[fromIndex];
        Slot to = slots[toIndex];

        if (from.IsEmpty || from.item == null || from.amount <= 1)
        {
            return false;
        }

        if (!to.IsEmpty && to.item != from.item)
        {
            return false;
        }

        int maxStack = Mathf.Max(1, from.item.MaxStack);
        int targetAmount = to.IsEmpty ? 0 : to.amount;
        int freeSpace = maxStack - targetAmount;
        if (freeSpace <= 0)
        {
            return false;
        }

        int movable = Mathf.Min(amount, from.amount - 1, freeSpace);
        if (movable <= 0)
        {
            return false;
        }

        from.amount -= movable;

        if (to.IsEmpty)
        {
            to.item = from.item;
            to.amount = movable;
        }
        else
        {
            to.amount += movable;
        }

        slots[fromIndex] = from;
        slots[toIndex] = to;
        NotifyChanged();
        return true;
    }

    public Slot GetSelectedSlot()
    {
        EnsureCapacity();
        ClampSelection();
        return slots[selectedSlotIndex];
    }

    private bool SelectByOffset(int offset)
    {
        if (slots.Count == 0 || offset == 0)
        {
            return false;
        }

        int previousIndex = selectedSlotIndex;
        selectedSlotIndex = (selectedSlotIndex + offset + slots.Count) % slots.Count;

        if (previousIndex == selectedSlotIndex)
        {
            return false;
        }

        SelectedSlotChanged?.Invoke(selectedSlotIndex, slots[selectedSlotIndex]);
        return true;
    }

    private int SimulateAddToSingleSlot(ItemDefinition item, int amount, int slotIndex, out int inserted)
    {
        inserted = 0;

        if (item == null || amount <= 0 || slotIndex < 0 || slotIndex >= slots.Count)
        {
            return amount;
        }

        int maxStack = Mathf.Max(1, item.MaxStack);
        Slot slot = slots[slotIndex];

        if (!slot.IsEmpty && slot.item != item)
        {
            return amount;
        }

        int currentAmount = slot.IsEmpty ? 0 : slot.amount;
        if (currentAmount >= maxStack)
        {
            return amount;
        }

        inserted = Mathf.Min(maxStack - currentAmount, amount);
        return amount - inserted;
    }

    private int ApplyAddToSingleSlot(ItemDefinition item, int amount, int slotIndex, out int inserted)
    {
        inserted = 0;

        int remaining = SimulateAddToSingleSlot(item, amount, slotIndex, out inserted);
        if (inserted <= 0 || slotIndex < 0 || slotIndex >= slots.Count)
        {
            return remaining;
        }

        Slot slot = slots[slotIndex];
        if (slot.IsEmpty)
        {
            slot.item = item;
            slot.amount = inserted;
        }
        else
        {
            slot.amount += inserted;
        }

        slots[slotIndex] = slot;
        return remaining;
    }

    private int SimulateAddToRange(ItemDefinition item, int amount, int startInclusive, int endExclusive, int skipIndex)
    {
        if (amount <= 0)
        {
            return 0;
        }

        int remaining = amount;
        int start = Mathf.Clamp(startInclusive, 0, slots.Count);
        int end = Mathf.Clamp(endExclusive, 0, slots.Count);

        if (end <= start)
        {
            return remaining;
        }

        int maxStack = Mathf.Max(1, item.MaxStack);

        for (int i = start; i < end && remaining > 0; i++)
        {
            if (i == skipIndex)
            {
                continue;
            }

            Slot slot = slots[i];
            if (slot.item != item || slot.amount >= maxStack)
            {
                continue;
            }

            int freeSpace = maxStack - slot.amount;
            int toInsert = Mathf.Min(freeSpace, remaining);
            remaining -= toInsert;
        }

        for (int i = start; i < end && remaining > 0; i++)
        {
            if (i == skipIndex)
            {
                continue;
            }

            Slot slot = slots[i];
            if (!slot.IsEmpty)
            {
                continue;
            }

            int toInsert = Mathf.Min(maxStack, remaining);
            remaining -= toInsert;
        }

        return remaining;
    }

    private int ApplyAddToRange(ItemDefinition item, int amount, int startInclusive, int endExclusive, int skipIndex)
    {
        if (amount <= 0)
        {
            return 0;
        }

        int remaining = amount;
        int start = Mathf.Clamp(startInclusive, 0, slots.Count);
        int end = Mathf.Clamp(endExclusive, 0, slots.Count);

        if (end <= start)
        {
            return remaining;
        }

        int maxStack = Mathf.Max(1, item.MaxStack);

        for (int i = start; i < end && remaining > 0; i++)
        {
            if (i == skipIndex)
            {
                continue;
            }

            Slot slot = slots[i];
            if (slot.item != item || slot.amount >= maxStack)
            {
                continue;
            }

            int freeSpace = maxStack - slot.amount;
            int toInsert = Mathf.Min(freeSpace, remaining);
            slot.amount += toInsert;
            slots[i] = slot;
            remaining -= toInsert;
        }

        for (int i = start; i < end && remaining > 0; i++)
        {
            if (i == skipIndex)
            {
                continue;
            }

            Slot slot = slots[i];
            if (!slot.IsEmpty)
            {
                continue;
            }

            int toInsert = Mathf.Min(maxStack, remaining);
            slot.item = item;
            slot.amount = toInsert;
            slots[i] = slot;
            remaining -= toInsert;
        }

        return remaining;
    }

    private void EnsureCapacity()
    {
        if (slots == null)
        {
            slots = new List<Slot>();
        }

        while (slots.Count < capacity)
        {
            slots.Add(new Slot());
        }

        if (slots.Count > capacity)
        {
            slots.RemoveRange(capacity, slots.Count - capacity);
        }
    }

    private void ClampSelection()
    {
        if (slots.Count == 0)
        {
            selectedSlotIndex = 0;
            return;
        }

        selectedSlotIndex = Mathf.Clamp(selectedSlotIndex, 0, slots.Count - 1);
    }

    private void NotifyChanged()
    {
        InventoryChanged?.Invoke();
        SelectedSlotChanged?.Invoke(selectedSlotIndex, slots[selectedSlotIndex]);
    }
}
