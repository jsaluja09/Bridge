namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>The phased test of the one level. All GUESS, to be tuned in playtests.</summary>
        public static class Phases
        {
            /// <summary>Cars per lane in the two-way traffic phase.</summary>
            public const int TrafficCarsPerLane = 3;

            /// <summary>A phase that passed with a member at or above this utilization gets a "close call" tip.</summary>
            public const double CloseCallUtilization = 0.8;

            /// <summary>
            /// Material for the one level, in Constants.Budget price units (shown as dollars); mirroring doubles each
            /// beam. Generous on purpose: room to experiment with every material, concrete included.
            /// </summary>
            public const double LevelBudget = 1000.0;
        }
    }
}
