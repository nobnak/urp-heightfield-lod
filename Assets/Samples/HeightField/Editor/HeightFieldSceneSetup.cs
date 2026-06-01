using System;
using System.IO;
using HeightField;
using HeightField.Samples;
using HeightFieldLod;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HeightField.Samples.Editor
{
    public static class HeightFieldSceneSetup
    {
        const string PackageRoot = "Packages/jp.nobnak.heightfield-lod";
        const string LitMaterialName = "HeightFieldLit";

        [MenuItem("GameObject/Height Field/Setup Sample Rig", false, 10)]
        static void SetupSampleRig()
        {
            var sceneFolder = TryGetActiveSceneAssetFolder();
            if (sceneFolder == null)
                return;

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                Undo.RegisterCreatedObjectUndo(camGo, "Setup HeightField Rig");
                cam = Undo.AddComponent<Camera>(camGo);
                Undo.AddComponent<AudioListener>(camGo);
                camGo.tag = "MainCamera";
            }

            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.transform.rotation = Quaternion.identity;

            var rig = new GameObject("HeightFieldRig");
            Undo.RegisterCreatedObjectUndo(rig, "Setup HeightField Rig");
            rig.transform.position = Vector3.zero;
            rig.transform.rotation = Quaternion.identity;

            var sine = Undo.AddComponent<SineHeightFieldSource>(rig);
            var host = Undo.AddComponent<HeightFieldLayoutHost>(rig);
            var compute = Undo.AddComponent<HeightFieldLodCompute>(rig);
            var drawer = Undo.AddComponent<HeightFieldChunkMeshDrawer>(rig);
            Undo.AddComponent<HeightFieldBridge>(rig);

            var fill = FindSineHeightFillShader();
            var normalFromHeight = LoadPackageAsset<ComputeShader>("Runtime/Shaders/NormalFromHeight.compute");
            var curvature = LoadPackageAsset<ComputeShader>("Runtime/Shaders/Curvature.compute");
            var reduction = LoadPackageAsset<ComputeShader>("Runtime/Shaders/ReductionMax.compute");
            var classify = LoadPackageAsset<ComputeShader>("Runtime/Shaders/ClassifyLOD.compute");
            var neighbor = LoadPackageAsset<ComputeShader>("Runtime/Shaders/NeighborLOD.compute");
            var litShader = Shader.Find("HeightFieldLod/HeightFieldLit");
            var mat = GetOrCreateSceneMaterial(sceneFolder, LitMaterialName, () =>
            {
                if (litShader == null)
                    return null;
                var m = new Material(litShader);
                m.enableInstancing = true;
                return m;
            });

            SetRef(sine, "_fillShader", fill);
            SetRef(compute, "_normalShader", normalFromHeight);
            SetRef(compute, "_curvatureShader", curvature);
            SetRef(compute, "_reductionShader", reduction);
            SetRef(compute, "_classifyShader", classify);
            SetRef(compute, "_neighborShader", neighbor);
            SetRef(host, "_camera", cam);
            SetRef(drawer, "_layoutHost", host);
            SetRef(drawer, "_lod", compute);
            SetRef(drawer, "_heightSource", sine);
            SetRef(drawer, "_material", mat);

            var bridge = rig.GetComponent<HeightFieldBridge>();
            SetRef(bridge, "_layoutHost", host);
            SetRef(bridge, "_camera", cam);
            SetRef(bridge, "_heightSource", sine);
            SetRef(bridge, "_lodCompute", compute);
            SetRef(bridge, "_drawer", drawer);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = rig;
            Debug.Log($"HeightField rig created. Assets folder: {sceneFolder}");
        }

        static string TryGetActiveSceneAssetFolder()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog(
                    "Height Field",
                    "Save the active scene before setting up the rig (assets are written next to the scene file).",
                    "OK");
                return null;
            }

            var scenePath = scene.path.Replace('\\', '/');
            var dir = Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
            var sceneName = Path.GetFileNameWithoutExtension(scenePath);
            var folder = $"{dir}/{sceneName}";
            EnsureAssetFolder(folder);
            return folder;
        }

        static void EnsureAssetFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
                return;
            var parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            var name = Path.GetFileName(assetFolder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                return;
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        static Material GetOrCreateSceneMaterial(string sceneFolder, string assetName, Func<Material> create)
        {
            var path = $"{sceneFolder}/{assetName}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;
            var asset = create();
            if (asset == null)
                return null;
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }

        static T LoadPackageAsset<T>(string relativePath) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");

        static ComputeShader FindSineHeightFillShader()
        {
            foreach (var guid in AssetDatabase.FindAssets("SineHeightFill t:ComputeShader"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                if (shader != null)
                    return shader;
            }
            return null;
        }

        static void SetRef(Object target, string propertyName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogWarning($"[HeightField] Property not found: {propertyName} on {target.GetType().Name}");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
