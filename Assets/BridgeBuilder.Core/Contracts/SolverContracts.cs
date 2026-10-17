using System;

namespace BridgeBuilder.Core
{
    // Data contract between the game and the solver (spec section 3). Plain in-memory types:
    // no serialization in the simulation loop. Ids are unique within their own array and need
    // not be contiguous or start at zero. Room units are meters in the player's room; bridge-world
    // units are room units times the scale factor (section 2.6). Y is up everywhere.

    /// <summary>A solver node: a snap point or a support.</summary>
    public class Node
    {
        public int Id { get; set; }

        /// <summary>Position in room units.</summary>
        public Vec3 Position { get; set; }

        /// <summary>True for a support. A fixed node restrains all six degrees of freedom (section 2.5).</summary>
        public bool Fixed { get; set; }

        /// <summary>The marked surface this node is a snap dot of, or null when it is not on a surface edge.</summary>
        public int? SurfaceId { get; set; }

        /// <summary>True for a snap dot on the virtual ground plane (section 2.5).</summary>
        public bool GroundAnchor { get; set; }
    }

    /// <summary>A rigid-jointed frame element between two nodes (section 2.7).</summary>
    public class Member
    {
        public int Id { get; set; }

        /// <summary>Node id at end A. Local x runs from end A to end B.</summary>
        public int NodeA { get; set; }

        /// <summary>Node id at end B.</summary>
        public int NodeB { get; set; }

        /// <summary>Id of an entry in <see cref="SolverInput.Sections"/>.</summary>
        public int SectionId { get; set; }

        /// <summary>Id of the <see cref="Line"/> whose chain contains this member. Hidden braces belong to no line.</summary>
        public int LineId { get; set; }

        /// <summary>True for automatic fill bracing (section 2.4), which the player does not draw.</summary>
        public bool HiddenBrace { get; set; }
    }

    /// <summary>A filled closed planar polygon (section 2.4).</summary>
    public class Fill
    {
        public int Id { get; set; }

        /// <summary>
        /// Node ids around the loop in order. The closing edge from the last id back to the
        /// first is implied, so the first id is not repeated.
        /// </summary>
        public int[] NodeIds { get; set; } = Array.Empty<int>();

        /// <summary>True when the player flagged this fill as road.</summary>
        public bool Road { get; set; }

        /// <summary>
        /// True while the fill is valid. An edit such as deleting a bordering line sets it false.
        /// Defaults to false, so whoever creates a fill sets it.
        /// </summary>
        public bool Valid { get; set; }
    }

    /// <summary>A drawn line: a chain of members created together (section 3).</summary>
    public class Line
    {
        public int Id { get; set; }

        /// <summary>Member ids in chain order from the line's start node.</summary>
        public int[] MemberIds { get; set; } = Array.Empty<int>();
    }

    /// <summary>
    /// Cross-section and material of a member, in bridge-world SI units. Bending about local y
    /// (deflection along local z) uses <see cref="Iy"/> and <see cref="Cy"/>; bending about
    /// local z uses <see cref="Iz"/> and <see cref="Cz"/>.
    /// </summary>
    public class SectionProperties
    {
        public int Id { get; set; }

        /// <summary>Young's modulus, Pa.</summary>
        public double E { get; set; }

        /// <summary>Shear modulus, Pa.</summary>
        public double G { get; set; }

        /// <summary>Area, m^2.</summary>
        public double A { get; set; }

        /// <summary>Second moment of area about local y, m^4.</summary>
        public double Iy { get; set; }

        /// <summary>Second moment of area about local z, m^4.</summary>
        public double Iz { get; set; }

        /// <summary>Torsion constant, m^4.</summary>
        public double J { get; set; }

        /// <summary>Yield strength, Pa.</summary>
        public double YieldStrength { get; set; }

        /// <summary>Density, kg/m^3.</summary>
        public double Density { get; set; }

        /// <summary>Outer-fiber distance for bending about local y, m. Elastic section modulus is Iy / Cy.</summary>
        public double Cy { get; set; }

        /// <summary>Outer-fiber distance for bending about local z, m. Elastic section modulus is Iz / Cz.</summary>
        public double Cz { get; set; }
    }

    /// <summary>
    /// Everything the solver needs for one bridge. Output arrays indexed by node follow the
    /// order of <see cref="Nodes"/>.
    /// </summary>
    public class SolverInput
    {
        public Node[] Nodes { get; set; } = Array.Empty<Node>();

        public Member[] Members { get; set; } = Array.Empty<Member>();

        public Fill[] Fills { get; set; } = Array.Empty<Fill>();

        /// <summary>Section table referenced by <see cref="Member.SectionId"/>.</summary>
        public SectionProperties[] Sections { get; set; } = Array.Empty<SectionProperties>();

        /// <summary>Room-to-bridge multiplier applied by the solver-side adapter (section 2.6). Must be positive.</summary>
        public double ScaleFactor { get; set; }
    }

    /// <summary>One test run: car, start surface, and conditions (sections 2.8 and 2.9).</summary>
    public class RunConfiguration
    {
        /// <summary>The marked surface the car starts from. The other surface is the far surface.</summary>
        public int StartSurfaceId { get; set; }

        public CarParameters Car { get; set; }

        /// <summary>Wind speed, m/s.</summary>
        public double WindSpeed { get; set; }

        /// <summary>Unit vector of the direction the wind blows toward.</summary>
        public Vec3 WindDirection { get; set; }

        /// <summary>Tire-to-deck friction coefficient.</summary>
        public double Grip { get; set; }

        public double MaterialBudget { get; set; }

        /// <summary>
        /// Absolute ground-plane height (room units, Y) that replaces the tracked floor height,
        /// or null to use the floor (section 2.5).
        /// </summary>
        public double? GroundHeightOverride { get; set; }

        /// <summary>Room-to-bridge multiplier (section 2.6). Must be positive.</summary>
        public double ScaleFactor { get; set; }
    }

    /// <summary>Suspension car parameters in bridge-world SI units (section 2.8). Spring and damper values are per wheel.</summary>
    public class CarParameters
    {
        public double MassKg { get; set; }

        public int WheelCount { get; set; }

        /// <summary>Front-to-rear axle distance, m.</summary>
        public double Wheelbase { get; set; }

        /// <summary>Left-to-right wheel distance, m.</summary>
        public double Track { get; set; }

        /// <summary>Spring stiffness, N/m.</summary>
        public double SpringStiffness { get; set; }

        /// <summary>Damper coefficient, N*s/m.</summary>
        public double DamperCoefficient { get; set; }

        /// <summary>Unloaded suspension length, m.</summary>
        public double RestLength { get; set; }
    }

    /// <summary>
    /// Car state in bridge-world units. Per-wheel arrays are indexed by wheel index
    /// (0 to <see cref="CarParameters.WheelCount"/> - 1).
    /// </summary>
    public class CarState
    {
        /// <summary>Car body reference point, m.</summary>
        public Vec3 Position { get; set; }

        /// <summary>Horizontal unit vector of the direction of travel.</summary>
        public Vec3 Heading { get; set; }

        /// <summary>Speed along <see cref="Heading"/>, m/s.</summary>
        public double Speed { get; set; }

        /// <summary>Vertical velocity of the car body, m/s, positive up.</summary>
        public double VerticalVelocity { get; set; }

        /// <summary>Load each wheel puts on the deck. Element i has <c>WheelIndex == i</c>.</summary>
        public WheelLoad[] WheelLoads { get; set; } = Array.Empty<WheelLoad>();

        /// <summary>Spring compression per wheel relative to <see cref="CarParameters.RestLength"/>, m, positive when compressed.</summary>
        public double[] SuspensionCompression { get; set; } = Array.Empty<double>();
    }

    /// <summary>
    /// End actions of a member in its local axes (local x from NodeA to NodeB), in N and N*m:
    /// the forces and moments the joints apply to the member at that end. With no load along the
    /// span, the end-A and end-B shears are equal and opposite.
    /// </summary>
    public class MemberForce
    {
        public int MemberId { get; set; }

        /// <summary>Signed axial force, tension positive.</summary>
        public double Axial { get; set; }

        public double ShearLocalZAtA { get; set; }

        public double MomentLocalYAtA { get; set; }

        public double ShearLocalZAtB { get; set; }

        public double MomentLocalYAtB { get; set; }

        /// <summary>Worst capacity ratio: 0 unloaded, 1 or more failed. The static solver leaves it 0.</summary>
        public double Utilization { get; set; }

        public double ShearLocalYAtA { get; set; }

        public double MomentLocalZAtA { get; set; }

        public double ShearLocalYAtB { get; set; }

        public double MomentLocalZAtB { get; set; }
    }

    /// <summary>An external load on a node, in bridge-world N and N*m.</summary>
    public class NodalLoad
    {
        public int NodeId { get; set; }

        public Vec3 Force { get; set; }

        public Vec3 Moment { get; set; }
    }

    /// <summary>The load one wheel puts on the deck (bridge-world units).</summary>
    public class WheelLoad
    {
        public int WheelIndex { get; set; }

        /// <summary>Load normal to the deck, N, positive pressing into the deck.</summary>
        public double NormalLoad { get; set; }

        /// <summary>Wheel contact point, m.</summary>
        public Vec3 Position { get; set; }
    }

    /// <summary>The deck under one wheel after a bridge step (bridge-world units).</summary>
    public class DeckSample
    {
        public int WheelIndex { get; set; }

        /// <summary>Absolute deck surface height (Y) at the wheel, m.</summary>
        public double Height { get; set; }

        /// <summary>
        /// Deck gradient along the direction of travel (tangent of the incline angle), positive
        /// when the deck descends in the direction of travel, so gravity speeds the car up.
        /// </summary>
        public double Slope { get; set; }

        /// <summary>Point where the deck was sampled, m.</summary>
        public Vec3 Position { get; set; }
    }

    /// <summary>
    /// Result of one dynamic step (section 3). <c>Displacements[i]</c> and <c>Rotations[i]</c>
    /// belong to <c>SolverInput.Nodes[i]</c>.
    /// </summary>
    public class StepOutput
    {
        /// <summary>Node translations in room units.</summary>
        public Vec3[] Displacements { get; set; } = Array.Empty<Vec3>();

        /// <summary>Node rotations about global X, Y, Z, radians.</summary>
        public Vec3[] Rotations { get; set; } = Array.Empty<Vec3>();

        /// <summary>Forces of the members still in the structure after this step, in <c>SolverInput.Members</c> order.</summary>
        public MemberForce[] MemberForces { get; set; } = Array.Empty<MemberForce>();

        /// <summary>Ids of members that failed and were removed during this step.</summary>
        public int[] FailedMemberIds { get; set; } = Array.Empty<int>();

        public CarState CarState { get; set; }
    }

    /// <summary>
    /// Latest state for the render thread (section 2.12). <c>Displacements[i]</c> and
    /// <c>Rotations[i]</c> belong to <c>SolverInput.Nodes[i]</c>; <c>Utilizations[k]</c> belongs
    /// to member <c>MemberIds[k]</c>.
    /// </summary>
    public class SimulationSnapshot
    {
        public int StepId { get; set; }

        /// <summary>Node translations in room units.</summary>
        public Vec3[] Displacements { get; set; } = Array.Empty<Vec3>();

        /// <summary>Node rotations about global X, Y, Z, radians.</summary>
        public Vec3[] Rotations { get; set; } = Array.Empty<Vec3>();

        public int[] MemberIds { get; set; } = Array.Empty<int>();

        public double[] Utilizations { get; set; } = Array.Empty<double>();
    }

    /// <summary>
    /// Result of one static solve. <c>Displacements[i]</c> and <c>Rotations[i]</c> belong to
    /// <c>SolverInput.Nodes[i]</c>, in the length units of the input positions; fixed nodes are zero.
    /// </summary>
    public class StaticSolveResult
    {
        /// <summary>Node translations.</summary>
        public Vec3[] Displacements { get; set; } = Array.Empty<Vec3>();

        /// <summary>Node rotations about global X, Y, Z, radians.</summary>
        public Vec3[] Rotations { get; set; } = Array.Empty<Vec3>();

        /// <summary>One entry per member in <c>SolverInput.Members</c> order, with <see cref="MemberForce.Utilization"/> left 0.</summary>
        public MemberForce[] MemberForces { get; set; } = Array.Empty<MemberForce>();
    }
}
