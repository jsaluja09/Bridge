using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class BridgeModelTests
    {
        private const int Section = 1;

        [Fact]
        public void DefaultPolicy_CopiesBudgetConstantsAndNodeCap()
        {
            BudgetPolicy policy = BudgetPolicy.Default;
            Assert.Equal(Constants.Budget.CostPerMeter, policy.CostPerMeter);
            Assert.Equal(Constants.Budget.CostPerSquareMeter, policy.CostPerSquareMeter);
            Assert.Equal(Constants.Budget.MaterialBudget, policy.MaterialBudget);
            Assert.Equal(Constants.Human.NodeCap, policy.NodeCap);
            Assert.Equal(150, policy.NodeCap);

            Assert.Equal(1.0, Constants.Budget.CostPerMeter);
            Assert.Equal(1.0, Constants.Budget.CostPerSquareMeter);
            Assert.Equal(500.0, Constants.Budget.MaterialBudget);

            var model = new BridgeModel();
            Assert.Same(BudgetPolicy.Default, model.Policy);
            Assert.Equal(0.0, model.SpentBudget);
            Assert.Equal(Constants.Budget.MaterialBudget, model.RemainingBudget);
        }

        [Fact]
        public void Budget_LineCostsLength()
        {
            var model = new BridgeModel(Policy(costPerMeter: 2.0, materialBudget: 100.0));
            int start = model.AddFixedNode(Vec3.Zero);
            EditResult path = model.TryAddLine(new[] { AtNode(start), At(3, 0, 0), At(3, 4, 0) }, Section);
            Assert.True(path.Succeeded);
            Assert.Equal(14.0, path.Cost);
            Assert.Equal(14.0, model.SpentBudget);
            Assert.Equal(86.0, model.RemainingBudget);
            AssertFinite(model);

            var exact = new BridgeModel(Policy(costPerMeter: 2.0, materialBudget: 10.0));
            int origin = exact.AddFixedNode(Vec3.Zero);
            EditResult segment = exact.TryAddLine(new[] { AtNode(origin), At(3, 4, 0) }, Section);
            Assert.True(segment.Succeeded);
            Assert.Equal(10.0, exact.SpentBudget);
            Assert.Equal(0.0, exact.RemainingBudget);
            AssertFinite(exact);
        }

        [Fact]
        public void Budget_FillCostsArea()
        {
            var model = new BridgeModel(Policy(costPerSquareMeter: 4.0, materialBudget: 100.0));
            int a = model.AddFixedNode(new Vec3(0, 0, 0));
            int b = model.AddFixedNode(new Vec3(3, 0, 0));
            int c = model.AddFixedNode(new Vec3(0, 4, 0));
            int nodesBefore = model.NodeCount;

            EditResult fill = model.TryAddFill(new[] { a, b, c }, false);
            Assert.True(fill.Succeeded);
            Assert.Equal(24.0, fill.Cost);
            Assert.Equal(24.0, model.SpentBudget);
            Assert.Equal(nodesBefore, model.NodeCount);
            Assert.Empty(fill.NodeIds);
            Fill stored = Assert.Single(model.Fills);
            Assert.Equal(fill.Id, stored.Id);
            Assert.Equal(new[] { a, b, c }, stored.NodeIds);
            Assert.True(stored.Valid);
            Assert.False(stored.Road);

            EditResult reversed = model.TryAddFill(new[] { c, b, a }, true);
            Assert.True(reversed.Succeeded);
            Assert.Equal(24.0, reversed.Cost);
            Assert.Equal(48.0, model.SpentBudget);
            AssertFinite(model);
        }

        [Fact]
        public void Budget_OverspendRejectedAndStateUnchanged()
        {
            var model = new BridgeModel(Policy(costPerMeter: 1.0, materialBudget: 10.0));
            int start = model.AddFixedNode(Vec3.Zero);

            EditResult result = model.TryAddLine(new[] { AtNode(start), At(10.5, 0, 0) }, Section);
            Assert.False(result.Succeeded);
            Assert.Equal(EditRejectionReason.BudgetExceeded, result.Reason);
            Assert.Equal(BudgetCause.MaterialBudget, result.Cause);
            Assert.Equal(0.0, result.Cost);
            Assert.Equal(1, model.NodeCount);
            Assert.Equal(0, model.MemberCount);
            Assert.Equal(0, model.LineCount);
            Assert.Equal(0, model.FillCount);
            Assert.Equal(0.0, model.SpentBudget);
            Assert.Equal(0, model.UndoDepth);
            AssertFinite(model);
        }

        [Fact]
        public void NodeCap_RejectedAsBudgetExceededWithNodeCapCause()
        {
            BudgetPolicy policy = Policy(nodeCap: 3);
            var model = new BridgeModel(policy);
            int a = model.AddFixedNode(new Vec3(0, 0, 0));
            model.AddFixedNode(new Vec3(1, 0, 0));
            EditResult accepted = model.TryAddLine(new[] { AtNode(a), At(0, 1, 0) }, Section);
            Assert.True(accepted.Succeeded);
            Assert.Equal(3, model.NodeCount);

            var fresh = new BridgeModel(policy);
            int c = fresh.AddFixedNode(new Vec3(0, 0, 0));
            fresh.AddFixedNode(new Vec3(1, 0, 0));
            EditResult rejected = fresh.TryAddLine(new[] { AtNode(c), At(0, 1, 0), At(0, 2, 0) }, Section);
            Assert.False(rejected.Succeeded);
            Assert.Equal(EditRejectionReason.BudgetExceeded, rejected.Reason);
            Assert.Equal(BudgetCause.NodeCap, rejected.Cause);
            Assert.Equal(2, fresh.NodeCount);
            Assert.Equal(0, fresh.MemberCount);
            Assert.Equal(0, fresh.LineCount);
            Assert.Equal(0.0, fresh.SpentBudget);
            Assert.Equal(0, fresh.UndoDepth);

            Assert.DoesNotContain(Enum.GetNames(typeof(EditRejectionReason)), name => name.Contains("Node", StringComparison.Ordinal) && name.Contains("Cap", StringComparison.Ordinal));
        }

        [Fact]
        public void Undo_RestoresMembersAndBudget()
        {
            var model = new BridgeModel(Policy(costPerMeter: 2.0, materialBudget: 100.0));
            int start = model.AddFixedNode(Vec3.Zero);
            EditResult line = model.TryAddLine(new[] { AtNode(start), At(3, 4, 0) }, Section);
            Assert.True(line.Succeeded);
            Assert.Equal(10.0, model.SpentBudget);
            Assert.Equal(1, model.MemberCount);
            Assert.Equal(1, model.UndoDepth);

            Assert.True(model.Undo());
            Assert.Equal(0.0, model.SpentBudget);
            Assert.Equal(0, model.MemberCount);
            Assert.Equal(0, model.LineCount);
            Assert.Equal(new[] { start }, model.Nodes.Select(n => n.Id));
            Assert.Equal(0, model.UndoDepth);

            Assert.False(model.Undo());
            AssertFinite(model);
        }

        [Fact]
        public void Undo_RejectedEditIsNotPushed()
        {
            var model = new BridgeModel(Policy(costPerMeter: 1.0, materialBudget: 10.0));
            int start = model.AddFixedNode(Vec3.Zero);
            Assert.True(model.TryAddLine(new[] { AtNode(start), At(4, 0, 0) }, Section).Succeeded);
            Assert.Equal(1, model.UndoDepth);

            EditResult overBudget = model.TryAddLine(new[] { AtNode(start), At(0, 7, 0) }, Section);
            Assert.Equal(EditRejectionReason.BudgetExceeded, overBudget.Reason);
            Assert.Equal(1, model.UndoDepth);
            Assert.Equal(4.0, model.SpentBudget);
        }

        [Fact]
        public void Reset_CanBeUndone()
        {
            var model = new BridgeModel(Policy());
            int a = model.AddFixedNode(new Vec3(0, 0, 0));
            int b = model.AddFixedNode(new Vec3(6, 0, 0));
            EditResult line = model.TryAddLine(new[] { AtNode(a), At(3, 4, 0), AtNode(b) }, Section);
            int apex = Assert.Single(line.NodeIds);
            Assert.True(model.TryAddFill(new[] { a, apex, b }, true).Succeeded);
            Assert.Equal(22.0, model.SpentBudget);
            Member[] membersBefore = model.Members.ToArray();

            Assert.True(model.Reset());
            Assert.Equal(new[] { a, b }, model.Nodes.Select(n => n.Id));
            Assert.All(model.Nodes, node => Assert.True(node.Fixed));
            Assert.Equal(0, model.MemberCount);
            Assert.Equal(0, model.LineCount);
            Assert.Equal(0, model.FillCount);
            Assert.Equal(0.0, model.SpentBudget);
            Assert.Equal(3, model.UndoDepth);

            Assert.True(model.Undo());
            Assert.Equal(22.0, model.SpentBudget);
            Assert.Equal(new[] { a, b, apex }, model.Nodes.Select(n => n.Id));
            Line restored = Assert.Single(model.Lines);
            Assert.Equal(line.Id, restored.Id);
            Assert.Equal(line.MemberIds, restored.MemberIds);
            Assert.Equal(membersBefore.Select(m => (m.Id, m.NodeA, m.NodeB)), model.Members.Select(m => (m.Id, m.NodeA, m.NodeB)));
            Assert.True(Assert.Single(model.Fills).Valid);
            Assert.Equal(2, model.UndoDepth);
            AssertFinite(model);
        }

        [Fact]
        public void Delete_BlockedWhenDependentsAndHighlightsThem()
        {
            var model = new BridgeModel(Policy());
            int a = model.AddFixedNode(new Vec3(0, 0, 0));
            int b = model.AddFixedNode(new Vec3(2, 0, 0));
            EditResult first = model.TryAddLine(new[] { AtNode(a), At(1, 1, 0) }, Section);
            int c = Assert.Single(first.NodeIds);
            EditResult second = model.TryAddLine(new[] { AtNode(c), AtNode(b) }, Section);
            int dependent = Assert.Single(second.MemberIds);
            double spent = model.SpentBudget;

            DeleteResult result = model.TryDeleteLine(first.Id);
            Assert.False(result.Succeeded);
            Assert.Equal(EditRejectionReason.HasDependents, result.Reason);
            Assert.Equal(new[] { dependent }, result.DependentMemberIds);
            Assert.Empty(result.InvalidatedFillIds);
            Assert.Equal(0.0, result.Refund);
            Assert.Equal(2, model.LineCount);
            Assert.Equal(2, model.MemberCount);
            Assert.Equal(3, model.NodeCount);
            Assert.Equal(spent, model.SpentBudget);
            Assert.Equal(2, model.UndoDepth);

            Assert.True(model.TryDeleteLine(second.Id).Succeeded);
            Assert.True(model.TryDeleteLine(first.Id).Succeeded);
            Assert.Equal(0.0, model.SpentBudget);
            AssertFinite(model);
        }

        [Fact]
        public void Delete_RefundsLineCost()
        {
            var model = new BridgeModel(Policy(costPerMeter: 2.0, materialBudget: 100.0));
            int a = model.AddFixedNode(new Vec3(0, 0, 0));
            int b = model.AddFixedNode(new Vec3(6, 0, 0));
            EditResult line = model.TryAddLine(new[] { AtNode(a), At(3, 4, 0), AtNode(b) }, Section);
            int created = Assert.Single(line.NodeIds);
            Assert.Equal(20.0, model.SpentBudget);

            DeleteResult result = model.TryDeleteLine(line.Id);
            Assert.True(result.Succeeded);
            Assert.Equal(20.0, result.Refund);
            Assert.Empty(result.DependentMemberIds);
            Assert.Empty(result.InvalidatedFillIds);
            Assert.Equal(0.0, model.SpentBudget);
            Assert.Equal(new[] { a, b }, model.Nodes.Select(n => n.Id));
            Assert.DoesNotContain(model.Nodes, node => node.Id == created);
            Assert.Equal(0, model.MemberCount);
            Assert.Equal(0, model.LineCount);
            Assert.Equal(2, model.UndoDepth);
            AssertFinite(model);
        }

        [Fact]
        public void Delete_BorderingLineInvalidatesFillAndRefundsArea()
        {
            Triangle triangle = BuildTriangle();
            BridgeModel model = triangle.Model;
            Assert.Equal(18.0, model.SpentBudget);

            DeleteResult result = model.TryDeleteLine(triangle.ClosingLineId);
            Assert.True(result.Succeeded);
            Assert.Empty(result.DependentMemberIds);
            Assert.Equal(new[] { triangle.FillId }, result.InvalidatedFillIds);
            Assert.Equal(4.0 + 6.0, result.Refund);

            Fill fill = Assert.Single(model.Fills);
            Assert.Equal(triangle.FillId, fill.Id);
            Assert.False(fill.Valid);
            Assert.False(fill.Road);
            Assert.Equal(8.0, model.SpentBudget);
            Assert.Equal(2, model.LineCount);
            Assert.Equal(3, model.NodeCount);

            Assert.True(model.Undo());
            Fill restored = Assert.Single(model.Fills);
            Assert.True(restored.Valid);
            Assert.True(restored.Road);
            Assert.Equal(18.0, model.SpentBudget);
            AssertFinite(model);
        }

        [Fact]
        public void DeleteFill_RefundsAreaAndLeavesFrame()
        {
            Triangle triangle = BuildTriangle();
            BridgeModel model = triangle.Model;
            Member[] frame = model.Members.ToArray();

            DeleteResult result = model.TryDeleteFill(triangle.FillId);
            Assert.True(result.Succeeded);
            Assert.Equal(6.0, result.Refund);
            Assert.Equal(12.0, model.SpentBudget);
            Assert.Equal(0, model.FillCount);
            Assert.Equal(3, model.LineCount);
            Assert.Equal(frame.Select(m => m.Id), model.Members.Select(m => m.Id));

            Assert.True(model.Undo());
            Fill restored = Assert.Single(model.Fills);
            Assert.Equal(triangle.FillId, restored.Id);
            Assert.True(restored.Valid);
            Assert.True(restored.Road);
            Assert.Equal(18.0, model.SpentBudget);
            AssertFinite(model);
        }

        [Fact]
        public void Copy_IsIndependent()
        {
            Triangle triangle = BuildTriangle();
            BridgeModel model = triangle.Model;
            int undoDepth = model.UndoDepth;

            BridgeModel copy = model.Copy();
            Assert.Equal(model.Nodes.Select(n => (n.Id, n.Position, n.Fixed)), copy.Nodes.Select(n => (n.Id, n.Position, n.Fixed)));
            Assert.Equal(model.Members.Select(m => (m.Id, m.NodeA, m.NodeB, m.LineId)), copy.Members.Select(m => (m.Id, m.NodeA, m.NodeB, m.LineId)));
            Assert.Equal(model.Lines.Select(l => l.Id), copy.Lines.Select(l => l.Id));
            Assert.Equal(model.Fills.Select(f => f.Id), copy.Fills.Select(f => f.Id));
            Assert.Equal(model.SpentBudget, copy.SpentBudget);
            Assert.Equal(0, copy.UndoDepth);
            Assert.False(copy.Undo());
            Assert.Equal(undoDepth, model.UndoDepth);

            Assert.True(copy.TryAddLine(new[] { AtNode(triangle.A), At(0, 0, 2) }, Section).Succeeded);
            Assert.True(copy.TryDeleteFill(triangle.FillId).Succeeded);
            copy.Nodes[0].Position = new Vec3(9, 9, 9);
            Assert.Equal(3, model.NodeCount);
            Assert.Equal(3, model.MemberCount);
            Assert.True(Assert.Single(model.Fills).Valid);
            Assert.Equal(Vec3.Zero, model.Nodes[0].Position);
            Assert.Equal(18.0, model.SpentBudget);

            Assert.True(model.TryDeleteLine(triangle.ClosingLineId).Succeeded);
            Assert.Equal(4, copy.LineCount);
            Assert.Equal(4, copy.MemberCount);
            Assert.Equal(0, copy.FillCount);
            Assert.Equal(2, copy.UndoDepth);
            AssertFinite(model);
            AssertFinite(copy);
        }

        [Fact]
        public void NoSplitApi_ExistingMemberCountUnchangedAfterAttach()
        {
            var model = new BridgeModel(Policy());
            int a = model.AddFixedNode(Vec3.Zero);
            EditResult first = model.TryAddLine(new[] { AtNode(a), At(1, 0, 0) }, Section);
            int end = Assert.Single(first.NodeIds);
            int original = Assert.Single(first.MemberIds);

            EditResult attached = model.TryAddLine(new[] { AtNode(end), At(1, 1, 0) }, Section);
            Assert.True(attached.Succeeded);
            Member kept = Assert.Single(model.Members, m => m.Id == original);
            Assert.Equal(a, kept.NodeA);
            Assert.Equal(end, kept.NodeB);
            Assert.Equal(new[] { original }, model.Lines.Single(l => l.Id == first.Id).MemberIds);
            Assert.Equal(2, model.MemberCount);

            Type[] modelTypes = { typeof(BridgeModel), typeof(BudgetPolicy), typeof(LineVertex), typeof(EditResult), typeof(DeleteResult) };
            string[] forbidden = { "Split", "InsertNode", "Subdivide" };
            foreach (Type type in modelTypes)
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    Assert.DoesNotContain(forbidden, word => method.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        [Fact]
        public void Reject_WhenLineDoesNotStartAtExistingNode()
        {
            var model = new BridgeModel(Policy());
            model.AddFixedNode(Vec3.Zero);

            EditResult fromPosition = model.TryAddLine(new[] { At(0, 0, 0), At(1, 0, 0) }, Section);
            EditResult fromUnknownId = model.TryAddLine(new[] { AtNode(999), At(1, 0, 0) }, Section);
            EditResult empty = model.TryAddLine(Array.Empty<LineVertex>(), Section);
            foreach (EditResult result in new[] { fromPosition, fromUnknownId, empty })
            {
                Assert.False(result.Succeeded);
                Assert.Equal(EditRejectionReason.MissingStartNode, result.Reason);
                Assert.Equal(BudgetCause.None, result.Cause);
                Assert.Equal(0, result.Id);
            }

            Assert.Equal(1, model.NodeCount);
            Assert.Equal(0, model.MemberCount);
            Assert.Equal(0, model.LineCount);
            Assert.Equal(0.0, model.SpentBudget);
            Assert.Equal(0, model.UndoDepth);
        }

        [Fact]
        public void HiddenBrace_DoesNotChangeBudget()
        {
            var model = new BridgeModel(Policy());
            int a = model.AddFixedNode(new Vec3(0, 0, 0));
            int b = model.AddFixedNode(new Vec3(3, 0, 0));
            EditResult line = model.TryAddLine(new[] { AtNode(a), At(0, 4, 0) }, Section);
            int c = Assert.Single(line.NodeIds);
            Assert.Equal(4.0, model.SpentBudget);

            EditResult brace = model.TryAddHiddenBrace(b, c, Section);
            Assert.True(brace.Succeeded);
            Assert.Equal(0.0, brace.Cost);
            Assert.Empty(brace.NodeIds);
            Member member = Assert.Single(model.Members, m => m.Id == brace.Id);
            Assert.True(member.HiddenBrace);
            Assert.Equal(b, member.NodeA);
            Assert.Equal(c, member.NodeB);
            Assert.Equal(Section, member.SectionId);
            Assert.Equal(4.0, model.SpentBudget);
            Assert.Equal(3, model.NodeCount);
            Assert.Equal(2, model.UndoDepth);

            Assert.True(model.Undo());
            Assert.DoesNotContain(model.Members, m => m.Id == brace.Id);
            Assert.Equal(1, model.MemberCount);
            Assert.Equal(4.0, model.SpentBudget);
            AssertFinite(model);
        }

        [Fact]
        public void FixedNodeAddedAfterEdit_SurvivesUndoAndReset()
        {
            var model = new BridgeModel(Policy());
            int a = model.AddFixedNode(Vec3.Zero);
            EditResult line = model.TryAddLine(new[] { AtNode(a), At(1, 0, 0) }, Section);
            int late = model.AddFixedNode(new Vec3(0, 0, 1), 2, true);
            Assert.Equal(1, model.UndoDepth);

            Assert.True(model.Undo());
            Node kept = Assert.Single(model.Nodes, n => n.Id == late);
            Assert.True(kept.Fixed);
            Assert.True(kept.GroundAnchor);
            Assert.Equal(2, kept.SurfaceId);

            EditResult again = model.TryAddLine(new[] { AtNode(late), At(1, 0, 1) }, Section);
            Assert.True(again.Succeeded);
            Assert.DoesNotContain(again.NodeIds, id => id == a || id == late || line.NodeIds.Contains(id));
            Assert.True(model.Reset());
            Assert.Equal(new[] { a, late }, model.Nodes.Select(n => n.Id));
            Assert.False(model.Reset());
            Assert.Throws<ArgumentException>(() => model.AddFixedNode(new Vec3(double.PositiveInfinity, 0, 0)));
        }

        [Fact]
        public void DeleteFill_RemovesBracesSpanningOnlyThatFill()
        {
            var model = new BridgeModel(Policy());
            int a = model.AddFixedNode(new Vec3(0, 0, 0));
            int b = model.AddFixedNode(new Vec3(1, 0, 0));
            int c = model.AddFixedNode(new Vec3(1, 0, 1));
            int d = model.AddFixedNode(new Vec3(0, 0, 1));
            int off = model.AddFixedNode(new Vec3(0, 1, 0));
            model.TryAddLine(new[] { AtNode(a), AtNode(b), AtNode(c), AtNode(d), AtNode(a) }, Section);
            int fill = model.TryAddFill(new[] { a, b, c, d }, true).Id;
            int brace = model.TryAddHiddenBrace(a, c, Section).Id;
            int offLoop = model.TryAddHiddenBrace(a, off, Section).Id;

            Assert.True(model.TryDeleteFill(fill).Succeeded);
            Assert.DoesNotContain(model.Members, m => m.Id == brace);
            Assert.Contains(model.Members, m => m.Id == offLoop);
            Assert.Equal(4, model.Members.Count(m => !m.HiddenBrace));

            Assert.True(model.Undo());
            Assert.Contains(model.Members, m => m.Id == brace);
        }

        [Fact]
        public void DeleteLine_DropsBracesOnRemovedNodesAndInvalidatesFillsUsingThem()
        {
            var model = new BridgeModel(Policy());
            int a = model.AddFixedNode(new Vec3(0, 0, 0));
            int b = model.AddFixedNode(new Vec3(2, 0, 0));
            EditResult line = model.TryAddLine(new[] { AtNode(a), At(0, 1, 0), At(1, 1, 0) }, Section);
            int tip = line.NodeIds[1];
            int fill = model.TryAddFill(new[] { a, b, tip }, false).Id;
            int brace = model.TryAddHiddenBrace(b, tip, Section).Id;
            Assert.Equal(3.0, model.SpentBudget);

            DeleteResult result = model.TryDeleteLine(line.Id);
            Assert.True(result.Succeeded);
            Assert.Equal(new[] { fill }, result.InvalidatedFillIds);
            Assert.Equal(3.0, result.Refund);
            Assert.DoesNotContain(model.Members, m => m.Id == brace);
            Assert.Equal(0, model.MemberCount);
            Assert.Equal(new[] { a, b }, model.Nodes.Select(n => n.Id));
            Assert.Equal(0.0, model.SpentBudget);
            Assert.False(Assert.Single(model.Fills).Valid);
            AssertFinite(model);
        }

        [Fact]
        public void ToSolverInput_CopiesStateAndSkipsInvalidFills()
        {
            Triangle triangle = BuildTriangle();
            BridgeModel model = triangle.Model;
            var sections = new[] { new SectionProperties { Id = Section } };

            SolverInput input = model.ToSolverInput(sections, 20.0);
            Assert.Equal(model.Nodes.Select(n => n.Id), input.Nodes.Select(n => n.Id));
            Assert.Equal(model.Members.Select(m => m.Id), input.Members.Select(m => m.Id));
            Assert.Equal(new[] { triangle.FillId }, input.Fills.Select(f => f.Id));
            Assert.Equal(20.0, input.ScaleFactor);
            Assert.NotSame(sections, input.Sections);

            input.Nodes[0].Position = new Vec3(5, 5, 5);
            input.Members[0].NodeB = input.Members[0].NodeA;
            Assert.Equal(Vec3.Zero, model.Nodes[0].Position);
            Assert.NotEqual(model.Members[0].NodeA, model.Members[0].NodeB);

            model.TryDeleteLine(triangle.ClosingLineId);
            Assert.Empty(model.ToSolverInput(sections, 20.0).Fills);
            Assert.Throws<ArgumentOutOfRangeException>(() => model.ToSolverInput(sections, 0.0));
            Assert.Throws<ArgumentNullException>(() => model.ToSolverInput(null, 20.0));
        }

        [Fact]
        public void InvalidEdits_AreRejectedWithoutChangingState()
        {
            var model = new BridgeModel(Policy(materialBudget: 10.0));
            int a = model.AddFixedNode(new Vec3(0, 0, 0));
            int b = model.AddFixedNode(new Vec3(1, 0, 0));
            int c = model.AddFixedNode(new Vec3(2, 0, 0));
            int far = model.AddFixedNode(new Vec3(0, 20, 0));

            Assert.Equal(EditRejectionReason.InvalidGeometry, model.TryAddLine(new[] { AtNode(a) }, Section).Reason);
            Assert.Equal(EditRejectionReason.InvalidGeometry, model.TryAddLine(new[] { AtNode(a), AtNode(a) }, Section).Reason);
            Assert.Equal(EditRejectionReason.InvalidGeometry, model.TryAddLine(new[] { AtNode(a), At(double.PositiveInfinity, 0, 0) }, Section).Reason);
            Assert.Equal(EditRejectionReason.UnknownNode, model.TryAddLine(new[] { AtNode(a), AtNode(999) }, Section).Reason);
            Assert.Equal(EditRejectionReason.InvalidGeometry, model.TryAddFill(new[] { a, b }, false).Reason);
            Assert.Equal(EditRejectionReason.InvalidGeometry, model.TryAddFill(new[] { a, b, c }, false).Reason);
            Assert.Equal(EditRejectionReason.InvalidGeometry, model.TryAddFill(new[] { a, b, a }, false).Reason);
            Assert.Equal(EditRejectionReason.UnknownNode, model.TryAddFill(new[] { a, b, 999 }, false).Reason);
            EditResult bigFill = model.TryAddFill(new[] { a, c, far }, false);
            Assert.Equal(EditRejectionReason.BudgetExceeded, bigFill.Reason);
            Assert.Equal(BudgetCause.MaterialBudget, bigFill.Cause);
            Assert.Equal(EditRejectionReason.UnknownNode, model.TryAddHiddenBrace(a, 999, Section).Reason);
            Assert.Equal(EditRejectionReason.InvalidGeometry, model.TryAddHiddenBrace(a, a, Section).Reason);
            Assert.Equal(EditRejectionReason.UnknownLine, model.TryDeleteLine(1).Reason);
            Assert.Equal(EditRejectionReason.UnknownFill, model.TryDeleteFill(1).Reason);
            Assert.False(model.Reset());

            Assert.Equal(4, model.NodeCount);
            Assert.Equal(0, model.MemberCount);
            Assert.Equal(0, model.FillCount);
            Assert.Equal(0.0, model.SpentBudget);
            Assert.Equal(0, model.UndoDepth);

            Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetPolicy(-1.0, 1.0, 1.0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetPolicy(1.0, double.NaN, 1.0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetPolicy(1.0, 1.0, double.PositiveInfinity, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetPolicy(1.0, 1.0, 1.0, -1));
            Assert.Throws<ArgumentNullException>(() => new BridgeModel(null));
        }

        private static BudgetPolicy Policy(double costPerMeter = 1.0, double costPerSquareMeter = 1.0, double materialBudget = 1000.0, int nodeCap = 1000)
        {
            return new BudgetPolicy(costPerMeter, costPerSquareMeter, materialBudget, nodeCap);
        }

        private static LineVertex AtNode(int nodeId)
        {
            return LineVertex.AtNode(nodeId);
        }

        private static LineVertex At(double x, double y, double z)
        {
            return LineVertex.AtPosition(new Vec3(x, y, z));
        }

        /// <summary>
        /// Fixed nodes A (0,0,0), B (3,0,0), C (0,4,0); lines A-B, B-C, and A-C; one road fill A, B, C.
        /// Costs 1 per meter and 1 per square meter, so the lines spend 12 and the fill 6. Line A-C
        /// runs against the loop's closing edge C-A.
        /// </summary>
        private static Triangle BuildTriangle()
        {
            var model = new BridgeModel(Policy());
            int a = model.AddFixedNode(new Vec3(0, 0, 0));
            int b = model.AddFixedNode(new Vec3(3, 0, 0));
            int c = model.AddFixedNode(new Vec3(0, 4, 0));
            Assert.True(model.TryAddLine(new[] { AtNode(a), AtNode(b) }, Section).Succeeded);
            Assert.True(model.TryAddLine(new[] { AtNode(b), AtNode(c) }, Section).Succeeded);
            EditResult closing = model.TryAddLine(new[] { AtNode(a), AtNode(c) }, Section);
            Assert.True(closing.Succeeded);
            EditResult fill = model.TryAddFill(new[] { a, b, c }, true);
            Assert.True(fill.Succeeded);
            return new Triangle(model, a, closing.Id, fill.Id);
        }

        private static void AssertFinite(BridgeModel model)
        {
            Assert.True(double.IsFinite(model.SpentBudget), "Spent budget is not finite.");
            Assert.True(double.IsFinite(model.RemainingBudget), "Remaining budget is not finite.");
            foreach (Node node in model.Nodes)
            {
                Assert.True(double.IsFinite(node.Position.X) && double.IsFinite(node.Position.Y) && double.IsFinite(node.Position.Z), "Node " + node.Id + " position is not finite.");
            }
        }

        private sealed class Triangle
        {
            public Triangle(BridgeModel model, int a, int closingLineId, int fillId)
            {
                Model = model;
                A = a;
                ClosingLineId = closingLineId;
                FillId = fillId;
            }

            public BridgeModel Model { get; }

            public int A { get; }

            public int ClosingLineId { get; }

            public int FillId { get; }
        }
    }
}
