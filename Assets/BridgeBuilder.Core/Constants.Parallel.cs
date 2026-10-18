namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Parallel rail assist (W4-02). Room meters and degrees.</summary>
        public static class Parallel
        {
            /// <summary>A new line within this angle of an existing one snaps parallel to it. GUESS.</summary>
            public const double ParallelAngleTolerance = 12.0;
            /// <summary>Only lines whose nearest point is this close to the new shape's middle are partners. GUESS.</summary>
            public const double ParallelSearchRadius = 0.4;
            /// <summary>
            /// Arcs: the new arc's center must be this close to the partner's center axis, and a radius this close to
            /// the partner's snaps to the same radius (side-by-side arches). GUESS.
            /// </summary>
            public const double ParallelArcRadiusTolerance = 0.03;
        }
    }
}
