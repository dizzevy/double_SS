using System.Collections.Generic;
using UnityEngine;

public static class ToonRuntimeStyleBootstrap
{
    private const string ToonShaderName = "DoubleSS/Toon Lit Outline";
    private const float ShadeSteps = 3f;
    private const float ShadowStrength = 0.48f;
    private const float AmbientStrength = 1.05f;
    private const float BandPower = 1.35f;
    private const float Saturation = 0.76f;
    private const float Brightness = 0.88f;
    private const float OutlineThickness = 0f;
    private const float RimLineStart = 0.58f;
    private const float RimLineStrength = 1f;
    private const float RimNormalBoost = 7f;

    private static readonly Color OutlineColor = Color.black;

    private static readonly Dictionary<Material, Material> RuntimeToonCache = new Dictionary<Material, Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyToScene()
    {
        Shader toonShader = Shader.Find(ToonShaderName);
        if (toonShader == null)
        {
            Debug.LogWarning("Toon shader was not found: " + ToonShaderName);
            return;
        }

        Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (renderers == null || renderers.Length == 0)
        {
            return;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer rendererComponent = renderers[i];
            if (rendererComponent == null)
            {
                continue;
            }

            if (rendererComponent is ParticleSystemRenderer || rendererComponent is TrailRenderer || rendererComponent is LineRenderer)
            {
                continue;
            }

            Material[] sourceMaterials = rendererComponent.sharedMaterials;
            if (sourceMaterials == null || sourceMaterials.Length == 0)
            {
                continue;
            }

            bool changed = false;

            for (int m = 0; m < sourceMaterials.Length; m++)
            {
                Material sourceMaterial = sourceMaterials[m];
                if (sourceMaterial == null)
                {
                    continue;
                }

                if (ShouldSkipMaterial(sourceMaterial))
                {
                    continue;
                }

                Material toonMaterial = GetOrCreateToonMaterial(sourceMaterial, toonShader);
                if (toonMaterial == null || ReferenceEquals(toonMaterial, sourceMaterial))
                {
                    continue;
                }

                sourceMaterials[m] = toonMaterial;
                changed = true;
            }

            if (changed)
            {
                rendererComponent.sharedMaterials = sourceMaterials;
            }

        }

        Debug.Log("Toon style applied to scene renderers.");
    }

    private static bool ShouldSkipMaterial(Material material)
    {
        if (material == null || material.shader == null)
        {
            return true;
        }

        if (material.shader.name == ToonShaderName)
        {
            return false;
        }

        if (material.renderQueue >= 3000)
        {
            return true;
        }

        string shaderName = material.shader.name;
        return shaderName.Contains("Particles")
               || shaderName.Contains("UI")
               || shaderName.Contains("Skybox")
               || shaderName.Contains("Sprite");
    }

    private static Material GetOrCreateToonMaterial(Material source, Shader toonShader)
    {
        if (source == null || toonShader == null)
        {
            return null;
        }

        if (source.shader != null && source.shader.name == ToonShaderName)
        {
            ApplyToonTuning(source);
            return source;
        }

        if (RuntimeToonCache.TryGetValue(source, out Material cached) && cached != null)
        {
            ApplyToonTuning(cached);
            return cached;
        }

        Material toonMaterial = new Material(toonShader)
        {
            name = source.name + "_ToonRuntime"
        };

        if (TryGetBaseTexture(source, out Texture texture, out Vector2 scale, out Vector2 offset))
        {
            toonMaterial.SetTexture("_BaseMap", texture);
            toonMaterial.SetTextureScale("_BaseMap", scale);
            toonMaterial.SetTextureOffset("_BaseMap", offset);
        }

        toonMaterial.SetColor("_BaseColor", TryGetBaseColor(source));
        ApplyToonTuning(toonMaterial);

        RuntimeToonCache[source] = toonMaterial;
        return toonMaterial;
    }

    private static void ApplyToonTuning(Material material)
    {
        if (material == null)
        {
            return;
        }

        SetFloatIfExists(material, "_ShadeSteps", ShadeSteps);
        SetFloatIfExists(material, "_ShadowStrength", ShadowStrength);
        SetFloatIfExists(material, "_AmbientStrength", AmbientStrength);
        SetFloatIfExists(material, "_BandPower", BandPower);
        SetFloatIfExists(material, "_Saturation", Saturation);
        SetFloatIfExists(material, "_Brightness", Brightness);
        SetFloatIfExists(material, "_OutlineThickness", OutlineThickness);
        SetFloatIfExists(material, "_RimLineStart", RimLineStart);
        SetFloatIfExists(material, "_RimLineStrength", RimLineStrength);
        SetFloatIfExists(material, "_RimNormalBoost", RimNormalBoost);
        SetColorIfExists(material, "_OutlineColor", OutlineColor);
    }

    private static void SetFloatIfExists(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private static void SetColorIfExists(Material material, string propertyName, Color value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetColor(propertyName, value);
        }
    }

    private static bool TryGetBaseTexture(Material source, out Texture texture, out Vector2 scale, out Vector2 offset)
    {
        texture = null;
        scale = Vector2.one;
        offset = Vector2.zero;

        if (source.HasProperty("_BaseMap"))
        {
            texture = source.GetTexture("_BaseMap");
            scale = source.GetTextureScale("_BaseMap");
            offset = source.GetTextureOffset("_BaseMap");
            return true;
        }

        if (source.HasProperty("_MainTex"))
        {
            texture = source.GetTexture("_MainTex");
            scale = source.GetTextureScale("_MainTex");
            offset = source.GetTextureOffset("_MainTex");
            return true;
        }

        return false;
    }

    private static Color TryGetBaseColor(Material source)
    {
        if (source.HasProperty("_BaseColor"))
        {
            return source.GetColor("_BaseColor");
        }

        if (source.HasProperty("_Color"))
        {
            return source.GetColor("_Color");
        }

        return Color.white;
    }
}
