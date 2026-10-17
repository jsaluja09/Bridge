namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Dead-load preview timing for <see cref="PreviewScheduler"/>.</summary>
        public static class Preview
        {
            /// <summary>Spec 2.10 and section 8 ("~300 ms preview debounce"). GUESS. Seconds the player must pause after an edit before the dead-load preview runs.</summary>
            public const double DebounceSeconds = 0.300;
        }
    }
}
