using System;
using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;
using Xunit.Abstractions;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>Wood, steel cable and concrete: prices, refunds, deck material, sections, capacity and balance.</summary>
    public class MaterialsTests
    {
        const double Top = Rooms.Top;
        const double Scale = Constants.Scale.RoomToBridge;

        /// <summary>The challenges the game ships with.</summary>
        static readonly ChallengeConfig[] DefaultChallenges = { ChallengeConfig.Level() };

        readonly ITestOutputHelper _out;
        public MaterialsTests(ITestOutputHelper output) => _out = output;

        static Vec3 P(double x, double y, double z) => new Vec3(x, y, z);

        static BridgeModel Model() => new BridgeModel(BudgetPolicy.ForMaterials(1000, 1000));

        static double TotalCost(BridgeModel m, Pad[] pads) => m.Spent + BridgeCompiler.Compile(m, pads, Scale).DeckCost(m.Policy);

        // ---------- Prices and refunds ----------

        [Theory]
        [InlineData(BridgeMaterial.Wood)]
        [InlineData(BridgeMaterial.Cable)]
        [InlineData(BridgeMaterial.Concrete)]
        public void Line_CostsItsMaterialsPricePerMeter_AndDeleteOrUndoRefundsIt(BridgeMaterial material)
        {
            var m = Model();
            int a = m.AddFixedNode(Vec3.Zero);
            var line = m.TryAddLine(new[] { LineVertex.Existing(a), LineVertex.New(P(0.3, 0, 0)), LineVertex.New(P(0.3, 0.4, 0)) }, (int)material);

            Assert.True(line.Ok);
            Assert.Equal(0.7 * Materials.CostPerMeter(material), m.Spent, 9);
            Assert.Equal((int)material, m.LineSection(line.Id));
            Assert.All(m.Members, x => Assert.Equal((int)material, x.SectionId));

            Assert.True(m.TryDeleteLine(line.Id).Ok);
            Assert.Equal(0, m.Spent, 9);
            Assert.True(m.Undo());
            Assert.Equal(0.7 * Materials.CostPerMeter(material), m.Spent, 9);
            Assert.True(m.Undo());
            Assert.Equal(0, m.Spent, 9);
        }

        [Fact]
        public void LinesOfDifferentMaterials_EachRefundTheirOwnPrice()
        {
            var m = Model();
            int a = m.AddFixedNode(Vec3.Zero);
            var wood = m.TryAddLine(new[] { LineVertex.Existing(a), LineVertex.New(P(0.5, 0, 0)) }, (int)BridgeMaterial.Wood);
            var cable = m.TryAddLine(new[] { LineVertex.Existing(a), LineVertex.New(P(0, 0.5, 0)) }, (int)BridgeMaterial.Cable);
            var concrete = m.TryAddLine(new[] { LineVertex.Existing(a), LineVertex.New(P(0, 0, 0.5)) }, (int)BridgeMaterial.Concrete);
            Assert.Equal(0.5 * (10 + 25 + 50), m.Spent, 9);

            Assert.True(m.TryDeleteLine(cable.Id).Ok);
            Assert.Equal(0.5 * (10 + 50), m.Spent, 9);
            Assert.True(m.TryDeleteLine(concrete.Id).Ok);
            Assert.Equal(0.5 * 10, m.Spent, 9);
            Assert.True(m.TryDeleteLine(wood.Id).Ok);
            Assert.Equal(0, m.Spent, 9);
        }

        [Fact]
        public void UnknownSection_IsAnInvalidLineOrFill_AndChangesNothing()
        {
            var m = Model();
            int a = m.AddFixedNode(Vec3.Zero), b = m.AddFixedNode(P(1, 0, 0)), c = m.AddFixedNode(P(0, 1, 0));
            Assert.Equal(EditReason.InvalidLine, m.TryAddLine(new[] { LineVertex.Existing(a), LineVertex.Existing(b) }, 3).Reason);
            Assert.Equal(EditReason.InvalidLine, m.TryAddFreeLine(new[] { LineVertex.Existing(a), LineVertex.Existing(b) }, -1).Reason);
            Assert.Equal(EditReason.InvalidLine, m.TryAddFill(new[] { a, b, c }, false, 3).Reason);
            Assert.Empty(m.Lines);
            Assert.Empty(m.Fills);
            Assert.Equal(0, m.Spent);
            Assert.Equal(0, m.UndoDepth);
            Assert.Equal(-1, m.LineSection(12345));
        }

        [Fact]
        public void Fill_IsPricedByItsSection_AndRefundedTheSame_WhenDeletedOrInvalidated()
        {
            var m = Model();
            int a = m.AddFixedNode(Vec3.Zero), b = m.AddFixedNode(P(0.3, 0, 0)), c = m.AddFixedNode(P(0, 0, 0.4));
            var ab = m.TryAddLine(new[] { LineVertex.Existing(a), LineVertex.Existing(b) });
            double frame = m.Spent;

            var fill = m.TryAddFill(new[] { a, b, c }, true, (int)BridgeMaterial.Concrete);
            Assert.True(fill.Ok);
            Assert.Equal((int)BridgeMaterial.Concrete, m.GetFill(fill.Id).SectionId);
            Assert.Equal(frame + 0.06 * Constants.Concrete.DeckCostPerSquareMeter, m.Spent, 9);

            var copy = m.Copy();
            Assert.Equal((int)BridgeMaterial.Concrete, copy.GetFill(fill.Id).SectionId);

            Assert.True(m.TryDeleteFill(fill.Id).Ok);
            Assert.Equal(frame, m.Spent, 9);
            Assert.True(m.Undo());
            Assert.Equal((int)BridgeMaterial.Concrete, m.GetFill(fill.Id).SectionId);

            Assert.True(m.TryDeleteLine(ab.Id).Ok);                     // borders the fill: invalidates and refunds it
            Assert.Equal(0, m.Spent, 9);
        }

        [Fact]
        public void StrokePieces_AndTheirMirrorCopies_KeepTheStrokesMaterial_AndRefundRight()
        {
            var pads = Rooms.Pads();
            var m = Model();
            var ctx = new StrokeBuilder.Context { Pads = pads, Mirror = MirrorPlane.Centerline(pads[0], pads[1]), SectionId = (int)BridgeMaterial.Cable };
            var stroke = new List<Vec3>();
            for (int i = 0; i <= 30; i++)
                stroke.Add(Vec3.Lerp(P(0.2, Top + 0.3, 0.1), P(0.5, Top + 0.5, 0.1), i / 30.0));
            for (int i = 1; i <= 30; i++)
                stroke.Add(Vec3.Lerp(P(0.5, Top + 0.5, 0.1), P(0.8, Top + 0.3, 0.1), i / 30.0));

            var result = StrokeBuilder.Build(m, StrokeBuilder.Shape(stroke, m, ctx), ctx);

            Assert.True(result.Ok);
            Assert.Contains(m.Nodes, n => n.Position.Z < 0);             // the mirror copy went in too
            Assert.All(m.Lines, l => Assert.Equal((int)BridgeMaterial.Cable, m.LineSection(l.Id)));
            Assert.All(m.Members, x => Assert.Equal((int)BridgeMaterial.Cable, x.SectionId));
            double length = m.Lines.Sum(l => m.LineLength(l.Id));
            Assert.Equal(length * Constants.Budget.CableCostPerMeter, m.Spent, 9);

            // Later pieces start on dots earlier ones made, so take them out newest first.
            foreach (var line in m.Lines.Reverse().ToList())
            {
                double before = m.Spent;
                double refund = m.LineLength(line.Id) * Constants.Budget.CableCostPerMeter;
                Assert.True(m.TryDeleteLine(line.Id).Ok);
                Assert.Equal(before - refund, m.Spent, 9);
            }
            Assert.Equal(0, m.Spent, 9);
        }

        [Fact]
        public void Prices_RiseFromWoodToCableToConcrete()
        {
            Assert.True(Materials.CostPerMeter(BridgeMaterial.Wood) < Materials.CostPerMeter(BridgeMaterial.Cable));
            Assert.True(Materials.CostPerMeter(BridgeMaterial.Cable) < Materials.CostPerMeter(BridgeMaterial.Concrete));
            Assert.Equal(10, Constants.Budget.WoodCostPerMeter);
            Assert.Equal(25, Constants.Budget.CableCostPerMeter);
            Assert.Equal(50, Constants.Budget.ConcreteCostPerMeter);
            Assert.True(Materials.DeckCostPerSquareMeter(BridgeMaterial.Wood) < Materials.DeckCostPerSquareMeter(BridgeMaterial.Concrete));
            Assert.Equal(Materials.DeckCostPerSquareMeter(BridgeMaterial.Wood), Materials.DeckCostPerSquareMeter(BridgeMaterial.Cable));
        }

        // ---------- Materials table ----------

        [Fact]
        public void Materials_AreThree_InSectionIdOrder_WithNames()
        {
            Assert.Equal(new[] { BridgeMaterial.Wood, BridgeMaterial.Cable, BridgeMaterial.Concrete }, Materials.All);
            Assert.Equal(new[] { 0, 1, 2 }, Materials.Sections().Select(s => s.Id));
            Assert.All(Materials.All, x => Assert.Equal(x, Materials.Of((int)x)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Materials.Of(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => Materials.Of(-1));
            Assert.Equal(new[] { "Wood", "Steel cable", "Concrete" }, Materials.All.Select(Materials.Name));
        }

        [Theory]
        [InlineData(BridgeMaterial.Wood)]
        [InlineData(BridgeMaterial.Cable)]
        [InlineData(BridgeMaterial.Concrete)]
        public void EverySection_IsFiniteAndPositive(BridgeMaterial material)
        {
            var s = Materials.Section(material);
            Assert.Equal((int)material, s.Id);
            foreach (double v in new[] { s.E, s.G, s.A, s.Iy, s.Iz, s.J, s.YieldStrength, s.TensionStrength, s.CompressionStrength,
                                         s.Density, s.OuterFiberY, s.OuterFiberZ })
                Assert.True(double.IsFinite(v) && v > 0, $"{material}: {v}");
        }

        [Fact]
        public void Sections_HaveTheirMaterialsCharacter()
        {
            var wood = Materials.Section(BridgeMaterial.Wood);
            var cable = Materials.Section(BridgeMaterial.Cable);
            var concrete = Materials.Section(BridgeMaterial.Concrete);
            double Mass(SectionProperties s) => s.A * s.Density;
            double Bending(SectionProperties s) => s.YieldStrength * s.Iy / s.OuterFiberY;

            Assert.True(cable.TensionStrength > 100 * cable.CompressionStrength);              // a cable only pulls
            Assert.True(concrete.CompressionStrength > 4 * concrete.TensionStrength);          // concrete is for pushing
            Assert.True(Mass(concrete) > 10 * Mass(wood));                                     // and very heavy
            Assert.True(Bending(cable) < Bending(wood) / 10);                                  // a cable barely bends
        }

        // ---------- Decks ----------

        [Theory]
        [InlineData(BridgeMaterial.Wood, BridgeMaterial.Wood, BridgeMaterial.Wood)]
        [InlineData(BridgeMaterial.Cable, BridgeMaterial.Cable, BridgeMaterial.Wood)]
        [InlineData(BridgeMaterial.Concrete, BridgeMaterial.Concrete, BridgeMaterial.Concrete)]
        [InlineData(BridgeMaterial.Wood, BridgeMaterial.Cable, BridgeMaterial.Wood)]
        [InlineData(BridgeMaterial.Cable, BridgeMaterial.Wood, BridgeMaterial.Wood)]
        [InlineData(BridgeMaterial.Wood, BridgeMaterial.Concrete, BridgeMaterial.Concrete)]
        [InlineData(BridgeMaterial.Concrete, BridgeMaterial.Wood, BridgeMaterial.Concrete)]
        [InlineData(BridgeMaterial.Cable, BridgeMaterial.Concrete, BridgeMaterial.Concrete)]
        [InlineData(BridgeMaterial.Concrete, BridgeMaterial.Cable, BridgeMaterial.Concrete)]
        public void DeckMaterial_MapsCableToWood_ThenTakesThePricier(BridgeMaterial a, BridgeMaterial b, BridgeMaterial deck)
        {
            Assert.Equal(deck, Materials.DeckMaterial(a, b));
        }

        /// <summary>Two rails pad to pad in the given materials, like Rooms.BareDeck.</summary>
        static BridgeModel Rails(Pad[] pads, BridgeMaterial left, BridgeMaterial right)
        {
            var m = Rooms.Model();
            Rooms.Draw(m, pads, left, P(0, Top, -0.1), P(1, Top, -0.1));
            Rooms.Draw(m, pads, right, P(0, Top, 0.1), P(1, Top, 0.1));
            return m;
        }

        [Theory]
        [InlineData(BridgeMaterial.Wood, BridgeMaterial.Wood, BridgeMaterial.Wood)]
        [InlineData(BridgeMaterial.Cable, BridgeMaterial.Cable, BridgeMaterial.Wood)]
        [InlineData(BridgeMaterial.Wood, BridgeMaterial.Concrete, BridgeMaterial.Concrete)]
        [InlineData(BridgeMaterial.Concrete, BridgeMaterial.Cable, BridgeMaterial.Concrete)]
        public void CompiledDeck_ItsBracesAndRoadFill_CarryTheDeckMaterial(BridgeMaterial left, BridgeMaterial right, BridgeMaterial expected)
        {
            var pads = Rooms.Pads();
            var m = Rails(pads, left, right);
            var deck = Assert.Single(AutoDeck.Find(m));
            Assert.Equal((int)expected, deck.SectionId);

            var bridge = BridgeCompiler.Compile(m, pads, Scale);
            Assert.Equal(new[] { 0, 1, 2 }, bridge.Input.Sections.Select(s => s.Id));
            var g = Assert.Single(bridge.Decks);
            Assert.Equal((int)expected, g.SectionId);
            Assert.Equal((int)expected, Assert.Single(bridge.Input.Fills).SectionId);
            var braces = bridge.Input.Members.Where(x => x.HiddenBrace).ToList();
            Assert.NotEmpty(braces);
            Assert.All(braces, x => Assert.Equal((int)expected, x.SectionId));
            Assert.Equal(new[] { (int)left, (int)right }, m.Lines.Select(l => m.LineSection(l.Id)));
        }

        [Theory]
        [InlineData(BridgeMaterial.Wood)]
        [InlineData(BridgeMaterial.Cable)]
        [InlineData(BridgeMaterial.Concrete)]
        public void DeckCost_IsAreaTimesTheDeckMaterialsPrice(BridgeMaterial rails)
        {
            var pads = Rooms.Pads();
            var bridge = BridgeCompiler.Compile(Rails(pads, rails, rails), pads, Scale);
            var policy = BudgetPolicy.Default;
            double area = bridge.DeckArea;
            Assert.InRange(area, 0.15, 0.25);
            Assert.Equal(area * Materials.DeckCostPerSquareMeter(rails), bridge.DeckCost(policy), 9);
            Assert.Equal(area * 7, bridge.DeckCost(BudgetPolicy.Uniform(1, 7, 100, 100)), 9);
        }

        [Fact]
        public void PairedRoad_BeamsAndDeckFill_AreInTheDeckMaterial()
        {
            var pads = Rooms.Pads();
            var m = Rails(pads, BridgeMaterial.Cable, BridgeMaterial.Concrete);
            var roads = new RoadShapes(m);
            double rails = m.Spent;

            var result = roads.TryPair(m.Lines[0].Id, m.Lines[1].Id);

            Assert.True(result.Ok);
            var road = roads.Roads[0];
            Assert.Equal((int)BridgeMaterial.Concrete, m.GetFill(road.DeckFillId).SectionId);
            Assert.All(road.CrossBeams, id => Assert.Equal((int)BridgeMaterial.Concrete, m.LineSection(id)));
            double beams = road.CrossBeams.Sum(id => m.LineLength(id)) * Constants.Budget.ConcreteCostPerMeter;
            double deck = m.FillArea(m.GetFill(road.DeckFillId)) * Constants.Concrete.DeckCostPerSquareMeter;
            Assert.Equal(rails + beams + deck, m.Spent, 9);

            Assert.True(roads.TryDeleteRail(road.RailA).Ok);
            Assert.Equal(m.LineLength(road.RailB) * Constants.Budget.ConcreteCostPerMeter, m.Spent, 9);
        }

        // ---------- Capacity ----------

        [Fact]
        public void Concrete_CrushesMuchLaterThanItCracks()
        {
            var s = Materials.Section(BridgeMaterial.Concrete);
            const double force = 2e5;
            var pulled = MemberCapacity.Evaluate(new MemberForce { Axial = force }, s, 1);
            var pushed = MemberCapacity.Evaluate(new MemberForce { Axial = -force }, s, 1);

            Assert.Equal(FailureMode.Tension, pulled.Governing);
            Assert.Equal(FailureMode.Compression, pushed.Governing);
            Assert.Equal(force / (Constants.Concrete.TensionStrength * Constants.Concrete.Area), pulled.Utilization, 12);
            Assert.Equal(force / (Constants.Concrete.CompressionStrength * Constants.Concrete.Area), pushed.Utilization, 12);
            Assert.True(pulled.Utilization > 4 * pushed.Utilization);
        }

        [Fact]
        public void Cable_HoldsAHardPull_ButALightPushBreaksIt()
        {
            var s = Materials.Section(BridgeMaterial.Cable);
            Assert.False(MemberCapacity.Evaluate(new MemberForce { Axial = 1e5 }, s, 1).Failed);
            var pushed = MemberCapacity.Evaluate(new MemberForce { Axial = -1e4 }, s, 1);
            Assert.True(pushed.Failed);
            Assert.Equal(FailureMode.Compression, pushed.Governing);
            Assert.True(double.IsFinite(pushed.Utilization));
        }

        [Fact]
        public void Strengths_FallBackToYieldStrength_WhenUnset()
        {
            var s = new SectionProperties { YieldStrength = 7 };
            Assert.Equal(7, MemberCapacity.TensionStrength(s));
            Assert.Equal(7, MemberCapacity.CompressionStrength(s));
            s.TensionStrength = 3;
            s.CompressionStrength = 5;
            Assert.Equal(3, MemberCapacity.TensionStrength(s));
            Assert.Equal(5, MemberCapacity.CompressionStrength(s));

            // The single-strength overload is the split one with every strength equal.
            var one = MemberCapacity.Evaluate(-3e5, 2e3, 1e3, 2e11, 1e-6, 2e-6, 1e-3, 250e6, 0.01, 0.02, 0.5, 2);
            var split = MemberCapacity.Evaluate(-3e5, 2e3, 1e3, 2e11, 1e-6, 2e-6, 1e-3, 250e6, 250e6, 250e6, 0.01, 0.02, 0.5, 2);
            Assert.Equal(one.Utilization, split.Utilization);
            Assert.Equal(one.Governing, split.Governing);
        }

        /// <summary>A 6 m wood cantilever held at its tip by a cable from a support above (a tie) or below (a strut).</summary>
        static (FrameDynamics frame, Vec3[] loads) Propped(bool fromAbove)
        {
            var input = new SolverInput { ScaleFactor = Scale };
            input.Sections.AddRange(Materials.Sections());
            input.Nodes.Add(new Node { Id = 0, Position = Vec3.Zero, Fixed = true });
            input.Nodes.Add(new Node { Id = 1, Position = P(0.3, 0, 0) });
            input.Nodes.Add(new Node { Id = 2, Position = P(0, fromAbove ? 0.15 : -0.15, 0), Fixed = true });
            input.Members.Add(new Member { Id = 0, NodeA = 0, NodeB = 1, SectionId = (int)BridgeMaterial.Wood });
            input.Members.Add(new Member { Id = 1, NodeA = 2, NodeB = 1, SectionId = (int)BridgeMaterial.Cable });

            var frame = new FrameDynamics(input);
            frame.Rebuild(true);
            var loads = new Vec3[input.Nodes.Count];
            foreach (var w in DeadLoadPreview.SelfWeight(input, Constants.Loads.Gravity, Scale))
                loads[input.Nodes.FindIndex(n => n.Id == w.NodeId)] += w.Force;
            loads[1] += new Vec3(0, -Constants.Human.CarMassKg * Constants.Loads.Gravity, 0);
            return (frame, loads);
        }

        [Fact]
        public void CableTie_HoldsTheCantilever_ButACableStrutIsPushedAndBreaks()
        {
            var (tie, tieLoads) = Propped(fromAbove: true);
            tie.SolveStatic(tieLoads);
            Assert.All(tie.Utilization, u => Assert.True(double.IsFinite(u)));
            Assert.True(tie.Forces[1].Axial > 0);
            Assert.Empty(tie.RemoveFailures(Constants.Dynamics.MaxFailuresPerStep));

            var (strut, strutLoads) = Propped(fromAbove: false);
            strut.SolveStatic(strutLoads);
            Assert.All(strut.Utilization, u => Assert.True(double.IsFinite(u)));
            Assert.True(strut.Forces[1].Axial < 0);
            Assert.Contains(1, strut.RemoveFailures(Constants.Dynamics.MaxFailuresPerStep));
            Assert.False(strut.Active[1]);
            Assert.Contains(strut.BrokenMode[1], new[] { FailureMode.Compression, FailureMode.Buckling });
        }

        // ---------- Balance ----------

        /// <summary>
        /// The test truss with wood rails, top chord, end posts and verticals, and steel cable on the outer
        /// diagonals, which only ever pull.
        /// </summary>
        static BridgeModel MixedTruss(Pad[] pads) => Rooms.Truss(pads, BridgeMaterial.Wood, BridgeMaterial.Wood, BridgeMaterial.Wood,
                                                                 BridgeMaterial.Cable, BridgeMaterial.Wood);

        /// <summary>The same with every diagonal in cable, including the center V that the bridge's weight and the car push.</summary>
        static BridgeModel AllCableDiagonals(Pad[] pads) => Rooms.Truss(pads, BridgeMaterial.Wood, BridgeMaterial.Wood, BridgeMaterial.Wood,
                                                                        BridgeMaterial.Cable, BridgeMaterial.Cable);

        static void AssertFinite(BridgeRun run)
        {
            Assert.All(run.Coupled.Positions, p => Assert.True(p.IsFinite));
            Assert.All(run.Coupled.Frame.Utilization, u => Assert.True(double.IsFinite(u)));
            Assert.All(run.Coupled.Frame.Forces, f => Assert.True(double.IsFinite(f.Axial) && double.IsFinite(f.MomentLocalYAtA) && double.IsFinite(f.MomentLocalZAtA)));
            Assert.All(run.Coupled.Car.WheelLoads, l => Assert.True(double.IsFinite(l)));
            Assert.True(double.IsFinite(run.Coupled.Car.Speed));
        }

        BridgeRun Cross(string name, BridgeModel m, Pad[] pads)
        {
            var bridge = BridgeCompiler.Compile(m.Copy(), pads, Scale);
            var preview = DeadLoadPreview.Run(bridge.Input, new BandedStaticSolver(), null);
            Assert.All(preview.Solve.Displacements, d => Assert.True(d.IsFinite));
            Assert.All(preview.Utilizations, u => Assert.True(double.IsFinite(u)));

            var config = ChallengeConfig.Level().ToRunConfiguration(new StartSurfaceChoice(0, 1), pads[0].Across, Scale);
            var run = new BridgeRun(bridge, config, 0);
            while (run.Result == RunResult.Running && run.Time < 40)
            {
                run.Step(1.0 / 60);
                AssertFinite(run);
            }
            _out.WriteLine($"{name}: {run.Result} {run.Message} lost {run.MembersLost} peak {run.PeakUtilization:F2} t {run.Time:F1}s");
            return run;
        }

        [Fact]
        public void MixedTrusses_FitEveryDefaultBudget()
        {
            var pads = Rooms.Pads();
            double mixed = TotalCost(MixedTruss(pads), pads);
            double allCable = TotalCost(AllCableDiagonals(pads), pads);
            double wood = TotalCost(Rooms.Truss(pads), pads);
            _out.WriteLine($"mixed truss costs {mixed:F1}; with every diagonal in cable {allCable:F1}; all wood {wood:F1}; level budget {Constants.Phases.LevelBudget}");
            Assert.True(wood < mixed && mixed < allCable);
            foreach (var c in DefaultChallenges)
                Assert.True(allCable <= c.Budget, $"{c.Name}: {allCable:F1} > {c.Budget}");
        }

        [Fact]
        public void MixedTruss_CarriesTheCarAcross_WithNothingLost()
        {
            var pads = Rooms.Pads();
            var m = MixedTruss(pads);
            Assert.Contains(m.Members, x => x.SectionId == (int)BridgeMaterial.Cable);

            var run = Cross("mixed truss", m, pads);

            Assert.Equal(RunResult.Passed, run.Result);
            Assert.Equal(0, run.MembersLost);
            Assert.True(run.PeakUtilization < 0.8);
        }

        [Fact]
        public void CableCenterDiagonals_ArePushedAndSnap_ButTheCarStillCrosses()
        {
            var pads = Rooms.Pads();
            var run = Cross("every diagonal in cable", AllCableDiagonals(pads), pads);

            Assert.Equal(RunResult.Passed, run.Result);
            Assert.True(run.MembersLost > 0);
            var frame = run.Coupled.Frame;
            for (int k = 0; k < frame.Active.Length; k++)
            {
                if (frame.Active[k])
                    continue;
                Assert.Equal((int)BridgeMaterial.Cable, run.Bridge.Input.Members[k].SectionId);
                Assert.Contains(frame.BrokenMode[k], new[] { FailureMode.Compression, FailureMode.Buckling });
            }
        }

        /// <summary>
        /// The $1,000 budget leaves room to try every material, concrete included; concrete is still the costly
        /// choice, several times the price of the same truss in wood.
        /// </summary>
        [Fact]
        public void ConcreteTruss_FitsTheBudget_ButCostsSeveralTimesWood()
        {
            var pads = Rooms.Pads();
            var concrete = Rooms.Truss(pads, BridgeMaterial.Concrete);
            double cost = TotalCost(concrete, pads);
            double wood = TotalCost(Rooms.Truss(pads, BridgeMaterial.Wood), pads);
            _out.WriteLine($"concrete truss costs {cost:F1} (lines {concrete.Spent:F1}); wood {wood:F1}");
            Assert.All(concrete.Members, x => Assert.Equal((int)BridgeMaterial.Concrete, x.SectionId));
            Assert.All(DefaultChallenges, c => Assert.True(cost <= c.Budget, $"{c.Name}: {cost:F1} > {c.Budget}"));
            Assert.True(cost > 3 * wood, $"concrete {cost:F1} vs wood {wood:F1}");
        }
    }
}
