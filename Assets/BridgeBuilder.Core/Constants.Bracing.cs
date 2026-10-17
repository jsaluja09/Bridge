namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class Bracing
        {
            /// <summary>
            /// Spec 2.4 Fills and roads and section 7 ("Fill bracing pattern"). HUMAN_OWNED (HG-bracing). Id of the
            /// hidden-bracing rule: triangulate the fill with its own loop nodes, shortest valid diagonal first.
            /// </summary>
            public const string PatternId = "shortest-diagonal-triangulation-v1";

            /// <summary>
            /// Spec 2.4 Fills and roads and section 7. GUESS. Step, m in room units, that candidate brace lengths are
            /// rounded to before sorting, so equal-length diagonals tie exactly and loop order breaks the tie.
            /// </summary>
            public const double LengthTieToleranceMeters = 1e-6;

            /// <summary>
            /// Spec 2.4 Fills and roads and section 7. GUESS. Distance, m in room units, within which a loop node counts
            /// as lying on a candidate brace, which drops the brace. Loop edges this close also count as touching.
            /// </summary>
            public const double VertexClearanceMeters = 1e-6;
        }
    }
}
