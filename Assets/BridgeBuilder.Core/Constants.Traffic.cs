namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Traffic on the bridge: lanes, spacing and jams. Bridge-world meters and seconds. All GUESS.</summary>
        public static class Traffic
        {
            /// <summary>Most cars on the bridge at once (CoupledStep slots). Each car is cheap next to the frame solve.</summary>
            public const int MaxCars = 20;

            /// <summary>One lane, half a real 3.5 m lane to match the half-size car. Two fit if the deck is twice this.</summary>
            public const double LaneWidthMeters = 1.75;

            /// <summary>Bumper-to-bumper gap a following car stops at.</summary>
            public const double MinGapMeters = 1.0;

            /// <summary>Drivers plan to brake at this share of what the tires can give (grip times gravity).</summary>
            public const double BrakeShareOfGrip = 0.5;

            /// <summary>
            /// How long a car takes to respond to a new target speed: SuspensionCar's cruise control closes the
            /// speed error over half a second, so a driver leaves this much extra room.
            /// </summary>
            public const double ResponseSeconds = 0.5;

            /// <summary>In a jam, how long the full bridge must hold.</summary>
            public const double JamHoldSeconds = 5.0;

            /// <summary>A car moving slower than this counts as stopped in the jam.</summary>
            public const double StoppedSpeed = 0.3;

            /// <summary>A traffic test that has not finished by now failed (cars stuck).</summary>
            public const double TimeoutSeconds = 60.0;
        }
    }
}
