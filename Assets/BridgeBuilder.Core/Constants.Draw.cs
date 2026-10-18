namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Free drawing (spec 11.1), room meters. All GUESS, to be settled in the headset.</summary>
        public static class Draw
        {
            public const double SnapRadiusMeters = 0.04;     // GUESS: a stroke this close to a dot fuses with it
            public const double MinLineMeters = 0.04;        // GUESS: shorter strokes are ignored
            public const int SmoothingWindow = 3;            // GUESS: samples each side (1 cm apart) in the moving average

            /// <summary>
            /// Spec 11.5: a stroke within this angle of an existing line runs along it. Leaving a shared dot, it
            /// traces over that line (the overlap is dropped); passing a line, it doesn't split it. GUESS.
            /// </summary>
            public const double RetraceAngleDegrees = 10;

            // Spec 11.1: the shape a stroke is drawn as. Bows and fit errors are fractions of the stroke's span (end to end).
            public const double StraightBowFraction = 0.11;  // GUESS: a stroke bowing less than this becomes straight
            public const double ArcFitFraction = 0.08;       // GUESS: a bowed stroke this close to a circle (of its span or bow, if larger) becomes a clean arc
            public const double CornerAngleDegrees = 35;     // GUESS: a turn this sharp between straight runs is a corner (zigzag, triangle, W)
            public const double CornerArmMeters = 0.03;      // GUESS: the straight run measured each side of a corner, beyond the smoothing
            public const double CornerSpreadDegrees = 8;     // GUESS: a corner's turn grows less than this one arm farther out; a smooth bend keeps turning
            public const double SideBowFraction = 0.06;      // GUESS: each side between corners bows less than this of its own length
            public const double BendLobeMinDegrees = 40;     // GUESS: a curve's bends smaller than this are wobble and not counted
            public const int MaxBendReversals = 2;           // GUESS: "about 2"; a curve that changes bend direction more often is a squiggle
            public const double MaxTurnDegrees = 360;        // GUESS: "about one full circle"; a curve that bends more in all is a squiggle

            /// <summary>
            /// Spec 11.2: pads and floor dots add supports on demand; these don't add equations, so the game's
            /// node cap is Human.NodeCap plus this allowance. GUESS.
            /// </summary>
            public const int SupportNodeAllowance = 60;
        }
    }
}
