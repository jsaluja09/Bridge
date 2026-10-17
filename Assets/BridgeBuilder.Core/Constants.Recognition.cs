namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Recognizer tolerances, room meters and degrees. Spec 2.1 and 7 defer them, so all GUESS.</summary>
        public static class Recognition
        {
            public const double SnapStartToleranceMeters = 0.02;   // GUESS, spec 2.1 and 7
            public const double LineResidualMeters = 0.01;         // GUESS, spec 2.1 and 7
            public const double PlaneResidualMeters = 0.015;       // GUESS, spec 2.1 and 7
            public const double ArcResidualMeters = 0.015;         // GUESS, spec 2.1 and 7
            public const double CornerAngleDegrees = 25;           // GUESS, spec 2.1 and 7
            public const double ArcSweepMinDegrees = 10;           // GUESS, spec 2.1 and 7 (the spec states no window)
            public const double ArcSweepMaxDegrees = 350;          // GUESS, spec 2.1 and 7 (the spec states no window)
        }
    }
}
