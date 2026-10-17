namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class Solver
        {
            /// <summary>Spec 2.12 Performance and 2.5. DERIVED. A node cap of about 150 is "roughly 900 equations", and 900 / 150 = 6: three translations and three rotations, all restrained at a fixed node.</summary>
            public const int DofPerNode = 6;
        }
    }
}
