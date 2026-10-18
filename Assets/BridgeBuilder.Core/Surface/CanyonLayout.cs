using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// The canyon the bridge crosses (room units), framed by the two ends: Forward runs from the near end to the far
    /// end, Across along the cliff edges. Along is the distance past the near cliff edge (0 there, Span at the far
    /// edge); across is measured from the bridge's centerline. The cliff walls drop straight down from each end's
    /// front edge to a flat floor DepthMeters below the lower end.
    /// </summary>
    public sealed class CanyonLayout
    {
        public Vec3 NearEdge { get; }          // the near end's front center (the first tap)
        public Vec3 Forward { get; }           // horizontal unit, near to far
        public Vec3 Across { get; }            // horizontal unit, along the cliff edges
        public double Span { get; }            // horizontal distance between the cliff edges
        public double NearTop { get; }         // clifftop heights
        public double FarTop { get; }
        public double FloorY { get; }
        public double HalfWidth { get; }

        public CanyonLayout(Pad near, Pad far, double depth = Constants.Canyon.DepthMeters, double halfWidth = Constants.Canyon.HalfWidthMeters)
        {
            NearEdge = near.FrontCenter;
            Forward = near.Forward;
            Across = near.Across;
            Span = Vec3.Dot(far.FrontCenter - near.FrontCenter, Forward);
            NearTop = near.Height;
            FarTop = far.Height;
            FloorY = Math.Min(NearTop, FarTop) - depth;
            HalfWidth = halfWidth;
        }

        /// <summary>
        /// How far to raise both taps so the canyon's base clears the real floor (0 if it already does): every
        /// bridge gets the full canyon depth, however low its ends were tapped.
        /// </summary>
        public static double LiftNeeded(Vec3 tapA, Vec3 tapB, double realFloorY)
        {
            double lowest = Math.Min(tapA.Y, tapB.Y);
            double needed = realFloorY + Constants.Canyon.FloorClearanceMeters + Constants.Canyon.BaseThicknessMeters + Constants.Canyon.DepthMeters;
            return Math.Max(0, needed - lowest);
        }

        /// <summary>A point in canyon coordinates: along past the near edge, across from the centerline, at height y.</summary>
        public Vec3 Point(double along, double across, double y)
        {
            Vec3 p = NearEdge + Forward * along + Across * across;
            return new Vec3(p.X, y, p.Z);
        }

        /// <summary>Along and across of a point (its height ignored).</summary>
        public (double along, double across) Local(Vec3 p)
        {
            Vec3 d = p - NearEdge;
            return (Vec3.Dot(d, Forward), Vec3.Dot(d, Across));
        }

        /// <summary>
        /// The buildable dots on the canyon floor: an even grid between the cliff walls (none on a wall), across the
        /// whole diorama width, river included.
        /// </summary>
        public List<Vec3> FloorDots(double spacing = Constants.Canyon.DotSpacingMeters)
        {
            if (!(spacing > 0))
                throw new ArgumentOutOfRangeException(nameof(spacing));
            int alongSteps = Math.Max(2, (int)Math.Round(Span / spacing));
            int acrossSteps = Math.Max(1, (int)Math.Round(2 * HalfWidth / spacing));
            var dots = new List<Vec3>((alongSteps - 1) * (acrossSteps + 1));
            for (int i = 1; i < alongSteps; i++)
            {
                for (int j = 0; j <= acrossSteps; j++)
                    dots.Add(Point(Span * i / alongSteps, -HalfWidth + 2 * HalfWidth * j / acrossSteps, FloorY));
            }
            return dots;
        }

        /// <summary>The river's band along the bridge, centered in the canyon: (start, end) in along.</summary>
        public (double from, double to) River()
        {
            double half = Span * Constants.Canyon.RiverShare / 2;
            return (Span / 2 - half, Span / 2 + half);
        }
    }
}
