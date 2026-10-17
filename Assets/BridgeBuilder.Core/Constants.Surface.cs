namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class Surface
        {
            /// <summary>Spec 2.2 and 7: edges more than this many degrees apart count as non-parallel. GUESS.</summary>
            public const double NonParallelAngleDegrees = 5.0;

            /// <summary>Spec 11.2: pad size across the gap, room meters. GUESS.</summary>
            public const double PadWidthMeters = 0.3;

            /// <summary>Spec 11.2: pad depth away from the gap, room meters. GUESS.</summary>
            public const double PadDepthMeters = 0.3;
        }
    }
}
