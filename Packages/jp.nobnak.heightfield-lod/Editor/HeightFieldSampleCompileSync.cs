using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace HeightFieldLod.Editor
{
    [InitializeOnLoad]
    static class HeightFieldSampleCompileSync
    {
        static HeightFieldSampleCompileSync() =>
            CompilationPipeline.compilationFinished += OnCompilationFinished;

        static void OnCompilationFinished(object _) => HeightFieldSampleSync.CopyToPackageSamples();
    }

    static class HeightFieldSampleSync
    {
        const string PackageName = "jp.nobnak.heightfield-lod";
        const string SampleFolder = "HeightField";

        internal static void CopyToPackageSamples()
        {
            var src = Path.Combine(Application.dataPath, "Samples", SampleFolder);
            var packageRoot = Path.GetFullPath(Path.Combine("Packages", PackageName));
            var dstRoot = Path.Combine(packageRoot, "Samples~");
            var dst = Path.Combine(dstRoot, SampleFolder);

            if (!Directory.Exists(src))
            {
                Debug.LogWarning($"[HeightFieldLod] Sample source not found, skipping Samples~ sync: {src}");
                return;
            }

            if (Directory.Exists(dstRoot))
                Directory.Delete(dstRoot, recursive: true);

            CopyDirectory(src, dst);
            Debug.Log($"[HeightFieldLod] Synced samples on compile: {src} -> {dst}");
        }

        static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.GetDirectories(src))
                CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }
    }
}
