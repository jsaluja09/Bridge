using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class DynamicSelfWeightTests
    {
        private const double Gravity = Constants.Loads.Gravity;
        private const double LowGravity = 3.72076;
        private const double Scale = 20.0;

        // Relative tolerances from the issue.
        private const double MatchTolerance = 1e-9;
        private const double TotalTolerance = 1e-9;
        private const double StillTolerance = 1e-9;
        private const double StaticTolerance = 1e-6;
        private const double FallTolerance = 0.01;
        private const double RemovalTolerance = 1e-12;
        private const int StillSteps = 100;

        // Hanging-piece frame: a fixed-fixed beam, a weak hanger at midspan, and a triangle hung from it.
        private const int BeamMidspanNodeId = 3;
        private const int PieceTopNodeId = 4;
        private const int PieceLeftNodeId = 5;
        private const int PieceRightNodeId = 6;
        private const int HangerId = 20;
        private const double HangerYieldStrength = 4.0e6;
        private const double TriggerLoad = 4.0e4;
        private const int StandingSteps = 3;
        private const int MaxTriggerSteps = 30;
        private const int StepsAfterFailure = 30;

        private static readonly int[] NoneRemoved = Array.Empty<int>();

        [Fact]
        public void SelfWeight_MatchesDeadLoadPreview()
        {
            SolverInput room = MixedFrame(Scale);
            room.Nodes = room.Nodes.Append(new Node { Id = 99, Position = new Vec3(2, 2, 2) }).ToArray();
            Assert.Equal(Scale, room.ScaleFactor);
            Assert.True(room.Sections.Length >= 2);
            Assert.Equal(room.Sections.Length, room.Sections.Select(s => s.A).Distinct().Count());
            Assert.Equal(room.Sections.Length, room.Sections.Select(s => s.Density).Distinct().Count());
            Vec3[] roomPositions = room.Nodes.Select(n => n.Position).ToArray();
            SolverInput bridge = RoomBridgeAdapter.ToBridge(room);
            Vec3[] bridgePositions = bridge.Nodes.Select(n => n.Position).ToArray();

            NodalLoad[] expected = DeadLoadPreview.SelfWeight(room, Gravity);
            NodalLoad[] actual = DynamicSelfWeight.Loads(bridge, NoneRemoved, Gravity);

            Assert.Equal(room.Nodes.Select(n => n.Id), expected.Select(l => l.NodeId));
            Assert.Equal(expected.Select(l => l.NodeId), actual.Select(l => l.NodeId));
            for (int i = 0; i < expected.Length; i++)
            {
                AssertFinite(actual[i]);
                AssertRelative(expected[i].Force, actual[i].Force, MatchTolerance, "Force on node " + actual[i].NodeId);
                AssertRelative(expected[i].Moment, actual[i].Moment, MatchTolerance, "Moment on node " + actual[i].NodeId);
            }

            Assert.Equal(Vec3.Zero, actual[IndexOf(room, 99)].Force);
            Assert.All(actual.Where(l => l.NodeId != 99), l => Assert.True(l.Force.Y < 0.0, "Node " + l.NodeId + " carries no weight."));
            Assert.Equal(Scale, room.ScaleFactor);
            Assert.Equal(roomPositions, room.Nodes.Select(n => n.Position));
            Assert.Equal(bridgePositions, bridge.Nodes.Select(n => n.Position));
        }

        [Fact]
        public void SelfWeight_TotalIsMassTimesGravity()
        {
            SolverInput bridge = RoomBridgeAdapter.ToBridge(MixedFrame(Scale));
            foreach (int[] removed in new[] { NoneRemoved, new[] { 3 }, new[] { 2, 7, 8 } })
            {
                foreach (double gravity in new[] { Gravity, LowGravity })
                {
                    NodalLoad[] loads = DynamicSelfWeight.Loads(bridge, removed, gravity);
                    double mass = bridge.Members.Where(m => !removed.Contains(m.Id)).Sum(m => MemberMass(bridge, m));

                    Assert.Equal(bridge.Nodes.Select(n => n.Id), loads.Select(l => l.NodeId));
                    AssertRelative(-gravity * mass, loads.Sum(l => l.Force.Y), TotalTolerance, "Total weight");
                    foreach (NodalLoad load in loads)
                    {
                        AssertFinite(load);
                        Assert.Equal(0.0, load.Force.X);
                        Assert.Equal(0.0, load.Force.Z);
                        Assert.Equal(Vec3.Zero, load.Moment);
                    }
                }
            }

            NodalLoad[] defaults = DynamicSelfWeight.Loads(bridge, NoneRemoved);
            NodalLoad[] standard = DynamicSelfWeight.Loads(bridge, NoneRemoved, Constants.Loads.Gravity);
            Assert.Equal(standard.Select(l => (l.NodeId, l.Force, l.Moment)), defaults.Select(l => (l.NodeId, l.Force, l.Moment)));
        }

        [Fact]
        public void SelfWeight_RemovedMembersCarryNoWeight()
        {
            SolverInput bridge = RoomBridgeAdapter.ToBridge(MixedFrame(Scale));
            NodalLoad[] full = DynamicSelfWeight.Loads(bridge, NoneRemoved, Gravity);

            foreach (Member member in bridge.Members)
            {
                NodalLoad[] without = DynamicSelfWeight.Loads(bridge, new[] { member.Id }, Gravity);
                double half = 0.5 * MemberMass(bridge, member) * Gravity;

                Assert.Equal(full.Select(l => l.NodeId), without.Select(l => l.NodeId));
                for (int i = 0; i < full.Length; i++)
                {
                    AssertFinite(without[i]);
                    Assert.Equal(Vec3.Zero, without[i].Moment);
                    int nodeId = full[i].NodeId;
                    if (nodeId == member.NodeA || nodeId == member.NodeB)
                    {
                        double lowered = Math.Abs(full[i].Force.Y) - Math.Abs(without[i].Force.Y);
                        AssertWithin(half, lowered, RemovalTolerance * Math.Abs(full[i].Force.Y), "Load removed from node " + nodeId + " with member " + member.Id);
                        Assert.Equal(0.0, without[i].Force.X);
                        Assert.Equal(0.0, without[i].Force.Z);
                    }
                    else
                    {
                        Assert.Equal(full[i].Force, without[i].Force);
                    }
                }
            }

            // Node 6 hangs from member 8 alone, and members 2, 3, 7, and 8 are every member at node 8.
            NodalLoad[] tipCut = DynamicSelfWeight.Loads(bridge, new List<int> { 8, 8 }, Gravity);
            Assert.Equal(Vec3.Zero, tipCut[IndexOf(bridge, 6)].Force);
            Assert.Equal(DynamicSelfWeight.Loads(bridge, new[] { 8 }, Gravity).Select(l => l.Force), tipCut.Select(l => l.Force));

            NodalLoad[] jointCut = DynamicSelfWeight.Loads(bridge, new HashSet<int> { 2, 3, 7, 8 }, Gravity);
            Assert.Equal(Vec3.Zero, jointCut[IndexOf(bridge, 8)].Force);
            Assert.Equal(Vec3.Zero, jointCut[IndexOf(bridge, 6)].Force);
            Assert.All(jointCut.Where(l => l.NodeId != 8 && l.NodeId != 6), l => Assert.True(l.Force.Y < 0.0, "Node " + l.NodeId + " lost its weight."));

            NodalLoad[] allCut = DynamicSelfWeight.Loads(bridge, bridge.Members.Select(m => m.Id).ToArray(), Gravity);
            Assert.All(allCut, l => Assert.True(l.Force == Vec3.Zero && l.Moment == Vec3.Zero, "Node " + l.NodeId + " is still loaded."));
        }

        [Fact]
        public void Integrator_ResetUnderSelfWeight_StaysStill()
        {
            SolverInput bridge = RoomBridgeAdapter.ToBridge(MixedFrame(Scale));
            NodalLoad[] selfWeight = DynamicSelfWeight.Loads(bridge, NoneRemoved);
            StaticSolveResult expected = new StaticSolver().Solve(bridge, selfWeight);
            double largest = expected.Displacements.Max(d => d.Length);
            Assert.True(largest > 1e-4, "The self-weight must move the frame measurably.");

            // Relative to the largest static displacement, since some components are near zero.
            double stillTolerance = StillTolerance * largest;
            double staticTolerance = StaticTolerance * largest;

            var integrator = new NewmarkIntegrator();
            integrator.Reset(bridge, selfWeight);
            Vec3[] first = null;
            for (int step = 1; step <= StillSteps; step++)
            {
                StepOutput output = integrator.Step(selfWeight, NewmarkIntegrator.DefaultTimestep);

                AssertFinite(output, bridge.Nodes.Length);
                Assert.Empty(output.FailedMemberIds);
                Assert.Equal(bridge.Members.Select(m => m.Id), output.MemberForces.Select(f => f.MemberId));
                first ??= output.Displacements;
                for (int i = 0; i < bridge.Nodes.Length; i++)
                {
                    string name = "Node " + bridge.Nodes[i].Id + " on step " + step;
                    AssertWithin(first[i], output.Displacements[i], stillTolerance, name + " moved from step 1");
                    AssertWithin(expected.Displacements[i], output.Displacements[i], staticTolerance, name + " left the static solution");
                }
            }
        }

        [Fact]
        public void Collapse_DetachedPieceFallsAtGravity()
        {
            SolverInput bridge = RoomBridgeAdapter.ToBridge(HangingPieceFrame(Scale));
            double dt = NewmarkIntegrator.DefaultTimestep;
            var failed = new List<int>();
            NodalLoad[] selfWeight = DynamicSelfWeight.Loads(bridge, failed);
            var trigger = new NodalLoad { NodeId = PieceTopNodeId, Force = new Vec3(0.0, -TriggerLoad, 0.0) };
            var integrator = new NewmarkIntegrator();
            integrator.Reset(bridge, selfWeight);

            // StepOutput displacements are in the input's units, bridge-world meters here. Entry k is after step k + 1.
            var displacements = new List<Vec3[]>();
            int failureStep = 0;
            int step = 0;
            while (failureStep == 0 || step < failureStep + StepsAfterFailure)
            {
                step++;
                Assert.True(failureStep > 0 || step <= StandingSteps + MaxTriggerSteps, "Member " + HangerId + " never failed.");
                NodalLoad[] loads = failureStep == 0 && step > StandingSteps ? selfWeight.Append(trigger).ToArray() : selfWeight;
                StepOutput output = integrator.Step(loads, dt);

                AssertFinite(output, bridge.Nodes.Length);
                displacements.Add(output.Displacements);
                if (output.FailedMemberIds.Length > 0)
                {
                    Assert.True(failureStep == 0, "A second failure on step " + step + ".");
                    Assert.True(step > StandingSteps, "Member " + HangerId + " failed under self-weight alone on step " + step + ".");
                    Assert.Equal(new[] { HangerId }, output.FailedMemberIds);
                    failureStep = step;
                    failed.AddRange(output.FailedMemberIds);
                    selfWeight = DynamicSelfWeight.Loads(bridge, failed);
                }

                if (failureStep > 0)
                {
                    Assert.DoesNotContain(output.MemberForces, f => f.MemberId == HangerId);
                }
            }

            int[] pieceIds = DetachedNodeIds(bridge, failed);
            Assert.Equal(new[] { PieceTopNodeId, PieceLeftNodeId, PieceRightNodeId }, pieceIds);
            Assert.DoesNotContain(BeamMidspanNodeId, pieceIds);
            double[] masses = LumpedMasses(bridge, failed);
            int[] pieceIndices = pieceIds.Select(id => IndexOf(bridge, id)).ToArray();
            double pieceMass = pieceIndices.Sum(i => masses[i]);
            Assert.True(pieceMass > 0.0);

            // The centered difference about step n averages the accelerations of steps n - 1, n, and n + 1,
            // so from n = failureStep + 2 every sample comes after the self-weight was recomputed.
            for (int n = failureStep + 2; n < failureStep + StepsAfterFailure; n++)
            {
                Vec3[] before = displacements[n - 2];
                Vec3[] at = displacements[n - 1];
                Vec3[] after = displacements[n];
                double weighted = pieceIndices.Sum(i => masses[i] * (after[i].Y - 2.0 * at[i].Y + before[i].Y));
                double acceleration = weighted / (pieceMass * dt * dt);

                Assert.True(double.IsFinite(acceleration), "Acceleration on step " + n + " is " + acceleration + ".");
                Assert.True(
                    Math.Abs(acceleration + Gravity) <= FallTolerance * Gravity,
                    "Piece acceleration on step " + n + " is " + acceleration.ToString("R") + " m/s^2, failure on step " + failureStep + ".");
            }
        }

        [Fact]
        public void SelfWeight_RejectsInvalidInput()
        {
            Assert.Throws<ArgumentNullException>(() => DynamicSelfWeight.Loads(null, NoneRemoved, Gravity));
            Assert.Throws<ArgumentNullException>(() => DynamicSelfWeight.Loads(null, NoneRemoved));
            Assert.Throws<ArgumentNullException>(() => DynamicSelfWeight.Loads(Portal(), null, Gravity));
            Assert.Throws<ArgumentNullException>(() => DynamicSelfWeight.Loads(Portal(), null));

            foreach (double gravity in new[] { 0.0, -Gravity, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                AssertRejectedLikePreview<ArgumentOutOfRangeException>(Portal(), gravity);
            }

            foreach (double bad in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            {
                AssertRejectedLikePreview<ArgumentOutOfRangeException>(WithSection(Portal(), 2, s => s.Density = bad), Gravity);
                AssertRejectedLikePreview<ArgumentOutOfRangeException>(WithSection(Portal(), 2, s => s.A = bad), Gravity);
            }

            SolverInput coincident = Portal();
            coincident.Nodes[2] = new Node { Id = 3, Position = coincident.Nodes[1].Position };
            AssertRejectedLikePreview<ArgumentOutOfRangeException>(coincident, Gravity);

            SolverInput overflowing = Portal();
            overflowing.Nodes[1] = new Node { Id = 2, Position = new Vec3(-1.0e308, 5, 0) };
            overflowing.Nodes[2] = new Node { Id = 3, Position = new Vec3(1.0e308, 5, 0) };
            AssertRejectedLikePreview<ArgumentOutOfRangeException>(overflowing, Gravity);

            SolverInput infinite = Portal();
            infinite.Nodes[2] = new Node { Id = 3, Position = new Vec3(double.PositiveInfinity, 5, 0) };
            AssertRejectedLikePreview<ArgumentOutOfRangeException>(infinite, Gravity);

            AssertRejectedLikePreview<ArgumentOutOfRangeException>(WithSection(Portal(), 1, s => { s.Density = 1.0e300; s.A = 1.0e10; }), Gravity);

            AssertRejectedLikePreview<ArgumentException>(With(Portal(), p => p.Nodes = null), Gravity);
            AssertRejectedLikePreview<ArgumentException>(With(Portal(), p => p.Members = null), Gravity);
            AssertRejectedLikePreview<ArgumentException>(With(Portal(), p => p.Sections = null), Gravity);
            AssertRejectedLikePreview<ArgumentException>(With(Portal(), p => p.Nodes[1] = null), Gravity);
            AssertRejectedLikePreview<ArgumentException>(With(Portal(), p => p.Members[1] = null), Gravity);
            AssertRejectedLikePreview<ArgumentException>(With(Portal(), p => p.Sections[1] = null), Gravity);
            AssertRejectedLikePreview<ArgumentException>(With(Portal(), p => p.Sections = new[] { p.Sections[0] }), Gravity);
            AssertRejectedLikePreview<ArgumentException>(With(Portal(), p => p.Sections[1] = Steel(1)), Gravity);
            AssertRejectedLikePreview<ArgumentException>(With(Portal(), p => p.Members[0] = new Member { Id = 10, NodeA = 1, NodeB = 77, SectionId = 1 }), Gravity);
            AssertRejectedLikePreview<ArgumentException>(With(Portal(), p => p.Nodes[3] = new Node { Id = 1, Position = new Vec3(10, 0, 0), Fixed = true }), Gravity);

            Assert.Throws<ArgumentException>(() => DynamicSelfWeight.Loads(Portal(), new[] { 77 }, Gravity));
            Assert.Throws<ArgumentException>(() => DynamicSelfWeight.Loads(With(Portal(), p => p.Members[2] = new Member { Id = 10, NodeA = 3, NodeB = 4, SectionId = 1 }), NoneRemoved, Gravity));
            Assert.Throws<ArgumentOutOfRangeException>(() => DynamicSelfWeight.Loads(WithSection(Portal(), 2, s => s.Density = 0.0), new[] { 11 }, Gravity));

            SolverInput dense = WithSection(Portal(), 1, s => { s.Density = 1.0e150; s.A = 1.0e100; });
            foreach (SolverInput valid in new[] { Portal(), dense })
            {
                foreach (int[] removed in new[] { NoneRemoved, new[] { 11 }, new[] { 10, 11, 12 } })
                {
                    NodalLoad[] loads = DynamicSelfWeight.Loads(valid, removed, Gravity);
                    Assert.Equal(valid.Nodes.Length, loads.Length);
                    Assert.All(loads, AssertFinite);
                }
            }
        }

        /// <summary>
        /// A room-unit 3D frame fixed at both ends, with sparse ids, nodes out of order, three materials,
        /// a hidden brace, and node 6 hanging from member 8 alone.
        /// </summary>
        private static SolverInput MixedFrame(double scale)
        {
            return new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 23, Position = new Vec3(0.25, 0.1, 0) },
                    new Node { Id = 11, Position = new Vec3(0, 0, 0), Fixed = true, SurfaceId = 1 },
                    new Node { Id = 17, Position = new Vec3(0.5, 0.3, 0) },
                    new Node { Id = 8, Position = new Vec3(0.5, 0.12, 0.05) },
                    new Node { Id = 4, Position = new Vec3(1, 0, 0), Fixed = true, SurfaceId = 2 },
                    new Node { Id = 30, Position = new Vec3(0.75, 0.1, 0) },
                    new Node { Id = 6, Position = new Vec3(0.5, 0.12, -0.15) },
                },
                Members = new[]
                {
                    new Member { Id = 1, NodeA = 11, NodeB = 23, SectionId = 1, LineId = 1 },
                    new Member { Id = 2, NodeA = 23, NodeB = 8, SectionId = 1, LineId = 1 },
                    new Member { Id = 3, NodeA = 8, NodeB = 30, SectionId = 1, LineId = 1 },
                    new Member { Id = 4, NodeA = 30, NodeB = 4, SectionId = 1, LineId = 1 },
                    new Member { Id = 5, NodeA = 23, NodeB = 17, SectionId = 2, LineId = 2 },
                    new Member { Id = 6, NodeA = 17, NodeB = 30, SectionId = 2, LineId = 2 },
                    new Member { Id = 7, NodeA = 8, NodeB = 17, SectionId = 3, HiddenBrace = true },
                    new Member { Id = 8, NodeA = 8, NodeB = 6, SectionId = 3, LineId = 3 },
                },
                Sections = new[] { Steel(1), Aluminium(2), Timber(3) },
                ScaleFactor = scale,
            };
        }

        /// <summary>
        /// A room-unit fixed-fixed beam 1-3-2 with a triangle 4-5-6 hung from its midspan by member 20,
        /// which is the only link between them and has a deliberately low yield strength.
        /// </summary>
        private static SolverInput HangingPieceFrame(double scale)
        {
            SectionProperties hanger = Steel(2);
            hanger.YieldStrength = HangerYieldStrength;
            return new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 1, Position = new Vec3(0, 1, 0), Fixed = true, SurfaceId = 1 },
                    new Node { Id = 2, Position = new Vec3(0.5, 1, 0), Fixed = true, SurfaceId = 2 },
                    new Node { Id = BeamMidspanNodeId, Position = new Vec3(0.25, 1, 0) },
                    new Node { Id = PieceTopNodeId, Position = new Vec3(0.25, 0.75, 0) },
                    new Node { Id = PieceLeftNodeId, Position = new Vec3(0, 0.5, 0) },
                    new Node { Id = PieceRightNodeId, Position = new Vec3(0.5, 0.5, 0) },
                },
                Members = new[]
                {
                    new Member { Id = 10, NodeA = 1, NodeB = BeamMidspanNodeId, SectionId = 1, LineId = 1 },
                    new Member { Id = 11, NodeA = BeamMidspanNodeId, NodeB = 2, SectionId = 1, LineId = 1 },
                    new Member { Id = HangerId, NodeA = BeamMidspanNodeId, NodeB = PieceTopNodeId, SectionId = 2, LineId = 2 },
                    new Member { Id = 30, NodeA = PieceTopNodeId, NodeB = PieceLeftNodeId, SectionId = 1, LineId = 3 },
                    new Member { Id = 31, NodeA = PieceTopNodeId, NodeB = PieceRightNodeId, SectionId = 1, LineId = 4 },
                    new Member { Id = 32, NodeA = PieceLeftNodeId, NodeB = PieceRightNodeId, SectionId = 1, LineId = 5 },
                },
                Sections = new[] { Steel(1), hanger },
                ScaleFactor = scale,
            };
        }

        /// <summary>A portal frame with two materials at scale 1, so room and bridge-world units agree.</summary>
        private static SolverInput Portal()
        {
            return new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 1, Position = new Vec3(0, 0, 0), Fixed = true },
                    new Node { Id = 2, Position = new Vec3(0, 5, 0) },
                    new Node { Id = 3, Position = new Vec3(10, 5, 0) },
                    new Node { Id = 4, Position = new Vec3(10, 0, 0), Fixed = true },
                },
                Members = new[]
                {
                    new Member { Id = 10, NodeA = 1, NodeB = 2, SectionId = 1, LineId = 1 },
                    new Member { Id = 11, NodeA = 2, NodeB = 3, SectionId = 2, LineId = 2 },
                    new Member { Id = 12, NodeA = 3, NodeB = 4, SectionId = 1, LineId = 3 },
                },
                Sections = new[] { Steel(1), Aluminium(2) },
                ScaleFactor = 1.0,
            };
        }

        private static SectionProperties Steel(int id)
        {
            return new SectionProperties
            {
                Id = id, E = 2.0e11, G = 7.7e10, A = 0.01, Iy = 1.0e-4, Iz = 1.0e-4, J = 2.0e-4,
                YieldStrength = 2.5e8, Density = 7850.0, Cy = 0.1, Cz = 0.1,
            };
        }

        private static SectionProperties Aluminium(int id)
        {
            return new SectionProperties
            {
                Id = id, E = 7.0e10, G = 2.6e10, A = 0.02, Iy = 2.0e-4, Iz = 5.0e-5, J = 1.0e-4,
                YieldStrength = 2.0e8, Density = 2700.0, Cy = 0.15, Cz = 0.05,
            };
        }

        private static SectionProperties Timber(int id)
        {
            return new SectionProperties
            {
                Id = id, E = 1.1e10, G = 6.9e8, A = 0.03, Iy = 7.5e-5, Iz = 7.5e-5, J = 1.27e-4,
                YieldStrength = 2.0e7, Density = 500.0, Cy = 0.0866, Cz = 0.0866,
            };
        }

        private static SolverInput With(SolverInput input, Action<SolverInput> change)
        {
            change(input);
            return input;
        }

        private static SolverInput WithSection(SolverInput input, int sectionId, Action<SectionProperties> change)
        {
            change(input.Sections.Single(s => s.Id == sectionId));
            return input;
        }

        private static void AssertRejectedLikePreview<TException>(SolverInput input, double gravity)
            where TException : ArgumentException
        {
            Assert.Throws<TException>(() => DeadLoadPreview.SelfWeight(input, gravity));
            Assert.Throws<TException>(() => DynamicSelfWeight.Loads(input, NoneRemoved, gravity));
        }

        private static int IndexOf(SolverInput input, int nodeId)
        {
            int index = Array.FindIndex(input.Nodes, n => n.Id == nodeId);
            Assert.True(index >= 0, "Node " + nodeId + " is not in the input.");
            return index;
        }

        private static double MemberLength(SolverInput input, Member member)
        {
            return Vec3.Distance(input.Nodes[IndexOf(input, member.NodeA)].Position, input.Nodes[IndexOf(input, member.NodeB)].Position);
        }

        private static double MemberMass(SolverInput input, Member member)
        {
            SectionProperties section = input.Sections.Single(s => s.Id == member.SectionId);
            return section.Density * section.A * MemberLength(input, member);
        }

        /// <summary>Translational lumped mass per node in node order: half of every standing member's mass.</summary>
        private static double[] LumpedMasses(SolverInput input, IReadOnlyCollection<int> removed)
        {
            var masses = new double[input.Nodes.Length];
            foreach (Member member in input.Members.Where(m => !removed.Contains(m.Id)))
            {
                double half = 0.5 * MemberMass(input, member);
                masses[IndexOf(input, member.NodeA)] += half;
                masses[IndexOf(input, member.NodeB)] += half;
            }

            return masses;
        }

        /// <summary>Ids, ascending, of nodes that still have a standing member but no path of standing members to a fixed node.</summary>
        private static int[] DetachedNodeIds(SolverInput input, IReadOnlyCollection<int> removed)
        {
            Member[] standing = input.Members.Where(m => !removed.Contains(m.Id)).ToArray();
            var reached = new HashSet<int>(input.Nodes.Where(n => n.Fixed).Select(n => n.Id));
            var frontier = new Queue<int>(reached);
            while (frontier.Count > 0)
            {
                int nodeId = frontier.Dequeue();
                foreach (Member member in standing)
                {
                    int other = member.NodeA == nodeId ? member.NodeB : member.NodeB == nodeId ? member.NodeA : nodeId;
                    if (other != nodeId && reached.Add(other))
                    {
                        frontier.Enqueue(other);
                    }
                }
            }

            return standing
                .SelectMany(m => new[] { m.NodeA, m.NodeB })
                .Where(id => !reached.Contains(id))
                .Distinct()
                .OrderBy(id => id)
                .ToArray();
        }

        private static void AssertFinite(NodalLoad load)
        {
            AssertFinite(load.Force, "Force on node " + load.NodeId);
            AssertFinite(load.Moment, "Moment on node " + load.NodeId);
        }

        private static void AssertFinite(StepOutput output, int nodeCount)
        {
            Assert.Equal(nodeCount, output.Displacements.Length);
            Assert.Equal(nodeCount, output.Rotations.Length);
            for (int i = 0; i < nodeCount; i++)
            {
                AssertFinite(output.Displacements[i], "Displacement " + i);
                AssertFinite(output.Rotations[i], "Rotation " + i);
            }

            foreach (MemberForce force in output.MemberForces)
            {
                foreach (double value in new[]
                {
                    force.Axial, force.ShearLocalYAtA, force.ShearLocalZAtA, force.MomentLocalYAtA, force.MomentLocalZAtA,
                    force.ShearLocalYAtB, force.ShearLocalZAtB, force.MomentLocalYAtB, force.MomentLocalZAtB, force.Utilization,
                })
                {
                    Assert.True(double.IsFinite(value), "Member " + force.MemberId + " has a non-finite force component " + value + ".");
                }
            }
        }

        private static void AssertFinite(Vec3 value, string name)
        {
            Assert.True(double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z), name + " is " + value + ".");
        }

        private static void AssertRelative(double expected, double actual, double tolerance, string name)
        {
            Assert.True(
                Math.Abs(actual - expected) <= tolerance * Math.Abs(expected),
                name + ": expected " + expected.ToString("R") + " within relative " + tolerance + " but got " + actual.ToString("R") + ".");
        }

        private static void AssertRelative(Vec3 expected, Vec3 actual, double tolerance, string name)
        {
            AssertRelative(expected.X, actual.X, tolerance, name + ".X");
            AssertRelative(expected.Y, actual.Y, tolerance, name + ".Y");
            AssertRelative(expected.Z, actual.Z, tolerance, name + ".Z");
        }

        private static void AssertWithin(double expected, double actual, double tolerance, string name)
        {
            Assert.True(
                Math.Abs(actual - expected) <= tolerance,
                name + ": expected " + expected.ToString("R") + " within " + tolerance + " but got " + actual.ToString("R") + ".");
        }

        private static void AssertWithin(Vec3 expected, Vec3 actual, double tolerance, string name)
        {
            AssertWithin(expected.X, actual.X, tolerance, name + ".X");
            AssertWithin(expected.Y, actual.Y, tolerance, name + ".Y");
            AssertWithin(expected.Z, actual.Z, tolerance, name + ".Z");
        }
    }
}
