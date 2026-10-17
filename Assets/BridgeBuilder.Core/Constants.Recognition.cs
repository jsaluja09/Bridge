namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Stroke recognizer thresholds. Spec section 7 defers them to tuning in the headset.</summary>
        public static class Recognition
        {
            /// <summary>Spec 2.1 and section 7. GUESS. Farthest the first sample may sit from a snap point, m. A distance equal to it is accepted.</summary>
            public const double SnapStartToleranceMeters = 0.02;

            /// <summary>Spec 2.1 and section 7. GUESS. Largest orthogonal distance from a sample to its fitted line, m.</summary>
            public const double LineResidualMeters = 0.01;

            /// <summary>Spec 2.1 and section 7. GUESS. Largest distance from a sample to the stroke's fitted plane, m.</summary>
            public const double PlaneResidualMeters = 0.015;

            /// <summary>Spec 2.1 and section 7. GUESS. Largest in-plane radial distance from a sample to its fitted circle, m.</summary>
            public const double ArcResidualMeters = 0.015;

            /// <summary>Spec 2.1 and section 7. GUESS. A polyline corner is a direction change larger than this, degrees.</summary>
            public const double CornerAngleDegrees = 25.0;

            /// <summary>Spec 2.1 and section 7. GUESS. Smallest swept angle an arc may have, degrees, inclusive. The spec states no sweep window.</summary>
            public const double ArcSweepMinDegrees = 10.0;

            /// <summary>Spec 2.1 and section 7. GUESS. Largest swept angle an arc may have, degrees, inclusive. The spec states no sweep window.</summary>
            public const double ArcSweepMaxDegrees = 350.0;
        }
    }
}
