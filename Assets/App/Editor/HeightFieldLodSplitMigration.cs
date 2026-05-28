using App.Bridge;
using HeightFieldLod;
using UnityEditor;
using UnityEngine;

namespace App.Editor
{
    public static class HeightFieldLodSplitMigration
    {
        [MenuItem("GameObject/Height Field/Migrate Rig To Split Components", false, 11)]
        static void MigrateSelected()
        {
            var rig = Selection.activeGameObject;
            if (rig == null)
            {
                Debug.LogWarning("Select a HeightField rig GameObject.");
                return;
            }
            var legacy = rig.GetComponent<HeightFieldLodRenderer>();
            if (legacy == null)
            {
                Debug.LogWarning($"{rig.name}: no {nameof(HeightFieldLodRenderer)} to migrate.");
                return;
            }
            Migrate(rig, legacy);
        }

        public static void Migrate(GameObject rig, HeightFieldLodRenderer legacy)
        {
            var host = rig.GetComponent<HeightFieldLayoutHost>();
            if (host == null)
                host = rig.AddComponent<HeightFieldLayoutHost>();

            var compute = rig.GetComponent<HeightFieldLodCompute>();
            if (compute == null)
                compute = rig.AddComponent<HeightFieldLodCompute>();

            var drawer = rig.GetComponent<HeightFieldChunkMeshDrawer>();
            if (drawer == null)
                drawer = rig.AddComponent<HeightFieldChunkMeshDrawer>();

            CopyLegacyToCompute(legacy, compute);
            CopyLegacyToDrawer(legacy, drawer);

            var bridge = rig.GetComponent<HeightFieldBridge>();
            if (bridge != null)
            {
                var soBridge = new SerializedObject(bridge);
                soBridge.FindProperty("_layoutHost").objectReferenceValue = host;
                soBridge.FindProperty("_lodCompute").objectReferenceValue = compute;
                soBridge.FindProperty("_drawer").objectReferenceValue = drawer;
                var legacyRef = soBridge.FindProperty("_lodRenderer");
                if (legacyRef != null)
                    legacyRef.objectReferenceValue = null;
                soBridge.ApplyModifiedPropertiesWithoutUndo();
            }

            var soDrawer = new SerializedObject(drawer);
            soDrawer.FindProperty("_layoutHost").objectReferenceValue = host;
            soDrawer.FindProperty("_lod").objectReferenceValue = compute;
            if (soDrawer.FindProperty("_heightSource").objectReferenceValue == null)
            {
                var bridgeSo = bridge != null ? new SerializedObject(bridge) : null;
                var hs = bridgeSo?.FindProperty("_heightSource").objectReferenceValue;
                if (hs != null)
                    soDrawer.FindProperty("_heightSource").objectReferenceValue = hs;
            }
            soDrawer.ApplyModifiedPropertiesWithoutUndo();

            Object.DestroyImmediate(legacy);
            Debug.Log($"Migrated {rig.name} to HeightFieldLodCompute + HeightFieldChunkMeshDrawer.");
        }

        static void CopyLegacyToCompute(HeightFieldLodRenderer legacy, HeightFieldLodCompute compute)
        {
            var src = new SerializedObject(legacy);
            var dst = new SerializedObject(compute);
            CopyRef(src, dst, "_normalShader");
            CopyRef(src, dst, "_curvatureShader");
            CopyRef(src, dst, "_reductionShader");
            CopyRef(src, dst, "_classifyShader");
            CopyRef(src, dst, "_neighborShader");
            CopyFloat(src, dst, "_skirtDepthMeters");
            CopyFloat(src, dst, "_curvatureScale");
            CopyFloat(src, dst, "_lodUpHigh");
            CopyFloat(src, dst, "_lodUpMid");
            CopyFloat(src, dst, "_lodUpLow");
            CopyFloat(src, dst, "_lodDownHigh");
            CopyFloat(src, dst, "_lodDownMid");
            CopyFloat(src, dst, "_lodDownLow");
            dst.ApplyModifiedPropertiesWithoutUndo();
        }

        static void CopyLegacyToDrawer(HeightFieldLodRenderer legacy, HeightFieldChunkMeshDrawer drawer)
        {
            var src = new SerializedObject(legacy);
            var dst = new SerializedObject(drawer);
            CopyRef(src, dst, "_material");
            CopyBool(src, dst, "_castShadows");
            dst.ApplyModifiedPropertiesWithoutUndo();
        }

        static void CopyRef(SerializedObject src, SerializedObject dst, string name)
        {
            var sp = src.FindProperty(name);
            if (sp != null)
                dst.FindProperty(name).objectReferenceValue = sp.objectReferenceValue;
        }

        static void CopyFloat(SerializedObject src, SerializedObject dst, string name)
        {
            var sp = src.FindProperty(name);
            if (sp != null)
                dst.FindProperty(name).floatValue = sp.floatValue;
        }

        static void CopyBool(SerializedObject src, SerializedObject dst, string name)
        {
            var sp = src.FindProperty(name);
            if (sp != null)
                dst.FindProperty(name).boolValue = sp.boolValue;
        }
    }
}
