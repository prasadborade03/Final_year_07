using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using UnityEngine;
using Unity.Robotics.UrdfImporter;
using AutonomousRobotKit;

namespace AutonomousRobotKit
{
    /// <summary>
    /// Core runtime URDF & ZIP importer backend:
    /// 1. Extracts ZIP packages containing URDF, STL, OBJ, and other mesh files into a dedicated persistent directory.
    /// 2. Recursively indexes all mesh assets and builds an intelligent filename-to-disk-path lookup.
    /// 3. Runtime XML Sanitization:
    ///    - Rewrites package://, file://, and relative paths into verified file:// absolute paths so StlImporter & Assimp load meshes with 100% reliability.
    ///    - Replaces missing meshes or unsupported formats (.gltf/.glb) with fallback primitives so import NEVER halts or crashes.
    ///    - Auto-heals missing or zero masses and zero-inertia tensors.
    /// 4. Assembles the robot at runtime via UrdfRobotExtensions.
    /// 5. Applies physics stabilization (mass conditioning, 32/16 solver iterations, 6-DOF fixed joint locking).
    /// 6. Auto-configures controls (SimpleDifferentialDrive for wheeled rovers, ArticulatedRobotController with Stand stance for legged/quadrupeds).
    /// 7. Positions the robot upright onto the terrain and binds to camera/simulation controllers.
    /// </summary>
    public class RuntimeUrdfImporter : MonoBehaviour
    {
        private static RuntimeUrdfImporter _instance;
        public static RuntimeUrdfImporter Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[RuntimeUrdfImporter]");
                    _instance = go.AddComponent<RuntimeUrdfImporter>();
                    if (Application.isPlaying) DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                if (Application.isPlaying) DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Entry point to import a robot from a .zip or .urdf file path asynchronously.
        /// </summary>
        public static void ImportRobot(
            string filePath,
            Action<float, string> onProgress = null,
            Action<GameObject> onSuccess = null,
            Action<string> onError = null)
        {
            Instance.StartCoroutine(Instance.ImportRobotRoutine(filePath, onProgress, onSuccess, onError));
        }

        public IEnumerator ImportRobotRoutine(
            string filePath,
            Action<float, string> onProgress,
            Action<GameObject> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                onError?.Invoke("Selected file does not exist: " + filePath);
                yield break;
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".zip" && ext != ".urdf")
            {
                onError?.Invoke("Unsupported file format: " + ext + ". Please select a .zip or .urdf file.");
                yield break;
            }

            // ----------------------------------------------------
            // Path A: In Unity Editor (Play Mode & Edit Mode)
            // Use the Master Package Quality Pipeline (Assets/ImportedRovers, AssetDatabase meshes, URP Lit healing)
            // ----------------------------------------------------
#if UNITY_EDITOR
            if (Application.isEditor)
            {
                onProgress?.Invoke(0.15f, "Staging robot assets & auto-healing meshes...");
                yield return null;

                onProgress?.Invoke(0.45f, "Importing hierarchy & upgrading materials to URP Lit...");
                yield return null;

                GameObject editorRover = null;
                try
                {
                    var importerType = Type.GetType("RoverCompatibility.Editor.RoverContextMenuImporter, RoverCompatibility.Editor");
                    if (importerType != null)
                    {
                        var method = importerType.GetMethod("ImportRoverAtPath", BindingFlags.Public | BindingFlags.Static);
                        if (method != null)
                        {
                            editorRover = (GameObject)method.Invoke(null, new object[] { filePath, RoverMassProfile.AutoDetect, null });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[RuntimeUrdfImporter] Editor pipeline notice: " + ex.Message);
                }

                if (editorRover != null)
                {
                    onProgress?.Invoke(0.80f, "Stabilizing physics & setting up drivetrain...");
                    yield return null;

                    // Ensure all materials are upgraded to active pipeline (Standard / URP / HDRP)
                    UpgradeMaterialsToActivePipeline(editorRover);

                    // Physics stabilization & stance setup
                    StabilizeImportedRobot(editorRover);
                    ConfigureRobotControls(editorRover);
                    PositionRobotOnGround(editorRover);
                    HookIntoSimulation(editorRover);

                    onProgress?.Invoke(1.0f, "Ready!");
                    yield return new WaitForSeconds(0.2f);

                    onSuccess?.Invoke(editorRover);
                    yield break;
                }
            }
#endif

            // ----------------------------------------------------
            // Path B: Standalone Runtime Pipeline (PersistentDataPath fallback)
            // ----------------------------------------------------
            string workingDir = "";
            string urdfPath = "";

            onProgress?.Invoke(0.1f, "Preparing robot package...");
            yield return null;

            try
            {
                if (ext == ".zip")
                {
                    onProgress?.Invoke(0.2f, "Extracting robot archive...");
                    string robotFolderName = Path.GetFileNameWithoutExtension(filePath) + "_" + DateTime.UtcNow.Ticks;
                    workingDir = Path.Combine(Application.persistentDataPath, "RuntimeRovers", robotFolderName);

                    ExtractZipArchive(filePath, workingDir);

                    // Locate .urdf file in extracted tree
                    var urdfFiles = Directory.GetFiles(workingDir, "*.urdf", SearchOption.AllDirectories);
                    if (urdfFiles.Length == 0)
                    {
                        onError?.Invoke("No .urdf file was found inside the selected zip archive.");
                        yield break;
                    }

                    // Pick the primary URDF (prefer matching package name or largest valid robot XML)
                    urdfPath = PickPrimaryUrdf(urdfFiles, Path.GetFileNameWithoutExtension(filePath));
                }
                else
                {
                    urdfPath = filePath;
                    workingDir = Path.GetDirectoryName(filePath);
                }
            }
            catch (Exception ex)
            {
                onError?.Invoke("Failed to unpack or read robot archive: " + ex.Message);
                yield break;
            }

            yield return null;

            // ----------------------------------------------------
            // Step 2: Index Mesh Files & Sanitize URDF XML
            // ----------------------------------------------------
            onProgress?.Invoke(0.4f, "Sanitizing URDF & resolving mesh paths...");
            yield return null;

            string sanitizedUrdfPath = "";
            ImportSettings.axisType detectedAxis = ImportSettings.axisType.yAxis;

            try
            {
                sanitizedUrdfPath = SanitizeUrdfAtRuntime(urdfPath, workingDir, out detectedAxis);
            }
            catch (Exception ex)
            {
                onError?.Invoke("Error sanitizing URDF: " + ex.Message);
                yield break;
            }

            yield return null;

            // ----------------------------------------------------
            // Step 3: Instantiate Robot Game Object
            // ----------------------------------------------------
            onProgress?.Invoke(0.6f, "Assembling robot hierarchy & physics...");
            yield return null;

            var settings = new ImportSettings
            {
                chosenAxis = detectedAxis,
                convexMethod = ImportSettings.convexDecomposer.unity
            };

            // Set package root so any remaining relative paths resolve to the working directory
            UrdfAssetPathHandler.SetPackageRoot(Path.GetDirectoryName(sanitizedUrdfPath));

            GameObject robotObject = null;
            IEnumerator<GameObject> createEnum = null;

            try
            {
                // Force runtime mode so AssetDatabase calls are bypassed
                createEnum = UrdfRobotExtensions.Create(sanitizedUrdfPath, settings, loadStatus: false, forceRuntimeMode: true);
            }
            catch (Exception ex)
            {
                onError?.Invoke("Failed to initialize URDF creation pipeline: " + ex.Message);
                yield break;
            }

            while (true)
            {
                bool hasNext = false;
                try
                {
                    hasNext = createEnum.MoveNext();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[RuntimeUrdfImporter] Pipeline warning during link assembly: " + ex.Message);
                    break;
                }

                if (!hasNext) break;

                if (createEnum.Current != null)
                {
                    robotObject = createEnum.Current;
                }
                yield return null;
            }

            if (robotObject == null)
            {
                // Fallback attempt with CreateRuntime synchronous call
                try
                {
                    robotObject = UrdfRobotExtensions.CreateRuntime(sanitizedUrdfPath, settings);
                }
                catch (Exception ex)
                {
                    onError?.Invoke("Failed to construct robot: " + ex.Message);
                    yield break;
                }
            }

            if (robotObject == null)
            {
                onError?.Invoke("Robot could not be created from URDF.");
                yield break;
            }

            // Unparent from anything selected or created
            robotObject.transform.SetParent(null, true);

            // ----------------------------------------------------
            // Step 4: Physics Stabilization & Joint Conditioning
            // ----------------------------------------------------
            onProgress?.Invoke(0.8f, "Applying physics stabilization & mass conditioning...");
            yield return null;

            UpgradeMaterialsToActivePipeline(robotObject);
            StabilizeImportedRobot(robotObject);

            // ----------------------------------------------------
            // Step 5: Auto-Configure Controls & Posture
            // ----------------------------------------------------
            onProgress?.Invoke(0.9f, "Configuring drivetrain & stance...");
            yield return null;

            ConfigureRobotControls(robotObject);

            // ----------------------------------------------------
            // Step 6: Position on Terrain & Hook Into Simulation
            // ----------------------------------------------------
            PositionRobotOnGround(robotObject);

            HookIntoSimulation(robotObject);

            onProgress?.Invoke(1.0f, "Ready!");
            yield return new WaitForSeconds(0.2f);

            onSuccess?.Invoke(robotObject);
        }

        #region ZIP Extraction
        public static void ExtractZipArchive(string zipFilePath, string destinationDirectory)
        {
            if (!File.Exists(zipFilePath))
                throw new FileNotFoundException("Zip file not found: " + zipFilePath);

            if (!Directory.Exists(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            // 1. Try System.IO.Compression.ZipFile via reflection
            try
            {
                var asm = Assembly.Load("System.IO.Compression.FileSystem");
                var zipFileType = asm?.GetType("System.IO.Compression.ZipFile");
                var extractMethod = zipFileType?.GetMethod("ExtractToDirectory", new Type[] { typeof(string), typeof(string) });
                if (extractMethod != null)
                {
                    extractMethod.Invoke(null, new object[] { zipFilePath, destinationDirectory });
                    Debug.Log("[RuntimeUrdfImporter] Extracted zip via ZipFile reflection.");
                    return;
                }
            }
            catch { }

            // 2. Fallback to Windows PowerShell Expand-Archive
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -Command \"Expand-Archive -Path '{zipFilePath.Replace("'", "''")}' -DestinationPath '{destinationDirectory.Replace("'", "''")}' -Force\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var p = System.Diagnostics.Process.Start(psi);
                p.WaitForExit(45000);
                Debug.Log("[RuntimeUrdfImporter] Extracted zip via PowerShell.");
                return;
            }
            catch (Exception ex)
            {
                throw new Exception("Extraction failed: " + ex.Message);
            }
        }

        private static string PickPrimaryUrdf(string[] urdfFiles, string baseName)
        {
            if (urdfFiles.Length == 1) return urdfFiles[0];

            // 1. Check for filename match
            foreach (var f in urdfFiles)
            {
                if (Path.GetFileNameWithoutExtension(f).Equals(baseName, StringComparison.OrdinalIgnoreCase))
                    return f;
            }

            // 2. Check for URDF containing '<robot' tag and pick largest
            string best = urdfFiles[0];
            long maxLen = 0;
            foreach (var f in urdfFiles)
            {
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.Length > maxLen)
                    {
                        string head = File.ReadAllText(f).Substring(0, Mathf.Min(500, (int)fi.Length));
                        if (head.Contains("<robot"))
                        {
                            maxLen = fi.Length;
                            best = f;
                        }
                    }
                }
                catch { }
            }
            return best;
        }
        #endregion

        #region URDF XML Sanitization
        public static string SanitizeUrdfAtRuntime(string urdfPath, string workingDir, out ImportSettings.axisType detectedAxis)
        {
            detectedAxis = ImportSettings.axisType.yAxis;

            // Build dictionary of all files in working directory and urdf directory
            var searchDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir)) searchDirs.Add(workingDir);
            string urdfDir = Path.GetDirectoryName(urdfPath);
            if (!string.IsNullOrEmpty(urdfDir) && Directory.Exists(urdfDir))
            {
                searchDirs.Add(urdfDir);
                var parent = Directory.GetParent(urdfDir);
                if (parent != null && parent.Exists) searchDirs.Add(parent.FullName);
            }

            // Map: cleanFileName.ToLower() -> fullPath
            var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in searchDirs)
            {
                foreach (var f in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(f);
                    if (!fileMap.ContainsKey(name))
                    {
                        fileMap[name] = f.Replace('\\', '/');
                    }
                }
            }

            XDocument doc = XDocument.Load(urdfPath);
            var robotElem = doc.Root;
            if (robotElem == null || robotElem.Name.LocalName != "robot")
                throw new InvalidDataException("Invalid URDF: Root element is not <robot>.");

            // 1. Scan joints to detect axis
            int zAxisCount = 0;
            int yAxisCount = 0;
            foreach (var joint in robotElem.Elements("joint"))
            {
                var axisElem = joint.Element("axis");
                if (axisElem != null)
                {
                    string xyz = axisElem.Attribute("xyz")?.Value ?? "";
                    if (xyz.Contains("0 0 1") || xyz.Contains("0 0 -1")) zAxisCount++;
                    else if (xyz.Contains("0 1 0") || xyz.Contains("0 -1 0")) yAxisCount++;
                }
            }
            if (zAxisCount > yAxisCount) detectedAxis = ImportSettings.axisType.zAxis;

            // 2. Sanitize Links: Inertials & Meshes
            foreach (var link in robotElem.Elements("link"))
            {
                // Ensure valid inertial block
                var inertial = link.Element("inertial");
                if (inertial == null)
                {
                    link.Add(new XElement("inertial",
                        new XElement("mass", new XAttribute("value", "1.0")),
                        new XElement("inertia",
                            new XAttribute("ixx", "0.01"), new XAttribute("ixy", "0"), new XAttribute("ixz", "0"),
                            new XAttribute("iyy", "0.01"), new XAttribute("iyz", "0"), new XAttribute("izz", "0.01"))
                    ));
                }
                else
                {
                    var massElem = inertial.Element("mass");
                    if (massElem == null)
                    {
                        inertial.Add(new XElement("mass", new XAttribute("value", "1.0")));
                    }
                    else
                    {
                        if (float.TryParse(massElem.Attribute("value")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float m))
                        {
                            if (m <= 0.0001f) massElem.SetAttributeValue("value", "0.5");
                        }
                    }

                    var inertiaElem = inertial.Element("inertia");
                    if (inertiaElem == null)
                    {
                        inertial.Add(new XElement("inertia",
                            new XAttribute("ixx", "0.01"), new XAttribute("ixy", "0"), new XAttribute("ixz", "0"),
                            new XAttribute("iyy", "0.01"), new XAttribute("iyz", "0"), new XAttribute("izz", "0.01")));
                    }
                }

                // Sanitize Visuals and Collisions
                SanitizeGeometries(link.Elements("visual"), fileMap);
                SanitizeGeometries(link.Elements("collision"), fileMap);
            }

            // Save sanitized URDF
            string outDir = Path.GetDirectoryName(urdfPath);
            string sanitizedPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(urdfPath) + "_runtime_sanitized.urdf");
            doc.Save(sanitizedPath);

            return sanitizedPath;
        }

        private static void SanitizeGeometries(IEnumerable<XElement> elements, Dictionary<string, string> fileMap)
        {
            foreach (var elem in elements.ToList())
            {
                var geom = elem.Element("geometry");
                if (geom == null) continue;

                var mesh = geom.Element("mesh");
                if (mesh != null)
                {
                    string filename = mesh.Attribute("filename")?.Value ?? "";
                    if (string.IsNullOrEmpty(filename))
                    {
                        // Fallback box
                        mesh.Remove();
                        geom.Add(new XElement("box", new XAttribute("size", "0.1 0.1 0.1")));
                        continue;
                    }

                    string cleanName = Path.GetFileName(filename.Split('?')[0]);
                    string ext = Path.GetExtension(cleanName).ToLowerInvariant();

                    string matchedFile = null;
                    if (fileMap.TryGetValue(cleanName, out string foundPath))
                    {
                        matchedFile = foundPath;
                    }
                    else
                    {
                        // If .gltf/.glb, try finding an STL or OBJ with the same basename
                        string baseWithoutExt = Path.GetFileNameWithoutExtension(cleanName);
                        if (ext == ".gltf" || ext == ".glb")
                        {
                            if (fileMap.TryGetValue(baseWithoutExt + ".stl", out matchedFile) ||
                                fileMap.TryGetValue(baseWithoutExt + ".obj", out matchedFile) ||
                                fileMap.TryGetValue(baseWithoutExt + ".dae", out matchedFile))
                            {
                                // Found substitute
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(matchedFile) && File.Exists(matchedFile))
                    {
                        // Set file:// URI so UrdfAssetPathHandler returns direct filesystem path!
                        mesh.SetAttributeValue("filename", "file://" + matchedFile);
                    }
                    else
                    {
                        // File not on disk; replace with a clean primitive fallback to prevent abort
                        mesh.Remove();
                        geom.Add(new XElement("box", new XAttribute("size", "0.15 0.15 0.15")));
                    }
                }
            }
        }
        #endregion

        #region Post-Import Physics & Controls
        private static void StabilizeImportedRobot(GameObject robotRoot)
        {
            // Add or retrieve RobotPhysicsStabilizer
            var stabilizer = robotRoot.GetComponent<RobotPhysicsStabilizer>();
            if (stabilizer == null) stabilizer = robotRoot.AddComponent<RobotPhysicsStabilizer>();

            stabilizer.solverIterations = 32;
            stabilizer.solverVelocityIterations = 16;
            stabilizer.maxDepenetrationVelocity = 1.0f;
            stabilizer.conditionMassRatios = true;
            stabilizer.lockAllFixedJointAxes = true;
            stabilizer.enforceSingleAxisRevolute = true;
            stabilizer.ignoreInternalCollisions = true;

            stabilizer.ApplyStabilization();

            // Direct mass hierarchy conditioning to eliminate >12:1 parent-child mass divergence & joint tearing
            var bodies = robotRoot.GetComponentsInChildren<ArticulationBody>(true);
            if (bodies != null && bodies.Length > 0)
            {
                float totalMass = 0f;
                foreach (var b in bodies) if (b != null) totalMass += b.mass;
                float minAllowedMass = Mathf.Max(0.25f, totalMass * 0.015f);

                foreach (var b in bodies)
                {
                    if (b == null) continue;
                    b.solverIterations = Mathf.Max(b.solverIterations, 32);
                    b.solverVelocityIterations = Mathf.Max(b.solverVelocityIterations, 16);
                    b.maxDepenetrationVelocity = 1.0f;

                    if (b.mass < minAllowedMass) b.mass = minAllowedMass;

                    var parentBody = b.transform.parent != null ? b.transform.parent.GetComponentInParent<ArticulationBody>() : null;
                    if (parentBody != null && parentBody.mass > 0f)
                    {
                        float minByParent = parentBody.mass / 12f;
                        float maxByParent = parentBody.mass * 12f;
                        b.mass = Mathf.Clamp(b.mass, minByParent, maxByParent);
                    }

                    if (b.jointType == ArticulationJointType.FixedJoint)
                    {
                        b.matchAnchors = true;
                        b.linearLockX = ArticulationDofLock.LockedMotion;
                        b.linearLockY = ArticulationDofLock.LockedMotion;
                        b.linearLockZ = ArticulationDofLock.LockedMotion;
                        b.twistLock = ArticulationDofLock.LockedMotion;
                        b.swingYLock = ArticulationDofLock.LockedMotion;
                        b.swingZLock = ArticulationDofLock.LockedMotion;
                    }
                    else if (b.jointType == ArticulationJointType.RevoluteJoint)
                    {
                        b.linearLockX = ArticulationDofLock.LockedMotion;
                        b.linearLockY = ArticulationDofLock.LockedMotion;
                        b.linearLockZ = ArticulationDofLock.LockedMotion;
                        b.swingYLock = ArticulationDofLock.LockedMotion;
                        b.swingZLock = ArticulationDofLock.LockedMotion;
                    }
                }
            }
        }

        private static void UpgradeMaterialsToActivePipeline(GameObject robotRoot)
        {
            var currentRP = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline ??
                            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline ??
                            UnityEngine.QualitySettings.renderPipeline;
            bool isURP = currentRP != null && currentRP.GetType().Name.Contains("Universal");
            bool isHDRP = currentRP != null && currentRP.GetType().Name.Contains("HighDefinition");

            Shader targetShader = null;
            if (isURP)
            {
                targetShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("URP/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            }
            else if (isHDRP)
            {
                targetShader = Shader.Find("HDRP/Lit");
            }

            if (targetShader == null)
            {
                targetShader = Shader.Find("Standard") ?? Shader.Find("Mobile/Diffuse") ?? Shader.Find("Diffuse");
            }

            if (targetShader == null) return;

            var renderers = robotRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var mr in renderers)
            {
                if (mr == null) continue;
                var sharedMats = mr.sharedMaterials;
                if (sharedMats == null || sharedMats.Length == 0) continue;

                bool changed = false;
                for (int i = 0; i < sharedMats.Length; i++)
                {
                    var mat = sharedMats[i];
                    if (mat == null)
                    {
                        var newM = new Material(targetShader);
                        newM.name = $"{mr.name}_Mat_{i}";
                        Color defColor = new Color(0.75f, 0.75f, 0.78f, 1f);
                        if (newM.HasProperty("_BaseColor")) newM.SetColor("_BaseColor", defColor);
                        if (newM.HasProperty("_Color")) newM.SetColor("_Color", defColor);
                        if (newM.HasProperty("_Smoothness")) newM.SetFloat("_Smoothness", 0.5f);
                        if (newM.HasProperty("_Glossiness")) newM.SetFloat("_Glossiness", 0.5f);
                        if (newM.HasProperty("_Metallic")) newM.SetFloat("_Metallic", 0.2f);
                        sharedMats[i] = newM;
                        changed = true;
                    }
                    else if (mat.shader != targetShader)
                    {
                        Color c = Color.white;
                        if (mat.HasProperty("_BaseColor")) c = mat.GetColor("_BaseColor");
                        else if (mat.HasProperty("_Color")) c = mat.GetColor("_Color");
                        else c = mat.color;

                        // Prevent completely black/invisible links
                        if (c.r < 0.05f && c.g < 0.05f && c.b < 0.05f)
                        {
                            string lName = mr.name.ToLowerInvariant();
                            if (!lName.Contains("black") && !lName.Contains("tire") && !lName.Contains("wheel"))
                            {
                                c = new Color(0.75f, 0.75f, 0.78f, 1f);
                            }
                        }

                        Texture tex = null;
                        if (mat.HasProperty("_BaseMap")) tex = mat.GetTexture("_BaseMap");
                        else if (mat.HasProperty("_MainTex")) tex = mat.GetTexture("_MainTex");
                        else tex = mat.mainTexture;

                        if (tex == null)
                        {
                            string cleanName = mr.name.Replace("(Clone)", "").Trim();
#if UNITY_EDITOR
                            string[] guids = UnityEditor.AssetDatabase.FindAssets($"{cleanName} t:Texture2D");
                            if (guids != null && guids.Length > 0)
                            {
                                string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                                tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                            }
#endif
                        }

                        var newM = new Material(targetShader);
                        newM.name = $"{mat.name}_Healed";
                        if (newM.HasProperty("_BaseColor")) newM.SetColor("_BaseColor", c);
                        if (newM.HasProperty("_Color")) newM.SetColor("_Color", c);
                        if (tex != null)
                        {
                            if (newM.HasProperty("_BaseMap")) newM.SetTexture("_BaseMap", tex);
                            if (newM.HasProperty("_MainTex")) newM.SetTexture("_MainTex", tex);
                        }
                        if (newM.HasProperty("_Smoothness")) newM.SetFloat("_Smoothness", 0.5f);
                        if (newM.HasProperty("_Glossiness")) newM.SetFloat("_Glossiness", 0.5f);
                        if (newM.HasProperty("_Metallic")) newM.SetFloat("_Metallic", 0.2f);

                        sharedMats[i] = newM;
                        changed = true;
                    }
                }

                if (changed) mr.sharedMaterials = sharedMats;
            }

            // Ensure all MeshCollider components are convex for ArticulationBody stability
            var colliders = robotRoot.GetComponentsInChildren<MeshCollider>(true);
            foreach (var mc in colliders)
            {
                if (mc != null && mc.sharedMesh != null) mc.convex = true;
            }
        }

        private static void ConfigureRobotControls(GameObject robotRoot)
        {
            // Run AutoConfigurator (model-specific physics, joints, and controllers)
            try
            {
                RoverAutoConfigurator.Configure(robotRoot);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RuntimeUrdfImporter] RoverAutoConfigurator notice: " + ex.Message);
            }

            var stabilizer = robotRoot.GetComponent<RobotPhysicsStabilizer>();
            if (stabilizer == null)
            {
                stabilizer = robotRoot.AddComponent<RobotPhysicsStabilizer>();
            }
            stabilizer.ApplyStabilization();
        }

        private static void PositionRobotOnGround(GameObject robotRoot)
        {
            var allBodies = robotRoot.GetComponentsInChildren<ArticulationBody>(true);
            var rootBody = allBodies != null ? allBodies.FirstOrDefault(b => b.isRoot) : null;

            // Calculate robot bottom bounding box from colliders
            var colliders = robotRoot.GetComponentsInChildren<Collider>(true);
            float lowestY = float.MaxValue;
            if (colliders != null && colliders.Length > 0)
            {
                foreach (var c in colliders)
                {
                    if (c != null && c.enabled && c.bounds.min.y < lowestY) lowestY = c.bounds.min.y;
                }
            }

            float referenceY = rootBody != null ? rootBody.transform.position.y : robotRoot.transform.position.y;
            float pivotToBottom = (lowestY != float.MaxValue) ? (referenceY - lowestY) : 0.35f;
            if (pivotToBottom < 0.05f || pivotToBottom > 2.5f) pivotToBottom = 0.35f;

            // Determine safe spawn location: prefer Active Terrain surface
            Vector3 targetPosition = Vector3.zero;
            var terrain = UnityEngine.Terrain.activeTerrain;
            Camera cam = Camera.main;

            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 tPos = terrain.transform.position;
                Vector3 tSize = terrain.terrainData.size;
                Vector3 center = tPos + tSize * 0.5f;

                if (cam != null)
                {
                    Vector3 camPoint = cam.transform.position + cam.transform.forward * 6f;
                    if (camPoint.x >= tPos.x + 10f && camPoint.x <= tPos.x + tSize.x - 10f &&
                        camPoint.z >= tPos.z + 10f && camPoint.z <= tPos.z + tSize.z - 10f)
                    {
                        targetPosition = camPoint;
                    }
                    else
                    {
                        targetPosition = center;
                    }
                }
                else
                {
                    targetPosition = center;
                }

                targetPosition.y = terrain.SampleHeight(targetPosition) + tPos.y;
            }
            else if (cam != null)
            {
                bool hitFound = false;

                // 1. Raycast along camera forward vector (where user is looking)
                Ray lookRay = new Ray(cam.transform.position, cam.transform.forward);
                var lookHits = Physics.RaycastAll(lookRay, 200f);
                foreach (var hit in lookHits)
                {
                    if (hit.transform != null && !hit.transform.IsChildOf(robotRoot.transform) && hit.transform != robotRoot.transform)
                    {
                        targetPosition = hit.point;
                        hitFound = true;
                        break;
                    }
                }

                // 2. Downward raycast 5m ahead of camera
                if (!hitFound)
                {
                    Ray downRay = new Ray(cam.transform.position + cam.transform.forward * 5f + Vector3.up * 10f, Vector3.down);
                    var hits = Physics.RaycastAll(downRay, 100f);
                    foreach (var hit in hits)
                    {
                        if (hit.transform != null && !hit.transform.IsChildOf(robotRoot.transform) && hit.transform != robotRoot.transform)
                        {
                            targetPosition = hit.point;
                            hitFound = true;
                            break;
                        }
                    }
                }

                // 3. Known ground plane fallback
                if (!hitFound)
                {
                    var plane = GameObject.Find("Plane") ?? GameObject.Find("Floor") ?? GameObject.Find("Ground");
                    if (plane != null)
                    {
                        var col = plane.GetComponent<Collider>();
                        if (col != null)
                        {
                            targetPosition = col.bounds.center;
                            targetPosition.y = col.bounds.max.y;
                            hitFound = true;
                        }
                    }
                }

                // 4. Default in front of camera
                if (!hitFound)
                {
                    targetPosition = cam.transform.position + cam.transform.forward * 4f;
                    targetPosition.y = 0f;
                }
            }
            else
            {
                var plane = GameObject.Find("Plane") ?? GameObject.Find("Floor") ?? GameObject.Find("Ground");
                if (plane != null)
                {
                    var col = plane.GetComponent<Collider>();
                    if (col != null)
                    {
                        targetPosition = col.bounds.center;
                        targetPosition.y = col.bounds.max.y;
                    }
                }
                else
                {
                    targetPosition = Vector3.zero;
                }
            }

            // Offset above surface to rest gently without collider interpenetration
            targetPosition.y += (pivotToBottom + 0.05f);

            // 1. Position parent container GameObject
            robotRoot.transform.position = targetPosition;

            // 2. Teleport ArticulationBody root with unpinned status and gravity enabled
            if (rootBody != null)
            {
                Quaternion rootRot = rootBody.transform.rotation;
                rootBody.transform.position = targetPosition;
                rootBody.TeleportRoot(targetPosition, rootRot);
                rootBody.immovable = false;
                rootBody.useGravity = true;
            }

            // 3. Zero out linear and angular velocities across all bodies to prevent impulse catapults
            if (allBodies != null)
            {
                foreach (var b in allBodies)
                {
                    if (b == null) continue;
                    b.linearVelocity = Vector3.zero;
                    b.angularVelocity = Vector3.zero;
                }
            }

            Physics.SyncTransforms();
            Debug.Log($"[RuntimeUrdfImporter] Placed robot '{robotRoot.name}' at ground level: {targetPosition}");
        }

        private static void HookIntoSimulation(GameObject robotRoot)
        {
            var allBodies = robotRoot.GetComponentsInChildren<ArticulationBody>(true);
            var rootBody = allBodies != null ? allBodies.FirstOrDefault(b => b.isRoot) : null;
            Transform trackTransform = (rootBody != null) ? rootBody.transform : robotRoot.transform;

            // 1. Camera focus with Chase perspective
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                var camMono = mainCam.GetComponent("FreeFlyCamera");
                if (camMono != null)
                {
                    camMono.SendMessage("SetTargetRover", trackTransform, SendMessageOptions.DontRequireReceiver);
                    var method = camMono.GetType().GetMethod("SetCameraPerspective");
                    if (method != null)
                    {
                        var enumType = camMono.GetType().GetNestedType("CameraPerspective");
                        if (enumType != null)
                        {
                            var chaseVal = Enum.Parse(enumType, "Chase");
                            method.Invoke(camMono, new object[] { chaseVal });
                        }
                    }
                }
            }

            // 2. SimulationFlowController integration
            var allMono = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var m in allMono)
            {
                if (m != null && m.GetType().Name == "SimulationFlowController")
                {
                    m.SendMessage("SetActiveRover", robotRoot, SendMessageOptions.DontRequireReceiver);
                    var stateProp = m.GetType().GetField("currentState");
                    var stateType = m.GetType().GetNestedType("State");
                    if (stateProp != null && stateType != null)
                    {
                        stateProp.SetValue(m, Enum.Parse(stateType, "ActiveDriving"));
                    }
                    break;
                }
            }

            // 3. Runtime Controller HUD Binding
            var runtimeUi = UnityEngine.Object.FindAnyObjectByType<RuntimeRoverImportUI>();
            if (runtimeUi != null)
            {
                runtimeUi.BindActiveRobot(robotRoot);
            }
        }
        #endregion
    }
}
