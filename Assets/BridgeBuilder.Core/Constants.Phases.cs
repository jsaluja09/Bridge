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
            /// Material for the one level, in Constants.Budget price units; mirroring doubles each beam. Fits a wood
            /// truss with cable diagonals and its deck, but not the same truss in concrete.
            /// </summary>
            public const double LevelBudget = 120.0;
        }
    }
}
