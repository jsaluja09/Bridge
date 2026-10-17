namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class Bracing
        {
            /// <summary>
            /// Spec 2.4 and 7 defer the bracing pattern; HG-bracing has not been written yet. This is the pattern in
            /// use until a human confirms or replaces it: ear clipping that always cuts the shortest diagonal, so a
            /// deck strip becomes a zigzag of short braces. GUESS (becomes HUMAN_OWNED once HG-bracing signs off).
            /// </summary>
            public const string PatternId = "shortest-diagonal-ear-clip";
        }
    }
}
