using UnityEngine;

namespace HeightField
{
    public static class HeightFieldSourceUtil
    {
        public static void EnsureUpdated(this IHeightFieldSource source, HeightFieldLayout layout, float time)
        {
            if (source == null) return;
            var mb = source as MonoBehaviour;
            if (mb == null)
            {
                source.UpdateHeight(layout, time);
                return;
            }
            int id = mb.GetInstanceID();
            int frame = Time.frameCount;
            if (s_lastFrame == frame && s_lastSourceId == id)
                return;
            s_lastFrame = frame;
            s_lastSourceId = id;
            source.UpdateHeight(layout, time);
        }

        static int s_lastFrame = -1;
        static int s_lastSourceId;
    }
}
