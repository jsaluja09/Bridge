namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class Solver
        {
            /// <summary>Spec 2.12: ~150 nodes is "roughly 900 equations"; 900 / 150 = 6. DERIVED.</summary>
            public const int DofPerNode = 6;
        }
    }
}
