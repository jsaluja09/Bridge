using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class RoadShapeTests
    {
        const double Top = 0.75;

        static BridgeModel Model(double budget = 500) => new BridgeModel(new BudgetPolicy
        {
            CostPerMeter = 1, CostPerSquareMeter = 1, MaterialBudget = budget, NodeCap = 500,
        });

        static int Rail(BridgeModel m, Vec3 a, Vec3 b)
        {
            var r = m.TryAddFreeLine(EqualSegments.Divide(a, b).Select(LineVertex.New).ToList());
            Assert.True(r.Ok);
            return r.Id;
        }

        /// <summary>Everything that defines the model, as text, to compare states exactly.</summary>
        static string State(BridgeModel m) => string.Join("|",
            string.Join(";", m.Nodes.Select(n => $"{n.Id}:{n.Position}:{n.Fixed}")),
            string.Join(";", m.Members.Select(x => $"{x.Id}:{x.NodeA}-{x.NodeB}:{x.LineId}:{x.HiddenBrace}")),
            string.Join(";", m.Lines.Select(l => $"{l.Id}:{string.Join(",", l.Members)}")),
            string.Join(";", m.Fills.Select(f => $"{f.Id}:{string.Join(",", f.Loop)}:{f.Road}:{f.Valid}")),
            m.Spent.ToString("R"));

        static (BridgeModel m, RoadShapes roads, int a, int b) TwoRails(double budget = 500)
        {
            var m = Model(budget);
            int a = Rail(m, new Vec3(0, Top, -0.1), new Vec3(1, Top, -0.1));
            int b = Rail(m, new Vec3(0, Top, 0.1), new Vec3(1, Top, 0.1));
            return (m, new RoadShapes(m), a, b);
        }

        [Fact]
        public void TwoParallelRails_GetACrossBeamAtEveryDotPair_AndOneDeck()
        {
            var (m, roads, a, b) = TwoRails();
            double railCost = m.Spent;
            var result = roads.TryPair(a, b);

            Assert.True(result.Ok);
            var road = Assert.Single(roads.Roads);
            int dots = m.LineNodes(a).Count;
            Assert.Equal(dots, road.CrossBeams.Count);
            var pairs = road.CrossBeams.Select(id => m.LineNodes(id)).ToList();
            Assert.All(pairs, p => Assert.Equal(2, p.Count));
            Assert.Equal(m.LineNodes(a).OrderBy(x => x), pairs.Select(p => p[0]).OrderBy(x => x));
            Assert.Equal(m.LineNodes(b).OrderBy(x => x), pairs.Select(p => p[1]).OrderBy(x => x));

            var deck = Assert.Single(AutoDeck.Find(m));                  // the beams never make decks of their own
            Assert.Equal(new[] { a, b }.OrderBy(x => x), new[] { deck.LineA, deck.LineB }.OrderBy(x => x));
            var fill = m.GetFill(road.DeckFillId);
            Assert.True(fill.Road && fill.Valid);

            // Paid for: beams by length, deck by area.
            Assert.Equal(railCost + dots * 0.2 + deck.Area, m.Spent, 9);
        }

        [Fact]
        public void RoadOnPads_PassesValidation()
        {
            var pads = Rooms.Pads();
            var m = Rooms.BareDeck(pads);
            var roads = new RoadShapes(m);
            var rails = m.Lines.Select(l => l.Id).ToList();
            Assert.True(roads.TryPair(rails[0], rails[1]).Ok);

            // Pairs where both dots are pad supports get no beam.
            Assert.All(roads.Roads[0].CrossBeams, id => Assert.False(m.LineNodes(id).All(n => m.GetNode(n).Fixed)));
            var compiled = BridgeCompiler.Compile(m, pads, Constants.Scale.RoomToBridge);
            Assert.True(compiled.Road.Ok, compiled.Road.Message);
            Assert.Single(compiled.Decks);
            var report = RoadValidation.Validate(m.Fills, m.GetNode, pads[0].Id, pads[1].Id);
            Assert.True(report.Ok, report.Message);
            Assert.Empty(report.TooSteepFillIds);
        }

        [Fact]
        public void Undo_AfterPairing_RestoresThePreviousModelExactly()
        {
            var (m, roads, a, b) = TwoRails();
            string before = State(m);
            Assert.True(roads.TryPair(a, b).Ok);
            Assert.NotEqual(before, State(m));
            Assert.True(roads.Undo());
            Assert.Equal(before, State(m));
            Assert.Empty(roads.Roads);
        }

        [Fact]
        public void DeletingARail_TakesItsBeamsAndDeck_AsOneUndoableAction()
        {
            var (m, roads, a, b) = TwoRails();
            Assert.True(roads.TryPair(a, b).Ok);
            string paired = State(m);
            var road = roads.Roads[0];

            var result = roads.TryDeleteRail(a);
            Assert.True(result.Ok);
            Assert.Null(m.GetLine(a));
            Assert.NotNull(m.GetLine(b));
            Assert.All(road.CrossBeams, id => Assert.Null(m.GetLine(id)));
            Assert.Null(m.GetFill(road.DeckFillId));
            Assert.Empty(AutoDeck.Find(m));
            Assert.Empty(roads.Roads);
            Assert.Equal(m.LineLength(b), m.Spent, 9);                    // only the other rail is still paid for

            Assert.True(roads.Undo());                                     // one undo brings it all back
            Assert.Equal(paired, State(m));
            Assert.Single(roads.Roads);
        }

        [Fact]
        public void PlainEdits_StillUndoOneAtATime()
        {
            var (m, roads, a, b) = TwoRails();
            Assert.True(roads.TryPair(a, b).Ok);
            string paired = State(m);
            Rail(m, new Vec3(0, Top + 0.3, 0), new Vec3(1, Top + 0.3, 0));
            Assert.True(roads.Undo());                                     // just the extra line
            Assert.Equal(paired, State(m));
            Assert.Single(roads.Roads);
        }

        [Fact]
        public void OverBudget_FailsAndChangesNothing()
        {
            var (m, roads, a, b) = TwoRails(budget: 2.5);                 // the rails cost 2; beams and deck don't fit
            string before = State(m);
            var result = roads.TryPair(a, b);
            Assert.False(result.Ok);
            Assert.Equal(EditReason.BudgetExceeded, result.Reason);
            Assert.Equal(before, State(m));
            Assert.Empty(roads.Roads);
        }

        [Fact]
        public void RailsThatMakeNoDeck_FailAndChangeNothing()
        {
            var m = Model();
            int a = Rail(m, new Vec3(0, Top, 0), new Vec3(1, Top, 0));
            int b = Rail(m, new Vec3(0.5, Top, -0.5), new Vec3(0.5, Top, 0.5));   // crosses at right angles
            var roads = new RoadShapes(m);
            string before = State(m);
            Assert.False(roads.TryPair(a, b).Ok);
            Assert.Equal(before, State(m));
            Assert.False(roads.TryPair(a, a).Ok);
            Assert.False(roads.TryPair(a, 9999).Ok);
        }

        [Fact]
        public void RailOutsideARoad_DeletesLikeAnyLine()
        {
            var (m, roads, a, _) = TwoRails();
            string before = State(m);
            Assert.True(roads.TryDeleteRail(a).Ok);
            Assert.True(roads.Undo());
            Assert.Equal(before, State(m));
        }
    }
}
