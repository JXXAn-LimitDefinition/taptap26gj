using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GTAO.Editor
{
    /// <summary>
    /// Small head-less verification helper: renders the AO test scene once with the GTAO
    /// feature enabled and once with it disabled, then writes both images to Logs/.
    /// Useful as a quick visual / regression check when changing the shader.
    ///
    ///     Unity -batchmode -projectPath &lt;project&gt; \
    ///           -executeMethod GTAO.Editor.GTAORenderCheck.Run -quit
    /// </summary>
    public static class GTAORenderCheck
    {
        private const string ScenePath    = GTAOSetup.TestScenePath;
        private const string RendererPath = GTAOSetup.RendererPath;
        private const string OutputDir    = "Logs";
        private const int    Width        = 1280;
        private const int    Height       = 720;

        public static void Run()
        {
            Shader shader = Shader.Find(GTAOShaderIDs.ShaderName);
            if (shader == null)
            {
                Debug.LogError("[GTAO] Shader 'Hidden/GTAO/URP' was not found.");
                return;
            }

            bool hasError = ShaderUtil.ShaderHasError(shader);
            Debug.Log($"[GTAO] shader found, supported={shader.isSupported}, hasError={hasError}");
            if (hasError)
                Debug.LogError("[GTAO] Shader reported compile errors - see the shader inspector.");

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            Camera camera = Camera.main;
            if (camera == null)
            {
                Debug.LogError("[GTAO] No main camera in the test scene.");
                return;
            }

            Directory.CreateDirectory(OutputDir);
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf) { name = "GTAO_Check" };
            rt.Create();
            camera.targetTexture = rt;

            // Warm up a few frames so the temporal history is populated.
            SetDebugMode(GTAODebugMode.Off);
            RenderToDisk(camera, rt, Path.Combine(OutputDir, "gtao_enabled.png"), warmupFrames: 4);

            SetDebugMode(GTAODebugMode.AmbientOcclusion);
            RenderToDisk(camera, rt, Path.Combine(OutputDir, "gtao_debug_ao.png"), warmupFrames: 2);

            SetDebugMode(GTAODebugMode.BentNormal);
            RenderToDisk(camera, rt, Path.Combine(OutputDir, "gtao_debug_bentnormal.png"), warmupFrames: 2);

            SetDebugMode(GTAODebugMode.Off);
            SetFeatureActive(false);
            RenderToDisk(camera, rt, Path.Combine(OutputDir, "gtao_disabled.png"), warmupFrames: 1);
            SetFeatureActive(true);

            camera.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);

            Debug.Log($"[GTAO] Wrote enabled / disabled / debug images to {OutputDir}/.");
        }

        private static void RenderToDisk(Camera camera, RenderTexture rt, string path, int warmupFrames)
        {
            for (int i = 0; i < warmupFrames; i++)
                camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAHalf, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();

            RenderTexture.active = previous;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        private static void SetFeatureActive(bool active)
        {
            var feature = FindFeature();
            if (feature == null)
                return;

            feature.SetActive(active);
            EditorUtility.SetDirty(feature);
            AssetDatabase.SaveAssets();
        }

        private static void SetDebugMode(GTAODebugMode mode)
        {
            var feature = FindFeature();
            if (feature == null)
                return;

            var so = new SerializedObject(feature);
            SerializedProperty settings = so.FindProperty("settings");
            settings.FindPropertyRelative("debugMode").intValue = (int)mode;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature);
            AssetDatabase.SaveAssets();
        }

        private static GTAORendererFeature FindFeature()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            return data == null ? null : data.rendererFeatures.OfType<GTAORendererFeature>().FirstOrDefault();
        }
    }
}
