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
            /// Spec 11.2: pads and floor dots add supports on demand; these don't add equations, so the game's
            /// node cap is Human.NodeCap plus this allowance. GUESS.
            /// </summary>
            public const int SupportNodeAllowance = 60;
        }
    }
}
