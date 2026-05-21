using System;
using Unity.Mathematics;
using UnityEngine;

namespace App.ViewMotion
{
    [Flags]
    public enum ViewMotionMode : byte
    {
        None = 0,
        Circular = 1 << 0,
        Bob = 1 << 1,
        Rotate = 1 << 2,
        Noise = 1 << 3,
        InertialSway = 1 << 4,
        FixedOffset = 1 << 5
    }

    /// <summary>接線平面の変位 d（m）を時間で合成する。</summary>
    public static class ViewMotion
    {
        [Serializable]
        public sealed class Params
        {
            public ViewMotionMode mode = ViewMotionMode.InertialSway | ViewMotionMode.Bob;

            [Header("Circular (m)")]
            public Vector2 circleSize = new(0.02f, 0.02f);
            [Min(0.001f)] public float circlePeriod = 6f;

            [Header("Noise (m)")]
            public Vector2 noiseSize = new(0.02f, 0.02f);
            [Min(0f)] public float noiseRate = 0.35f;
            public float noiseSeed;

            [Header("Inertial sway")]
            [Min(0f)] public float inertialSpring = 12f;
            [Min(0f)] public float inertialDamping = 5f;
            public Vector2 inertialNoiseAmp = new(0.03f, 0.03f);
            [Min(0f)] public float inertialNoiseRate = 0.35f;
            public float inertialNoiseSeed;

            [Header("Bob (m)")]
            [Min(0.001f)] public float bobRespPeriod = 5f;
            [Min(0f)] public float bobRespAmp = 0.02f;
            [Min(0.001f)] public float bobHeartPeriod = 60f / 70f;
            [Min(0f)] public float bobHeartAmp;
            [Min(1f)] public float bobHeartExp = 4f;

            [Header("Fixed offset (m)")]
            [Range(0f, 360f)] public float fixedOffsetDirectionDeg;
            [Min(0f)] public float fixedOffsetLength;

            [Header("Rotate (deg, snoise)")]
            [Range(0f, 89f)] public float rotateSnoiseDeg;
            [Min(0f)] public float rotateSnoiseRate = 0.35f;
            public float rotateSnoiseSeed;
        }

        public struct State
        {
            public float2 InertialPos;
            public float2 InertialVel;
        }

        public static Vector2 Evaluate(in Params p, float time, float deltaTime, ref State s)
        {
            var vm = p.mode;
            if (vm == ViewMotionMode.None)
                return Vector2.zero;
            if ((vm & ViewMotionMode.InertialSway) == 0) {
                s.InertialPos = 0;
                s.InertialVel = 0;
            }
            var view = Vector2.zero;
            if ((vm & ViewMotionMode.Circular) != 0)
                view += SampleCircular(p, time);
            if ((vm & ViewMotionMode.Noise) != 0)
                view += SampleNoise(p, time);
            if ((vm & ViewMotionMode.Bob) != 0)
                view += SampleBob(p, time);
            if ((vm & ViewMotionMode.InertialSway) != 0)
                view += IntegrateInertial(p, time, deltaTime, ref s);
            if ((vm & ViewMotionMode.FixedOffset) != 0)
                view += SampleFixedOffset(p);
            if ((vm & ViewMotionMode.Rotate) != 0)
                view = RotateCombined(p, time, view);
            return view;
        }

        static Vector2 SampleCircular(in Params p, float time)
        {
            if (p.circlePeriod <= 0f)
                return Vector2.zero;
            var a = time * (2f * Mathf.PI) / p.circlePeriod;
            return new Vector2(Mathf.Cos(a) * p.circleSize.x, Mathf.Sin(a) * p.circleSize.y);
        }

        static Vector2 SampleNoise(in Params p, float time)
        {
            if (p.noiseRate <= 0f || (p.noiseSize.x == 0f && p.noiseSize.y == 0f))
                return Vector2.zero;
            var t = time * p.noiseRate;
            var nx = noise.snoise(new float2(t, p.noiseSeed));
            var ny = noise.snoise(new float2(t, p.noiseSeed + 19.19f));
            nx = math.clamp(nx, -1f, 1f);
            ny = math.clamp(ny, -1f, 1f);
            return new Vector2(nx * p.noiseSize.x, ny * p.noiseSize.y);
        }

        static Vector2 IntegrateInertial(in Params p, float time, float deltaTime, ref State s)
        {
            var k = p.inertialSpring;
            var c = p.inertialDamping;
            var amp = p.inertialNoiseAmp;
            if (amp.x == 0f && amp.y == 0f && k <= 0f && c <= 0f)
                return Vector2.zero;
            var dt = deltaTime <= 0f ? 0f : math.min(deltaTime, 0.05f);
            if (dt <= 0f)
                return new Vector2(s.InertialPos.x, s.InertialPos.y);
            var rate = p.inertialNoiseRate;
            var t = time * rate;
            var nx = noise.snoise(new float2(t, p.inertialNoiseSeed));
            var ny = noise.snoise(new float2(t, p.inertialNoiseSeed + 19.19f));
            nx = math.clamp(nx, -1f, 1f);
            ny = math.clamp(ny, -1f, 1f);
            var n = new float2(nx * amp.x, ny * amp.y);
            s.InertialVel += (-k * s.InertialPos - c * s.InertialVel + n) * dt;
            s.InertialPos += s.InertialVel * dt;
            return new Vector2(s.InertialPos.x, s.InertialPos.y);
        }

        static Vector2 SampleBob(in Params p, float time)
        {
            if (p.bobRespPeriod <= 0f)
                return Vector2.zero;
            var aResp = time * (2f * Mathf.PI) / p.bobRespPeriod;
            var mag = Mathf.Sin(aResp) * p.bobRespAmp;
            if (p.bobHeartAmp > 0f && p.bobHeartPeriod > 0f) {
                var aHeart = time * (2f * Mathf.PI) / p.bobHeartPeriod;
                var sHeart = Mathf.Max(0f, Mathf.Sin(aHeart));
                mag += Mathf.Pow(sHeart, p.bobHeartExp) * p.bobHeartAmp;
            }
            return new Vector2(0f, mag);
        }

        static Vector2 SampleFixedOffset(in Params p)
        {
            var s = p.fixedOffsetLength;
            if (s <= 0f)
                return Vector2.zero;
            var rad = p.fixedOffsetDirectionDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * s;
        }

        static Vector2 RotateCombined(in Params p, float time, Vector2 view)
        {
            if (p.rotateSnoiseDeg <= 0f)
                return view;
            var rate = Mathf.Max(1e-4f, p.rotateSnoiseRate);
            var sn = noise.snoise(new float2(time * rate, p.rotateSnoiseSeed));
            sn = math.clamp(sn, -1f, 1f);
            var delta = sn * (p.rotateSnoiseDeg * Mathf.Deg2Rad);
            var c = Mathf.Cos(delta);
            var si = Mathf.Sin(delta);
            return new Vector2(view.x * c - view.y * si, view.x * si + view.y * c);
        }
    }
}
