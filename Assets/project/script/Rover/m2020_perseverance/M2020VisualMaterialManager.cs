using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// VISUAL ONLY – assigns NASA/JPL-inspired materials to the Perseverance model.
/// Does not touch ArticulationBodies, colliders, joints, hierarchy, or names.
///
/// Call ApplyTo(roverRoot) after the robot is instantiated.
/// </summary>
public class M2020VisualMaterialManager : MonoBehaviour
{
    [Header("Optional: auto-apply to this transform's children on Start")]
    public bool applyOnStart = false;

    // Runtime material library
    private Material matBodyLight;
    private Material matMobilityMetal;
    private Material matWheelAluminum;
    private Material matWheelDark;
    private Material matMechanicalDark;
    private Material matInstrumentDarkGray;
    private Material matLensBlack;
    private Material matThermalGold;
    private Material matNeutralGray;

    private bool libraryBuilt;

    // Counters for console summary
    private int cBody, cMobility, cWheel, cWheelDark, cMech, cInstr, cGold, cNeutral, cUnclassified;
    private readonly List<string> unclassifiedNames = new List<string>();

    void Start()
    {
        if (applyOnStart)
            ApplyTo(transform);
    }

    /// <summary>
    /// Main entry: walk every Renderer under root and assign classified materials.
    /// Safe to call multiple times; builds the material library once.
    /// </summary>
    public void ApplyTo(Transform root)
    {
        if (root == null)
        {
            Debug.LogWarning("[M2020 Visuals] root is null");
            return;
        }

        EnsureLibrary();
        ResetCounters();

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            // Skip particle systems etc. if any
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer))
                continue;

            Material chosen = Classify(r.gameObject);
            AssignInstance(r, chosen);
        }

        Debug.Log(
            "[M2020 Visuals]\n" +
            $"  Body materials assigned: {cBody}\n" +
            $"  Mobility materials assigned: {cMobility}\n" +
            $"  Wheel aluminum assigned: {cWheel}\n" +
            $"  Wheel dark assigned: {cWheelDark}\n" +
            $"  Dark mechanical assigned: {cMech}\n" +
            $"  Instrument materials assigned: {cInstr}\n" +
            $"  Gold accent materials assigned: {cGold}\n" +
            $"  Neutral/fallback assigned: {cNeutral}\n" +
            $"  Unclassified renderers: {cUnclassified}"
        );

        if (unclassifiedNames.Count > 0)
        {
            Debug.Log("[M2020 Visuals] Unclassified (using neutral gray):\n  " +
                      string.Join("\n  ", unclassifiedNames));
        }
    }

    // ------------------------------------------------------------------
    // Classification
    // ------------------------------------------------------------------
    private Material Classify(GameObject go)
    {
        string n = go.name;
        string path = BuildPath(go.transform);

        // --- Wheels (exact Body_Wheel* first) ---
        if (StartsWith(n, "Body_Wheel") || Contains(n, "Wheel_Left") || Contains(n, "Wheel_Right")
            || Contains(path, "Body_Wheel"))
        {
            // Child hub / interior pieces if named as such
            if (Contains(n, "Hub") || Contains(n, "Interior") || Contains(n, "Spoke")
                || Contains(n, "Rim_Inner") || Contains(n, "Dark"))
            {
                cWheelDark++;
                return matWheelDark;
            }
            cWheel++;
            return matWheelAluminum;
        }

        // Frame_WHEEL markers – dark mechanical
        if (StartsWith(n, "Frame_WHEEL") || Contains(n, "Frame_WHEEL"))
        {
            cMech++;
            return matMechanicalDark;
        }

        // --- Main chassis ---
        if (n == "Body_Chassis" || Contains(n, "CHASSIS") || Contains(n, "Chassis"))
        {
            cBody++;
            return matBodyLight;
        }

        // --- Mobility structure ---
        if (IsMobility(n, path))
        {
            cMobility++;
            return matMobilityMetal;
        }

        // --- Robotic arm ---
        if (Contains(n, "RA_") || Contains(n, "Body_RA") || Contains(n, "Arm_")
            || Contains(path, "Body_RA") || Contains(n, "JOINT"))
        {
            // Turret / corer / stabilizer on arm stack
            if (Contains(n, "Turret") || Contains(n, "Corer") || Contains(n, "Stabilizer")
                || Contains(n, "Drill") || Contains(n, "WATSON") || Contains(n, "SHERLOC")
                || Contains(n, "PIXL"))
            {
                return ClassifyInstrumentOrDetail(n, path);
            }
            // Main arm links: mobility metal with light accents on larger links
            if (Contains(n, "Link1") || Contains(n, "Link2") || Contains(n, "RA_Base")
                || Contains(n, "Body_RA_Base"))
            {
                cMobility++;
                return matMobilityMetal;
            }
            if (Contains(n, "Link3") || Contains(n, "Link4") || Contains(n, "Link5"))
            {
                cBody++;
                return matBodyLight;
            }
            cMobility++;
            return matMobilityMetal;
        }

        // --- Turret / science hardware ---
        if (Contains(n, "Turret") || Contains(n, "Body_Turret"))
        {
            cBody++;
            return matBodyLight;
        }
        if (Contains(n, "Corer") || Contains(n, "Stabilizer") || Contains(n, "DrillCorrer")
            || Contains(n, "Abrading") || Contains(n, "Regolith") || Contains(n, "LaunchBit")
            || Contains(n, "CoringBit"))
        {
            cMobility++;
            return matMobilityMetal;
        }

        // --- Mast / RSM / HGA / antennas ---
        if (Contains(n, "RSM") || Contains(n, "HGA") || Contains(n, "Antenna")
            || Contains(n, "Body_RSM") || Contains(n, "Body_HGA"))
        {
            return ClassifyInstrumentOrDetail(n, path);
        }

        // Camera / instrument housings
        if (Contains(n, "Camera") || Contains(n, "WATSON") || Contains(n, "SHERLOC")
            || Contains(n, "PIXL") || Contains(n, "Imager") || Contains(n, "Lens")
            || Contains(n, "FOV") || Contains(n, "NCL") || Contains(n, "NCR")
            || Contains(n, "MCL") || Contains(n, "MCR") || Contains(n, "SCI")
            || Contains(n, "FHC") || Contains(n, "RHC"))
        {
            return ClassifyInstrumentOrDetail(n, path);
        }

        // Dark frames / calibration / small mounts
        if (StartsWith(n, "Frame_") || Contains(n, "Frame_"))
        {
            // Optical FOV / lens-like frames
            if (Contains(n, "FOV") || Contains(n, "Lens") || Contains(n, "ANT"))
            {
                cMech++;
                return matLensBlack;
            }
            cMech++;
            return matMechanicalDark;
        }

        // Debris shield / miscellaneous body pieces
        if (Contains(n, "DebrisShield") || Contains(n, "MHS"))
        {
            cBody++;
            return matBodyLight;
        }

        // Thermal / gold candidates (restrained)
        if (Contains(n, "Thermal") || Contains(n, "Gold") || Contains(n, "MLI")
            || Contains(n, "Blanket") || Contains(n, "Cable"))
        {
            cGold++;
            return matThermalGold;
        }

        // Unknown – neutral + report
        cUnclassified++;
        if (unclassifiedNames.Count < 40)
            unclassifiedNames.Add(path);
        cNeutral++;
        return matNeutralGray;
    }

    private Material ClassifyInstrumentOrDetail(string n, string path)
    {
        if (Contains(n, "Lens") || Contains(n, "FOV") || Contains(n, "Cap")
            || Contains(n, "Opening") || Contains(n, "Aperture"))
        {
            cMech++;
            return matLensBlack;
        }
        if (Contains(n, "Head") || Contains(n, "Housing") || Contains(n, "Cover"))
        {
            cInstr++;
            return matInstrumentDarkGray;
        }
        // RSM / HGA main structure
        if (Contains(n, "Body_RSM") || Contains(n, "Body_HGA") || Contains(n, "RSM_AZ")
            || Contains(n, "RSM_EL") || Contains(n, "HGA_AZ") || Contains(n, "HGA_EL"))
        {
            cInstr++;
            return matInstrumentDarkGray;
        }
        cInstr++;
        return matInstrumentDarkGray;
    }

    private static bool IsMobility(string n, string path)
    {
        return n == "Body_Differential" || n == "Body_RockerLeft" || n == "Body_RockerRight"
            || n == "Body_BogieLeft" || n == "Body_BogieRight"
            || n == "Body_SteerLeftFront" || n == "Body_SteerLeftRear"
            || n == "Body_SteerRightFront" || n == "Body_SteerRightRear"
            || Contains(n, "Rocker") || Contains(n, "Bogie") || Contains(n, "Differential")
            || Contains(n, "Steer_Left") || Contains(n, "Steer_Right")
            || Contains(n, "Body_Steer");
    }

    // ------------------------------------------------------------------
    // Material library
    // ------------------------------------------------------------------
    private void EnsureLibrary()
    {
        if (libraryBuilt) return;
        libraryBuilt = true;

        Shader shader = FindLitShader();

        // A. Main chassis – light warm gray / off-white
        matBodyLight = MakeMat(shader, "M2020_Body_Light",
            new Color(199f / 255f, 198f / 255f, 191f / 255f),
            metallic: 0.22f, smoothness: 0.55f);

        // B. Mobility – medium metallic gray
        matMobilityMetal = MakeMat(shader, "M2020_Mobility_Metal",
            new Color(119f / 255f, 122f / 255f, 124f / 255f),
            metallic: 0.75f, smoothness: 0.45f);

        // C. Wheels – aluminum
        matWheelAluminum = MakeMat(shader, "M2020_Wheel_Aluminum",
            new Color(166f / 255f, 168f / 255f, 165f / 255f),
            metallic: 0.82f, smoothness: 0.42f);

        // D. Wheel hubs / dark interiors
        matWheelDark = MakeMat(shader, "M2020_Wheel_Dark",
            new Color(52f / 255f, 54f / 255f, 56f / 255f),
            metallic: 0.55f, smoothness: 0.30f);

        // E. Dark mechanical details
        matMechanicalDark = MakeMat(shader, "M2020_Mechanical_Dark",
            new Color(32f / 255f, 34f / 255f, 36f / 255f),
            metallic: 0.40f, smoothness: 0.25f);

        // F. Instrument housings
        matInstrumentDarkGray = MakeMat(shader, "M2020_Instrument_DarkGray",
            new Color(95f / 255f, 98f / 255f, 99f / 255f),
            metallic: 0.50f, smoothness: 0.40f);

        // G. Lens / optical openings
        matLensBlack = MakeMat(shader, "M2020_Lens_Black",
            new Color(17f / 255f, 19f / 255f, 21f / 255f),
            metallic: 0.20f, smoothness: 0.55f);

        // H. Muted thermal gold (accent only)
        matThermalGold = MakeMat(shader, "M2020_Thermal_Gold",
            new Color(181f / 255f, 138f / 255f, 69f / 255f),
            metallic: 0.75f, smoothness: 0.45f);

        // Fallback neutral
        matNeutralGray = MakeMat(shader, "M2020_Neutral_Gray",
            new Color(0.55f, 0.55f, 0.56f),
            metallic: 0.30f, smoothness: 0.40f);
    }

    private static Shader FindLitShader()
    {
        // Prefer URP Lit, fall back to Built-in Standard
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        if (s != null) return s;
        s = Shader.Find("Standard");
        if (s != null) return s;
        s = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (s != null) return s;
        Debug.LogWarning("[M2020 Visuals] No Lit/Standard shader found – using Diffuse");
        return Shader.Find("Diffuse") ?? Shader.Find("Sprites/Default");
    }

    private static Material MakeMat(Shader shader, string name, Color color, float metallic, float smoothness)
    {
        var m = new Material(shader);
        m.name = name;

        // Built-in Standard
        if (m.HasProperty("_Color"))
            m.SetColor("_Color", color);
        if (m.HasProperty("_MainColor"))
            m.SetColor("_MainColor", color);

        // URP Lit
        if (m.HasProperty("_BaseColor"))
            m.SetColor("_BaseColor", color);

        if (m.HasProperty("_Metallic"))
            m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Glossiness"))
            m.SetFloat("_Glossiness", smoothness);          // Built-in
        if (m.HasProperty("_Smoothness"))
            m.SetFloat("_Smoothness", smoothness);          // URP

        // Slightly reduce specular intensity if available (less chrome)
        if (m.HasProperty("_SpecularHighlights"))
            m.SetFloat("_SpecularHighlights", 1f);

        return m;
    }

    /// <summary>
    /// Assign a unique material instance so we never mutate a shared URDF material.
    /// </summary>
    private static void AssignInstance(Renderer r, Material source)
    {
        if (r == null || source == null) return;

        // Multi-material meshes: apply same classified material to all slots
        // (single-mesh wheels stay aluminum as required)
        var mats = r.sharedMaterials;
        if (mats == null || mats.Length == 0)
        {
            r.sharedMaterial = source;
            return;
        }

        var instances = new Material[mats.Length];
        for (int i = 0; i < mats.Length; i++)
            instances[i] = source;
        r.sharedMaterials = instances;
    }

    private void ResetCounters()
    {
        cBody = cMobility = cWheel = cWheelDark = cMech = cInstr = cGold = cNeutral = cUnclassified = 0;
        unclassifiedNames.Clear();
    }

    private static string BuildPath(Transform t)
    {
        var parts = new List<string>();
        while (t != null)
        {
            parts.Add(t.name);
            t = t.parent;
            if (parts.Count > 8) break;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static bool Contains(string hay, string needle)
        => hay != null && hay.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool StartsWith(string hay, string needle)
        => hay != null && hay.StartsWith(needle, System.StringComparison.OrdinalIgnoreCase);
}
