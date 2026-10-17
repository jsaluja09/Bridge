using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// The solver-side adapter between room units and bridge-world units (spec 2.6). Only coordinates and
    /// displacements scale; section properties, forces and car parameters pass through unchanged.
    /// </summary>
    public static class RoomBridgeAdapter
    {
        static double Check(double scale) => scale > 0 ? scale : throw new ArgumentOutOfRangeException(nameof(scale));

        public static Vec3 ToBridge(Vec3 room, double scale) => room * Check(scale);
        public static Vec3 ToBridge(Vec3 room) => ToBridge(room, Constants.Scale.RoomToBridge);

        public static Vec3 DisplacementToRoom(Vec3 bridge, double scale) => bridge / Check(scale);
        public static Vec3 DisplacementToRoom(Vec3 bridge) => DisplacementToRoom(bridge, Constants.Scale.RoomToBridge);

        public static double LengthToBridge(double room, double scale) => room * Check(scale);

        public static double SectionArea(double area) => area;
        public static double Force(double newtons) => newtons;
    }
}
