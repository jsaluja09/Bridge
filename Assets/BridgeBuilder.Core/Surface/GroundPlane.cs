using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Snap dots on the virtual ground plane (spec section 2.5). The grid covers the axis-aligned XZ
    /// bounding rectangle of the two marked edges with no padding, starting at its minimum XZ corner.
    /// Every dot is a fixed ground anchor, since all supports use one fixed condition.
    /// </summary>
    public static class GroundPlane
    {
        /// <summary>
        /// Height of the ground plane, room units (Y): <paramref name="heightOverride"/> when present,
        /// as an absolute height that replaces the floor, otherwise <paramref name="floorHeight"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A height is NaN or infinite.</exception>
        public static double Height(double floorHeight, double? heightOverride)
        {
            RequireFinite(floorHeight, nameof(floorHeight));
            if (heightOverride.HasValue)
            {
                RequireFinite(heightOverride.Value, nameof(heightOverride));
                return heightOverride.Value;
            }

            return floorHeight;
        }

        /// <summary>Builds the grid at <see cref="Constants.Ground.GridSpacingMeters"/>.</summary>
        public static Node[] BuildGrid(
            Vec3 edgeAStart,
            Vec3 edgeAEnd,
            Vec3 edgeBStart,
            Vec3 edgeBEnd,
            double floorHeight,
            double? heightOverride)
        {
            return BuildGrid(edgeAStart, edgeAEnd, edgeBStart, edgeBEnd, floorHeight, heightOverride, Constants.Ground.GridSpacingMeters);
        }

        /// <summary>
        /// Builds the ground-plane snap dots under the span between two marked edges, in room units.
        /// Each axis takes the coordinates <c>min + i * spacing</c> that do not pass the footprint's
        /// maximum. Edge heights are ignored; every dot sits at <see cref="Height"/>. Points are ordered
        /// by X, then Z. Ids run from 0 in that order, and <see cref="Node.SurfaceId"/> is null.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The spacing is not positive and finite, a height is NaN or infinite, or the spacing is too
        /// fine for the footprint to fit in one array.
        /// </exception>
        /// <exception cref="ArgumentException">An edge point has an infinite component.</exception>
        public static Node[] BuildGrid(
            Vec3 edgeAStart,
            Vec3 edgeAEnd,
            Vec3 edgeBStart,
            Vec3 edgeBEnd,
            double floorHeight,
            double? heightOverride,
            double spacingMeters)
        {
            if (!(spacingMeters > 0.0) || double.IsInfinity(spacingMeters))
            {
                throw new ArgumentOutOfRangeException(nameof(spacingMeters), spacingMeters, "Grid spacing must be positive and finite.");
            }

            RequireFinite(edgeAStart, nameof(edgeAStart));
            RequireFinite(edgeAEnd, nameof(edgeAEnd));
            RequireFinite(edgeBStart, nameof(edgeBStart));
            RequireFinite(edgeBEnd, nameof(edgeBEnd));
            double height = Height(floorHeight, heightOverride);

            double minX = Math.Min(Math.Min(edgeAStart.X, edgeAEnd.X), Math.Min(edgeBStart.X, edgeBEnd.X));
            double maxX = Math.Max(Math.Max(edgeAStart.X, edgeAEnd.X), Math.Max(edgeBStart.X, edgeBEnd.X));
            double minZ = Math.Min(Math.Min(edgeAStart.Z, edgeAEnd.Z), Math.Min(edgeBStart.Z, edgeBEnd.Z));
            double maxZ = Math.Max(Math.Max(edgeAStart.Z, edgeAEnd.Z), Math.Max(edgeBStart.Z, edgeBEnd.Z));

            long columns = PointCount(minX, maxX, spacingMeters);
            long rows = PointCount(minZ, maxZ, spacingMeters);
            if (columns * rows > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(spacingMeters), spacingMeters, "Grid spacing is too fine for the footprint.");
            }

            var nodes = new Node[columns * rows];
            int index = 0;
            for (int i = 0; i < columns; i++)
            {
                double x = Coordinate(minX, maxX, i, spacingMeters);
                for (int j = 0; j < rows; j++)
                {
                    double z = Coordinate(minZ, maxZ, j, spacingMeters);
                    nodes[index] = new Node
                    {
                        Id = index,
                        Position = new Vec3(x, height, z),
                        Fixed = true,
                        GroundAnchor = true,
                    };
                    index++;
                }
            }

            return nodes;
        }

        /// <summary>
        /// Counts the steps <c>min + i * spacing</c> that do not pass <paramref name="max"/>. The ratio
        /// is floored after conversion to decimal, which keeps 15 significant digits, so a step that
        /// lands on the maximum (such as 7 * 0.1 against 0.7) is not lost to double rounding.
        /// </summary>
        private static long PointCount(double min, double max, double spacing)
        {
            // Clamped so the decimal conversion stays in range; the caller rejects a clamped count.
            double ratio = Math.Min((max - min) / spacing, int.MaxValue);
            return (long)Math.Floor((decimal)ratio) + 1;
        }

        /// <summary>The last step can overshoot the maximum by a rounding error, so it is clamped back onto it.</summary>
        private static double Coordinate(double min, double max, int step, double spacing)
        {
            return Math.Min(min + step * spacing, max);
        }

        private static void RequireFinite(double value, string paramName)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Ground-plane heights must be finite.");
            }
        }

        private static void RequireFinite(Vec3 point, string paramName)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
            {
                throw new ArgumentException("Edge points must be finite.", paramName);
            }
        }
    }
}
