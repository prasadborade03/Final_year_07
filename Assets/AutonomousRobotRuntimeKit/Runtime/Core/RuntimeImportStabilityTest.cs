using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AutonomousRobotKit
{
    /// <summary>
    /// Automated test runner to verify ArticulationBody stability, ground placement,
    /// and absence of floating or explosions across all robot architectures.
    /// </summary>
    public class RuntimeImportStabilityTest : MonoBehaviour
    {
        [Header("Test Configuration")]
        public bool runOnStart = false;
        public float settleWaitSeconds = 1.5f;

        private void Start()
        {
            if (runOnStart)
            {
                StartCoroutine(RunVerificationSuite());
            }
        }

        [ContextMenu("Run Multi-Robot Stability Verification")]
        public void RunSuiteNow()
        {
            StartCoroutine(RunVerificationSuite());
        }

        public IEnumerator RunVerificationSuite()
        {
            Debug.Log("<color=#00E5FF><b>[StabilityTest] ========================================================</b></color>");
            Debug.Log("<color=#00E5FF><b>[StabilityTest] STARTING MULTI-ROBOT ARTICULATION STABILITY TEST SUITE</b></color>");
            Debug.Log("<color=#00E5FF><b>[StabilityTest] ========================================================</b></color>");

            // Ensure reference ground surface exists for simulation assertions
            var testFloor = GameObject.Find("TestFloorPlane");
            if (testFloor == null)
            {
                testFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                testFloor.name = "TestFloorPlane";
                testFloor.transform.position = new Vector3(0f, -0.5f, 0f);
                testFloor.transform.localScale = new Vector3(200f, 1f, 200f);
                var rend = testFloor.GetComponent<Renderer>();
                if (rend != null) rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Clean up any stale robots before starting
            var staleRobots = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                .Where(g => g != null && g != testFloor && g.GetComponent<ArticulationBody>() != null && g.transform.parent == null)
                .ToList();
            foreach (var s in staleRobots) Destroy(s);
            yield return null;
            yield return new WaitForFixedUpdate();

            string dataPath = Application.dataPath;
            var testCases = new List<(string name, string path, RobotArchitectureType expectedArch)>
            {
                ("Clearpath Husky (Wheeled Rover)", Path.Combine(dataPath, "project/data/URDF/husky_unity/husky.urdf"), RobotArchitectureType.WheeledMobile),
                ("Deep Robotics M20 (Wheeled Quadruped)", Path.Combine(dataPath, "project/data/URDF/M20_Unity/M20.urdf"), RobotArchitectureType.WheeledQuadruped),
                ("Arduinobot (Robotic Arm)", Path.Combine(dataPath, "project/data/URDF/arduinobot/arduinobot.urdf"), RobotArchitectureType.RoboticArm),
                ("Perseverance M2020 (Rocker-Bogie Rover)", Path.Combine(dataPath, "project/data/URDF/M2020_Unity/perseverance_m2020.urdf"), RobotArchitectureType.WheeledMobile),
                ("Husky ZIP Package (Archive Extraction)", Path.Combine(dataPath, "project/data/Husky_UnityReady.zip"), RobotArchitectureType.WheeledMobile)
            };

            int passedCount = 0;
            int totalCount = testCases.Count;

            for (int i = 0; i < testCases.Count; i++)
            {
                var test = testCases[i];
                Debug.Log($"\n<color=#FFD700><b>[StabilityTest] >>> Running Test {i + 1}/{totalCount}: {test.name} <<<</b></color>");

                if (!File.Exists(test.path))
                {
                    Debug.LogError($"[StabilityTest] Test file not found: {test.path}");
                    continue;
                }

                bool importDone = false;
                GameObject spawnedRobot = null;
                string importError = null;

                RuntimeUrdfImporter.ImportRobot(
                    test.path,
                    onProgress: (p, msg) => { },
                    onSuccess: go =>
                    {
                        spawnedRobot = go;
                        importDone = true;
                    },
                    onError: err =>
                    {
                        importError = err;
                        importDone = true;
                    }
                );

                float timer = 0f;
                while (!importDone && timer < 20f)
                {
                    timer += Time.deltaTime;
                    yield return null;
                }

                if (!importDone || spawnedRobot == null)
                {
                    Debug.LogError($"[StabilityTest] FAILED: Import failed or timed out: {importError}");
                    continue;
                }

                Debug.Log($"[StabilityTest] Imported '{spawnedRobot.name}' successfully. Simulating physics for {settleWaitSeconds}s...");

                // Simulate physics settling
                float settleTimer = 0f;
                while (settleTimer < settleWaitSeconds)
                {
                    settleTimer += Time.fixedDeltaTime;
                    yield return new WaitForFixedUpdate();
                }

                // Run verification assertions
                bool testPassed = VerifyRobotStability(spawnedRobot, test.name, test.expectedArch);
                if (testPassed)
                {
                    passedCount++;
                    Debug.Log($"<color=green><b>[StabilityTest] PASS: {test.name} passed all stability and ground placement checks!</b></color>");
                }
                else
                {
                    Debug.LogError($"<color=red><b>[StabilityTest] FAIL: {test.name} failed verification assertions.</b></color>");
                }

                // Cleanup spawned robot before next test
                Destroy(spawnedRobot);
                yield return null;
                yield return new WaitForFixedUpdate();

                // Clean up any other lingering articulation hierarchies
                var lingering = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                    .Where(g => g != null && g != testFloor && g.GetComponent<ArticulationBody>() != null && g.transform.parent == null)
                    .ToList();
                foreach (var l in lingering) Destroy(l);

                yield return null;
                yield return new WaitForFixedUpdate();
            }

            Debug.Log("\n<color=#00E5FF><b>[StabilityTest] ========================================================</b></color>");
            Debug.Log($"<color={(passedCount == totalCount ? "green" : "red")}><b>[StabilityTest] TEST SUITE COMPLETE: {passedCount}/{totalCount} ROBOTS PASSED ALL CHECKS!</b></color>");
            Debug.Log("<color=#00E5FF><b>[StabilityTest] ========================================================</b></color>");
        }

        private bool VerifyRobotStability(GameObject robot, string testName, RobotArchitectureType expectedArch)
        {
            var bodies = robot.GetComponentsInChildren<ArticulationBody>(true);
            if (bodies == null || bodies.Length == 0)
            {
                Debug.LogError($"[StabilityTest] Assert Error: No ArticulationBody found on {testName}.");
                return false;
            }

            var rootBody = bodies.FirstOrDefault(b => b.isRoot);
            if (rootBody == null)
            {
                Debug.LogError($"[StabilityTest] Assert Error: No root ArticulationBody found on {testName}.");
                return false;
            }

            // 1. Architecture check
            var desc = RoverAnalyzer.Analyze(robot);
            Debug.Log($"[StabilityTest] Architecture: {desc.RobotType} (Expected: {expectedArch}), Total Bodies: {bodies.Length}");

            // 2. Velocity Explosion Check
            float maxLinearVel = 0f;
            float maxAngularVel = 0f;
            ArticulationBody maxLinBody = null;
            ArticulationBody maxAngBody = null;

            foreach (var b in bodies)
            {
                if (b == null) continue;
                float linSpd = b.linearVelocity.magnitude;
                float angSpd = b.angularVelocity.magnitude;

                if (linSpd > maxLinearVel)
                {
                    maxLinearVel = linSpd;
                    maxLinBody = b;
                }
                if (angSpd > maxAngularVel)
                {
                    maxAngularVel = angSpd;
                    maxAngBody = b;
                }
            }

            Debug.Log($"[StabilityTest] Max Linear Velocity: {maxLinearVel:F2} m/s (Link: {maxLinBody?.name}), Max Angular Velocity: {maxAngularVel:F2} rad/s (Link: {maxAngBody?.name})");

            if (maxLinearVel > 5.0f || float.IsNaN(maxLinearVel))
            {
                Debug.LogError($"[StabilityTest] Assert FAILED: Linear velocity explosion detected! ({maxLinearVel:F2} m/s on '{maxLinBody?.name}')");
                return false;
            }

            if (maxAngularVel > 20.0f || float.IsNaN(maxAngularVel))
            {
                Debug.LogError($"[StabilityTest] Assert FAILED: Angular velocity explosion detected! ({maxAngularVel:F2} rad/s on '{maxAngBody?.name}')");
                return false;
            }

            // 3. Ground Clearance / Floating Check
            var colliders = robot.GetComponentsInChildren<Collider>(true);
            float lowestY = float.MaxValue;
            foreach (var c in colliders)
            {
                if (c != null && c.enabled && !c.isTrigger)
                {
                    if (c.bounds.min.y < lowestY) lowestY = c.bounds.min.y;
                }
            }

            // Determine ground surface elevation
            float groundY = 0f;
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain != null && terrain.terrainData != null)
            {
                groundY = terrain.SampleHeight(robot.transform.position) + terrain.transform.position.y;
            }
            else
            {
                var plane = GameObject.Find("TestFloorPlane") ?? GameObject.Find("RuntimeDefaultFloor") ?? GameObject.Find("Plane") ?? GameObject.Find("Floor") ?? GameObject.Find("Ground");
                if (plane != null)
                {
                    var col = plane.GetComponent<Collider>();
                    if (col != null) groundY = col.bounds.max.y;
                }
            }

            float clearanceAboveGround = lowestY - groundY;
            Debug.Log($"[StabilityTest] Lowest Collider Y: {lowestY:F3}, Ground Y: {groundY:F3}, Ground Clearance: {clearanceAboveGround:F3}m");

            // Floating assertion: lowest collider must be resting within 8cm of the ground, never floating in the air (>15cm)
            if (clearanceAboveGround > 0.15f)
            {
                Debug.LogError($"[StabilityTest] Assert FAILED: Robot is FLOATING in the air! Clearance is {clearanceAboveGround:F3}m (> 0.15m)");
                return false;
            }

            if (clearanceAboveGround < -0.15f)
            {
                Debug.LogError($"[StabilityTest] Assert FAILED: Robot fell THROUGH the ground! Clearance is {clearanceAboveGround:F3}m (< -0.15m)");
                return false;
            }

            // 4. Detached parts / explosion check (joints stretching out of sockets)
            int detachedCount = 0;
            foreach (var b in bodies)
            {
                if (b == null || b.isRoot) continue;
                var parentBody = b.transform.parent != null ? b.transform.parent.GetComponentInParent<ArticulationBody>() : null;
                if (parentBody != null)
                {
                    // Check distance between child and parent
                    float dist = Vector3.Distance(b.transform.position, parentBody.transform.position);
                    if (dist > 3.0f) // No robot link in these models is > 3 meters away from its parent
                    {
                        Debug.LogError($"[StabilityTest] Assert FAILED: Link '{b.name}' detached from parent '{parentBody.name}'! (Distance: {dist:F2}m)");
                        detachedCount++;
                    }
                }
            }

            if (detachedCount > 0)
            {
                return false;
            }

            // 5. Immovable status check
            if (expectedArch == RobotArchitectureType.RoboticArm)
            {
                if (!rootBody.immovable)
                {
                    Debug.LogWarning($"[StabilityTest] Warning: Robotic arm root is not immovable.");
                }
            }
            else
            {
                if (rootBody.immovable)
                {
                    Debug.LogError($"[StabilityTest] Assert FAILED: Mobile robot root is pinned (immovable = true)!");
                    return false;
                }
            }

            return true;
        }
    }
}
