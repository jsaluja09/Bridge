namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>
        /// The falling-pieces effect (W4-07): broken members and torn deck pieces fall, land, lie on the floor for a
        /// moment, then shrink away. Room units and simulation seconds. Looks only, so all GUESS.
        /// </summary>
        public static class CollapseEffects
        {
            public const double KickSpeed = 0.04;          // GUESS: m/s the two halves of a snapped member fly apart
            public const double SpinRate = 2.5;            // GUESS: rad/s a snapped half tumbles
            public const double FloorGrip = 0.85;          // GUESS: share of sliding speed and spin kept per step on the floor
            public const double SettleSpeed = 0.01;        // GUESS: m/s below which a piece on the floor counts as at rest
            public const double LingerSeconds = 2.0;       // GUESS: how long a fallen piece lies there
            public const double ShrinkSeconds = 0.6;       // GUESS: how long it takes to shrink away
            public const double MaxSeconds = 12.0;         // GUESS: a piece that never settles still goes after this
            public const int MaxFragments = 300;           // GUESS: cap so a big collapse stays cheap on Quest 2
        }
    }
}
