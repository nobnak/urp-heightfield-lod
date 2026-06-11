using HeightField;
using UnityEngine;

namespace HeightFieldLod
{
    public static class HeightFieldRigUtil
    {
        public static IHeightFieldSource FindHeightSource(GameObject go)
        {
            var components = go.GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is IHeightFieldSource source)
                    return source;
            }
            return null;
        }
    }
}
