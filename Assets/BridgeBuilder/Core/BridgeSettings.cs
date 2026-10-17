using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Every tuning value in one place. Units are room units (meters, kilograms, newtons).
    /// Public fields so Unity shows and serializes them in the Inspector.
    /// </summary>
    [Serializable]
    public class BridgeSettings
    {
        // Drawing
        public float SnapGrid = 0.05f;
        public float NodeSnapRadius = 0.04f;
        public float MinLineLength = 0.04f;
        public int MaxDrawnMembers = 80;
        public float AnchorSpacing = 0.1f;

        // Geometry
        public float RoadWidth = 0.15f;
        public float MaxRoadSlopeDeg = 20f;

        // Members
        public float LinearDensity = 0.1f;        // kg per meter of member
        public float AxialStiffness = 20000f;     // EA, newtons
        public float TensionLimit = 40f;          // newtons
        public float CompressionLimit = 40f;      // newtons, short-member crushing
        public float BucklingCoefficient = 15f;   // pi^2 EI, N*m^2. Long members buckle at this / L^2
        public float BendingLimit = 1.6f;         // N*m, a road member's capacity for the car's axle loads.
                                                  // 0.5 m road spans hold (orange), 1 m spans break.

        // Simulation
        public float FixedDt = 1f / 60f;
        public int Substeps = 30;
        public int Iterations = 2;
        public float Gravity = 9.81f;
        public float Damping = 0.5f;              // 1/s
        public float SettleDamping = 8f;          // 1/s while gravity ramps in
        public float SettleTime = 1f;             // seconds
        public int MaxBreaksPerStep = 2;
        public float Timeout = 20f;               // seconds after the car starts

        // Car
        public float CarMass = 2f;
        public float CarSpeed = 0.25f;            // m/s
        public float Wheelbase = 0.09f;
        public float CarStartBack = 0.15f;        // starts this far back on the start surface
        public float CarStepUp = 0.02f;           // tallest lip the car can climb onto
        public float CarMaxDrop = 0.06f;          // biggest drop per step before it counts as falling

        public float CompressionCapacity(float length)
        {
            float euler = BucklingCoefficient / (length * length);
            return Math.Min(CompressionLimit, euler);
        }
    }
}
