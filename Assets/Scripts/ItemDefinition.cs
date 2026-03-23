using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Items/Item Definition", fileName = "ItemDefinition")]
public class ItemDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string itemId = "new_item";
    [SerializeField] private string displayName = "New Item";

    [Header("Stack")]
    [SerializeField, Min(1)] private int maxStack = 1;

    [Header("World")]
    [SerializeField] private GameObject worldPrefab;

    [Header("Hold Pose")]
    [SerializeField] private Vector3 holdLocalPosition;
    [SerializeField] private Vector3 holdLocalEulerAngles;

    [Header("Physics Defaults")]
    [SerializeField, Min(0f)] private float linearDamping = 1.8f;
    [SerializeField, Min(0f)] private float angularDamping = 2.4f;

    public string ItemId => itemId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public int MaxStack => Mathf.Max(1, maxStack);
    public GameObject WorldPrefab => worldPrefab;
    public Vector3 HoldLocalPosition => holdLocalPosition;
    public Vector3 HoldLocalEulerAngles => holdLocalEulerAngles;
    public float LinearDamping => Mathf.Max(0f, linearDamping);
    public float AngularDamping => Mathf.Max(0f, angularDamping);

    private void OnValidate()
    {
        maxStack = Mathf.Max(1, maxStack);

        if (string.IsNullOrWhiteSpace(itemId))
        {
            itemId = SanitizeId(name);
        }
    }

    public static string SanitizeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "item";
        }

        string lower = value.Trim().ToLowerInvariant();
        char[] chars = lower.ToCharArray();

        for (int i = 0; i < chars.Length; i++)
        {
            bool valid = (chars[i] >= 'a' && chars[i] <= 'z')
                || (chars[i] >= '0' && chars[i] <= '9')
                || chars[i] == '_';

            if (!valid)
            {
                chars[i] = '_';
            }
        }

        string result = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "item" : result;
    }
}
