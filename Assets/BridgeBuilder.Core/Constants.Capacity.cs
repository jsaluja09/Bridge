namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class Capacity
        {
            /// <summary>
            /// Fixed-fixed, no sidesway, matching fixture 4; spec never states K for a rigid frame that can sway;
            /// do not change. HUMAN_OWNED.
            /// </summary>
            public const double EffectiveLengthFactor = 0.5;
        }
    }
}
