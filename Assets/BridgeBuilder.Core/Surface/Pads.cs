using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// One end of the bridge (spec 11.2): a flat rectangle whose front edge is centered on the player's tap
    /// and faces the other tap. Its whole top is a fixed support; the car starts on one pad.
    /// </summary>
    public sealed class Pad
    {
        public int Id { get; }
        public Vec3 FrontCenter { get; }     // the tap, room units
        public Vec3 Forward { get; }         // horizontal, toward the other pad
        public Vec3 Across { get; }          // horizontal, along the front edge
        public double Width { get; }
        public double Depth { get; }
        public double Height => FrontCenter.Y;

        public Pad(int id, Vec3 tap, Vec3 otherTap, double width, double depth)
        {
            Id = id;
            FrontCenter = tap;
            var toward = new Vec3(otherTap.X - tap.X, 0, otherTap.Z - tap.Z);
            Forward = toward.Length > 1e-9 ? toward.Normalized() : Vec3.UnitX;
            Across = Vec3.Cross(Vec3.UnitY, Forward);
            Width = width;
            Depth = depth;
        }

        public static (Pad start, Pad far) FromTaps(Vec3 first, Vec3 second) => (
            new Pad(0, first, second, Constants.Surface.PadWidthMeters, Constants.Surface.PadDepthMeters),
            new Pad(1, second, first, Constants.Surface.PadWidthMeters, Constants.Surface.PadDepthMeters));

        /// <summary>The four top corners: front-left, front-right, back-right, back-left.</summary>
        public Vec3[] Corners()
        {
            Vec3 h = Across * (Width / 2), back = -Forward * Depth;
            return new[] { FrontCenter - h, FrontCenter + h, FrontCenter + h + back, FrontCenter - h + back };
        }

        /// <summary>Position in pad coordinates: along = across the front edge (0 at center), back = distance behind it.</summary>
        public void Local(Vec3 p, out double along, out double back, out double above)
        {
            Vec3 d = p - FrontCenter;
            along = Vec3.Dot(d, Across);
            back = -Vec3.Dot(d, Forward);
            above = d.Y;
        }

        /// <summary>True if p is over the pad's top (horizontally), with a small margin.</summary>
        public bool Over(Vec3 p, double margin = 0)
        {
            Local(p, out double along, out double back, out _);
            return Math.Abs(along) <= Width / 2 + margin && back >= -margin && back <= Depth + margin;
        }

        /// <summary>
        /// If p is touching the pad's top (within radius above or below and over it), the support point on the top.
        /// </summary>
        public bool TrySupportPoint(Vec3 p, double radius, out Vec3 support)
        {
            Local(p, out double along, out double back, out double above);
            support = default;
            if (Math.Abs(above) > radius || !Over(p, radius))
                return false;
            along = Math.Clamp(along, -Width / 2, Width / 2);
            back = Math.Clamp(back, 0, Depth);
            support = FrontCenter + Across * along - Forward * back;
            return true;
        }
    }
}
