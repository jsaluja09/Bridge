namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Material budget defaults (spec 2.9). Section 8 says budget values are not chosen yet.</summary>
        public static class Budget
        {
            /// <summary>Spec 2.9 and section 8. HUMAN_OWNED. Cost of one meter of line, measured in room units. Not chosen yet.</summary>
            public const double CostPerMeter = 1.0;

            /// <summary>Spec 2.9 and section 8. HUMAN_OWNED. Cost of one square meter of fill, measured in room units. Not chosen yet.</summary>
            public const double CostPerSquareMeter = 1.0;

            /// <summary>Spec 2.9 and section 8. HUMAN_OWNED. Default material budget. The spec's "budget 500" is an example challenge. Not chosen yet.</summary>
            public const double MaterialBudget = 500.0;
        }
    }
}
