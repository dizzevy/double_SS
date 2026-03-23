using System.Collections.Generic;
using UnityEngine;

public static class ItemDefinitionRegistry
{
    private static Dictionary<string, ItemDefinition> definitionsById;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        definitionsById = null;
    }

    public static ItemDefinition GetById(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        EnsureCache();
        string normalized = ItemDefinition.SanitizeId(itemId);

        definitionsById.TryGetValue(normalized, out ItemDefinition definition);
        return definition;
    }

    private static void EnsureCache()
    {
        if (definitionsById != null)
        {
            return;
        }

        definitionsById = new Dictionary<string, ItemDefinition>();
        ItemDefinition[] allDefinitions = Resources.LoadAll<ItemDefinition>("ItemDefinitions");

        for (int i = 0; i < allDefinitions.Length; i++)
        {
            ItemDefinition definition = allDefinitions[i];
            if (definition == null || string.IsNullOrWhiteSpace(definition.ItemId))
            {
                continue;
            }

            string normalized = ItemDefinition.SanitizeId(definition.ItemId);
            if (!definitionsById.ContainsKey(normalized))
            {
                definitionsById.Add(normalized, definition);
            }
        }
    }
}
