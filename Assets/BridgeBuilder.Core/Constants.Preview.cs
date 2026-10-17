namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class Preview
        {
            /// <summary>Spec 2.10 and 8: "~300 ms" before the dead-load preview runs, seconds. GUESS.</summary>
            public const double DebounceSeconds = 0.300;
        }
    }
}
