namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Auto-mirror across the road's centerline, room meters.</summary>
        public static class Mirror
        {
            /// <summary>
            /// A piece whose mirror copy lands within this of itself (drawn on the centerline, or already symmetric
            /// like a cross-beam) is not copied. Matches the draw snap radius, since a copy that close would snap
            /// onto the original anyway. GUESS.
            /// </summary>
            public const double SelfMirrorToleranceMeters = Draw.SnapRadiusMeters;
        }
    }
}
