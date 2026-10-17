namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class Scale
        {
            /// <summary>Spec 2.6 and section 8 ("20x world scale"). GUESS. Room-to-bridge scale factor: a 1 m room gap is a 20 m bridge span.</summary>
            public const double RoomToBridge = 20.0;
        }
    }
}
