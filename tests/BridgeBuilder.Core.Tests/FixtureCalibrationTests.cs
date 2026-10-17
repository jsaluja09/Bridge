using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>
    /// Section calibration (spec 2.7, 5, and 8): one static solve of fixture 1 and fixture 5 under
    /// the standard car's weight, with every member built from <see cref="Constants.Section"/> and the
    /// <see cref="Constants.Human"/> material. Self-weight and dynamic impact are left out.
    /// </summary>
    public class FixtureCalibrationTests
    {
        private const string Fixture1 = "fixture-01-bare-cantilevers.json";
        private const string Fixture5 = "fixture-05-triangulated-truss.json";
        private const int StandardSectionId = 1;

        // Newtons; the split loads must add back up to the car's weight.
        private const double LoadSumTolerance = 1e-9;

        private readonly ITestOutputHelper output;

        public FixtureCalibrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void Fixture1_BareCantilevers_UtilizationAtLeastHumanMinimum()
        {
            BridgeFixture fixture = FixtureLoader.Load(Fixture1);
            Fixture1Data data = FixtureLoader.LoadAs<Fixture1Data>(Fixture1);
            DividedBridge bridge = DividedBridge.From(fixture);

            Assert.Equal(2, fixture.Lines.Length);
            Assert.Equal(2, data.TipNodeIds.Distinct().Count());
            foreach (int tip in data.TipNodeIds)
            {
                Assert.False(bridge.NodeById(tip).Fixed, "Tip node " + tip + " must be free.");
                Assert.True(bridge.MemberCountAt(tip) == 1, "Tip node " + tip + " must end its cantilever.");
            }

            CalibrationResult result = SolveUnderStandardCar(bridge, data.TipNodeIds);
            Report("Fixture 1", result);

            Assert.True(
                result.WorstUtilization >= Constants.Human.Fixture1MinimumUtilization,
                "Fixture 1 worst-member utilization " + Format(result.WorstUtilization)
                + " is below " + Format(Constants.Human.Fixture1MinimumUtilization) + ".");
        }

        [Fact]
        public void Fixture5_Truss_UtilizationBelowHumanMaximum()
        {
            BridgeFixture fixture = FixtureLoader.Load(Fixture5);
            Fixture5Data data = FixtureLoader.LoadAs<Fixture5Data>(Fixture5);
            DividedBridge bridge = DividedBridge.From(fixture);

            Assert.True(bridge.LineNodeIds.ContainsKey(data.DeckLineId), "Deck line " + data.DeckLineId + " is not in the fixture.");
            int[] deckNodes = bridge.LineNodeIds[data.DeckLineId].Where(id => !bridge.NodeById(id).Fixed).ToArray();
            Assert.NotEmpty(deckNodes);
            Assert.Contains(bridge.LineNodeIds[data.DeckLineId], id => bridge.NodeById(id).Fixed);

            CalibrationResult result = SolveUnderStandardCar(bridge, deckNodes);
            Report("Fixture 5", result);

            Assert.True(
                result.WorstUtilization < Constants.Human.Fixture5MaximumUtilization,
                "Fixture 5 worst-member utilization " + Format(result.WorstUtilization)
                + " is not below " + Format(Constants.Human.Fixture5MaximumUtilization) + ".");
        }

        private static CalibrationResult SolveUnderStandardCar(DividedBridge bridge, int[] loadedNodeIds)
        {
            Assert.Equal(loadedNodeIds.Length, loadedNodeIds.Distinct().Count());
            SectionProperties section = StandardSection();
            var roomInput = new SolverInput
            {
                Nodes = bridge.Nodes,
                Members = bridge.Members,
                Sections = new[] { section },
                ScaleFactor = Constants.Scale.RoomToBridge,
            };
            SolverInput bridgeInput = RoomBridgeAdapter.ToBridge(roomInput);

            double carWeight = Constants.Human.CarMassKg * Constants.Loads.Gravity;
            double share = carWeight / loadedNodeIds.Length;
            NodalLoad[] loads = loadedNodeIds
                .Select(id => new NodalLoad { NodeId = id, Force = new Vec3(0.0, -share, 0.0), Moment = Vec3.Zero })
                .ToArray();
            Assert.All(loads, load => AssertFinite(load.Force, "Load on node " + load.NodeId));
            Assert.Equal(-carWeight, loads.Sum(load => load.Force.Y), LoadSumTolerance);

            StaticSolveResult solve = new StaticSolver().Solve(bridgeInput, loads);

            Assert.Equal(bridgeInput.Nodes.Length, solve.Displacements.Length);
            Assert.Equal(bridgeInput.Nodes.Length, solve.Rotations.Length);
            Assert.All(solve.Displacements, d => AssertFinite(d, "Displacement"));
            Assert.All(solve.Rotations, r => AssertFinite(r, "Rotation"));
            Assert.Equal(bridgeInput.Members.Select(m => m.Id), solve.MemberForces.Select(f => f.MemberId));
            Assert.All(solve.MemberForces, AssertFinite);

            Dictionary<int, Vec3> bridgePositions = bridgeInput.Nodes.ToDictionary(n => n.Id, n => n.Position);
            var capacities = new MemberCapacityResult[bridgeInput.Members.Length];
            for (int m = 0; m < capacities.Length; m++)
            {
                Member member = bridgeInput.Members[m];
                double length = Vec3.Distance(bridgePositions[member.NodeA], bridgePositions[member.NodeB]);
                capacities[m] = MemberCapacity.Evaluate(solve.MemberForces[m], section, length);
                AssertFinite(capacities[m].Utilization, "Utilization of member " + member.Id);
            }

            int worst = 0;
            for (int m = 1; m < capacities.Length; m++)
            {
                if (capacities[m].Utilization > capacities[worst].Utilization)
                {
                    worst = m;
                }
            }

            Assert.True(capacities[worst].Utilization > 0.0, "The car load must load at least one member.");
            return new CalibrationResult(bridgeInput.Members[worst], capacities[worst], loadedNodeIds.Length, bridgeInput.Members.Length);
        }

        private static SectionProperties StandardSection()
        {
            return new SectionProperties
            {
                Id = StandardSectionId,
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

        private void Report(string fixtureName, CalibrationResult result)
        {
            MemberCapacityResult worst = result.Worst;
            output.WriteLine(
                fixtureName + ": worst-member utilization " + Format(worst.Utilization)
                + " in member " + result.WorstMember.Id + " (line " + result.WorstMember.LineId + ")"
                + "; tension " + Format(worst.TensionUtilization)
                + ", compression " + Format(worst.CompressionUtilization)
                + ", buckling " + Format(worst.BucklingUtilization)
                + ", bending " + Format(worst.BendingUtilization)
                + "; " + result.MemberCount + " members, car load split over " + result.LoadedNodeCount + " nodes.");
        }

        private static string Format(double value)
        {
            return value.ToString("0.0000", CultureInfo.InvariantCulture);
        }

        private static void AssertFinite(MemberForce force)
        {
            foreach (double value in new[]
            {
                force.Axial, force.ShearLocalYAtA, force.ShearLocalZAtA, force.MomentLocalYAtA, force.MomentLocalZAtA,
                force.ShearLocalYAtB, force.ShearLocalZAtB, force.MomentLocalYAtB, force.MomentLocalZAtB, force.Utilization,
            })
            {
                AssertFinite(value, "Force component of member " + force.MemberId);
            }
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

        private sealed class CalibrationResult
        {
            public CalibrationResult(Member worstMember, MemberCapacityResult worst, int loadedNodeCount, int memberCount)
            {
                WorstMember = worstMember;
                Worst = worst;
                LoadedNodeCount = loadedNodeCount;
                MemberCount = memberCount;
            }

            public Member WorstMember { get; }

            public MemberCapacityResult Worst { get; }

            public double WorstUtilization => Worst.Utilization;

            public int LoadedNodeCount { get; }

            public int MemberCount { get; }
        }

        /// <summary>
        /// A fixture with every drawn line divided by <see cref="EqualSegments.SnapPolyline(IReadOnlyList{Node}, IReadOnlyList{Member}, IReadOnlyList{Vec3})"/>
        /// at <see cref="Constants.Snapping.TargetSpacingMeters"/>, the way the game turns a stroke into members.
        /// The fixture's nodes keep their ids; division nodes and every member get new ids.
        /// </summary>
        private sealed class DividedBridge
        {
            private DividedBridge(Node[] nodes, Member[] members, Dictionary<int, int[]> lineNodeIds)
            {
                Nodes = nodes;
                Members = members;
                LineNodeIds = lineNodeIds;
            }

            public Node[] Nodes { get; }

            public Member[] Members { get; }

            /// <summary>Node ids along each divided line, keyed by the fixture's line id.</summary>
            public Dictionary<int, int[]> LineNodeIds { get; }

            public static DividedBridge From(BridgeFixture fixture)
            {
                Assert.Equal(
                    fixture.Members.Select(m => m.Id).OrderBy(id => id),
                    fixture.Lines.SelectMany(line => line.MemberIds).OrderBy(id => id));

                var nodes = new List<Node>(fixture.Nodes);
                var members = new List<Member>();
                var lineNodeIds = new Dictionary<int, int[]>();
                foreach (Line line in fixture.Lines)
                {
                    SnappedPolyline snapped = EqualSegments.SnapPolyline(nodes, members, DrawnVertices(fixture, line));
                    foreach (Member member in snapped.NewMembers)
                    {
                        member.SectionId = StandardSectionId;
                        member.LineId = line.Id;
                    }

                    nodes.AddRange(snapped.NewNodes);
                    members.AddRange(snapped.NewMembers);
                    lineNodeIds.Add(line.Id, snapped.NodeIds);
                }

                return new DividedBridge(nodes.ToArray(), members.ToArray(), lineNodeIds);
            }

            public Node NodeById(int id)
            {
                return Assert.Single(Nodes, node => node.Id == id);
            }

            public int MemberCountAt(int nodeId)
            {
                return Members.Count(member => member.NodeA == nodeId || member.NodeB == nodeId);
            }

            /// <summary>The line's vertices in chain order. Each member must run from the previous member's end B.</summary>
            private static Vec3[] DrawnVertices(BridgeFixture fixture, Line line)
            {
                Member[] chain = line.MemberIds.Select(id => fixture.Members.Single(m => m.Id == id)).ToArray();
                Assert.NotEmpty(chain);
                var nodeIds = new List<int> { chain[0].NodeA };
                foreach (Member member in chain)
                {
                    Assert.Equal(nodeIds[nodeIds.Count - 1], member.NodeA);
                    nodeIds.Add(member.NodeB);
                }

                return nodeIds.Select(id => fixture.Nodes.Single(n => n.Id == id).Position).ToArray();
            }
        }

        private sealed class Fixture1Data
        {
            public int[] TipNodeIds { get; set; } = Array.Empty<int>();
        }

        private sealed class Fixture5Data
        {
            public int DeckLineId { get; set; }
        }
    }
}
