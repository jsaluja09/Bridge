namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class FailureFeedback
        {
            /// <summary>
            /// W4-06: a failed member is called "too long" only when its slenderness (unbraced length over radius of
            /// gyration) is above this. About 7.6 m of bridge (38 cm in the room) for the standard section. GUESS.
            /// </summary>
            public const double SlendernessMessageThreshold = 120;
        }
    }
}
