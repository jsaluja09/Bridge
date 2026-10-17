namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class Budget
        {
            /// <summary>Spec 2.9 and 8: cost of one room meter of line; "not chosen yet". HUMAN_OWNED.</summary>
            public const double CostPerMeter = 1.0;

            /// <summary>Spec 2.9 and 8: cost of one room square meter of fill; "not chosen yet". HUMAN_OWNED.</summary>
            public const double CostPerSquareMeter = 1.0;

            /// <summary>Spec 2.9: default material budget (the spec's "budget 500" is an example challenge). HUMAN_OWNED.</summary>
            public const double MaterialBudget = 500.0;
        }
    }
}
