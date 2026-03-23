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
    [SerializeField, Min(1)] private int capacity = 16;
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
