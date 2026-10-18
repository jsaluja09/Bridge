namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>The optional online coach (spec 11.7). The game works the same without it, so all GUESS.</summary>
        public static class Coach
        {
            /// <summary>Gemini model id. GUESS.</summary>
            public const string Model = "gemini-3.8-flash";

            /// <summary>Gemini thinking level: "low" for a faster reply. GUESS.</summary>
            public const string ThinkingLevel = "low";

            /// <summary>"About 40" words: the longest reply shown under the result. GUESS.</summary>
            public const int MaxReplyWords = 40;

            /// <summary>"About 15 s" before a request gives up and the coach shows as offline. GUESS.</summary>
            public const int RequestTimeoutSeconds = 15;
        }
    }
}
