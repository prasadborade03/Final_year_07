using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Universal URP material fixer for URDF-imported rovers (Husky, M20, and any future rover).
/// Called at runtime after SpawnRover() to replace every Built-in Standard material
/// (or any pink/error material) with a proper URP Lit material.
///
/// For M2020, the dedicated M2020VisualMaterialManager is preferred — this script
/// provides the fallback for Husky and M20 which have no per-renderer classification.
/// </summary>
public class RoverURPMaterialFixer : MonoBehaviour
{
    [Header("Optional auto-apply on Start")]
    public bool applyOnStart = false;

    // ── Husky colour scheme ──────────────────────────────────────────────────
    // Clearpath Husky A200: yellow/black industrial field robot
    private static readonly RoverScheme HuskyScheme = new RoverScheme
    {
        name = "Husky",
        defaultColor   = new Color(0.95f, 0.73f, 0.07f),   // Clearpath yellow
        darkColor      = new Color(0.10f, 0.10f, 0.10f),   // chassis black
        metalColor     = new Color(0.60f, 0.60f, 0.62f),   // aluminium frame
        defaultMetal   = 0.20f, defaultSmooth = 0.40f,
        darkMetal      = 0.30f, darkSmooth    = 0.25f,
        metalMetal     = 0.80f, metalSmooth   = 0.50f,
        // Name-based dark triggers
        darkKeywords   = new[]{ "black", "dark", "frame", "hub", "axle", "bracket", "mount" },
        metalKeywords  = new[]{ "grey", "gray", "metal", "wheel", "link", "arm", "chassis" },
    };

    // ── M20 colour scheme ────────────────────────────────────────────────────
    // Deep Robotics M20: matte dark body, silver joints, yellow accents
    private static readonly RoverScheme M20Scheme = new RoverScheme
    {
        name = "M20",
        defaultColor   = new Color(0.25f, 0.27f, 0.30f),   // dark charcoal body
        darkColor      = new Color(0.08f, 0.08f, 0.09f),   // near-black joints
        metalColor     = new Color(0.65f, 0.66f, 0.67f),   // silver links
        defaultMetal   = 0.45f, defaultSmooth = 0.35f,
        darkMetal      = 0.35f, darkSmooth    = 0.20f,
        metalMetal     = 0.82f, metalSmooth   = 0.55f,
        darkKeywords   = new[]{ "black", "dark", "0-0-0", "knee", "hip" },
        metalKeywords  = new[]{ "grey", "gray", "metal", "wheel", "link", "thigh", "shin" },
    };

    // ─────────────────────────────────────────────────────────────────────────

    private struct RoverScheme
    {
        public string   name;
        public Color    defaultColor, darkColor, metalColor;
        public float    defaultMetal, defaultSmooth;
        public float    darkMetal,    darkSmooth;
        public float    metalMetal,   metalSmooth;
        public string[] darkKeywords;
        public string[] metalKeywords;
    }

    void Start()
    {
        if (applyOnStart)
            ApplyTo(transform);
    }

    /// <summary>
    /// Replaces all Built-in Standard / pink materials on roverRoot with proper URP Lit ones.
    /// Pass schemeHint "husky" or "m20" to get the correct colour palette.
    /// </summary>
    public void ApplyTo(Transform roverRoot, string schemeHint = "")
    {
        if (roverRoot == null) return;

        RoverScheme scheme = schemeHint.ToLower().Contains("husky") ? HuskyScheme : M20Scheme;
        Shader urpLit = FindURPLitShader();
        if (urpLit == null)
        {
            Debug.LogError("[RoverURPFixer] URP Lit shader not found – cannot fix rover materials!");
            return;
        }

        // Build a small material cache so identical-looking parts share one instance
        var matCache = new Dictionary<int, Material>();
        int fixed_count = 0;

        var renderers = roverRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;

            // Check if any current material needs fixing
            bool needsFix = false;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || IsBuiltInOrPink(m))
                { needsFix = true; break; }
            }
            if (!needsFix) continue;

            // Classify by GameObject name
            string n    = r.gameObject.name.ToLower();
            Color  col  = ClassifyColor(n, scheme, out float metal, out float smooth);

            int key = col.GetHashCode() ^ (int)(metal * 100) ^ (int)(smooth * 100);
            if (!matCache.TryGetValue(key, out Material mat))
            {
                mat = new Material(urpLit)
                {
                    name = $"{scheme.name}_URP_{fixed_count}"
                };
                SetURPLitProperties(mat, col, metal, smooth);
                matCache[key] = mat;
            }

            // Apply to all slots
            var slots = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < slots.Length; i++) slots[i] = mat;
            r.sharedMaterials = slots;
            fixed_count++;
        }

        Debug.Log($"[RoverURPFixer] {scheme.name}: fixed {fixed_count} renderer(s) → URP Lit.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsBuiltInOrPink(Material m)
    {
        if (m.shader == null) return true;
        string sname = m.shader.name;
        // Pink = error shader, or Built-in Standard/Diffuse
        return sname.Contains("Error")
            || sname.Contains("Hidden/InternalError")
            || sname == "Standard"
            || sname == "Standard (Specular setup)"
            || sname == "Diffuse"
            || sname == "Bumped Diffuse";
    }

    private static Color ClassifyColor(string nameLower, RoverScheme s,
                                       out float metal, out float smooth)
    {
        foreach (string kw in s.darkKeywords)
        {
            if (nameLower.Contains(kw))
            { metal = s.darkMetal; smooth = s.darkSmooth; return s.darkColor; }
        }
        foreach (string kw in s.metalKeywords)
        {
            if (nameLower.Contains(kw))
            { metal = s.metalMetal; smooth = s.metalSmooth; return s.metalColor; }
        }
        metal  = s.defaultMetal;
        smooth = s.defaultSmooth;
        return s.defaultColor;
    }

    private static void SetURPLitProperties(Material m, Color col, float metal, float smooth)
    {
        if (m.HasProperty("_BaseColor"))   m.SetColor("_BaseColor",   col);
        if (m.HasProperty("_Color"))       m.SetColor("_Color",       col);
        if (m.HasProperty("_Metallic"))    m.SetFloat("_Metallic",    metal);
        if (m.HasProperty("_Smoothness"))  m.SetFloat("_Smoothness",  smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness",  smooth);
        // Ensure opaque rendering mode
        if (m.HasProperty("_Surface"))     m.SetFloat("_Surface", 0f);
    }

    private static Shader FindURPLitShader()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        if (s != null) return s;
        s = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (s != null) return s;
        Debug.LogWarning("[RoverURPFixer] Universal Render Pipeline/Lit not found, trying Simple Lit");
        return null;
    }
}
