using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Every tuning value in one place. Drawing values are in room units (meters in the real room);
    /// structural values are in bridge-world SI units (room units times Scale).
    /// Public fields so Unity shows and serializes them in the Inspector.
    /// </summary>
    [Serializable]
    public class BridgeSettings
    {
        // Drawing (room units)
        public float SnapSpacing = 0.1f;          // target element length along a line (spec 2.3)
        public float NodeSnapRadius = 0.04f;
        public float MinLineLength = 0.05f;
        public float LineTolerance = 0.015f;      // max deviation for a straight line, absolute...
        public float LineToleranceRelative = 0.06f; // ...or relative to its length, whichever is larger
        public float ArcTolerance = 0.02f;
        public float PlaneTolerance = 0.02f;      // arcs and fills must be this flat
        public float PolylineTolerance = 0.025f;
        public int MaxPolylineCorners = 6;
        public float MinArcAngleDeg = 25f;
        public float GroundGridSpacing = 0.15f;
        public float SurfaceDepth = 1f;           // how far back from a marked edge the surface extends
        public float MaxRoadSlopeDeg = 20f;

        // Budget (room units)
        public int NodeCap = 150;                 // hidden; enforced as "over budget"
        public float CostPerMeter = 50f;
        public float CostPerSquareMeter = 400f;

        // World
        public float Scale = 20f;                 // 1 m in the room = 20 m of bridge
        public float Gravity = 9.81f;
        public bool PinnedSupports;               // false = fixed (spec 2.5)

        // Material and section (bridge world, SI): aluminum tube, calibrated by the fixtures
        public float YoungsModulus = 70e9f;
        public float ShearModulus = 26e9f;
        public float YieldStrength = 240e6f;
        public float Density = 2700f;
        public float TubeDiameter = 0.16f;
        public float TubeWall = 0.006f;
        public float DeckMassPerArea = 15f;       // kg/m^2 of fill
        public float MemberDragCoefficient = 1.2f;
        public float FillDragCoefficient = 1.2f;
        public float AirDensity = 1.225f;

        // Dynamics
        public float FixedDt = 1f / 60f;
        public float RayleighAlpha = 0.2f;        // mass-proportional damping
        public float RayleighBeta = 0.002f;       // stiffness-proportional damping
        public int MaxFailuresPerStep = 3;
        public float Timeout = 30f;

        // Debris
        public int DebrisSubsteps = 10;
        public float DebrisFriction = 0.6f;

        // Car (bridge world)
        public float CarSpeed = 8f;
        public float CarLength = 4.5f;
        public float Wheelbase = 2.7f;
        public float Track = 1.6f;
        public float RideHeight = 0.5f;
        public float SpringSag = 0.12f;
        public float SuspensionDamping = 0.5f;    // fraction of critical
        public float CarSideArea = 6f;
        public float CarFrontArea = 2.5f;
        public float CarDragCoefficient = 1f;
        public int CarSubsteps = 8;

        // Feedback
        public float PulseThreshold = 0.9f;
        public float PreviewDelay = 0.3f;

        public float WorldToRoom => 1f / Scale;
    }

    /// <summary>A design brief shown before building (spec 2.9). One small config object per challenge.</summary>
    [Serializable]
    public class Challenge
    {
        public string Name = "First crossing";
        public string Description = "Get a car across. Triangles help.";
        public float CarMass = 1500f;             // kg
        public float WindSpeed;                   // m/s
        public float WindDirectionDeg = 90f;      // relative to the gap direction; 90 = crosswind
        public float Grip = 0.9f;                 // tire friction coefficient
        public float Budget = 600f;

        public static Challenge[] Defaults() => new[]
        {
            new Challenge(),
            new Challenge { Name = "Heavy truck", Description = "A 12 tonne truck. Spread the load.", CarMass = 12000f, Budget = 900f },
            new Challenge { Name = "Crosswind", Description = "15 m/s wind from the side.", WindSpeed = 15f, Budget = 700f },
            new Challenge { Name = "Icy and windy", Description = "Low grip and a 10 m/s crosswind.", WindSpeed = 10f, Grip = 0.3f, Budget = 700f },
        };
    }
}
