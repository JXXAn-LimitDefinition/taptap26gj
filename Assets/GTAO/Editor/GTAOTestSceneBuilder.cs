using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GTAO.Editor
{
    /// <summary>
    /// Builds a deterministic, medium sized "courtyard" scene that is deliberately full
    /// of ambient occlusion test cases:
    ///
    ///   * a colonnade with arched spaces (deep concavities, contact shadows),
    ///   * a raised platform with stairs (long screen space horizons),
    ///   * scattered crates / barrels / spheres (small scale contact AO),
    ///   * an overhang that darkens the walkway below it,
    ///   * a mix of roughness values so bent normals and multi-bounce are visible.
    ///
    /// The scene is generated procedurally (no external assets) and with a fixed random
    /// seed, so every machine gets exactly the same setup.
    /// </summary>
    public static class GTAOTestSceneBuilder
    {
        private const int k_Seed = 20240607;

        private static Material s_Floor;
        private static Material s_Stone;
        private static Material s_StoneDark;
        private static Material s_Wood;
        private static Material s_Metal;
        private static Material s_Cloth;

        public static void Build(string scenePath)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = Path.GetFileNameWithoutExtension(scenePath);

            CreateMaterials();

            var root = new GameObject("AO_Courtyard");

            BuildGround(root);
            BuildPerimeter(root);
            BuildColonnade(root);
            BuildPlatform(root);
            BuildOverhang(root);
            BuildProps(root);

            BuildLighting();
            BuildCamera();

            // Solid, slightly cool background - a skybox would hide how the AO behaves
            // against the environment.
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.48f, 0.55f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.32f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.15f, 0.17f);
            RenderSettings.fog = false;

            Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[GTAO] Ambient occlusion test scene written to '{scenePath}'.");
        }

        // -------------------------------------------------------------------------------
        // Materials
        // -------------------------------------------------------------------------------
        private static void CreateMaterials()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            s_Floor     = MakeMaterial("AO_Floor",     lit, new Color(0.42f, 0.42f, 0.45f), 0.25f, 0.0f);
            s_Stone     = MakeMaterial("AO_Stone",     lit, new Color(0.72f, 0.68f, 0.60f), 0.15f, 0.0f);
            s_StoneDark = MakeMaterial("AO_StoneDark", lit, new Color(0.45f, 0.43f, 0.40f), 0.30f, 0.0f);
            s_Wood      = MakeMaterial("AO_Wood",      lit, new Color(0.55f, 0.34f, 0.18f), 0.35f, 0.0f);
            s_Metal     = MakeMaterial("AO_Metal",     lit, new Color(0.75f, 0.76f, 0.78f), 0.80f, 1.0f);
            s_Cloth     = MakeMaterial("AO_Cloth",     lit, new Color(0.62f, 0.20f, 0.22f), 0.05f, 0.0f);
        }

        private static Material MakeMaterial(string name, Shader shader, Color color, float smoothness, float metallic)
        {
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_EnvironmentReflections", 1.0f);
            return material;
        }

        // -------------------------------------------------------------------------------
        // Geometry helpers
        // -------------------------------------------------------------------------------
        private static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent,
            Vector3 position, Vector3 scale, Material material, Vector3 eulerAngles = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localEulerAngles = eulerAngles;
            go.transform.localScale = scale;

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return go;
        }

        private static GameObject CreateBox(string name, Transform parent, Vector3 position, Vector3 size,
            Material material, Vector3 eulerAngles = default)
        {
            return CreatePrimitive(PrimitiveType.Cube, name, parent, position, size, material, eulerAngles);
        }

        // -------------------------------------------------------------------------------
        // Scene content
        // -------------------------------------------------------------------------------
        private static void BuildGround(GameObject root)
        {
            CreatePrimitive(PrimitiveType.Plane, "Ground", root.transform,
                Vector3.zero, new Vector3(9f, 1f, 9f), s_Floor); // 90 x 90 m

            CreateBox("CourtyardFloor", root.transform,
                new Vector3(0f, 0.05f, 0f), new Vector3(46f, 0.1f, 40f), s_StoneDark);
        }

        private static void BuildPerimeter(GameObject root)
        {
            var walls = new GameObject("Walls").transform;
            walls.SetParent(root.transform, false);

            const float height = 14f;
            const float halfDepth = 20f;
            const float halfWidth = 23f;

            // Back wall and side walls are solid.
            CreateBox("Wall_Back", walls,
                new Vector3(0f, height * 0.5f, halfDepth), new Vector3(halfWidth * 2f, height, 1f), s_Stone);

            CreateBox("Wall_Left", walls,
                new Vector3(-halfWidth, height * 0.5f, 0f), new Vector3(1f, height, halfDepth * 2f), s_Stone);

            CreateBox("Wall_Right", walls,
                new Vector3(halfWidth, height * 0.5f, 0f), new Vector3(1f, height, halfDepth * 2f), s_Stone);

            // Front wall with a doorway in the middle.
            const float doorway = 6f;
            float sideWidth = halfWidth - doorway * 0.5f;
            CreateBox("Wall_Front_L", walls,
                new Vector3(-(doorway * 0.5f + sideWidth * 0.5f), height * 0.5f, -halfDepth),
                new Vector3(sideWidth, height, 1f), s_Stone);
            CreateBox("Wall_Front_R", walls,
                new Vector3(doorway * 0.5f + sideWidth * 0.5f, height * 0.5f, -halfDepth),
                new Vector3(sideWidth, height, 1f), s_Stone);
            CreateBox("Wall_Front_Lintel", walls,
                new Vector3(0f, height - 1.5f, -halfDepth), new Vector3(doorway, 3f, 1f), s_Stone);

            // A cornice all around the top edge adds a strong horizontal occlusion line.
            CreateBox("Cornice_Back", walls, new Vector3(0f, height - 0.5f, halfDepth - 0.6f),
                new Vector3(halfWidth * 2f, 1f, 1.6f), s_StoneDark);
            CreateBox("Cornice_Left", walls, new Vector3(-halfWidth + 0.6f, height - 0.5f, 0f),
                new Vector3(1.6f, 1f, halfDepth * 2f), s_StoneDark);
            CreateBox("Cornice_Right", walls, new Vector3(halfWidth - 0.6f, height - 0.5f, 0f),
                new Vector3(1.6f, 1f, halfDepth * 2f), s_StoneDark);
        }

        private static void BuildColonnade(GameObject root)
        {
            var colonnade = new GameObject("Colonnade").transform;
            colonnade.SetParent(root.transform, false);

            const int count = 9;          // pillars per side
            const float spacing = 4.2f;
            const float pillarHeight = 9f;
            const float radius = 0.7f;

            float startX = -(count - 1) * spacing * 0.5f;

            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? -13f : 11f;
                string sideName = side == 0 ? "South" : "North";

                for (int i = 0; i < count; i++)
                {
                    float x = startX + i * spacing;

                    // Base + shaft + capital (three pieces read well in AO).
                    CreateBox($"Pillar_{sideName}_{i}_Base", colonnade,
                        new Vector3(x, 0.4f, z), new Vector3(2.0f, 0.8f, 2.0f), s_StoneDark);
                    CreatePrimitive(PrimitiveType.Cylinder, $"Pillar_{sideName}_{i}", colonnade,
                        new Vector3(x, 0.8f + pillarHeight * 0.5f, z),
                        new Vector3(radius, pillarHeight * 0.5f, radius), s_Stone);
                    CreateBox($"Pillar_{sideName}_{i}_Capital", colonnade,
                        new Vector3(x, 0.8f + pillarHeight + 0.35f, z), new Vector3(1.8f, 0.7f, 1.8f), s_StoneDark);

                    // Semicircular arch to the next pillar.
                    if (i < count - 1)
                        BuildArch(colonnade, new Vector3(x + spacing * 0.5f, 0.8f + pillarHeight + 0.7f, z), spacing * 0.5f, sideName, i);
                }

                // Lintel beam spanning the whole colonnade.
                CreateBox($"Lintel_{sideName}", colonnade,
                    new Vector3(0f, 0.8f + pillarHeight + 1.4f, z),
                    new Vector3(count * spacing, 1.4f, 1.6f), s_StoneDark);
            }
        }

        private static void BuildArch(Transform parent, Vector3 center, float archRadius, string sideName, int index)
        {
            const int segments = 7;
            var arch = new GameObject($"Arch_{sideName}_{index}").transform;
            arch.SetParent(parent, false);
            arch.localPosition = center;

            for (int s = 0; s < segments; s++)
            {
                float t = (s + 0.5f) / segments;          // 0..1 across the arch
                float angle = Mathf.Lerp(0f, Mathf.PI, t); // 0..180 degrees
                float x = Mathf.Cos(angle) * archRadius;
                float y = Mathf.Sin(angle) * archRadius;

                CreateBox($"Voussoir_{s}", arch, new Vector3(x, y, 0f),
                    new Vector3(archRadius / segments * 1.6f, 0.7f, 1.4f),
                    (s & 1) == 0 ? s_Stone : s_StoneDark,
                    new Vector3(0f, 0f, -Mathf.Rad2Deg * (angle - Mathf.PI * 0.5f)));
            }
        }

        private static void BuildPlatform(GameObject root)
        {
            var platform = new GameObject("Platform").transform;
            platform.SetParent(root.transform, false);

            // A raised dais at the back of the courtyard.
            CreateBox("Dais", platform, new Vector3(0f, 1.5f, 15f), new Vector3(30f, 3f, 8f), s_Stone);

            // Steps leading up to it: each step is a little shorter and higher.
            const int stepCount = 6;
            for (int i = 0; i < stepCount; i++)
            {
                float height = 0.5f * (i + 1);
                float depth = 0.9f;
                CreateBox($"Step_{i}", platform,
                    new Vector3(0f, height * 0.5f, 10.5f - i * depth),
                    new Vector3(14f - i * 0.2f, height, depth),
                    (i & 1) == 0 ? s_Stone : s_StoneDark);
            }

            // Two statuesque blocks on the dais to cast long shadows.
            CreateBox("DaisBlock_L", platform, new Vector3(-9f, 4.5f, 15f), new Vector3(3f, 3f, 3f), s_StoneDark);
            CreateBox("DaisBlock_R", platform, new Vector3(9f, 4.5f, 15f), new Vector3(3f, 3f, 3f), s_StoneDark);
            CreatePrimitive(PrimitiveType.Sphere, "DaisOrb", platform,
                new Vector3(0f, 4.7f, 15f), new Vector3(4f, 4f, 4f), s_Metal);
        }

        private static void BuildOverhang(GameObject root)
        {
            var overhang = new GameObject("Overhang").transform;
            overhang.SetParent(root.transform, false);

            // Roof slab that darkens the south walkway.
            CreateBox("Roof_South", overhang, new Vector3(0f, 11.5f, -10f),
                new Vector3(46f, 0.8f, 8f), s_StoneDark);

            // Support beams underneath (thin geometry = plenty of contact AO).
            const int beams = 6;
            for (int i = 0; i < beams; i++)
            {
                float x = -18f + i * 7.2f;
                CreateBox($"Beam_{i}", overhang, new Vector3(x, 11f, -10f),
                    new Vector3(0.6f, 0.6f, 8f), s_Wood);
            }
        }

        private static void BuildProps(GameObject root)
        {
            var props = new GameObject("Props").transform;
            props.SetParent(root.transform, false);
            var random = new System.Random(k_Seed);

            // Crates, deterministically scattered but always away from the walkways.
            for (int i = 0; i < 46; i++)
            {
                float x = Mathf.Lerp(-20f, 20f, (float)random.NextDouble());
                float z = Mathf.Lerp(-17f, 17f, (float)random.NextDouble());
                if (Mathf.Abs(x) < 7f && Mathf.Abs(z) < 5f)
                    continue; // keep the centre of the courtyard clear for the camera

                float s = Mathf.Lerp(0.7f, 2.2f, (float)random.NextDouble());
                var go = CreateBox($"Crate_{i}", props,
                    new Vector3(x, s * 0.5f, z),
                    new Vector3(s, s, s),
                    ((i % 3) == 0) ? s_Wood : s_StoneDark,
                    new Vector3(0f, (float)random.NextDouble() * 360f, 0f));

                // Rotate a handful of crates so they rest on a corner (hard AO case).
                if ((i % 7) == 0)
                    go.transform.localEulerAngles = new Vector3((float)random.NextDouble() * 20f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 20f);
            }

            // Barrels (cylinders).
            for (int i = 0; i < 14; i++)
            {
                float x = Mathf.Lerp(-18f, 18f, (float)random.NextDouble());
                float z = Mathf.Lerp(-16f, 16f, (float)random.NextDouble());
                float h = Mathf.Lerp(1.2f, 2.0f, (float)random.NextDouble());
                CreatePrimitive(PrimitiveType.Cylinder, $"Barrel_{i}", props,
                    new Vector3(x, h * 0.5f, z), new Vector3(0.8f, h * 0.5f, 0.8f),
                    (i % 2 == 0) ? s_Metal : s_Wood);
            }

            // Spheres and capsules to show how bent normals handle curved surfaces.
            for (int i = 0; i < 12; i++)
            {
                float x = Mathf.Lerp(-18f, 18f, (float)random.NextDouble());
                float z = Mathf.Lerp(-16f, 16f, (float)random.NextDouble());
                float r = Mathf.Lerp(0.6f, 1.6f, (float)random.NextDouble());
                CreatePrimitive(i % 2 == 0 ? PrimitiveType.Sphere : PrimitiveType.Capsule,
                    $"Prop_{i}", props, new Vector3(x, r, z), new Vector3(r, r, r),
                    (i % 3 == 0) ? s_Metal : s_Cloth);
            }

            // A few tall thin poles - stress test for thin occluders / temporal stability.
            for (int i = 0; i < 8; i++)
            {
                float x = -16f + i * 4.6f;
                CreatePrimitive(PrimitiveType.Cylinder, $"Pole_{i}", props,
                    new Vector3(x, 5f, -6f), new Vector3(0.12f, 5f, 0.12f), s_Metal);
            }
        }

        private static void BuildLighting()
        {
            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.0f, 0.96f, 0.88f);
            light.intensity = 2.0f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -37f, 0f);

            // Two warm point lights inside so the AO reads against local contrast.
            CreatePointLight("Lamp_A", new Vector3(-14f, 6f, -8f), new Color(1.0f, 0.75f, 0.45f), 450f, 22f);
            CreatePointLight("Lamp_B", new Vector3(14f, 6f, 6f), new Color(0.55f, 0.7f, 1.0f), 400f, 22f);
        }

        private static void CreatePointLight(string name, Vector3 position, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.Soft;
        }

        private static void BuildCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";

            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.11f, 0.13f);
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 300f;

            // Three-quarter interior view of the courtyard: colonnade on the right,
            // the raised platform at the back and plenty of props in the foreground.
            go.transform.position = new Vector3(17f, 9f, -17f);
            go.transform.LookAt(new Vector3(-3f, 3.5f, 9f));

            var additionalData = go.AddComponent<UniversalAdditionalCameraData>();
            additionalData.renderPostProcessing = false;
            additionalData.antialiasing = AntialiasingMode.None;
        }
    }
}
