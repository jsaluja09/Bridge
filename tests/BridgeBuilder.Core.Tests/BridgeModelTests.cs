using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>W1-06: bridge model, budget, node cap, undo and delete.</summary>
    public class BridgeModelTests
    {
        static BudgetPolicy Policy(double perMeter = 1, double perSquare = 1, double budget = 1000, int cap = 1000) =>
            new BudgetPolicy { CostPerMeter = perMeter, CostPerSquareMeter = perSquare, MaterialBudget = budget, NodeCap = cap };

        static LineVertex E(int id) => LineVertex.Existing(id);
        static LineVertex N(double x, double y, double z) => LineVertex.New(new Vec3(x, y, z));

        [Fact]
        public void DefaultPolicy_CopiesBudgetConstantsAndNodeCap()
        {
            var p = BudgetPolicy.Default;
            Assert.Equal(Constants.Budget.CostPerMeter, p.CostPerMeter);
            Assert.Equal(Constants.Budget.CostPerSquareMeter, p.CostPerSquareMeter);
            Assert.Equal(Constants.Budget.MaterialBudget, p.MaterialBudget);
            Assert.Equal(150, p.NodeCap);
        }

        [Fact]
        public void Budget_LineCostsLength()
        {
            var m = new BridgeModel(Policy(perMeter: 2));
            int a = m.AddFixedNode(Vec3.Zero);
            Assert.True(m.TryAddLine(new[] { E(a), N(3, 0, 0), N(3, 4, 0) }).Ok);
            Assert.Equal(14, m.Spent, 9);

            var tight = new BridgeModel(Policy(perMeter: 2, budget: 10));
            int b = tight.AddFixedNode(Vec3.Zero);
            Assert.True(tight.TryAddLine(new[] { E(b), N(5, 0, 0) }).Ok);
            Assert.Equal(10, tight.Spent, 9);
        }

        [Fact]
        public void Budget_FillCostsArea()
        {
            var m = new BridgeModel(Policy(perSquare: 4));
            int a = m.AddFixedNode(Vec3.Zero), b = m.AddFixedNode(new Vec3(3, 0, 0)), c = m.AddFixedNode(new Vec3(0, 4, 0));
            int nodes = m.Nodes.Count;
            Assert.True(m.TryAddFill(new[] { a, b, c }, false).Ok);
            Assert.Equal(24, m.Spent, 9);
            Assert.Equal(nodes, m.Nodes.Count);
        }

        [Fact]
        public void Budget_OverspendRejectedAndStateUnchanged()
        {
            var m = new BridgeModel(Policy(budget: 10));
            int a = m.AddFixedNode(Vec3.Zero);
            var r = m.TryAddLine(new[] { E(a), N(10.5, 0, 0) });
            Assert.False(r.Ok);
            Assert.Equal(EditReason.BudgetExceeded, r.Reason);
            Assert.Equal(BudgetCause.MaterialBudget, r.Cause);
            Assert.Empty(m.Lines);
            Assert.Equal(0, m.Spent);
            Assert.Equal(0, m.UndoDepth);
        }

        [Fact]
        public void NodeCap_RejectedAsBudgetExceededWithNodeCapCause()
        {
            var m = new BridgeModel(Policy(cap: 3));
            int a = m.AddFixedNode(Vec3.Zero);
            m.AddFixedNode(new Vec3(5, 0, 0));
            Assert.True(m.TryAddLine(new[] { E(a), N(1, 0, 0) }).Ok);

            var fresh = new BridgeModel(Policy(cap: 3));
            int f = fresh.AddFixedNode(Vec3.Zero);
            fresh.AddFixedNode(new Vec3(5, 0, 0));
            var r = fresh.TryAddLine(new[] { E(f), N(1, 0, 0), N(2, 0, 0) });
            Assert.Equal(EditReason.BudgetExceeded, r.Reason);
            Assert.Equal(BudgetCause.NodeCap, r.Cause);
            Assert.Equal(2, fresh.Nodes.Count);
        }

        [Fact]
        public void Undo_RestoresMembersAndBudget()
        {
            var m = new BridgeModel(Policy(perMeter: 2));
            int a = m.AddFixedNode(Vec3.Zero);
            m.TryAddLine(new[] { E(a), N(5, 0, 0) });
            Assert.Equal(10, m.Spent, 9);
            Assert.True(m.Undo());
            Assert.Empty(m.Members);
            Assert.Equal(0, m.Spent);
            Assert.False(m.Undo());
        }

        [Fact]
        public void Undo_RejectedEditIsNotPushed()
        {
            var m = new BridgeModel(Policy(budget: 1));
            int a = m.AddFixedNode(Vec3.Zero);
            m.TryAddLine(new[] { E(a), N(5, 0, 0) });
            Assert.Equal(0, m.UndoDepth);
        }

        [Fact]
        public void Reset_CanBeUndone()
        {
            var m = new BridgeModel(Policy());
            int a = m.AddFixedNode(Vec3.Zero);
            m.TryAddLine(new[] { E(a), N(1, 0, 0) });
            m.Reset();
            Assert.Single(m.Nodes);
            Assert.True(m.Nodes[0].Fixed);
            Assert.True(m.Undo());
            Assert.Single(m.Lines);
            Assert.Equal(1, m.Spent, 9);
        }

        [Fact]
        public void Delete_BlockedWhenDependentsAndHighlightsThem()
        {
            var m = new BridgeModel(Policy());
            int a = m.AddFixedNode(Vec3.Zero);
            var line1 = m.TryAddLine(new[] { E(a), N(1, 0, 0) });
            int c = m.Nodes.Last().Id;
            var line2 = m.TryAddLine(new[] { E(c), N(1, 1, 0) });
            var r = m.TryDeleteLine(line1.Id);
            Assert.False(r.Ok);
            Assert.Equal(EditReason.Blocked, r.Reason);
            Assert.Equal(m.GetLine(line2.Id).Members, r.BlockingMemberIds);
        }

        [Fact]
        public void Delete_RefundsLineCost()
        {
            var m = new BridgeModel(Policy(perMeter: 3));
            int a = m.AddFixedNode(Vec3.Zero);
            var line = m.TryAddLine(new[] { E(a), N(2, 0, 0) });
            Assert.True(m.TryDeleteLine(line.Id).Ok);
            Assert.Equal(0, m.Spent, 9);
            Assert.Single(m.Nodes);
        }

        [Fact]
        public void Delete_BorderingLineInvalidatesFillAndRefundsArea()
        {
            var m = new BridgeModel(Policy());
            int a = m.AddFixedNode(Vec3.Zero), b = m.AddFixedNode(new Vec3(3, 0, 0)), c = m.AddFixedNode(new Vec3(0, 4, 0));
            var ab = m.TryAddLine(new[] { E(a), E(b) });
            m.TryAddLine(new[] { E(b), E(c) });
            m.TryAddLine(new[] { E(c), E(a) });
            var fill = m.TryAddFill(new[] { a, b, c }, true);
            double before = m.Spent;
            var r = m.TryDeleteLine(ab.Id);
            Assert.True(r.Ok);
            var f = m.GetFill(fill.Id);
            Assert.NotNull(f);
            Assert.False(f.Valid);
            Assert.False(f.Road);
            Assert.Equal(new[] { fill.Id }, r.InvalidatedFillIds);
            Assert.Equal(before - 3 - 6, m.Spent, 9);
        }

        [Fact]
        public void DeleteFill_RefundsAreaAndLeavesFrame()
        {
            var m = new BridgeModel(Policy());
            int a = m.AddFixedNode(Vec3.Zero), b = m.AddFixedNode(new Vec3(3, 0, 0)), c = m.AddFixedNode(new Vec3(0, 4, 0));
            m.TryAddLine(new[] { E(a), E(b) });
            m.TryAddLine(new[] { E(b), E(c) });
            m.TryAddLine(new[] { E(c), E(a) });
            double frame = m.Spent;
            var fill = m.TryAddFill(new[] { a, b, c }, false);
            Assert.True(m.TryDeleteFill(fill.Id).Ok);
            Assert.Equal(3, m.Lines.Count);
            Assert.Equal(frame, m.Spent, 9);
            Assert.True(m.Undo());
            Assert.NotNull(m.GetFill(fill.Id));
        }

        [Fact]
        public void Copy_IsIndependent()
        {
            var m = new BridgeModel(Policy());
            int a = m.AddFixedNode(Vec3.Zero);
            m.TryAddLine(new[] { E(a), N(1, 0, 0) });
            var copy = m.Copy();
            Assert.Equal(m.Nodes.Select(n => n.Id), copy.Nodes.Select(n => n.Id));
            Assert.Equal(m.Spent, copy.Spent);
            Assert.Equal(0, copy.UndoDepth);
            copy.TryAddLine(new[] { E(a), N(0, 1, 0) });
            Assert.Single(m.Lines);
            Assert.Equal(2, copy.Lines.Count);
        }

        [Fact]
        public void AttachAtExistingNode_LeavesExistingMemberUnchanged()
        {
            var m = new BridgeModel(Policy());
            int a = m.AddFixedNode(Vec3.Zero);
            m.TryAddLine(new[] { E(a), N(1, 0, 0) });
            var original = m.Members[0];
            int end = original.NodeB;
            m.TryAddLine(new[] { E(end), N(1, 1, 0) });
            Assert.Equal(original.NodeA, m.Members[0].NodeA);
            Assert.Equal(original.NodeB, m.Members[0].NodeB);
            Assert.Equal(2, m.Members.Count);
        }

        [Fact]
        public void Reject_WhenLineDoesNotStartAtExistingNode()
        {
            var m = new BridgeModel(Policy());
            var r = m.TryAddLine(new[] { N(0, 0, 0), N(1, 0, 0) });
            Assert.Equal(EditReason.MissingStartNode, r.Reason);
            Assert.Empty(m.Nodes);
        }

        [Fact]
        public void HiddenBrace_DoesNotChangeBudget()
        {
            var m = new BridgeModel(Policy());
            int a = m.AddFixedNode(Vec3.Zero);
            m.TryAddLine(new[] { E(a), N(1, 0, 0), N(1, 1, 0) });
            double spent = m.Spent;
            var r = m.TryAddHiddenBrace(a, m.Nodes.Last().Id, 0);
            Assert.True(r.Ok);
            Assert.Equal(spent, m.Spent);
            Assert.True(m.GetMember(r.Id).HiddenBrace);
            Assert.True(m.Undo());
            Assert.Null(m.GetMember(r.Id));
        }

        [Fact]
        public void FreeLine_MayStartInMidAir()
        {
            var m = new BridgeModel(Policy());
            var r = m.TryAddFreeLine(new[] { N(0, 1, 0), N(1, 1, 0) });
            Assert.True(r.Ok);
            Assert.Equal(2, m.Nodes.Count);
        }
    }
}
