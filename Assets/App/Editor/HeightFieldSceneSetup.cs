using App.Bridge;
using HeightField;
using HeightFieldLod;
using UnityEditor;
using UnityEngine;

namespace App.Editor
{
    public static class HeightFieldSceneSetup
    {
        [MenuItem("GameObject/Height Field/Setup Sample Rig", false, 10)]
        static void SetupSampleRig()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                camGo.tag = "MainCamera";
            }

            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.transform.rotation = Quaternion.identity;

            var rig = new GameObject("HeightFieldRig");
            rig.transform.position = Vector3.zero;
            rig.transform.rotation = Quaternion.identity;

            var sine = rig.AddComponent<SineHeightFieldSource>();
            var host = rig.AddComponent<HeightFieldLayoutHost>();
            var compute = rig.AddComponent<HeightFieldLodCompute>();
            var drawer = rig.AddComponent<HeightFieldChunkMeshDrawer>();
            rig.AddComponent<HeightFieldBridge>();

            var fill = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Samples/HeightField/Shaders/SineHeightFill.compute");
            var normalFromHeight = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/HeightFieldLod/Shaders/NormalFromHeight.compute");
            var curvature = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/HeightFieldLod/Shaders/Curvature.compute");
            var reduction = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/HeightFieldLod/Shaders/ReductionMax.compute");
            var classify = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/HeightFieldLod/Shaders/ClassifyLOD.compute");
            var neighbor = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/HeightFieldLod/Shaders/NeighborLOD.compute");
            var litShader = Shader.Find("HeightFieldLod/HeightFieldLit");

            var soSine = new SerializedObject(sine);
            soSine.FindProperty("_fillShader").objectReferenceValue = fill;
            soSine.ApplyModifiedPropertiesWithoutUndo();

            var mat = litShader != null ? new Material(litShader) : null;
            var soCompute = new SerializedObject(compute);
            soCompute.FindProperty("_normalShader").objectReferenceValue = normalFromHeight;
            soCompute.FindProperty("_curvatureShader").objectReferenceValue = curvature;
            soCompute.FindProperty("_reductionShader").objectReferenceValue = reduction;
            soCompute.FindProperty("_classifyShader").objectReferenceValue = classify;
            soCompute.FindProperty("_neighborShader").objectReferenceValue = neighbor;
            soCompute.ApplyModifiedPropertiesWithoutUndo();

            var soHost = new SerializedObject(host);
            soHost.FindProperty("_camera").objectReferenceValue = cam;
            soHost.ApplyModifiedPropertiesWithoutUndo();

            var soDrawer = new SerializedObject(drawer);
            soDrawer.FindProperty("_layoutHost").objectReferenceValue = host;
            soDrawer.FindProperty("_lod").objectReferenceValue = compute;
            soDrawer.FindProperty("_heightSource").objectReferenceValue = sine;
            soDrawer.FindProperty("_material").objectReferenceValue = mat;
            soDrawer.FindProperty("_camera").objectReferenceValue = cam;
            soDrawer.ApplyModifiedPropertiesWithoutUndo();

            var bridge = rig.GetComponent<HeightFieldBridge>();
            var soBridge = new SerializedObject(bridge);
            soBridge.FindProperty("_layoutHost").objectReferenceValue = host;
            soBridge.FindProperty("_camera").objectReferenceValue = cam;
            soBridge.FindProperty("_heightSource").objectReferenceValue = sine;
            soBridge.FindProperty("_lodCompute").objectReferenceValue = compute;
            soBridge.FindProperty("_drawer").objectReferenceValue = drawer;
            soBridge.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = rig;
            Debug.Log("HeightField rig created (split Compute + Drawer).");
        }
    }
}
