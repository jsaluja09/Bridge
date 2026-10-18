namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class Bracing
        {
            /// <summary>
            /// Spec 2.4 and 7, HG-bracing (from bridge-core): triangulate each fill with its own loop nodes,
            /// shortest valid diagonal first.
            /// </summary>
            public const string PatternId = "shortest-diagonal-triangulation-v1";

            /// <summary>Diagonals whose lengths differ by less than this count as tied, m. Ties go to the lower loop position.</summary>
            public const double LengthTieToleranceMeters = 1e-6;

            /// <summary>A diagonal within this distance of another loop node or segment counts as touching it, m.</summary>
            public const double VertexClearanceMeters = 1e-6;
        }
    }
}
