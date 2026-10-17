namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class Road
        {
            /// <summary>Spec 2.4 and 8: "target maximum slope about 20 degrees, tunable". GUESS.</summary>
            public const double MaxSlopeDegrees = 20.0;

            /// <summary>Spec 11.3: closest two flat lines may be for an automatic deck, room meters. GUESS.</summary>
            public const double MinDeckSpacingMeters = 0.08;

            /// <summary>Spec 11.3: farthest two flat lines may be for an automatic deck, room meters. GUESS.</summary>
            public const double MaxDeckSpacingMeters = 0.30;

            /// <summary>Spec 11.3: how far from parallel two deck lines may be, degrees. GUESS.</summary>
            public const double MaxDeckAngleDegrees = 20.0;
        }
    }
}
