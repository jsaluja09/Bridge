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

            /// <summary>Material for the one level, room meters of line at the default cost (mirroring doubles each beam).</summary>
            public const double LevelBudget = 12.0;
        }
    }
}
