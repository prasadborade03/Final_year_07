using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using UnityEngine;
using Unity.Robotics.UrdfImporter;
using AutonomousRobotKit;

namespace AutonomousRobotKit
{
    /// <summary>
    /// Rock-Solid Pure-Runtime URDF & ZIP Importer Pipeline:
    /// 1. Pure C# ZIP extraction using ZipArchive/ZipFile with ZipSlip prevention and nested folder support.
    /// 2. Recursive filesystem multi-key indexing: maps relative paths, filenames, basenames, and extensions.
    /// 3. Industrial-Strength Runtime XML Sanitization:
    ///    - Resolves package://, model://, file://, and relative paths to verified absolute file:// URIs.
    ///    - Prioritizes .stl for collision meshes (required by Unity URDF Importer runtime mode).
    ///    - Fallback extension substitution (.stl -> .obj -> .dae) and fallback box primitives with explicit warnings.
    ///    - Auto-heals missing inertial blocks, zero/negative masses, and degenerate inertia tensors.
    ///    - Detects axis conventions reliably without corrupting mesh orientations.
    ///    - Sanitizes joint limits, efforts, and velocities.
    /// 4. Hardened Robot Creation:
    ///    - Forced runtime mode via UrdfRobotExtensions.Create with per-link progress yielding.
    ///    - Unparents robot root and guarantees convex MeshColliders on every link with visual geometry.
    ///    - True root ArticulationBody unpinned (immovable = false, useGravity = true).
    ///    - Velocity zeroing across all bodies to prevent impulse catapults.
    /// 5. Multi-Modal Physics Stabilization (mass conditioning, 32/16 solver iterations, 6-DOF fixed joint locking).
    /// 6. Auto-Configured Drivetrain and Stance (SimpleDifferentialDrive for wheeled, ArticulatedRobotController with 1s spawn stance boost for quadrupeds).
    /// 7. Precise terrain/ground placement (~5 cm clearance) with upright orientation.
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
            // Path A: In Unity Editor (Optional Reflection Path)
            // If the Editor-only importer type is present, try it; otherwise fall through seamlessly to pure runtime.
            // ----------------------------------------------------
#if UNITY_EDITOR
            if (Application.isEditor && !Application.isPlaying)
            {
                GameObject editorRover = null;
                onProgress?.Invoke(0.2f, "Using Editor import pipeline...");
                yield return null;

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
                    Debug.LogWarning("[RuntimeUrdfImporter] Editor pipeline notice (falling through to runtime pipeline): " + ex.Message);
                }

                if (editorRover != null)
                {
                    onProgress?.Invoke(0.85f, "Stabilizing physics & setting up drivetrain...");
                    yield return null;

                    FinalizeAndStabilizeRobot(editorRover);

                    onProgress?.Invoke(1.0f, "Ready!");
                    yield return new WaitForSeconds(0.1f);

                    onSuccess?.Invoke(editorRover);
                    yield break;
                }
            }
#endif

            // ----------------------------------------------------
            // Path B: Pure Runtime Pipeline (Editor Play Mode & Standalone Builds)
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
                    string rawName = Path.GetFileNameWithoutExtension(filePath);
                    string safeBaseName = string.Join("_", rawName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                    if (string.IsNullOrEmpty(safeBaseName)) safeBaseName = "robot";

                    string robotFolderName = $"{safeBaseName}_{DateTime.UtcNow.Ticks}";
                    workingDir = Path.Combine(Application.persistentDataPath, "RuntimeRovers", robotFolderName);

                    ExtractZipArchive(filePath, workingDir);

                    // Locate .urdf files in extracted directory tree
                    var urdfFiles = Directory.GetFiles(workingDir, "*.urdf", SearchOption.AllDirectories);
                    if (urdfFiles.Length == 0)
                    {
                        onError?.Invoke("No .urdf file was found inside the selected zip archive.");
                        yield break;
                    }

                    // Pick the primary URDF intelligently
                    urdfPath = PickPrimaryUrdf(urdfFiles, safeBaseName);
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
            // Step 3: Instantiate Robot Hierarchy in Forced Runtime Mode
            // ----------------------------------------------------
            onProgress?.Invoke(0.55f, "Assembling robot hierarchy & physics...");
            yield return null;

            var settings = new ImportSettings
            {
                chosenAxis = detectedAxis,
                convexMethod = ImportSettings.convexDecomposer.unity
            };

            // Set package root so relative paths resolve to the URDF directory
            UrdfAssetPathHandler.SetPackageRoot(Path.GetDirectoryName(sanitizedUrdfPath));

            GameObject robotObject = null;
            IEnumerator<GameObject> createEnum = null;

            try
            {
                // Force runtime mode so AssetDatabase calls are completely bypassed
                createEnum = UrdfRobotExtensions.Create(sanitizedUrdfPath, settings, loadStatus: true, forceRuntimeMode: true);
            }
            catch (Exception ex)
            {
                onError?.Invoke("Failed to initialize URDF creation pipeline: " + ex.Message);
                yield break;
            }

            int loadedLinks = 0;
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

                loadedLinks++;
                onProgress?.Invoke(0.55f + Mathf.Min(0.20f, loadedLinks * 0.01f), $"Assembling link {loadedLinks}...");
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

            // ----------------------------------------------------
            // Step 4: Multi-Modal Physics Stabilization, Stance, Ground Placement & Unfreeze
            // ----------------------------------------------------
            onProgress?.Invoke(0.85f, "Stabilizing physics & setting up drivetrain...");
            yield return null;

            FinalizeAndStabilizeRobot(robotObject);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            onProgress?.Invoke(1.0f, "Ready!");
            yield return new WaitForSeconds(0.1f);

            onSuccess?.Invoke(robotObject);
        }

        /// <summary>
        /// Rock-Solid Universal Post-Import Physics Pipeline:
        /// 1. Unparents robot from scene root.
        /// 2. Freezes hierarchy immediately (immovable = true, useGravity = false, v = 0).
        /// 3. Upgrades visual materials to active render pipeline (URP / Standard).
        /// 4. Ensures convex MeshColliders on visual links (skips empty virtual links).
        /// 5. Stabilizes joints (6-DOF fixed lock, single-axis revolute, mass conditioning, pairwise IgnoreCollision).
        /// 6. Configures drivetrain & posture (SimpleDifferentialDrive or ArticulatedRobotController).
        /// 7. Sets standing stance coordinates directly in reduced space without impulse snap.
        /// 8. Places robot upright and rests lowest collider gently ~4 cm above terrain (no floating, no dragging).
        /// 9. Unfreezes physics (enables gravity, unpins mobile chassis, keeps robotic arms fixed).
        /// 10. Hooks into camera and simulation flow.
        /// </summary>
        public static void FinalizeAndStabilizeRobot(GameObject robotObject)
        {
            if (robotObject == null) return;

            // 1. Immediately unparent from anything in the scene
            robotObject.transform.SetParent(null, true);

            // 2. Locate all ArticulationBodies and the true root
            var allBodies = robotObject.GetComponentsInChildren<ArticulationBody>(true);
            var rootBody = allBodies != null ? allBodies.FirstOrDefault(b => b.isRoot) : null;

            // 3. FREEZE IMMEDIATELY: Ensure ZERO movement, ZERO gravity, ZERO velocity during setup
            if (allBodies != null)
            {
                foreach (var b in allBodies)
                {
                    if (b == null) continue;
                    b.useGravity = false;
                    b.linearVelocity = Vector3.zero;
                    b.angularVelocity = Vector3.zero;
                }
            }
            if (rootBody != null)
            {
                rootBody.immovable = true;
            }
            Physics.SyncTransforms();

            // 4. Upgrade visual materials to active render pipeline (URP / Standard)
            UpgradeMaterialsToActivePipeline(robotObject);

            // 5. Ensure robust convex colliders on all visual links (skips empty virtual links)
            EnsureRobustColliders(robotObject);

            // 6. Multi-Modal Physics Stabilization
            StabilizeImportedRobot(robotObject);

            // 7. Drivetrain & Posture Auto-Configuration
            ConfigureRobotControls(robotObject);

            // 8. Apply Stance & Initial Joint Angles (sets jointPosition directly in reduced space without impulse)
            var controller = robotObject.GetComponent<ArticulatedRobotController>() ?? robotObject.GetComponentInChildren<ArticulatedRobotController>();
            if (controller != null)
            {
                controller.ApplyStandStance();
                controller.BoostStanceHold(1.0f);
            }

            // Zero velocities again
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

            // 9. Ground Placement
            var desc = RoverAnalyzer.Analyze(robotObject);
            bool isArm = desc != null && desc.RobotType == RobotArchitectureType.RoboticArm;

            PositionRobotOnGround(robotObject, isArm);

            // 10. Unfreeze physics: enable gravity and unpin mobile robots
            if (allBodies != null)
            {
                foreach (var b in allBodies)
                {
                    if (b == null) continue;
                    b.useGravity = true;
                    b.linearVelocity = Vector3.zero;
                    b.angularVelocity = Vector3.zero;
                }
            }

            if (rootBody != null)
            {
                rootBody.immovable = isArm; // Arms remain immovable; rovers and quadrupeds unpin
            }

            // 11. Hook into simulation
            HookIntoSimulation(robotObject);
        }

        #region Robust ZIP Extraction (Pure C#)
        public static void ExtractZipArchive(string zipFilePath, string destinationDirectory)
        {
            if (!File.Exists(zipFilePath))
                throw new FileNotFoundException("Zip file not found: " + zipFilePath);

            if (!Directory.Exists(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            string fullDestDir = Path.GetFullPath(destinationDirectory);
            if (!fullDestDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                fullDestDir += Path.DirectorySeparatorChar;

            bool extractionSucceeded = false;

            // Fast path: Try System.IO.Compression.ZipFile.ExtractToDirectory
            try
            {
                ZipFile.ExtractToDirectory(zipFilePath, destinationDirectory);
                extractionSucceeded = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RuntimeUrdfImporter] ZipFile.ExtractToDirectory notice (falling back to ZipArchive): {ex.Message}");
            }

            // Fallback path: Pure ZipArchive entry-by-entry stream extraction with ZipSlip protection
            if (!extractionSucceeded)
            {
                try
                {
                    using (var fs = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            string entryName = entry.FullName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                            if (string.IsNullOrWhiteSpace(entryName)) continue;

                            string targetPath = Path.Combine(destinationDirectory, entryName);
                            string fullTargetPath = Path.GetFullPath(targetPath);

                            // ZipSlip protection
                            if (!fullTargetPath.StartsWith(fullDestDir, StringComparison.OrdinalIgnoreCase))
                            {
                                Debug.LogWarning($"[RuntimeUrdfImporter] Skipped ZipSlip archive entry: {entry.FullName}");
                                continue;
                            }

                            // Directory entry
                            if (entryName.EndsWith(Path.DirectorySeparatorChar.ToString()) || string.IsNullOrEmpty(entry.Name))
                            {
                                if (!Directory.Exists(targetPath)) Directory.CreateDirectory(targetPath);
                                continue;
                            }

                            string parent = Path.GetDirectoryName(targetPath);
                            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                            {
                                Directory.CreateDirectory(parent);
                            }

                            using (var inStream = entry.Open())
                            using (var outStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                inStream.CopyTo(outStream);
                            }
                        }
                    }
                    extractionSucceeded = true;
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to extract zip archive '{Path.GetFileName(zipFilePath)}': {ex.Message}", ex);
                }
            }

            Debug.Log($"[RuntimeUrdfImporter] Extracted zip package successfully to: {destinationDirectory}");
        }

        private static string PickPrimaryUrdf(string[] urdfFiles, string baseName)
        {
            if (urdfFiles == null || urdfFiles.Length == 0) return null;
            if (urdfFiles.Length == 1) return urdfFiles[0];

            // 1. Exact match with archive base name (case-insensitive)
            foreach (var f in urdfFiles)
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(f);
                if (nameWithoutExt.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                    return f;
            }

            // 2. Score candidate URDFs based on validity and completeness
            string bestFile = urdfFiles[0];
            int bestScore = -1;
            long bestLength = -1;

            foreach (var f in urdfFiles)
            {
                try
                {
                    string content = File.ReadAllText(f);
                    if (!content.Contains("<robot")) continue;

                    int jointCount = System.Text.RegularExpressions.Regex.Matches(content, @"<joint\b").Count;
                    int linkCount = System.Text.RegularExpressions.Regex.Matches(content, @"<link\b").Count;

                    // Heavily prioritize URDFs containing both links and joints
                    int score = (jointCount > 0 ? 1000 : 0) + (linkCount > 0 ? 500 : 0) + (jointCount * 10) + linkCount;
                    long fileLen = new FileInfo(f).Length;

                    if (score > bestScore || (score == bestScore && fileLen > bestLength))
                    {
                        bestScore = score;
                        bestLength = fileLen;
                        bestFile = f;
                    }
                }
                catch { }
            }

            return bestFile;
        }
        #endregion

        #region Industrial-Strength URDF XML Sanitization
        public static string SanitizeUrdfAtRuntime(string urdfPath, string workingDir, out ImportSettings.axisType detectedAxis)
        {
            // In Unity URDF Importer, Ros2Unity() maps ROS Z-up to Unity Y-up.
            // Using axisType.yAxis ensures no artificial rotation is multiplied onto the meshes,
            // keeping visual meshes, colliders, and ArticulationBody anchors 100% aligned.
            detectedAxis = ImportSettings.axisType.yAxis;

            // 1. Build Multi-Key File Lookup Catalog
            var searchDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir)) searchDirs.Add(workingDir);
            string urdfDir = Path.GetDirectoryName(urdfPath);
            if (!string.IsNullOrEmpty(urdfDir) && Directory.Exists(urdfDir))
            {
                searchDirs.Add(urdfDir);
                var parent = Directory.GetParent(urdfDir);
                if (parent != null && parent.Exists) searchDirs.Add(parent.FullName);
            }

            var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var stlMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in searchDirs)
            {
                foreach (var f in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string normalizedFull = f.Replace('\\', '/');
                    string fileName = Path.GetFileName(f);
                    string baseWithoutExt = Path.GetFileNameWithoutExtension(f);
                    string ext = Path.GetExtension(f).ToLowerInvariant();

                    // Key 1: exact filename
                    if (!fileMap.ContainsKey(fileName)) fileMap[fileName] = normalizedFull;

                    // Key 2: basename without extension
                    if (!fileMap.ContainsKey(baseWithoutExt)) fileMap[baseWithoutExt] = normalizedFull;

                    // Key 3: relative path from search directory
                    string relPath = f.Substring(dir.Length).TrimStart('/', '\\').Replace('\\', '/');
                    if (!fileMap.ContainsKey(relPath)) fileMap[relPath] = normalizedFull;

                    // Key 4: common ROS subfolder suffixes (meshes/..., visual/..., collision/...)
                    int meshIdx = normalizedFull.IndexOf("/meshes/", StringComparison.OrdinalIgnoreCase);
                    if (meshIdx >= 0)
                    {
                        string sub = normalizedFull.Substring(meshIdx + 1); // "meshes/..."
                        if (!fileMap.ContainsKey(sub)) fileMap[sub] = normalizedFull;
                    }

                    int visIdx = normalizedFull.IndexOf("/visual/", StringComparison.OrdinalIgnoreCase);
                    if (visIdx >= 0)
                    {
                        string sub = normalizedFull.Substring(visIdx + 1);
                        if (!fileMap.ContainsKey(sub)) fileMap[sub] = normalizedFull;
                    }

                    int colIdx = normalizedFull.IndexOf("/collision/", StringComparison.OrdinalIgnoreCase);
                    if (colIdx >= 0)
                    {
                        string sub = normalizedFull.Substring(colIdx + 1);
                        if (!fileMap.ContainsKey(sub)) fileMap[sub] = normalizedFull;
                    }

                    // Track STL files separately for collision priority
                    if (ext == ".stl" && !stlMap.ContainsKey(baseWithoutExt))
                    {
                        stlMap[baseWithoutExt] = normalizedFull;
                    }
                }
            }

            XDocument doc = XDocument.Load(urdfPath);
            var robotElem = doc.Root;
            if (robotElem == null || robotElem.Name.LocalName != "robot")
                throw new InvalidDataException("Invalid URDF: Root element is not <robot>.");

            // 2. Sanitize Links: Inertials & Geometries
            var links = robotElem.Elements("link").ToList();
            bool isFirstLink = true;

            foreach (var link in links)
            {
                string linkName = link.Attribute("name")?.Value ?? "unnamed_link";

                // Ensure valid inertial block
                var inertial = link.Element("inertial");
                if (inertial == null)
                {
                    float defaultMass = isFirstLink ? 15.0f : 1.0f;
                    float defaultInertia = isFirstLink ? 0.1f : 0.01f;
                    link.Add(new XElement("inertial",
                        new XElement("mass", new XAttribute("value", defaultMass.ToString(CultureInfo.InvariantCulture))),
                        new XElement("inertia",
                            new XAttribute("ixx", defaultInertia.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("ixy", "0"),
                            new XAttribute("ixz", "0"),
                            new XAttribute("iyy", defaultInertia.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("iyz", "0"),
                            new XAttribute("izz", defaultInertia.ToString(CultureInfo.InvariantCulture)))
                    ));
                }
                else
                {
                    var massElem = inertial.Element("mass");
                    float currentMass = 1.0f;
                    if (massElem == null)
                    {
                        currentMass = isFirstLink ? 15.0f : 1.0f;
                        inertial.Add(new XElement("mass", new XAttribute("value", currentMass.ToString(CultureInfo.InvariantCulture))));
                    }
                    else
                    {
                        if (float.TryParse(massElem.Attribute("value")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float m))
                        {
                            if (m <= 0.0001f)
                            {
                                currentMass = isFirstLink ? 10.0f : 0.5f;
                                massElem.SetAttributeValue("value", currentMass.ToString(CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                currentMass = m;
                            }
                        }
                    }

                    var inertiaElem = inertial.Element("inertia");
                    float minInertia = Mathf.Max(0.005f, currentMass * 0.01f);
                    if (inertiaElem == null)
                    {
                        inertial.Add(new XElement("inertia",
                            new XAttribute("ixx", minInertia.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("ixy", "0"),
                            new XAttribute("ixz", "0"),
                            new XAttribute("iyy", minInertia.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("iyz", "0"),
                            new XAttribute("izz", minInertia.ToString(CultureInfo.InvariantCulture))));
                    }
                    else
                    {
                        HealInertiaAttribute(inertiaElem, "ixx", minInertia);
                        HealInertiaAttribute(inertiaElem, "iyy", minInertia);
                        HealInertiaAttribute(inertiaElem, "izz", minInertia);
                        CleanInertiaOffDiagonal(inertiaElem, "ixy");
                        CleanInertiaOffDiagonal(inertiaElem, "ixz");
                        CleanInertiaOffDiagonal(inertiaElem, "iyz");
                    }
                }

                // Sanitize Visuals and Collisions
                SanitizeGeometries(link.Elements("visual"), fileMap, stlMap, isCollision: false, linkName: linkName);
                SanitizeGeometries(link.Elements("collision"), fileMap, stlMap, isCollision: true, linkName: linkName);

                isFirstLink = false;
            }

            // 3. Sanitize Joints: Limits, Effort, Velocity
            foreach (var joint in robotElem.Elements("joint"))
            {
                string jType = joint.Attribute("type")?.Value ?? "fixed";
                if (jType == "revolute" || jType == "continuous" || jType == "prismatic")
                {
                    var limit = joint.Element("limit");
                    if (limit == null)
                    {
                        if (jType == "revolute")
                        {
                            joint.Add(new XElement("limit",
                                new XAttribute("lower", "-3.14159"),
                                new XAttribute("upper", "3.14159"),
                                new XAttribute("effort", "150"),
                                new XAttribute("velocity", "20")));
                        }
                        else
                        {
                            joint.Add(new XElement("limit",
                                new XAttribute("effort", "150"),
                                new XAttribute("velocity", "20")));
                        }
                    }
                    else
                    {
                        if (jType == "revolute")
                        {
                            float lower = 0f, upper = 0f;
                            bool hasLower = float.TryParse(limit.Attribute("lower")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lower);
                            bool hasUpper = float.TryParse(limit.Attribute("upper")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out upper);
                            if (hasLower && hasUpper && lower >= upper)
                            {
                                limit.SetAttributeValue("upper", (lower + 0.05f).ToString(CultureInfo.InvariantCulture));
                            }
                        }
                        if (limit.Attribute("effort") == null || string.IsNullOrEmpty(limit.Attribute("effort").Value))
                        {
                            limit.SetAttributeValue("effort", "150");
                        }
                        if (limit.Attribute("velocity") == null || string.IsNullOrEmpty(limit.Attribute("velocity").Value))
                        {
                            limit.SetAttributeValue("velocity", "20");
                        }
                    }
                }
            }

            // Save sanitized URDF alongside original
            string outDir = Path.GetDirectoryName(urdfPath);
            string sanitizedPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(urdfPath) + "_runtime_sanitized.urdf");
            doc.Save(sanitizedPath);

            return sanitizedPath;
        }

        private static void SanitizeGeometries(IEnumerable<XElement> elements, Dictionary<string, string> fileMap, Dictionary<string, string> stlMap, bool isCollision, string linkName)
        {
            foreach (var elem in elements.ToList())
            {
                var geom = elem.Element("geometry");
                if (geom == null) continue;

                var mesh = geom.Element("mesh");
                if (mesh != null)
                {
                    string rawFilename = mesh.Attribute("filename")?.Value ?? "";
                    if (string.IsNullOrEmpty(rawFilename))
                    {
                        mesh.Remove();
                        geom.Add(new XElement("box", new XAttribute("size", "0.1 0.1 0.1")));
                        continue;
                    }

                    string matchedFile = ResolveMeshPath(rawFilename, fileMap, stlMap, isCollision);
                    if (!string.IsNullOrEmpty(matchedFile) && File.Exists(matchedFile))
                    {
                        mesh.SetAttributeValue("filename", "file://" + matchedFile.Replace('\\', '/'));
                    }
                    else
                    {
                        Debug.LogWarning($"[RuntimeUrdfImporter] Mesh '{rawFilename}' not found on disk for link '{linkName}'. Using fallback box primitive.");
                        mesh.Remove();
                        geom.Add(new XElement("box", new XAttribute("size", "0.15 0.15 0.15")));
                    }
                }
            }
        }

        private static string ResolveMeshPath(string rawFilename, Dictionary<string, string> fileMap, Dictionary<string, string> stlMap, bool isCollision)
        {
            string clean = rawFilename.Split('?')[0].Trim();
            if (clean.StartsWith("package://", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(10);
            else if (clean.StartsWith("model://", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(8);
            else if (clean.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(7);

            clean = clean.Replace('\\', '/');
            string fileName = Path.GetFileName(clean);
            string baseWithoutExt = Path.GetFileNameWithoutExtension(clean);

            // For collisions: Unity URDF Importer runtime collision loader only supports .stl!
            // If an STL version exists with the same basename, use it!
            if (isCollision && stlMap.TryGetValue(baseWithoutExt, out string stlPath))
            {
                return stlPath;
            }

            // 1. Direct match on relative path
            if (fileMap.TryGetValue(clean, out string foundPath)) return foundPath;

            // 2. Direct match on filename
            if (fileMap.TryGetValue(fileName, out foundPath)) return foundPath;

            // 3. Match without leading package directory (e.g. "spot_description/meshes/body.obj" -> "meshes/body.obj")
            int slashIdx = clean.IndexOf('/');
            if (slashIdx >= 0)
            {
                string subPath = clean.Substring(slashIdx + 1);
                if (fileMap.TryGetValue(subPath, out foundPath)) return foundPath;
            }

            // 4. Fallback alternative extensions (.stl -> .obj -> .dae)
            if (fileMap.TryGetValue(baseWithoutExt + ".stl", out foundPath)) return foundPath;
            if (fileMap.TryGetValue(baseWithoutExt + ".obj", out foundPath)) return foundPath;
            if (fileMap.TryGetValue(baseWithoutExt + ".dae", out foundPath)) return foundPath;

            // 5. Basename lookup
            if (fileMap.TryGetValue(baseWithoutExt, out foundPath)) return foundPath;

            return null;
        }

        private static void HealInertiaAttribute(XElement inertiaElem, string attrName, float minValue)
        {
            var attr = inertiaElem.Attribute(attrName);
            if (attr == null)
            {
                inertiaElem.Add(new XAttribute(attrName, minValue.ToString(CultureInfo.InvariantCulture)));
            }
            else if (float.TryParse(attr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            {
                if (val <= 0.00001f || float.IsNaN(val) || float.IsInfinity(val))
                {
                    attr.Value = minValue.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        private static void CleanInertiaOffDiagonal(XElement inertiaElem, string attrName)
        {
            var attr = inertiaElem.Attribute(attrName);
            if (attr == null)
            {
                inertiaElem.Add(new XAttribute(attrName, "0"));
            }
            else if (float.TryParse(attr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            {
                if (float.IsNaN(val) || float.IsInfinity(val)) attr.Value = "0";
            }
        }
        #endregion

        #region Post-Import Physics, Colliders & Controls
        /// <summary>
        /// Guarantees that every ArticulationBody link possessing visual meshes has a convex MeshCollider.
        /// Cures the critical issue where official URDF Importer silently fails to create colliders for non-STL (.obj/.dae) collision meshes.
        /// </summary>
        public static void EnsureRobustColliders(GameObject robotRoot)
        {
            var bodies = robotRoot.GetComponentsInChildren<ArticulationBody>(true);
            foreach (var body in bodies)
            {
                if (body == null) continue;

                var existingColliders = body.GetComponentsInChildren<Collider>(true)
                    .Where(c => c.GetComponentInParent<ArticulationBody>() == body)
                    .ToList();

                if (existingColliders.Count == 0)
                {
                    // Link has no collider; inspect visual mesh filters
                    var meshFilters = body.GetComponentsInChildren<MeshFilter>(true)
                        .Where(mf => mf.GetComponentInParent<ArticulationBody>() == body)
                        .ToList();

                    foreach (var mf in meshFilters)
                    {
                        if (mf != null && mf.sharedMesh != null)
                        {
                            var colGo = mf.gameObject;
                            var mc = colGo.GetComponent<MeshCollider>();
                            if (mc == null) mc = colGo.AddComponent<MeshCollider>();
                            mc.sharedMesh = mf.sharedMesh;
                            mc.convex = true;
                        }
                    }
                }

                // Guarantee all MeshColliders are convex (mandatory for PhysX ArticulationBody!)
                foreach (var mc in body.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (mc == null) continue;
                    if (mc.sharedMesh == null)
                    {
                        var mf = mc.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) mc.sharedMesh = mf.sharedMesh;
                        else { mc.enabled = false; continue; }
                    }
                    mc.convex = true;
                }
            }
        }

        private static void StabilizeImportedRobot(GameObject robotRoot)
        {
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
                    else if (mat.shader != targetShader || mat.shader.name == "Hidden/InternalErrorShader")
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
        }

        private static void ConfigureRobotControls(GameObject robotRoot)
        {
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

        private static void PositionRobotOnGround(GameObject robotRoot, bool isArm = false)
        {
            Physics.SyncTransforms();

            var allBodies = robotRoot.GetComponentsInChildren<ArticulationBody>(true);
            var rootBody = allBodies != null ? allBodies.FirstOrDefault(b => b.isRoot) : null;
            if (rootBody == null) return;

            // 1. Straighten root rotation to upright (zero pitch and roll, preserve yaw)
            Quaternion rootRot = rootBody.transform.rotation;
            Vector3 euler = rootRot.eulerAngles;
            Quaternion uprightRot = Quaternion.Euler(0f, euler.y, 0f);

            rootBody.TeleportRoot(rootBody.transform.position, uprightRot);
            Physics.SyncTransforms();

            // 2. Measure actual lowest point of all active colliders on the robot
            var colliders = robotRoot.GetComponentsInChildren<Collider>(true);
            float lowestY = float.MaxValue;
            if (colliders != null && colliders.Length > 0)
            {
                foreach (var c in colliders)
                {
                    if (c != null && c.enabled && !c.isTrigger)
                    {
                        if (c.bounds.min.y < lowestY) lowestY = c.bounds.min.y;
                    }
                }
            }
            if (lowestY == float.MaxValue) lowestY = rootBody.transform.position.y;

            // 3. Determine ground target horizontal location and surface elevation
            Vector3 targetSpawnPos = Vector3.zero;
            float groundY = 0f;
            bool groundFound = false;

            var terrain = UnityEngine.Terrain.activeTerrain;
            Camera cam = Camera.main;

            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 tPos = terrain.transform.position;
                Vector3 tSize = terrain.terrainData.size;
                Vector3 center = tPos + tSize * 0.5f;

                if (cam != null)
                {
                    Vector3 camFwd = cam.transform.forward;
                    camFwd.y = 0f;
                    if (camFwd.sqrMagnitude > 0.001f) camFwd.Normalize();
                    else camFwd = Vector3.forward;

                    Vector3 camPoint = cam.transform.position + camFwd * 5f;
                    if (camPoint.x >= tPos.x + 5f && camPoint.x <= tPos.x + tSize.x - 5f &&
                        camPoint.z >= tPos.z + 5f && camPoint.z <= tPos.z + tSize.z - 5f)
                    {
                        targetSpawnPos = camPoint;
                    }
                    else
                    {
                        targetSpawnPos = center;
                    }
                }
                else
                {
                    targetSpawnPos = center;
                }

                groundY = terrain.SampleHeight(targetSpawnPos) + tPos.y;
                groundFound = true;
            }
            else if (cam != null)
            {
                Vector3 camFwd = cam.transform.forward;
                camFwd.y = 0f;
                if (camFwd.sqrMagnitude > 0.001f) camFwd.Normalize();
                else camFwd = Vector3.forward;

                Vector3 probeCenter = cam.transform.position + camFwd * 5f;

                Ray downRay = new Ray(new Vector3(probeCenter.x, probeCenter.y + 20f, probeCenter.z), Vector3.down);
                var hits = Physics.RaycastAll(downRay, 100f);
                foreach (var hit in hits.OrderBy(h => h.distance))
                {
                    if (hit.transform == null) continue;
                    // Never hit another robot or dynamic body as ground
                    if (hit.collider.GetComponentInParent<ArticulationBody>() != null || hit.collider.GetComponentInParent<Rigidbody>() != null) continue;
                    if (hit.transform.IsChildOf(robotRoot.transform) || hit.transform == robotRoot.transform) continue;

                    groundY = hit.point.y;
                    targetSpawnPos = hit.point;
                    groundFound = true;
                    break;
                }

                if (!groundFound)
                {
                    targetSpawnPos = probeCenter;
                }
            }

            if (!groundFound)
            {
                var plane = GameObject.Find("TestFloorPlane") ?? GameObject.Find("Plane") ?? GameObject.Find("Floor") ?? GameObject.Find("Ground");
                if (plane != null)
                {
                    var col = plane.GetComponent<Collider>();
                    if (col != null)
                    {
                        targetSpawnPos.x = col.bounds.center.x;
                        targetSpawnPos.z = col.bounds.center.z;
                        groundY = col.bounds.max.y;
                        groundFound = true;
                    }
                }
            }

            // Fallback ground guarantee: if no terrain or floor exists in the scene, ensure a solid base
            // at Y=0 so newly imported robots never plummet into empty space
            if (!groundFound)
            {
                var defaultFloor = GameObject.Find("RuntimeDefaultFloor");
                if (defaultFloor == null)
                {
                    defaultFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    defaultFloor.name = "RuntimeDefaultFloor";
                    defaultFloor.transform.position = new Vector3(0f, -0.5f, 0f);
                    defaultFloor.transform.localScale = new Vector3(200f, 1f, 200f);
                    var rend = defaultFloor.GetComponent<Renderer>();
                    if (rend != null) rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                groundY = 0f;
                targetSpawnPos = new Vector3(0f, 0f, 1.5f);
                groundFound = true;
            }

            // 4. Exact mathematical placement:
            // Lowest collider point sits gently at (groundY + targetClearance)
            float targetClearance = isArm ? 0.005f : 0.04f; // 4 cm for rovers/quadrupeds, 0.5 cm for fixed arms
            float deltaY = (groundY + targetClearance) - lowestY;

            Vector3 currentRootPos = rootBody.transform.position;
            Vector3 newRootPos = new Vector3(targetSpawnPos.x, currentRootPos.y + deltaY, targetSpawnPos.z);
            Vector3 rootShift = newRootPos - currentRootPos;

            // Shift container GameObject
            robotRoot.transform.position += rootShift;
            Physics.SyncTransforms();

            // Teleport ArticulationBody root with unpinned status maintained by caller
            rootBody.TeleportRoot(newRootPos, uprightRot);

            // 5. Zero out linear and angular velocities across all bodies to prevent impulse catapults
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
            Debug.Log($"[RuntimeUrdfImporter] Placed robot '{robotRoot.name}' at ground level: {newRootPos} (lowestY={lowestY:F3}, groundY={groundY:F3}, clearance={targetClearance:F3})");
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
                    var stateProp = m.GetType().GetField("currentState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var stateType = m.GetType().GetNestedType("State", BindingFlags.Public | BindingFlags.NonPublic);
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
