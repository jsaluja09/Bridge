namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Member capacity checks (spec 2.7).</summary>
        public static class Capacity
        {
            /// <summary>Spec 2.7 and section 5, fixture 4. HUMAN_OWNED. Euler effective-length factor K: fixed-fixed, no sidesway, matching fixture 4; spec never states K for a rigid frame that can sway; do not change.</summary>
            public const double EffectiveLengthFactor = 0.5;
        }
    }
}
