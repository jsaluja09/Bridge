namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class Budget
        {
            /// <summary>Spec 2.9 and 8: cost of one room meter of wood line. HUMAN_OWNED, set by the human on 2026-10-18.</summary>
            public const double WoodCostPerMeter = 10.0;

            /// <summary>Spec 2.9 and 8: cost of one room meter of steel cable line. HUMAN_OWNED, set by the human on 2026-10-18.</summary>
            public const double CableCostPerMeter = 25.0;

            /// <summary>Spec 2.9 and 8: cost of one room meter of concrete line. HUMAN_OWNED, set by the human on 2026-10-18.</summary>
            public const double ConcreteCostPerMeter = 50.0;

            /// <summary>Spec 2.9: default material budget (the spec's "budget 500" is an example challenge). HUMAN_OWNED.</summary>
            public const double MaterialBudget = 500.0;
        }
    }
}
