using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class SupportPartitionTests
    {
        private const double RelativeTolerance = 1e-9;

        private static readonly int[] TrussNodeIds = { 1, 2, 3, 4, 5 };
        private static readonly int[] TrussMemberIds = { 1, 2, 3, 4, 5, 6, 7 };
        private static readonly int[] LineNodeIds = { 101, 102, 103 };
        private static readonly int[] LineMemberIds = { 11, 12 };
        private static readonly int[] TriangleNodeIds = { 201, 202, 203 };
        private static readonly int[] TriangleMemberIds = { 21, 22, 23 };

        [Fact]
        public void Partition_SplitsSupportedAndUnsupported()
        {
            SolverInput input = FloatingFrame(Constants.Scale.RoomToBridge);
            Node[] nodesBefore = input.Nodes;
            Member[] membersBefore = input.Members;

            SupportPartitionResult split = SupportPartition.Split(input);

            SolverInput supported = split.SupportedInput;
            Assert.Equal(TrussNodeIds, supported.Nodes.Select(n => n.Id));
            Assert.Equal(TrussMemberIds, supported.Members.Select(m => m.Id));
            for (int i = 0; i < supported.Nodes.Length; i++)
            {
                Assert.Same(input.Nodes[split.SupportedNodeIndices[i]], supported.Nodes[i]);
            }

            for (int k = 0; k < supported.Members.Length; k++)
            {
                Assert.Same(input.Members[split.SupportedMemberIndices[k]], supported.Members[k]);
            }

            Assert.Same(input.Sections, supported.Sections);
            Assert.Equal(new[] { input.Fills[0] }, supported.Fills);
            Assert.Equal(input.ScaleFactor, supported.ScaleFactor);
            Assert.True(split.HasSupportedMembers);

            Assert.Equal(new[] { 101, 201, 102, 202, 103, 203 }, split.UnsupportedNodeIds);
            Assert.Equal(new[] { 11, 21, 12, 22, 23 }, split.UnsupportedMemberIds);
            Assert.Equal(2, split.UnsupportedPieces.Length);
            Assert.Equal(LineMemberIds, split.UnsupportedPieces[0]);
            Assert.Equal(TriangleMemberIds, split.UnsupportedPieces[1]);

            Assert.Same(nodesBefore, input.Nodes);
            Assert.Same(membersBefore, input.Members);
            Assert.Equal(11, input.Nodes.Length);
            Assert.Equal(12, input.Members.Length);

            SolverInput bridge = RoomBridgeAdapter.ToBridge(input);
            Assert.Throws<InvalidOperationException>(() => new StaticSolver().Solve(bridge, DynamicSelfWeight.Loads(bridge, Array.Empty<int>())));
            SolverInput bridgeSupported = SupportPartition.Split(bridge).SupportedInput;
            StaticSolveResult solved = new StaticSolver().Solve(bridgeSupported, DynamicSelfWeight.Loads(bridgeSupported, Array.Empty<int>()));
            Assert.All(solved.Displacements, v => AssertFinite(v, "Displacement"));
        }

        [Fact]
        public void Partition_AllSupported_KeepsEverything()
        {
            SolverInput input = TrussOnly(Constants.Scale.RoomToBridge);
            var nodes = input.Nodes.ToList();
            nodes.Insert(2, new Node { Id = 30, Position = new Vec3(1.2, 0, 0), Fixed = true, SurfaceId = 2 });
            nodes.Add(new Node { Id = 31, Position = new Vec3(0.75, 0.5, 0) });
            nodes.Add(new Node { Id = 32, Position = new Vec3(0.75, 0.75, 0) });
            var members = input.Members.ToList();
            members.Insert(1, new Member { Id = 40, NodeA = 4, NodeB = 31, SectionId = 1, LineId = 9 });
            members.Add(new Member { Id = 41, NodeA = 31, NodeB = 32, SectionId = 1, LineId = 9 });
            input.Nodes = nodes.ToArray();
            input.Members = members.ToArray();

            SupportPartitionResult split = SupportPartition.Split(input);

            Assert.Equal(input.Nodes, split.SupportedInput.Nodes);
            Assert.Equal(input.Members, split.SupportedInput.Members);
            Assert.Equal(Enumerable.Range(0, input.Nodes.Length), split.SupportedNodeIndices);
            Assert.Equal(Enumerable.Range(0, input.Members.Length), split.SupportedMemberIndices);
            Assert.Same(input.Sections, split.SupportedInput.Sections);
            Assert.Same(input.Fills, split.SupportedInput.Fills);
            Assert.Equal(input.ScaleFactor, split.SupportedInput.ScaleFactor);
            Assert.Empty(split.UnsupportedNodeIds);
            Assert.Empty(split.UnsupportedMemberIds);
            Assert.Empty(split.UnsupportedPieces);
            Assert.True(split.HasSupportedMembers);
        }

        [Fact]
        public void Partition_DropsFillsThatNameUnsupportedNodes()
        {
            SolverInput input = FloatingFrame(Constants.Scale.RoomToBridge);
            Fill trussRoad = new Fill { Id = 1, NodeIds = new[] { 1, 5, 3 }, Road = true, Valid = true };
            Fill floatingRoad = new Fill { Id = 2, NodeIds = new[] { 201, 202, 203 }, Road = true, Valid = true };
            Fill spanningRoad = new Fill { Id = 3, NodeIds = new[] { 3, 4, 201 }, Road = true, Valid = true };
            Fill trussPanel = new Fill { Id = 4, NodeIds = new[] { 5, 4, 3 }, Valid = true };
            input.Fills = new[] { trussRoad, floatingRoad, spanningRoad, trussPanel };

            SolverInput supported = SupportPartition.Split(input).SupportedInput;

            Assert.Equal(new[] { trussRoad, trussPanel }, supported.Fills);
            Assert.Equal(new[] { trussRoad, floatingRoad, spanningRoad, trussPanel }, input.Fills);
            Assert.Throws<ArgumentException>(() => RoadValidation.Validate(supported.Nodes, input.Fills, 1, 2));
            RoadValidationResult road = RoadValidation.Validate(supported.Nodes, supported.Fills, 1, 2);
            Assert.NotNull(road);
        }

        [Fact]
        public void Partition_FreeNodeWithNoMember_IsUnsupportedAndInNoPiece()
        {
            SolverInput input = TrussOnly(1.0);
            input.Nodes = input.Nodes.Concat(new[] { new Node { Id = 77, Position = new Vec3(0.5, 2, 0) } }).ToArray();

            SupportPartitionResult split = SupportPartition.Split(input);

            Assert.Equal(TrussNodeIds, split.SupportedInput.Nodes.Select(n => n.Id));
            Assert.Equal(new[] { 77 }, split.UnsupportedNodeIds);
            Assert.Empty(split.UnsupportedMemberIds);
            Assert.Empty(split.UnsupportedPieces);
            new StaticSolver().Solve(split.SupportedInput, Array.Empty<NodalLoad>());
        }

        [Fact]
        public void Partition_NoFixedNode_NothingSupported()
        {
            SolverInput input = FloatingFrame(1.0);
            foreach (Node node in input.Nodes)
            {
                node.Fixed = false;
            }

            SupportPartitionResult split = SupportPartition.Split(input);

            Assert.Empty(split.SupportedInput.Nodes);
            Assert.Empty(split.SupportedInput.Members);
            Assert.False(split.HasSupportedMembers);
            Assert.Equal(input.Nodes.Select(n => n.Id), split.UnsupportedNodeIds);
            Assert.Equal(input.Members.Select(m => m.Id), split.UnsupportedMemberIds);
            Assert.Equal(new[] { TrussMemberIds, LineMemberIds, TriangleMemberIds }, split.UnsupportedPieces);
        }

        [Fact]
        public void Partition_RejectsInvalidInput()
        {
            Assert.Throws<ArgumentNullException>(() => SupportPartition.Split(null));

            SolverInput noNodes = TrussOnly(1.0);
            noNodes.Nodes = null;
            Assert.Throws<ArgumentException>(() => SupportPartition.Split(noNodes));

            SolverInput noMembers = TrussOnly(1.0);
            noMembers.Members = null;
            Assert.Throws<ArgumentException>(() => SupportPartition.Split(noMembers));

            SolverInput nullNode = TrussOnly(1.0);
            nullNode.Nodes[1] = null;
            Assert.Throws<ArgumentException>(() => SupportPartition.Split(nullNode));

            SolverInput nullMember = TrussOnly(1.0);
            nullMember.Members[1] = null;
            Assert.Throws<ArgumentException>(() => SupportPartition.Split(nullMember));

            SolverInput repeatedNode = TrussOnly(1.0);
            repeatedNode.Nodes[3] = new Node { Id = 1, Position = new Vec3(2, 2, 0) };
            Assert.Throws<ArgumentException>(() => SupportPartition.Split(repeatedNode));

            SolverInput repeatedMember = TrussOnly(1.0);
            repeatedMember.Members[3] = new Member { Id = 1, NodeA = 3, NodeB = 4, SectionId = 1 };
            Assert.Throws<ArgumentException>(() => SupportPartition.Split(repeatedMember));

            SolverInput missingNode = TrussOnly(1.0);
            missingNode.Members[0] = new Member { Id = 1, NodeA = 1, NodeB = 99, SectionId = 1 };
            Assert.Throws<ArgumentException>(() => SupportPartition.Split(missingNode));
        }

        [Fact]
        public void StaticWrapper_FloatingLine_PreviewCompletes()
        {
            SolverInput input = FloatingFrame(Constants.Scale.RoomToBridge);
            SolverInput trussAlone = TrussOnly(Constants.Scale.RoomToBridge);

            Assert.Throws<InvalidOperationException>(() => DeadLoadPreview.Run(input, new StaticSolver(), () => false));
            DeadLoadPreviewResult result = DeadLoadPreview.Run(input, new SupportedOnlyStaticSolver(new StaticSolver()), () => false);
            DeadLoadPreviewResult alone = DeadLoadPreview.Run(trussAlone, new StaticSolver(), () => false);

            Assert.Equal(DeadLoadPreviewStatus.Completed, result.Status);
            Assert.Equal(input.Members.Select(m => m.Id), result.MemberIds);
            Assert.Equal(input.Members.Length, result.Utilizations.Length);
            Assert.All(result.Utilizations, u => AssertFinite(u, "Utilization"));
            Assert.All(result.StaticResult.Displacements, v => AssertFinite(v, "Displacement"));
            Assert.All(result.StaticResult.Rotations, v => AssertFinite(v, "Rotation"));
            Assert.All(result.StaticResult.MemberForces, AssertFinite);

            for (int k = 0; k < trussAlone.Members.Length; k++)
            {
                int index = Array.IndexOf(result.MemberIds, trussAlone.Members[k].Id);
                Assert.True(alone.Utilizations[k] > 0.0, "Truss member " + trussAlone.Members[k].Id + " should carry its weight.");
                AssertRelative(alone.Utilizations[k], result.Utilizations[index], RelativeTolerance);
            }

            for (int i = 0; i < trussAlone.Nodes.Length; i++)
            {
                int index = IndexOf(input, trussAlone.Nodes[i].Id);
                Assert.Equal(alone.StaticResult.Displacements[i], result.StaticResult.Displacements[index]);
                Assert.Equal(alone.StaticResult.Rotations[i], result.StaticResult.Rotations[index]);
            }

            foreach (int id in LineMemberIds.Concat(TriangleMemberIds))
            {
                Assert.Equal(0.0, result.Utilizations[Array.IndexOf(result.MemberIds, id)]);
            }

            foreach (int id in LineNodeIds.Concat(TriangleNodeIds))
            {
                Assert.Equal(Vec3.Zero, result.StaticResult.Displacements[IndexOf(input, id)]);
                Assert.Equal(Vec3.Zero, result.StaticResult.Rotations[IndexOf(input, id)]);
            }
        }

        [Fact]
        public void StaticWrapper_NothingSupported_DoesNotCallSolver()
        {
            SolverInput input = FloatingFrame(Constants.Scale.RoomToBridge);
            input.Nodes = input.Nodes.Where(n => !TrussNodeIds.Contains(n.Id)).ToArray();
            input.Members = input.Members.Where(m => !TrussMemberIds.Contains(m.Id)).ToArray();
            input.Nodes = input.Nodes.Concat(new[] { new Node { Id = 50, Position = new Vec3(0, 0, 0), Fixed = true, SurfaceId = 1 } }).ToArray();
            var spy = new SpyStaticSolver(_ => throw new InvalidOperationException("The solver must not be called."));
            var wrapper = new SupportedOnlyStaticSolver(spy);
            NodalLoad[] loads = input.Nodes.Select(n => new NodalLoad { NodeId = n.Id, Force = new Vec3(10, -200, 5), Moment = new Vec3(1, 2, 3) }).ToArray();

            StaticSolveResult result = wrapper.Solve(input, loads);
            DeadLoadPreviewResult preview = DeadLoadPreview.Run(input, wrapper, () => false);

            Assert.Equal(0, spy.CallCount);
            Assert.Equal(input.Nodes.Length, result.Displacements.Length);
            Assert.Equal(input.Nodes.Length, result.Rotations.Length);
            Assert.All(result.Displacements, v => Assert.Equal(Vec3.Zero, v));
            Assert.All(result.Rotations, v => Assert.Equal(Vec3.Zero, v));
            Assert.Equal(input.Members.Select(m => m.Id), result.MemberForces.Select(f => f.MemberId));
            Assert.All(result.MemberForces, AssertZero);

            Assert.Equal(DeadLoadPreviewStatus.Completed, preview.Status);
            Assert.All(preview.Utilizations, u => Assert.Equal(0.0, u));
            Assert.All(preview.StaticResult.Displacements, v => Assert.Equal(Vec3.Zero, v));
            Assert.All(preview.StaticResult.MemberForces, AssertZero);
        }

        [Fact]
        public void StaticWrapper_SolvesSupportedPartAndMapsBack()
        {
            SolverInput input = RoomBridgeAdapter.ToBridge(FloatingFrame(Constants.Scale.RoomToBridge));
            NodalLoad[] loads = input.Nodes.Select((n, i) => new NodalLoad { NodeId = n.Id, Force = new Vec3(i, -100.0 * (i + 1), 0), Moment = Vec3.Zero }).ToArray();
            var spy = new SpyStaticSolver(received => new StaticSolveResult
            {
                Displacements = received.Nodes.Select((n, i) => new Vec3(0.001 * (i + 1), -0.002 * (i + 1), 0.0005 * i)).ToArray(),
                Rotations = received.Nodes.Select((n, i) => new Vec3(0.0001 * i, 0.0002, -0.0003 * (i + 1))).ToArray(),
                MemberForces = received.Members.Select((m, k) => new MemberForce { MemberId = m.Id, Axial = 1000.0 * (k + 1), MomentLocalYAtA = -50.0 * k }).ToArray(),
            });

            StaticSolveResult result = new SupportedOnlyStaticSolver(spy).Solve(input, loads);

            Assert.Equal(1, spy.CallCount);
            Assert.Equal(TrussNodeIds, spy.ReceivedInput.Nodes.Select(n => n.Id));
            Assert.Equal(TrussMemberIds, spy.ReceivedInput.Members.Select(m => m.Id));
            Assert.Equal(loads.Where(l => TrussNodeIds.Contains(l.NodeId)), spy.ReceivedLoads);

            StaticSolveResult solved = spy.LastResult;
            Assert.Equal(input.Nodes.Length, result.Displacements.Length);
            for (int i = 0; i < input.Nodes.Length; i++)
            {
                int received = Array.FindIndex(spy.ReceivedInput.Nodes, n => n.Id == input.Nodes[i].Id);
                Assert.Equal(received >= 0 ? solved.Displacements[received] : Vec3.Zero, result.Displacements[i]);
                Assert.Equal(received >= 0 ? solved.Rotations[received] : Vec3.Zero, result.Rotations[i]);
            }

            Assert.Equal(input.Members.Select(m => m.Id), result.MemberForces.Select(f => f.MemberId));
            for (int m = 0; m < input.Members.Length; m++)
            {
                int received = Array.FindIndex(spy.ReceivedInput.Members, x => x.Id == input.Members[m].Id);
                if (received >= 0)
                {
                    Assert.Same(solved.MemberForces[received], result.MemberForces[m]);
                }
                else
                {
                    AssertZero(result.MemberForces[m]);
                }
            }
        }

        [Fact]
        public void StaticWrapper_PreviewRejectsInvalidUnsupportedPart()
        {
            var spy = new SpyStaticSolver(_ => throw new InvalidOperationException("The solver must not be called."));
            var wrapper = new SupportedOnlyStaticSolver(spy);

            SolverInput missingSection = FloatingFrame(Constants.Scale.RoomToBridge);
            missingSection.Members[2] = new Member { Id = 11, NodeA = 101, NodeB = 102, SectionId = 9, LineId = 10 };
            Assert.Throws<ArgumentException>(() => DeadLoadPreview.Run(missingSection, wrapper, () => false));

            SolverInput infinitePosition = FloatingFrame(Constants.Scale.RoomToBridge);
            infinitePosition.Nodes[1] = new Node { Id = 101, Position = new Vec3(double.PositiveInfinity, 0.6, 0) };
            Assert.ThrowsAny<ArgumentException>(() => DeadLoadPreview.Run(infinitePosition, wrapper, () => false));

            Assert.Equal(0, spy.CallCount);
        }

        [Fact]
        public void StaticWrapper_RejectsInvalidUse()
        {
            SolverInput input = RoomBridgeAdapter.ToBridge(FloatingFrame(Constants.Scale.RoomToBridge));
            var wrapper = new SupportedOnlyStaticSolver(new StaticSolver());

            Assert.Throws<ArgumentNullException>(() => new SupportedOnlyStaticSolver(null));
            Assert.Throws<ArgumentNullException>(() => wrapper.Solve(null, Array.Empty<NodalLoad>()));
            Assert.Throws<ArgumentNullException>(() => wrapper.Solve(input, null));
            Assert.Throws<ArgumentException>(() => wrapper.Solve(input, new NodalLoad[] { null }));
            Assert.Throws<ArgumentException>(() => wrapper.Solve(input, new[] { new NodalLoad { NodeId = 999, Force = Vec3.UnitY } }));
            Assert.Throws<ArgumentException>(() => wrapper.Solve(input, new[] { new NodalLoad { NodeId = 101, Force = new Vec3(0, double.NegativeInfinity, 0) } }));

            SolverInput missingNode = RoomBridgeAdapter.ToBridge(FloatingFrame(1.0));
            missingNode.Members[2] = new Member { Id = 11, NodeA = 101, NodeB = 999, SectionId = 1 };
            Assert.Throws<ArgumentException>(() => wrapper.Solve(missingNode, Array.Empty<NodalLoad>()));

            StaticSolveResult Valid(SolverInput received) => new StaticSolver().Solve(received, Array.Empty<NodalLoad>());
            var broken = new Func<SolverInput, StaticSolveResult>[]
            {
                _ => null,
                received => { StaticSolveResult r = Valid(received); r.Displacements = r.Displacements.Skip(1).ToArray(); return r; },
                received => { StaticSolveResult r = Valid(received); r.Rotations = null; return r; },
                received => { StaticSolveResult r = Valid(received); r.MemberForces = r.MemberForces.Reverse().ToArray(); return r; },
                received => { StaticSolveResult r = Valid(received); r.MemberForces = r.MemberForces.Take(2).ToArray(); return r; },
            };
            foreach (Func<SolverInput, StaticSolveResult> respond in broken)
            {
                Assert.Throws<InvalidOperationException>(() => new SupportedOnlyStaticSolver(new SpyStaticSolver(respond)).Solve(input, Array.Empty<NodalLoad>()));
            }
        }

        // The truss of DeadLoadPreviewTests, fixed at both surfaces, interleaved with a floating
        // two-member line and a floating triangle that share no node with it.
        private static SolverInput FloatingFrame(double scale)
        {
            return new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 1, Position = new Vec3(0, 0, 0), Fixed = true, SurfaceId = 1 },
                    new Node { Id = 101, Position = new Vec3(0.2, 0.6, 0) },
                    new Node { Id = 2, Position = new Vec3(1, 0, 0), Fixed = true, SurfaceId = 2 },
                    new Node { Id = 3, Position = new Vec3(0.25, 0.25, 0) },
                    new Node { Id = 201, Position = new Vec3(0.4, 0.8, 0.1) },
                    new Node { Id = 4, Position = new Vec3(0.75, 0.25, 0) },
                    new Node { Id = 102, Position = new Vec3(0.4, 0.6, 0) },
                    new Node { Id = 5, Position = new Vec3(0.5, 0, 0) },
                    new Node { Id = 202, Position = new Vec3(0.6, 0.8, 0.1) },
                    new Node { Id = 103, Position = new Vec3(0.6, 0.6, 0) },
                    new Node { Id = 203, Position = new Vec3(0.5, 0.95, 0.1) },
                },
                Members = new[]
                {
                    new Member { Id = 1, NodeA = 1, NodeB = 5, SectionId = 1, LineId = 1 },
                    new Member { Id = 2, NodeA = 5, NodeB = 2, SectionId = 1, LineId = 1 },
                    new Member { Id = 11, NodeA = 101, NodeB = 102, SectionId = 1, LineId = 10 },
                    new Member { Id = 3, NodeA = 3, NodeB = 4, SectionId = 1, LineId = 2 },
                    new Member { Id = 21, NodeA = 201, NodeB = 202, SectionId = 1, LineId = 20 },
                    new Member { Id = 4, NodeA = 1, NodeB = 3, SectionId = 1, LineId = 3 },
                    new Member { Id = 12, NodeA = 102, NodeB = 103, SectionId = 1, LineId = 10 },
                    new Member { Id = 5, NodeA = 3, NodeB = 5, SectionId = 1, LineId = 3 },
                    new Member { Id = 22, NodeA = 202, NodeB = 203, SectionId = 1, LineId = 20 },
                    new Member { Id = 6, NodeA = 5, NodeB = 4, SectionId = 1, LineId = 3 },
                    new Member { Id = 7, NodeA = 4, NodeB = 2, SectionId = 1, LineId = 3 },
                    new Member { Id = 23, NodeA = 203, NodeB = 201, SectionId = 1, LineId = 20 },
                },
                Fills = new[]
                {
                    new Fill { Id = 1, NodeIds = new[] { 1, 5, 3 }, Valid = true },
                    new Fill { Id = 2, NodeIds = new[] { 201, 202, 203 }, Valid = true },
                },
                Sections = new[] { StandardSection() },
                ScaleFactor = scale,
            };
        }

        private static SolverInput TrussOnly(double scale)
        {
            SolverInput frame = FloatingFrame(scale);
            frame.Nodes = frame.Nodes.Where(n => TrussNodeIds.Contains(n.Id)).ToArray();
            frame.Members = frame.Members.Where(m => TrussMemberIds.Contains(m.Id)).ToArray();
            frame.Fills = frame.Fills.Take(1).ToArray();
            return frame;
        }

        private static SectionProperties StandardSection()
        {
            return new SectionProperties
            {
                Id = 1,
                E = Constants.Human.YoungsModulus,
                G = Constants.Human.ShearModulus,
                A = Constants.Section.Area,
                Iy = Constants.Section.SecondMoment,
                Iz = Constants.Section.SecondMoment,
                J = Constants.Human.TorsionConstant,
                YieldStrength = Constants.Section.YieldStrength,
                Density = Constants.Human.Density,
                Cy = Constants.Section.OuterFiberDistance,
                Cz = Constants.Section.OuterFiberDistance,
            };
        }

        private static int IndexOf(SolverInput input, int nodeId)
        {
            int index = Array.FindIndex(input.Nodes, n => n.Id == nodeId);
            Assert.True(index >= 0, "Node " + nodeId + " is not in the input.");
            return index;
        }

        private static IEnumerable<double> Components(MemberForce force)
        {
            return new[]
            {
                force.Axial, force.ShearLocalYAtA, force.ShearLocalZAtA, force.MomentLocalYAtA, force.MomentLocalZAtA,
                force.ShearLocalYAtB, force.ShearLocalZAtB, force.MomentLocalYAtB, force.MomentLocalZAtB, force.Utilization,
            };
        }

        private static void AssertZero(MemberForce force)
        {
            Assert.All(Components(force), value => Assert.Equal(0.0, value));
        }

        private static void AssertFinite(MemberForce force)
        {
            Assert.All(Components(force), value => AssertFinite(value, "Force component of member " + force.MemberId));
        }

        private static void AssertFinite(Vec3 value, string name)
        {
            AssertFinite(value.X, name + ".X");
            AssertFinite(value.Y, name + ".Y");
            AssertFinite(value.Z, name + ".Z");
        }

        private static void AssertFinite(double value, string name)
        {
            Assert.True(double.IsFinite(value), name + " is " + value + ".");
        }

        private static void AssertRelative(double expected, double actual, double tolerance)
        {
            Assert.True(
                Math.Abs(actual - expected) <= tolerance * Math.Abs(expected),
                "Expected " + expected.ToString("R") + " within relative " + tolerance + " but got " + actual.ToString("R") + ".");
        }

        private sealed class SpyStaticSolver : IStaticSolver
        {
            private readonly Func<SolverInput, StaticSolveResult> respond;

            public SpyStaticSolver(Func<SolverInput, StaticSolveResult> respond)
            {
                this.respond = respond;
            }

            public int CallCount { get; private set; }

            public SolverInput ReceivedInput { get; private set; }

            public NodalLoad[] ReceivedLoads { get; private set; }

            public StaticSolveResult LastResult { get; private set; }

            public StaticSolveResult Solve(SolverInput input, NodalLoad[] loads)
            {
                CallCount++;
                ReceivedInput = input;
                ReceivedLoads = loads;
                LastResult = respond(input);
                return LastResult;
            }
        }
    }
}
