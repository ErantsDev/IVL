using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class FixMagentaMaterials
{
    sealed class Snap
    {
        public string Path;
        public string SourceShader;
        public Texture MainTex;
        public Vector2 MainTexScale = Vector2.one;
        public Vector2 MainTexOffset = Vector2.zero;
        public Color Color = Color.white;
        public Texture Normal;
        public Texture MetallicGloss;
        public Texture Emission;
        public Color EmissionColor = Color.black;
        public bool HasEmissionKeyword;
        public float Cutoff = 0.5f;
        public int Mode; // 0 Opaque, 1 Cutout, 2 Fade, 3 Transparent
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        var urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            return "ERROR: Universal Render Pipeline/Lit shader not found.";
        }

        var guids = AssetDatabase.FindAssets("t:Material");
        var snapshots = new List<Snap>();

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Packages/") || path.StartsWith("Library/PackageCache/"))
                continue;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null)
                continue;

            if (mat.shader.name != "Standard" && mat.shader.name != "Standard (Specular setup)")
                continue;

            snapshots.Add(new Snap
            {
                Path = path,
                SourceShader = mat.shader.name,
                MainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null,
                MainTexScale = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one,
                MainTexOffset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero,
                Color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white,
                Normal = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null,
                MetallicGloss = mat.HasProperty("_MetallicGlossMap") ? mat.GetTexture("_MetallicGlossMap") : null,
                Emission = mat.HasProperty("_EmissionMap") ? mat.GetTexture("_EmissionMap") : null,
                EmissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black,
                HasEmissionKeyword = System.Array.IndexOf(mat.shaderKeywords, "_EMISSION") >= 0,
                Cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f,
                Mode = mat.HasProperty("_Mode") ? (int)mat.GetFloat("_Mode") : 0
            });
        }

        sb.AppendLine($"Found {snapshots.Count} Standard-shader materials to convert.");

        int okCount = 0, warnCount = 0;

        foreach (var snap in snapshots)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(snap.Path);
            if (mat == null)
            {
                sb.AppendLine($"SKIP (reload failed): {snap.Path}");
                warnCount++;
                continue;
            }

            mat.shader = urpLit;

            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", snap.MainTex);
                mat.SetTextureScale("_BaseMap", snap.MainTexScale);
                mat.SetTextureOffset("_BaseMap", snap.MainTexOffset);
            }
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", snap.Color);

            if (snap.Normal != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", snap.Normal);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (snap.MetallicGloss != null && mat.HasProperty("_MetallicGlossMap"))
            {
                mat.SetTexture("_MetallicGlossMap", snap.MetallicGloss);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            if ((snap.Emission != null || snap.HasEmissionKeyword) && mat.HasProperty("_EmissionColor"))
            {
                if (snap.Emission != null && mat.HasProperty("_EmissionMap"))
                    mat.SetTexture("_EmissionMap", snap.Emission);
                mat.SetColor("_EmissionColor", snap.EmissionColor);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            if (mat.HasProperty("_Cutoff"))
                mat.SetFloat("_Cutoff", snap.Cutoff);

            // Surface type: preserve Cutout / Fade / Transparent from the source Standard material.
            if (snap.Mode == 1) // Cutout
            {
                mat.SetFloat("_Surface", 0);
                mat.SetFloat("_AlphaClip", 1);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetOverrideTag("RenderType", "TransparentCutout");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            else if (snap.Mode == 2 || snap.Mode == 3) // Fade / Transparent
            {
                mat.SetFloat("_Surface", 1);
                mat.SetFloat("_Blend", 0); // alpha
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else // Opaque
            {
                mat.SetFloat("_Surface", 0);
                mat.SetInt("_ZWrite", 1);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "");
                mat.renderQueue = -1;
            }

            EditorUtility.SetDirty(mat);

            bool textureOk = snap.MainTex == null || (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null);
            if (mat.shader.name != "Universal Render Pipeline/Lit" || !textureOk)
            {
                sb.AppendLine($"WARN: {snap.Path} -> shader={mat.shader.name}, textureOk={textureOk}");
                warnCount++;
            }
            else
            {
                okCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        sb.AppendLine($"Converted OK: {okCount}, Warnings: {warnCount}");

        // Verification pass: re-load each and confirm shader landed correctly.
        foreach (var snap in snapshots)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(snap.Path);
            if (mat == null || mat.shader == null || mat.shader.name != "Universal Render Pipeline/Lit")
            {
                sb.AppendLine($"VERIFY-FAIL: {snap.Path} shader={(mat != null && mat.shader != null ? mat.shader.name : "NULL")}");
            }
        }

        return sb.ToString();
    }
}
