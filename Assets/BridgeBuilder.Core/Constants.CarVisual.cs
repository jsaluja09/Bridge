namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>
        /// Proportions of the primitive "Hot Wheels" car (W4-08), as ratios of the car's wheelbase, track and
        /// length so a bigger car gets a bigger body. Looks only; the physics uses Constants.Car. All GUESS.
        /// </summary>
        public static class CarVisual
        {
            public const double WheelRadiusPerWheelbase = 0.15;     // GUESS: oversized toy wheels
            public const double WheelWidthPerTrack = 0.18;          // GUESS
            public const double BodyWidthPerTrack = 1.05;           // GUESS: wheels stick out a little
            public const double BodyHeightPerLength = 0.12;         // GUESS
            public const double BodyClearancePerWheelRadius = 0.55; // GUESS: body bottom above the ground
            public const double HoodRisePerLength = 0.03;           // GUESS: hood and trunk sit on the body
            public const double CabinLengthPerLength = 0.42;        // GUESS
            public const double CabinHeightPerLength = 0.11;        // GUESS
            public const double CabinWidthPerBody = 0.84;           // GUESS
            public const double CabinRearwardPerLength = 0.06;      // GUESS: cabin sits back of center
            public const double LampSizePerLength = 0.05;           // GUESS: headlights and taillights
            public const double BumperDepthPerLength = 0.04;        // GUESS
        }
    }
}
