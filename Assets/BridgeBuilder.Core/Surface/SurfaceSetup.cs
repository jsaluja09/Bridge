using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// One marked surface edge as the player tapped it (section 2.2): the two ends of the edge
    /// that faces the gap, in room units, in tap order.
    /// </summary>
    public readonly struct MarkedEdge
    {
        public MarkedEdge(int surfaceId, Vec3 start, Vec3 end)
        {
            SurfaceId = surfaceId;
            Start = start;
            End = end;
        }

        /// <summary>Id written to <see cref="Node.SurfaceId"/> on every snap dot of this edge.</summary>
        public int SurfaceId { get; }

        public Vec3 Start { get; }

        public Vec3 End { get; }
    }

    /// <summary>A marked surface after setup: its edge, its maximum road width, and its snap dots.</summary>
    public sealed class MarkedSurface
    {
        internal MarkedSurface(int surfaceId, Vec3 edgeStart, Vec3 edgeEnd, double edgeLength, Vec3 edgeMidpoint, Node[] snapDots)
        {
            SurfaceId = surfaceId;
            EdgeStart = edgeStart;
            EdgeEnd = edgeEnd;
            EdgeLength = edgeLength;
            EdgeMidpoint = edgeMidpoint;
            SnapDots = snapDots;
        }

        public int SurfaceId { get; }

        public Vec3 EdgeStart { get; }

        public Vec3 EdgeEnd { get; }

        /// <summary>Edge length, room units.</summary>
        public double EdgeLength { get; }

        /// <summary>Maximum road width on this side, room units. The marked edge length sets it (section 2.2).</summary>
        public double MaxRoadWidth => EdgeLength;

        public Vec3 EdgeMidpoint { get; }

        /// <summary>
        /// Snap dots evenly spaced from <see cref="EdgeStart"/> to <see cref="EdgeEnd"/>, both ends
        /// included. Every dot is <see cref="Node.Fixed"/>, is not a <see cref="Node.GroundAnchor"/>,
        /// and carries <see cref="SurfaceId"/>.
        /// </summary>
        public IReadOnlyList<Node> SnapDots { get; }
    }

    /// <summary>Result of marking both surfaces (section 2.2).</summary>
    public sealed class SurfaceSetupResult
    {
        internal SurfaceSetupResult(MarkedSurface first, MarkedSurface second, Vec3 up, Vec3 gap, double edgeAngleDegrees, bool nonParallel, Node[] snapDots)
        {
            First = first;
            Second = second;
            Up = up;
            Gap = gap;
            EdgeAngleDegrees = edgeAngleDegrees;
            NonParallel = nonParallel;
            SnapDots = snapDots;
        }

        public MarkedSurface First { get; }

        public MarkedSurface Second { get; }

        /// <summary>Unit vector opposite headset gravity.</summary>
        public Vec3 Up { get; }

        /// <summary>
        /// Vector from the first edge midpoint to the second, room units. Its direction is the gap
        /// direction and its Y component is the height difference between the surfaces.
        /// </summary>
        public Vec3 Gap { get; }

        /// <summary>Angle between the two edge lines, degrees, from 0 to 90. Tap order on either edge does not change it.</summary>
        public double EdgeAngleDegrees { get; }

        /// <summary>
        /// True when <see cref="EdgeAngleDegrees"/> exceeds <see cref="Constants.Surface.NonParallelAngleDegrees"/>.
        /// Section 2.2 defers the policy for this case, so setup only raises the flag and both
        /// edges keep all their dots.
        /// </summary>
        public bool NonParallel { get; }

        /// <summary>Snap dots of the first surface followed by those of the second, with consecutive node ids.</summary>
        public IReadOnlyList<Node> SnapDots { get; }
    }

    /// <summary>Surface setup math (section 2.2): snap dots along each marked edge, road width, up, and gap.</summary>
    public static class SurfaceSetup
    {
        private const double DegreesPerRadian = 180.0 / Math.PI;

        /// <summary>Same as the full overload with node ids starting at 0.</summary>
        public static SurfaceSetupResult Build(MarkedEdge first, MarkedEdge second, Vec3 gravity, double spacing)
        {
            return Build(first, second, gravity, spacing, 0);
        }

        /// <summary>
        /// Places fixed snap dots along both marked edges and derives up, the gap, and the
        /// non-parallel flag. Snap dot node ids run consecutively from <paramref name="firstNodeId"/>,
        /// first edge then second, each in tap order.
        /// </summary>
        /// <param name="gravity">Headset gravity in any units. Only its direction is used.</param>
        /// <param name="spacing">Target snap spacing, room units.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Spacing is not positive and finite, an edge has zero or non-finite length, or gravity has zero or non-finite length.
        /// </exception>
        /// <exception cref="ArgumentException">Both edges have the same surface id.</exception>
        public static SurfaceSetupResult Build(MarkedEdge first, MarkedEdge second, Vec3 gravity, double spacing, int firstNodeId)
        {
            if (first.SurfaceId == second.SurfaceId)
            {
                throw new ArgumentException("The two marked edges need different surface ids.", nameof(second));
            }

            Vec3 up = UpFromGravity(gravity);
            Vec3[] firstPositions = SnapDotPositions(first.Start, first.End, spacing, nameof(first));
            Vec3[] secondPositions = SnapDotPositions(second.Start, second.End, spacing, nameof(second));
            if ((long)firstNodeId + firstPositions.Length + secondPositions.Length - 1 > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(firstNodeId), firstNodeId, "Snap dot node ids would overflow.");
            }

            Node[] firstDots = ToSnapDots(firstPositions, first.SurfaceId, firstNodeId);
            Node[] secondDots = ToSnapDots(secondPositions, second.SurfaceId, firstNodeId + firstPositions.Length);
            Vec3 firstDelta = first.End - first.Start;
            Vec3 secondDelta = second.End - second.Start;
            Vec3 firstMidpoint = first.Start + firstDelta * 0.5;
            Vec3 secondMidpoint = second.Start + secondDelta * 0.5;
            Vec3 gap = secondMidpoint - firstMidpoint;
            if (!IsFinite(gap))
            {
                throw new ArgumentOutOfRangeException(nameof(second), gap, "The distance between the edge midpoints is not finite.");
            }

            double angle = EdgeAngleDegrees(firstDelta, secondDelta);
            var allDots = new Node[firstDots.Length + secondDots.Length];
            firstDots.CopyTo(allDots, 0);
            secondDots.CopyTo(allDots, firstDots.Length);

            return new SurfaceSetupResult(
                new MarkedSurface(first.SurfaceId, first.Start, first.End, firstDelta.Length, firstMidpoint, firstDots),
                new MarkedSurface(second.SurfaceId, second.Start, second.End, secondDelta.Length, secondMidpoint, secondDots),
                up,
                gap,
                angle,
                angle > Constants.Surface.NonParallelAngleDegrees,
                allDots);
        }

        /// <summary>
        /// Evenly spaced points from <paramref name="start"/> to <paramref name="end"/>, both included.
        /// The segment count is the edge length over <paramref name="spacing"/>, rounded half away
        /// from zero in decimal, and at least one.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Spacing is not positive and finite, or the edge has zero or non-finite length.</exception>
        public static Vec3[] SnapDotPositions(Vec3 start, Vec3 end, double spacing)
        {
            return SnapDotPositions(start, end, spacing, nameof(end));
        }

        /// <summary>The unit vector opposite <paramref name="gravity"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Gravity has zero or non-finite length.</exception>
        public static Vec3 UpFromGravity(Vec3 gravity)
        {
            double length = gravity.Length;
            if (!(length > 0.0 && double.IsFinite(length)))
            {
                throw new ArgumentOutOfRangeException(nameof(gravity), gravity, "Gravity must have a positive, finite length.");
            }

            return -gravity / length;
        }

        private static Vec3[] SnapDotPositions(Vec3 start, Vec3 end, double spacing, string edgeName)
        {
            if (!(spacing > 0.0 && double.IsFinite(spacing)))
            {
                throw new ArgumentOutOfRangeException(nameof(spacing), spacing, "Spacing must be positive and finite.");
            }

            if (!IsFinite(start) || !IsFinite(end))
            {
                throw new ArgumentOutOfRangeException(edgeName, "Edge endpoints must be finite.");
            }

            Vec3 delta = end - start;
            double length = delta.Length;
            if (!(length > 0.0 && double.IsFinite(length)))
            {
                throw new ArgumentOutOfRangeException(edgeName, length, "The edge must have a positive, finite length.");
            }

            int segments = SegmentCount(length, spacing);
            var points = new Vec3[segments + 1];
            points[0] = start;
            for (int i = 1; i < segments; i++)
            {
                points[i] = start + delta * ((double)i / segments);
            }

            points[segments] = end;
            return points;
        }

        private static int SegmentCount(double length, double spacing)
        {
            // Decimal, because the double ratio 0.15 / 0.10 is 1.4999999999999998 and rounds down.
            decimal segments;
            try
            {
                decimal decimalSpacing = (decimal)spacing;
                segments = decimalSpacing == 0m
                    ? decimal.MaxValue
                    : Math.Round((decimal)length / decimalSpacing, MidpointRounding.AwayFromZero);
            }
            catch (OverflowException)
            {
                segments = decimal.MaxValue;
            }

            if (segments >= int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(spacing), spacing, "The edge length and spacing give too many segments to count.");
            }

            return Math.Max(1, (int)segments);
        }

        private static Node[] ToSnapDots(Vec3[] positions, int surfaceId, int firstNodeId)
        {
            var dots = new Node[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                dots[i] = new Node
                {
                    Id = firstNodeId + i,
                    Position = positions[i],
                    Fixed = true,
                    SurfaceId = surfaceId,
                    GroundAnchor = false,
                };
            }

            return dots;
        }

        private static double EdgeAngleDegrees(Vec3 firstDelta, Vec3 secondDelta)
        {
            double cosine = Math.Abs(Vec3.Dot(firstDelta.Normalized(), secondDelta.Normalized()));
            return Math.Acos(Math.Min(1.0, cosine)) * DegreesPerRadian;
        }

        private static bool IsFinite(Vec3 v)
        {
            return double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
        }
    }
}
