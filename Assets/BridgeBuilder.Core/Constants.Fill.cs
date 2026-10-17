namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class Fill
        {
            /// <summary>
            /// Spec 2.4 Fills and roads. GUESS. Largest distance, m in room units, from a fill vertex to the
            /// fill plane that still counts as planar. A vertex exactly at this distance passes. The spec
            /// states no tolerance.
            /// </summary>
            public const double PlanarityToleranceMeters = 0.01;
        }
    }
}
