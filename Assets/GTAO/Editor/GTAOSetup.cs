using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GTAO.Editor
{
    /// <summary>
    /// One-click project setup for the GTAO feature.  Everything here can also be done
    /// by hand in the URP renderer inspector - it is scripted so that a fresh clone of
    /// the project can be configured in a single batch-mode call:
    ///
    ///     Unity -batchmode -projectPath &lt;project&gt; -executeMethod GTAO.Editor.GTAOSetup.SetupEverything -quit
    ///
    /// It performs three steps:
    ///   1. switches the High Fidelity renderer to the Deferred path,
    ///   2. adds the <see cref="GTAORendererFeature"/> to that renderer,
    ///   3. builds the ambient occlusion test scene.
    /// </summary>
    public static class GTAOSetup
    {
        public const string RendererPath   = "Assets/Settings/URP-HighFidelity-Renderer.asset";
        public const string UrpAssetPath   = "Assets/Settings/URP-HighFidelity.asset";
        public const string TestScenePath  = "Assets/Scenes/AOTestScene.unity";

        // -------------------------------------------------------------------------------
        // Menu items
        // -------------------------------------------------------------------------------
        [MenuItem("Tools/GTAO/Setup URP + Renderer Feature", priority = 0)]
        public static void SetupURPMenu() => SetupURP();

        [MenuItem("Tools/GTAO/Build Ambient Occlusion Test Scene", priority = 1)]
        public static void BuildTestSceneMenu() => GTAOTestSceneBuilder.Build(TestScenePath);

        [MenuItem("Tools/GTAO/Setup Everything (URP + Scene)", priority = 20)]
        public static void SetupEverythingMenu() => SetupEverything();

        // -------------------------------------------------------------------------------
        // Batch entry points
        // -------------------------------------------------------------------------------
        public static void SetupEverything()
        {
            SetupURP();
            GTAOTestSceneBuilder.Build(TestScenePath);
            RegisterSceneInBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[GTAO] Setup complete. Open " + TestScenePath + " and enter Play mode.");
        }

        // -------------------------------------------------------------------------------
        // URP renderer setup
        // -------------------------------------------------------------------------------
        public static void SetupURP()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                Debug.LogError($"[GTAO] Could not find a UniversalRendererData at '{RendererPath}'.");
                return;
            }

            var so = new SerializedObject(rendererData);
            so.Update();

            // 1. Deferred rendering path is required for the multi-bounce albedo term.
            SerializedProperty renderingMode = so.FindProperty("m_RenderingMode");
            if (renderingMode != null)
                renderingMode.intValue = (int)RenderingMode.Deferred;

            // Keep the default (raw) G-buffer normal encoding so it lines up with
            // SampleSceneNormals() without extra decoding.
            SerializedProperty accurateNormals = so.FindProperty("m_AccurateGbufferNormals");
            if (accurateNormals != null)
                accurateNormals.boolValue = false;

            // 2. Add the GTAO renderer feature if it is not there yet.
            bool hasFeature = ContainsFeature(rendererData);
            if (!hasFeature)
            {
                var newFeature = ScriptableObject.CreateInstance<GTAORendererFeature>();
                newFeature.name = "Ground Truth Ambient Occlusion";
                AssetDatabase.AddObjectToAsset(newFeature, rendererData);

                SerializedProperty features = so.FindProperty("m_RendererFeatures");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = newFeature;

                SerializedProperty featureMap = so.FindProperty("m_RendererFeatureMap");
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(newFeature, out _, out long localId))
                {
                    featureMap.arraySize++;
                    featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rendererData);

            // Make sure the feature holds a direct reference to the shader.  Relying on
            // Shader.Find() alone would let the shader be stripped from player builds.
            var feature = rendererData.rendererFeatures.OfType<GTAORendererFeature>().FirstOrDefault();
            if (feature != null && feature.settings.shader == null)
            {
                feature.settings.shader = Shader.Find(GTAOShaderIDs.ShaderName);
                EditorUtility.SetDirty(feature);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(RendererPath, ImportAssetOptions.ForceUpdate);

            Debug.Log("[GTAO] URP renderer set to Deferred and GTAO renderer feature ensured.");
        }

        private static bool ContainsFeature(UniversalRendererData rendererData)
        {
            return rendererData.rendererFeatures != null
                && rendererData.rendererFeatures.OfType<GTAORendererFeature>().Any();
        }

        private static void RegisterSceneInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Any(s => s.path == TestScenePath))
                return;

            var list = scenes.ToList();
            list.Insert(0, new EditorBuildSettingsScene(TestScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
