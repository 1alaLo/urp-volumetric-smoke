using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SmokeVolumetric.EditorTools
{
    /// <summary>
    /// Builds the fully playable demo scene from code so the project has no
    /// hand-authored scene/prefab dependencies.
    /// Run with:  unity command smoke_demo_build
    /// </summary>
    public static class SmokeDemoBuilder
    {
        const string Root = "Assets/SmokeVolumetric";
        const string ScenePath = Root + "/Scenes/SmokeDemo.unity";
        const string ResDir = Root + "/Resources";
        const string MatDir = Root + "/Materials";
        const string TexDir = Root + "/Textures";

        [CliCommand("smoke_demo_build", "Rebuild, save and open the volumetric smoke demo scene.")]
        public static void BuildDemo()
        {
            EnsureFolder(Root);
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(ResDir);
            EnsureFolder(MatDir);
            EnsureFolder(TexDir);

            var grid = MakeGridTexture();
            MakeSmokeMaterial();

            var floorMat = MakeLit("SmokeDemo_Floor", new Color(0.34f, 0.33f, 0.30f), 0.14f, 0f, grid, new Vector2(8f, 8f));
            var wallMat = MakeLit("SmokeDemo_Wall", new Color(0.62f, 0.60f, 0.56f), 0.20f, 0f, grid, new Vector2(2f, 2f));
            var crateMat = MakeLit("SmokeDemo_Crate", new Color(0.46f, 0.32f, 0.19f), 0.24f, 0f, null, Vector2.one);
            var metalMat = MakeLit("SmokeDemo_Metal", new Color(0.38f, 0.40f, 0.43f), 0.62f, 0.75f, null, Vector2.one);
            var targetMat = MakeLit("SmokeDemo_Target", new Color(0.66f, 0.13f, 0.11f), 0.34f, 0f, null, Vector2.one);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            SetupEnvironment();

            var arena = new GameObject("Arena").transform;

            // ---- ground -----------------------------------------------------
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground";
            floor.transform.SetParent(arena, false);
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(4f, 1f, 4f); // 40 x 40 m
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            // ---- perimeter --------------------------------------------------
            Box("Wall_N", new Vector3(0f, 2.5f, 20f), new Vector3(40f, 5f, 0.8f), wallMat, arena);
            Box("Wall_S", new Vector3(0f, 2.5f, -20f), new Vector3(40f, 5f, 0.8f), wallMat, arena);
            Box("Wall_W", new Vector3(-20f, 2.5f, 0f), new Vector3(0.8f, 5f, 40f), wallMat, arena);
            Box("Wall_E", new Vector3(20f, 2.5f, 0f), new Vector3(0.8f, 5f, 40f), wallMat, arena);

            // ---- mid wall with a flanking gap -------------------------------
            Box("MidWall", new Vector3(-5f, 1.6f, 5f), new Vector3(14f, 3.2f, 0.7f), wallMat, arena);

            // ---- central cover ----------------------------------------------
            Box("CenterCover", new Vector3(0f, 0.6f, -2f), new Vector3(6f, 1.2f, 0.7f), wallMat, arena);

            // ---- left building ----------------------------------------------
            Box("Building", new Vector3(-12f, 2.5f, 9f), new Vector3(8f, 5f, 8f), wallMat, arena);
            Box("BuildingLedge", new Vector3(-12f, 5.3f, 9f), new Vector3(8.6f, 0.6f, 8.6f), wallMat, arena);

            // ---- crates (right) ---------------------------------------------
            Box("Crate_A", new Vector3(7f, 0.55f, 0f), new Vector3(1.7f, 1.1f, 1.7f), crateMat, arena);
            Box("Crate_B", new Vector3(8.9f, 0.55f, 1.3f), new Vector3(1.7f, 1.1f, 1.7f), crateMat, arena);
            Box("Crate_C", new Vector3(7f, 1.65f, 0f), new Vector3(1.7f, 1.1f, 1.7f), crateMat, arena);

            // ---- tower / pillars --------------------------------------------
            Box("Tower", new Vector3(8f, 3f, 12f), new Vector3(5f, 6f, 5f), wallMat, arena);
            Box("Pillar_A", new Vector3(3f, 2f, 3f), new Vector3(1f, 4f, 1f), wallMat, arena);
            Box("Pillar_B", new Vector3(-2f, 2f, 10f), new Vector3(1f, 4f, 1f), wallMat, arena);

            // ---- barrels -----------------------------------------------------
            Cylinder("Barrel_A", new Vector3(-8f, 0.6f, -6f), new Vector3(0.9f, 0.6f, 0.9f), metalMat, arena);
            Cylinder("Barrel_B", new Vector3(-9.2f, 0.6f, -5f), new Vector3(0.9f, 0.6f, 0.9f), metalMat, arena);
            Cylinder("Barrel_C", new Vector3(10.5f, 0.6f, -6f), new Vector3(0.9f, 0.6f, 0.9f), metalMat, arena);

            // ---- reference target behind the cover ---------------------------
            Box("Target", new Vector3(0f, 1.15f, 2f), new Vector3(0.9f, 2.3f, 0.9f), targetMat, arena);

            // ---- player ------------------------------------------------------
            var player = BuildPlayer();

            // ---- hud ---------------------------------------------------------
            var hud = new GameObject("HUD");
            hud.AddComponent<DemoHud>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild();

            Selection.activeGameObject = player;
            Debug.Log("[SmokeDemo] Built " + ScenePath + " (smoke shader + playable FPS setup).");
        }

        [CliCommand("smoke_preview", "Spawn a preview smoke cloud at the Scene view pivot.")]
        public static void SpawnPreview()
        {
            Vector3 pivot = Vector3.zero;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null)
                pivot = sv.pivot;

            if (Physics.Raycast(pivot + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 80f))
                pivot = hit.point;

            var vol = SmokeVolume.Spawn(pivot, 3.3f, 18f);
            vol.transform.SetParent(null);
            vol.Preview(5f);

            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
                EditorSceneManager.MarkSceneDirty(scene);
        }

        // ------------------------------------------------------------------
        // scene helpers
        // ------------------------------------------------------------------
        static void SetupEnvironment()
        {
            var sunGO = new GameObject("Sun");
            var sun = sunGO.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.955f, 0.87f);
            sun.intensity = 1.55f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sunGO.transform.rotation = Quaternion.Euler(50f, -34f, 0f);

            var fillGO = new GameObject("FillLight");
            var fill = fillGO.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.52f, 0.66f, 0.92f);
            fill.intensity = 0.30f;
            fill.shadows = LightShadows.None;
            fillGO.transform.rotation = Quaternion.Euler(28f, 152f, 0f);

            var skyPath = MatDir + "/SmokeDemo_Sky.mat";
            AssetDatabase.DeleteAsset(skyPath);
            var sky = new Material(Shader.Find("Skybox/Procedural")) { name = "SmokeDemo_Sky" };
            sky.SetFloat("_AtmosphereThickness", 1.05f);
            sky.SetColor("_SkyTint", new Color(0.52f, 0.58f, 0.70f));
            sky.SetColor("_GroundColor", new Color(0.34f, 0.33f, 0.32f));
            sky.SetFloat("_Exposure", 1.15f);
            AssetDatabase.CreateAsset(sky, skyPath);

            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.60f, 0.66f, 0.74f);
            RenderSettings.fogStartDistance = 35f;
            RenderSettings.fogEndDistance = 140f;
        }

        static GameObject BuildPlayer()
        {
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 0.05f, -12f);
            player.transform.rotation = Quaternion.identity;

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.34f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.4f;
            cc.skinWidth = 0.02f;

            var camGO = new GameObject("PlayerCamera");
            camGO.transform.SetParent(player.transform, false);
            camGO.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            camGO.tag = "MainCamera";

            var cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = 72f;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 260f;
            cam.allowHDR = true;
            cam.allowMSAA = false;
            camGO.AddComponent<AudioListener>();

            var fps = player.AddComponent<FpsPlayer>();
            fps.viewCamera = cam;

            return player;
        }

        static GameObject Box(string name, Vector3 pos, Vector3 scale, Material mat, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
            return go;
        }

        static GameObject Cylinder(string name, Vector3 pos, Vector3 scale, Material mat, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
            return go;
        }

        // ------------------------------------------------------------------
        // assets
        // ------------------------------------------------------------------
        static Material MakeLit(string name, Color color, float smoothness, float metallic,
                                Texture2D baseMap, Vector2 tiling)
        {
            var path = MatDir + "/" + name + ".mat";
            AssetDatabase.DeleteAsset(path);

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);

            if (baseMap != null)
            {
                mat.SetTexture("_BaseMap", baseMap);
                mat.SetTextureScale("_BaseMap", tiling);
            }

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void MakeSmokeMaterial()
        {
            var shader = Shader.Find("SmokeVolumetric/SmokeVolume");
            if (shader == null)
            {
                Debug.LogError("[SmokeDemo] Smoke shader not found - check compile errors first.");
                return;
            }

            var path = ResDir + "/SmokeVolume.mat";
            AssetDatabase.DeleteAsset(path);

            var mat = new Material(shader) { name = "SmokeVolume" };
            mat.SetColor("_ScatterColor", new Color(0.93f, 0.945f, 0.97f, 1f));
            mat.SetColor("_AmbientColor", new Color(0.34f, 0.40f, 0.50f, 1f));
            mat.SetFloat("_AmbientStrength", 1.25f);
            mat.SetFloat("_Density", 5.0f);
            mat.SetFloat("_Absorption", 1.45f);
            mat.SetFloat("_NoiseScale", 1.05f);
            mat.SetFloat("_StepCount", 56f);
            mat.SetFloat("_EdgeSoftness", 0.42f);
            mat.SetFloat("_ShadowStrength", 1.05f);

            AssetDatabase.CreateAsset(mat, path);
        }

        static Texture2D MakeGridTexture()
        {
            const int size = 256;
            const int cell = 32;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false);
            var pts = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.09f, y * 0.09f) * 0.06f - 0.03f;
                    float v = 0.50f + n;

                    bool line = (x % cell == 0) || (y % cell == 0);
                    if (line) v += 0.10f;

                    // slightly darker every 4th line for scale reference
                    if (x % (cell * 4) == 0 || y % (cell * 4) == 0) v += 0.06f;

                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                    pts[y * size + x] = new Color32(b, b, b, 255);
                }
            }

            tex.SetPixels32(pts);
            tex.Apply(true);

            var path = TexDir + "/SmokeDemo_Grid.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void AddSceneToBuild()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath))
            {
                scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
