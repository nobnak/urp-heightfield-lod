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
            var lod = rig.AddComponent<HeightFieldLodRenderer>();
            rig.AddComponent<HeightFieldBridge>();

            var fill = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/HeightField/Shaders/SineHeightFill.compute");
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
            var soLod = new SerializedObject(lod);
            soLod.FindProperty("_material").objectReferenceValue = mat;
            soLod.FindProperty("_normalShader").objectReferenceValue = normalFromHeight;
            soLod.FindProperty("_curvatureShader").objectReferenceValue = curvature;
            soLod.FindProperty("_reductionShader").objectReferenceValue = reduction;
            soLod.FindProperty("_classifyShader").objectReferenceValue = classify;
            soLod.FindProperty("_neighborShader").objectReferenceValue = neighbor;
            soLod.ApplyModifiedPropertiesWithoutUndo();

            var bridge = rig.GetComponent<HeightFieldBridge>();
            var soBridge = new SerializedObject(bridge);
            soBridge.FindProperty("_camera").objectReferenceValue = cam;
            soBridge.FindProperty("_heightSource").objectReferenceValue = sine;
            soBridge.FindProperty("_lodRenderer").objectReferenceValue = lod;
            soBridge.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = rig;
            Debug.Log("HeightField rig created. Camera looks +Z; mesh on XY plane (Quad convention, normal -Z).");
        }
    }
}
