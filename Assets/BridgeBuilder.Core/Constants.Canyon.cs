namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>The canyon diorama the bridge crosses, room meters. All GUESS, to be tuned in the headset.</summary>
        public static class Canyon
        {
            /// <summary>Canyon floor below the lower end of the bridge: deep enough to need piers, within reach.</summary>
            public const double DepthMeters = 0.30;

            /// <summary>How far the diorama reaches either side of the bridge's centerline.</summary>
            public const double HalfWidthMeters = 0.60;

            /// <summary>How far the clifftops reach back behind each end.</summary>
            public const double ClifftopDepthMeters = 0.35;

            /// <summary>Thickness of the base slab under the canyon floor (so it reads as a model in the room).</summary>
            public const double BaseThicknessMeters = 0.04;

            /// <summary>Gap kept between the base slab and the real floor when the ends must be lifted.</summary>
            public const double FloorClearanceMeters = 0.02;

            /// <summary>Spacing of the buildable dots on the canyon floor.</summary>
            public const double DotSpacingMeters = Ground.GridSpacingMeters;

            /// <summary>River width as a share of the canyon floor's length along the bridge.</summary>
            public const double RiverShare = 0.25;
        }
    }
}
