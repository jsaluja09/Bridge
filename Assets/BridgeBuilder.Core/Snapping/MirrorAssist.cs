using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>A vertical plane the bridge is mirrored across. Room units.</summary>
    public readonly struct MirrorPlane
    {
        public readonly Vec3 Origin;
        public readonly Vec3 Normal;           // unit, horizontal

        public MirrorPlane(Vec3 origin, Vec3 normal)
        {
            Origin = origin;
            Normal = normal.Normalized();
        }

        /// <summary>The road's centerline plane: through both pads' front centers, standing upright.</summary>
        public static MirrorPlane Centerline(Pad a, Pad b) =>
            new MirrorPlane((a.FrontCenter + b.FrontCenter) * 0.5, a.Across);

        public double SignedDistance(Vec3 p) => Vec3.Dot(p - Origin, Normal);

        public Vec3 Reflect(Vec3 p) => p - Normal * (2 * SignedDistance(p));
    }

    /// <summary>
    /// Auto-mirror: real bridges carry two matching trusses, one each side of the road, so whatever is drawn on
    /// one side is copied to the other across the road's centerline. A piece that is already its own mirror
    /// image (on the centerline, or a symmetric cross-beam) is not copied.
    /// </summary>
    public static class MirrorAssist
    {
        /// <summary>The mirror copy of a piece, or null when the piece is already its own mirror image.</summary>
        public static List<Vec3> Mirror(IReadOnlyList<Vec3> piece, MirrorPlane plane) =>
            Mirror(piece, plane, Constants.Mirror.SelfMirrorToleranceMeters);

        public static List<Vec3> Mirror(IReadOnlyList<Vec3> piece, MirrorPlane plane, double tolerance)
        {
            if (piece == null || piece.Count < 2)
                return null;
            var copy = new List<Vec3>(piece.Count);
            foreach (var p in piece)
                copy.Add(plane.Reflect(p));
            return IsSelfMirror(piece, copy, tolerance) ? null : copy;
        }

        /// <summary>True if the copy matches the piece point for point, in the same or the reverse order.</summary>
        static bool IsSelfMirror(IReadOnlyList<Vec3> piece, List<Vec3> copy, double tolerance)
        {
            int n = piece.Count;
            double same = 0, reversed = 0;
            for (int i = 0; i < n; i++)
            {
                same = Math.Max(same, Vec3.Distance(copy[i], piece[i]));
                reversed = Math.Max(reversed, Vec3.Distance(copy[i], piece[n - 1 - i]));
            }
            return Math.Min(same, reversed) <= tolerance;
        }
    }
}
