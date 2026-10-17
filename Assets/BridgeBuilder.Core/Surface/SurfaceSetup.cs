using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    public sealed class SurfaceSetupResult
    {
        public List<Node> DotsA { get; } = new List<Node>();
        public List<Node> DotsB { get; } = new List<Node>();
        public double MaxRoadWidthA { get; set; }
        public double MaxRoadWidthB { get; set; }
        public Vec3 Up { get; set; }
        public Vec3 GapDirection { get; set; }     // first edge midpoint to second edge midpoint (not normalized)
        public bool NonParallel { get; set; }
    }

    /// <summary>
    /// Four-tap surface setup math (spec 2.2): snap dots evenly along each marked edge (fixed supports),
    /// road width from edge length, up from gravity, gap from midpoint to midpoint. Non-parallel edges are
    /// detected and flagged only; spec 2.2 and 7 defer the policy. (The game now uses Pads, spec 11.2.)
    /// </summary>
    public static class SurfaceSetup
    {
        public static SurfaceSetupResult Compute(Vec3 a0, Vec3 a1, Vec3 b0, Vec3 b1, Vec3 gravity, double spacing)
        {
            if (!(spacing > 0))
                throw new ArgumentOutOfRangeException(nameof(spacing));
            if (!(gravity.Length > 0))
                throw new ArgumentOutOfRangeException(nameof(gravity));
            double lenA = Vec3.Distance(a0, a1), lenB = Vec3.Distance(b0, b1);
            if (!(lenA > 0) || !(lenB > 0))
                throw new ArgumentOutOfRangeException(nameof(a0), "Zero-length edge");

            var result = new SurfaceSetupResult
            {
                Up = -gravity.Normalized(),
                GapDirection = (b0 + b1) * 0.5 - (a0 + a1) * 0.5,
                MaxRoadWidthA = lenA,
                MaxRoadWidthB = lenB,
            };
            AddDots(result.DotsA, a0, a1, spacing, 0);
            AddDots(result.DotsB, b0, b1, spacing, 1);

            Vec3 ua = (a1 - a0) / lenA, ub = (b1 - b0) / lenB;
            double angle = Math.Acos(Math.Clamp(Math.Abs(Vec3.Dot(ua, ub)), 0, 1)) * 180 / Math.PI;
            result.NonParallel = angle > Constants.Surface.NonParallelAngleDegrees;
            return result;
        }

        static void AddDots(List<Node> dots, Vec3 a, Vec3 b, double spacing, int surface)
        {
            int n = EqualSegments.Count(Vec3.Distance(a, b), spacing);
            for (int i = 0; i <= n; i++)
                dots.Add(new Node { Id = i, Position = i == n ? b : Vec3.Lerp(a, b, i / (double)n), Fixed = true, SurfaceId = surface });
        }
    }
}
