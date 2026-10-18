using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    // The data contract between the game and the solver (spec section 3). Plain data, double scalars,
    // int ids, no JSON attributes; passed in memory. Fields marked "extension" go beyond the W0-01 list
    // because a later issue or the game needs them; nothing listed in W0-01 was removed.

    public sealed class Node
    {
        public int Id { get; set; }
        public Vec3 Position { get; set; }           // room units
        public bool Fixed { get; set; }
        public int? SurfaceId { get; set; }
        public bool GroundAnchor { get; set; }
    }

    public sealed class Member
    {
        public int Id { get; set; }
        public int NodeA { get; set; }
        public int NodeB { get; set; }
        public int SectionId { get; set; }
        public int LineId { get; set; } = -1;
        public bool HiddenBrace { get; set; }
    }

    public sealed class Fill
    {
        public int Id { get; set; }
        public List<int> Loop { get; set; } = new List<int>();
        public bool Road { get; set; }
        public bool Valid { get; set; } = true;
        public int SectionId { get; set; }             // extension: the material the fill is priced and braced as
    }

    /// <summary>"A line is just a chain of members created together" (spec 3).</summary>
    public sealed class Line
    {
        public int Id { get; set; }
        public List<int> Members { get; set; } = new List<int>();
    }

    public sealed class CarParameters
    {
        public double MassKg { get; set; }
        public int WheelCount { get; set; } = 4;
        public double Wheelbase { get; set; }
        public double Track { get; set; }
        public double SpringStiffness { get; set; }
        public double DamperCoefficient { get; set; }
        public double RestLength { get; set; }
    }

    public sealed class RunConfiguration
    {
        public int StartSurfaceId { get; set; }
        public CarParameters Car { get; set; } = new CarParameters();
        public double WindSpeed { get; set; }
        public Vec3 WindDirection { get; set; }
        public double Grip { get; set; }
        public double MaterialBudget { get; set; }
        public double? GroundHeightOverride { get; set; }  // absolute height
        public double ScaleFactor { get; set; }
        public TrafficSpec Traffic { get; set; }              // extension: null = the single test car
        public double SnowLoadPa { get; set; }                 // extension: snow on the road, pascals (0 = none)
    }

    public sealed class CarState
    {
        public Vec3 Position { get; set; }            // bridge world, body reference point at axle height
        public double[] WheelLoads { get; set; } = new double[4];
        public double[] Compression { get; set; } = new double[4];
        public double Speed { get; set; }              // along travel direction
        public Vec3 Heading { get; set; } = Vec3.UnitX;   // extension: horizontal travel direction
        public double LateralSpeed { get; set; }       // extension: sideways slide under crosswind
        public double VerticalSpeed { get; set; }      // extension: body heave rate
        public double Pitch { get; set; }              // extension: nose up, radians
        public double Roll { get; set; }               // extension: left side up, radians
        public bool Falling { get; set; }              // extension: no wheel on any deck or surface
        public double? TargetSpeed { get; set; }       // extension: this car's cruise speed (traffic); null = the model's default

        public CarState Clone() => new CarState
        {
            Position = Position,
            WheelLoads = (double[])WheelLoads.Clone(),
            Compression = (double[])Compression.Clone(),
            Speed = Speed,
            Heading = Heading,
            LateralSpeed = LateralSpeed,
            VerticalSpeed = VerticalSpeed,
            Pitch = Pitch,
            Roll = Roll,
            Falling = Falling,
            TargetSpeed = TargetSpeed,
        };
    }

    public sealed class SectionProperties
    {
        public int Id { get; set; }
        public double E { get; set; }
        public double G { get; set; }
        public double A { get; set; }
        public double Iy { get; set; }
        public double Iz { get; set; }
        public double J { get; set; }
        public double YieldStrength { get; set; }      // bending, and axial where the two below are 0
        public double TensionStrength { get; set; }    // extension: axial stress at failure in tension; 0 = YieldStrength
        public double CompressionStrength { get; set; } // extension: axial stress at crushing; 0 = YieldStrength
        public double Density { get; set; }
        public double OuterFiberY { get; set; }        // extension: c for bending about local y
        public double OuterFiberZ { get; set; }        // extension: c for bending about local z
    }

    public sealed class SolverInput
    {
        public List<Node> Nodes { get; set; } = new List<Node>();
        public List<Member> Members { get; set; } = new List<Member>();
        public List<Fill> Fills { get; set; } = new List<Fill>();
        public List<SectionProperties> Sections { get; set; } = new List<SectionProperties>();
        public double ScaleFactor { get; set; } = 1.0;
    }

    public readonly struct NodalLoad
    {
        public readonly int NodeId;
        public readonly Vec3 Force;
        public readonly Vec3 Moment;

        public NodalLoad(int nodeId, Vec3 force, Vec3 moment = default)
        {
            NodeId = nodeId;
            Force = force;
            Moment = moment;
        }
    }

    public readonly struct WheelLoad
    {
        public readonly int WheelIndex;
        public readonly double NormalLoad;

        public WheelLoad(int wheelIndex, double normalLoad)
        {
            WheelIndex = wheelIndex;
            NormalLoad = normalLoad;
        }
    }

    public struct DeckSample
    {
        public int WheelIndex;
        public double Height;
        public double Slope;            // drop per unit of travel along the heading: positive = downhill
        public Vec3 Position;           // where the wheel touches, bridge world
        public bool HasDeck;            // extension: false = nothing under this wheel
    }

    public sealed class MemberForce
    {
        public int MemberId { get; set; }
        public double Axial { get; set; }              // tension positive
        public double ShearLocalZAtA { get; set; }
        public double MomentLocalYAtA { get; set; }
        public double ShearLocalZAtB { get; set; }
        public double MomentLocalYAtB { get; set; }
        public double ShearLocalYAtA { get; set; }     // extension: the other bending plane
        public double MomentLocalZAtA { get; set; }    // extension
        public double ShearLocalYAtB { get; set; }     // extension
        public double MomentLocalZAtB { get; set; }    // extension
        public double Utilization { get; set; }
    }

    public sealed class StepOutput
    {
        public Vec3[] Displacements { get; set; } = Array.Empty<Vec3>();    // room units, same order as SolverInput.Nodes
        public MemberForce[] MemberForces { get; set; } = Array.Empty<MemberForce>();
        public List<int> FailedMemberIds { get; set; } = new List<int>();
        public CarState Car { get; set; }
    }

    public sealed class SimulationSnapshot
    {
        public long StepId { get; set; }
        public Vec3[] Displacements { get; set; } = Array.Empty<Vec3>();    // room units
        public double[] Utilizations { get; set; } = Array.Empty<double>();
    }

    public sealed class StaticSolveResult
    {
        public Vec3[] Displacements { get; set; } = Array.Empty<Vec3>();    // room units, same order as SolverInput.Nodes
        public MemberForce[] MemberForces { get; set; } = Array.Empty<MemberForce>();
    }
}
