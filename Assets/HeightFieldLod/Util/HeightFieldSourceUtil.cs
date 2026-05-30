using UnityEngine;

namespace HeightField
{
    public static class HeightFieldSourceUtil
    {
        public static void EnsureUpdated(this IHeightFieldSource source, HeightFieldLayout layout, float time)
        {
            if (source == null) return;
            int frame = Time.frameCount;
            if (s_lastFrame == frame && ReferenceEquals(s_lastSource, source))
                return;
            s_lastFrame = frame;
            s_lastSource = source;
            source.UpdateHeight(layout, time);
        }

        static int s_lastFrame = -1;
        static IHeightFieldSource s_lastSource;
    }
}
