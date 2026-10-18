using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class CoupledStepTests
    {
        // Spy runs: a 1 x 1 room-unit road square at height 0.5, scale 2, so the bridge-world deck is
        // 2 x 2 at height 1 with fan triangles (1, 2, 3) and (1, 3, 4).
        private const double SpyScale = 2.0;
        private const double SpyDt = 0.02;
        private const int SpySteps = 6;
        private const int BraceId = 14;
        private const int SideMemberId = 11;
        private const double Tilt = 0.05;
        private const double FrontLoadPerCall = 100.0;
        private const double RearLoadExtra = 7.0;
        private const double LoadTolerance = 1e-9;
        private const double GeometryTolerance = 1e-12;

        // Real runs: two girders on fixed surface nodes with road fills, braces, and cross members.
        private const double Scale = 20.0;
        private const int SectionId = 1;
        private const int StartSurfaceId = 1;
        private const int FarSurfaceId = 2;
        private const int Panels = 5;
        private const double PanelLength = 0.1;
        private const double DeckHeight = 1.0;
        private const double DeckWidth = 0.2;
        private const double Grip = 0.8;
        private const double StartSpeed = 5.0;
        private const double StartX = -3.0;
        private const int RunSteps = 120;

        private static readonly double Dt = NewmarkIntegrator.DefaultTimestep;
        private static readonly int[] NoneFailed = Array.Empty<int>();
        private static readonly Dictionary<int, Vec3> NoWheelForces = new Dictionary<int, Vec3>();
        private static readonly Vec3 Headwind = new Vec3(-300.0, 0.0, 0.0);
        private static readonly Vec3 SideForce = new Vec3(3.0, 0.0, -2.0);

        [Fact]
        public void Coupling_UsesPreviousWheelLoads()
        {
            var log = new List<string>();
            var integrator = new SpyIntegrator(log, FlatDeck);
            var car = new SpyCar(log);
            var coupled = new CoupledStep(integrator, car, SpyStartCar());
            SolverInput room = SpyRoomInput();
            coupled.Reset(room, SpyConfiguration());
            NodalLoad[] selfWeight = DynamicSelfWeight.Loads(RoomBridgeAdapter.ToBridge(room), NoneFailed);

            WheelLoad[] previous = Array.Empty<WheelLoad>();
            for (int step = 1; step <= SpySteps; step++)
            {
                StepOutput output = coupled.Step(previous, SpyDt, () => false);

                Assert.Equal(step, integrator.StepLoads.Count);
                Assert.Equal(step, car.States.Count);
                NodalLoad[] received = integrator.StepLoads[step - 1];
                AssertLoads(selfWeight, step == 1 ? NoWheelForces : SpyWheelForces(step - 1, Vec3.UnitY), received, "Step " + step);
                Assert.NotEqual(selfWeight[IndexOf(room, 2)].Force.Y - FrontLoadPerCall * step, received[IndexOf(room, 2)].Force.Y);
                Assert.Same(car.Produced[step - 1], output.CarState);
                previous = output.CarState.WheelLoads;
            }

            Assert.Equal(Enumerable.Repeat(new[] { "bridge", "car" }, SpySteps).SelectMany(pair => pair), log);
        }

        [Fact]
        public void Coupling_ThenUpdatesCarFromNewDeck()
        {
            var log = new List<string>();
            var integrator = new SpyIntegrator(log, TiltedDeck);
            var car = new SpyCar(log);
            var coupled = new CoupledStep(integrator, car, SpyStartCar()) { ExternalForceBridge = SideForce };
            SolverInput room = SpyRoomInput();
            SolverInput bridge = RoomBridgeAdapter.ToBridge(room);
            coupled.Reset(room, SpyConfiguration());
            NodalLoad[] selfWeight = DynamicSelfWeight.Loads(bridge, NoneFailed);

            StepOutput first = coupled.Step(Array.Empty<WheelLoad>(), SpyDt, () => false);

            Assert.Equal(new[] { "bridge", "car" }, log);
            AssertSameState(SpyStartCar(), Assert.Single(car.States));
            CarParameters parameters = Assert.Single(car.Parameters);
            Assert.Equal(SpyParameters().WheelCount, parameters.WheelCount);
            Assert.Equal(SpyParameters().Track, parameters.Track);
            Assert.Equal(SideForce, Assert.Single(car.Forces));
            Assert.Equal(SpyDt, Assert.Single(car.Dts));
            AssertTiltedDeckUnderSpyCar(car.Decks[0], Tilt);

            Assert.Same(car.Produced[0], first.CarState);
            for (int i = 0; i < bridge.Nodes.Length; i++)
            {
                Vec3 expected = new Vec3(0.0, -Tilt * bridge.Nodes[i].Position.X, 0.0) / SpyScale;
                AssertClose(expected, first.Displacements[i], GeometryTolerance, "Room displacement of node " + bridge.Nodes[i].Id);
            }

            // The wheel loads go onto the deck as it stood after step 1, along its normal.
            StepOutput second = coupled.Step(first.CarState.WheelLoads, SpyDt, () => false);
            Vec3 normal = new Vec3(Tilt, 1.0, 0.0).Normalized();
            AssertLoads(selfWeight, SpyWheelForces(1, normal), integrator.StepLoads[1], "Step 2");
            Assert.Same(first.CarState, car.States[1]);
            AssertTiltedDeckUnderSpyCar(car.Decks[1], 2.0 * Tilt);
            Assert.Same(car.Produced[1], second.CarState);
        }

        [Fact]
        public void Coupling_AddsSelfWeight()
        {
            var log = new List<string>();
            var failures = new Dictionary<int, int[]> { [2] = new[] { BraceId }, [4] = new[] { SideMemberId } };
            var integrator = new SpyIntegrator(log, (input, step) => DeckOutput(input, 0.0, failures.TryGetValue(step, out int[] ids) ? ids : NoneFailed));
            var car = new SpyCar(log);
            var coupled = new CoupledStep(integrator, car, SpyStartCar());
            SolverInput room = SpyRoomInput();
            SolverInput bridge = RoomBridgeAdapter.ToBridge(room);
            coupled.Reset(room, SpyConfiguration());

            Assert.Equal(bridge.Nodes.Select(n => (n.Id, n.Position, n.Fixed)), integrator.ResetInput.Nodes.Select(n => (n.Id, n.Position, n.Fixed)));
            Assert.Equal(room.Members.Select(m => m.Id), integrator.ResetInput.Members.Select(m => m.Id));
            NodalLoad[] atRest = DynamicSelfWeight.Loads(bridge, NoneFailed);
            Assert.Equal(atRest.Select(l => (l.NodeId, l.Force, l.Moment)), integrator.ResetLoads.Select(l => (l.NodeId, l.Force, l.Moment)));
            Assert.All(integrator.ResetLoads, l => Assert.True(l.Force.Y < 0.0, "Node " + l.NodeId + " starts without its weight."));
            Assert.Empty(integrator.StepLoads);

            var failed = new List<int>();
            var selfWeights = new List<NodalLoad[]>();
            WheelLoad[] previous = Array.Empty<WheelLoad>();
            for (int step = 1; step <= SpySteps; step++)
            {
                StepOutput output = coupled.Step(previous, SpyDt, () => false);

                NodalLoad[] selfWeight = DynamicSelfWeight.Loads(bridge, failed);
                AssertLoads(selfWeight, step == 1 ? NoWheelForces : SpyWheelForces(step - 1, Vec3.UnitY), integrator.StepLoads[step - 1], "Step " + step);
                selfWeights.Add(selfWeight);
                Assert.Equal(failures.TryGetValue(step, out int[] reported) ? reported : NoneFailed, output.FailedMemberIds);
                failed.AddRange(output.FailedMemberIds);
                previous = output.CarState.WheelLoads;
            }

            // Steps 3 and 5 follow the failures, so their self-weight drops half of each removed member at its ends.
            double halfBrace = 0.5 * MemberWeight(bridge, BraceId);
            double halfSide = 0.5 * MemberWeight(bridge, SideMemberId);
            AssertWeightChange(room, selfWeights[1], selfWeights[2], new Dictionary<int, double> { [1] = halfBrace, [3] = halfBrace });
            AssertWeightChange(room, selfWeights[3], selfWeights[4], new Dictionary<int, double> { [2] = halfSide, [3] = halfSide });
        }

        [Fact]
        public void Coupling_CopyDoesNotMutateOriginal()
        {
            BridgeModel original = DeckModel();
            var nodesBefore = NodeRecords(original.Nodes);
            var membersBefore = MemberRecords(original.Members);
            var fillsBefore = original.Fills.Select(f => (f.Id, string.Join(",", f.NodeIds), f.Road, f.Valid)).ToArray();
            double spentBefore = original.SpentBudget;
            int undoBefore = original.UndoDepth;

            BridgeModel copy = original.Copy();
            SolverInput input = copy.ToSolverInput(DeckSections(), Scale);
            Vec3[] inputPositions = input.Nodes.Select(n => n.Position).ToArray();
            List<StepOutput> outputs = RealRun(input);

            Assert.Contains(outputs[outputs.Count - 1].Displacements, d => d != Vec3.Zero);
            Assert.Equal(nodesBefore, NodeRecords(original.Nodes));
            Assert.Equal(membersBefore, MemberRecords(original.Members));
            Assert.Equal(fillsBefore, original.Fills.Select(f => (f.Id, string.Join(",", f.NodeIds), f.Road, f.Valid)));
            Assert.Equal(spentBefore, original.SpentBudget);
            Assert.Equal(undoBefore, original.UndoDepth);
            Assert.Equal(nodesBefore, NodeRecords(copy.Nodes));
            Assert.Equal(membersBefore, MemberRecords(copy.Members));
            Assert.Equal(inputPositions, input.Nodes.Select(n => n.Position));
            Assert.Equal(membersBefore, MemberRecords(input.Members));
        }

        [Fact]
        public void Coupling_IsDeterministic()
        {
            BridgeModel original = DeckModel();
            List<StepOutput> first = RealRun(original.Copy().ToSolverInput(DeckSections(), Scale));
            List<StepOutput> second = RealRun(original.Copy().ToSolverInput(DeckSections(), Scale));

            Assert.Equal(RunSteps, first.Count);
            Assert.Equal(RunSteps, second.Count);
            for (int step = 0; step < RunSteps; step++)
            {
                StepOutput a = first[step];
                StepOutput b = second[step];
                Assert.Equal(a.Displacements, b.Displacements);
                Assert.Equal(a.Rotations, b.Rotations);
                Assert.Equal(a.FailedMemberIds, b.FailedMemberIds);
                Assert.Equal(a.MemberForces.Select(ForceRecord), b.MemberForces.Select(ForceRecord));
                AssertSameState(a.CarState, b.CarState);
            }
        }

        [Fact]
        public void Coupling_NoNaN()
        {
            List<StepOutput> outputs = RealRun(DeckModel().Copy().ToSolverInput(DeckSections(), Scale));

            foreach (StepOutput output in outputs)
            {
                Assert.All(output.Displacements, d => AssertFinite(d, "displacement"));
                Assert.All(output.Rotations, r => AssertFinite(r, "rotation"));
                Assert.NotEmpty(output.MemberForces);
                foreach (MemberForce force in output.MemberForces)
                {
                    Assert.All(ForceValues(force), v => Assert.True(double.IsFinite(v), "Member " + force.MemberId + " has a non-finite force " + v + "."));
                }

                CarState car = output.CarState;
                AssertFinite(car.Position, "car position");
                Assert.True(double.IsFinite(car.Speed), "Non-finite speed " + car.Speed + ".");
                Assert.True(double.IsFinite(car.VerticalVelocity), "Non-finite vertical velocity " + car.VerticalVelocity + ".");
                Assert.All(car.SuspensionCompression, c => Assert.True(double.IsFinite(c), "Non-finite compression " + c + "."));
                foreach (WheelLoad wheel in car.WheelLoads)
                {
                    Assert.True(double.IsFinite(wheel.NormalLoad), "Wheel " + wheel.WheelIndex + " has a non-finite load.");
                    AssertFinite(wheel.Position, "wheel contact");
                }
            }

            // The car has driven fully onto the deck and is carried by it.
            CarState last = outputs[outputs.Count - 1].CarState;
            Assert.True(last.Position.X > RealCar().Wheelbase, "The car stopped at x = " + last.Position.X + ".");
            Assert.True(last.WheelLoads.Sum(w => w.NormalLoad) > 0.0, "The car is not touching the deck.");
        }

        [Fact]
        public void Coupling_CancelFlagSkipsSolve()
        {
            var log = new List<string>();
            var integrator = new SpyIntegrator(log, FlatDeck);
            var car = new SpyCar(log);
            var coupled = new CoupledStep(integrator, car, SpyStartCar());
            SolverInput room = SpyRoomInput();
            coupled.Reset(room, SpyConfiguration());
            NodalLoad[] selfWeight = DynamicSelfWeight.Loads(RoomBridgeAdapter.ToBridge(room), NoneFailed);
            int polls = 0;
            Func<bool> cancel = () =>
            {
                polls++;
                return true;
            };

            Assert.Null(coupled.Step(Array.Empty<WheelLoad>(), SpyDt, cancel));
            Assert.Empty(integrator.StepLoads);
            Assert.Empty(car.States);

            StepOutput first = coupled.Step(Array.Empty<WheelLoad>(), SpyDt, () => false);
            for (int i = 0; i < 3; i++)
            {
                Assert.Same(first, coupled.Step(first.CarState.WheelLoads, SpyDt, cancel));
            }

            Assert.Equal(4, polls);
            Assert.Single(integrator.StepLoads);
            Assert.Single(car.States);
            Assert.Equal(new[] { "bridge", "car" }, log);

            StepOutput second = coupled.Step(first.CarState.WheelLoads, SpyDt, () => false);
            Assert.NotSame(first, second);
            Assert.Equal(2, integrator.StepLoads.Count);
            Assert.Same(first.CarState, car.States[1]);
            AssertLoads(selfWeight, SpyWheelForces(1, Vec3.UnitY), integrator.StepLoads[1], "Step after the cancels");
        }

        [Fact]
        public void Coupling_OffRoadWheelsStandOnLevelSurface()
        {
            var log = new List<string>();
            var integrator = new SpyIntegrator(log, TiltedDeck);
            var car = new SpyCar(log);
            CarState start = SpyStartCar();
            start.Position = new Vec3(-1.0, start.Position.Y, start.Position.Z);
            var coupled = new CoupledStep(integrator, car, start);
            SolverInput room = SpyRoomInput();
            coupled.Reset(room, SpyConfiguration());
            NodalLoad[] selfWeight = DynamicSelfWeight.Loads(RoomBridgeAdapter.ToBridge(room), NoneFailed);

            coupled.Step(Array.Empty<WheelLoad>(), SpyDt, () => false);

            // The nearest deck point is on the fixed edge x = 0, at the undisplaced deck height 1.
            DeckSample[] deck = car.Decks[0];
            Assert.Equal(new[] { 0, 1 }, deck.Select(s => s.WheelIndex));
            foreach (DeckSample sample in deck)
            {
                AssertClose(1.0, sample.Height, GeometryTolerance, "Off-road height of wheel " + sample.WheelIndex);
                Assert.Equal(0.0, sample.Slope);
                AssertClose(new Vec3(-1.0, 1.0, sample.WheelIndex == 0 ? 0.5 : 1.5), sample.Position, GeometryTolerance, "Off-road sample point");
            }

            var offRoad = new[] { new WheelLoad { WheelIndex = 0, NormalLoad = 500.0, Position = new Vec3(-1.0, 1.0, 0.5) } };
            coupled.Step(offRoad, SpyDt, () => false);
            AssertLoads(selfWeight, NoWheelForces, integrator.StepLoads[1], "Off-road wheel load");
        }

        [Fact]
        public void Coupling_RejectsInvalidUse()
        {
            var log = new List<string>();
            CarState start = SpyStartCar();
            Assert.Throws<ArgumentNullException>(() => new CoupledStep(null));
            Assert.Throws<ArgumentNullException>(() => new CoupledStep(null, new SpyCar(log), start));
            Assert.Throws<ArgumentNullException>(() => new CoupledStep(new SpyIntegrator(log, FlatDeck), null, start));
            Assert.Throws<ArgumentNullException>(() => new CoupledStep(new SpyIntegrator(log, FlatDeck), new SpyCar(log), null));
            Assert.Throws<ArgumentException>(() => new CoupledStep(start) { ExternalForceBridge = new Vec3(double.PositiveInfinity, 0.0, 0.0) });

            var coupled = new CoupledStep(new SpyIntegrator(log, FlatDeck), new SpyCar(log), start);
            Assert.Throws<InvalidOperationException>(() => coupled.Step(Array.Empty<WheelLoad>(), SpyDt, () => false));
            Assert.Throws<ArgumentNullException>(() => coupled.Reset(null, SpyConfiguration()));
            Assert.Throws<ArgumentNullException>(() => coupled.Reset(SpyRoomInput(), null));
            Assert.Throws<ArgumentException>(() => coupled.Reset(SpyRoomInput(), new RunConfiguration { Grip = Grip, ScaleFactor = SpyScale }));

            SolverInput notRoad = SpyRoomInput();
            notRoad.Fills[0].Road = false;
            Assert.Throws<ArgumentException>(() => coupled.Reset(notRoad, SpyConfiguration()));
            SolverInput invalid = SpyRoomInput();
            invalid.Fills[0].Valid = false;
            Assert.Throws<ArgumentException>(() => coupled.Reset(invalid, SpyConfiguration()));

            // A road fill that names a node missing from the input, as in a supported sub-input, is not part of the deck.
            SolverInput onlyMissing = SpyRoomInput();
            onlyMissing.Fills[0].NodeIds = new[] { 1, 2, 99 };
            Assert.Throws<ArgumentException>(() => coupled.Reset(onlyMissing, SpyConfiguration()));
            SolverInput withMissing = SpyRoomInput();
            withMissing.Fills = withMissing.Fills.Append(new Fill { Id = 2, NodeIds = new[] { 2, 3, 99 }, Road = true, Valid = true }).ToArray();
            coupled.Reset(withMissing, SpyConfiguration());
            Assert.Empty(log);

            coupled.Reset(SpyRoomInput(), SpyConfiguration());
            Assert.Throws<ArgumentNullException>(() => coupled.Step(null, SpyDt, () => false));
            Assert.Throws<ArgumentNullException>(() => coupled.Step(Array.Empty<WheelLoad>(), SpyDt, null));
            foreach (double dt in new[] { 0.0, -SpyDt, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => coupled.Step(Array.Empty<WheelLoad>(), dt, () => false));
            }

            Assert.Throws<ArgumentException>(() => coupled.Step(new WheelLoad[] { null }, SpyDt, () => false));
            Assert.Throws<ArgumentOutOfRangeException>(() => coupled.Step(new[] { new WheelLoad { NormalLoad = -1.0, Position = new Vec3(1, 1, 1) } }, SpyDt, () => false));
            Assert.Empty(log);
            Assert.NotNull(coupled.Step(Array.Empty<WheelLoad>(), SpyDt, () => false));
        }

        private static List<StepOutput> RealRun(SolverInput input)
        {
            var coupled = new CoupledStep(RealStartCar()) { ExternalForceBridge = Headwind };
            coupled.Reset(input, new RunConfiguration { StartSurfaceId = StartSurfaceId, Car = RealCar(), Grip = Grip, ScaleFactor = Scale });
            var outputs = new List<StepOutput>();
            WheelLoad[] previous = Array.Empty<WheelLoad>();
            for (int step = 0; step < RunSteps; step++)
            {
                StepOutput output = coupled.Step(previous, Dt, () => false);
                outputs.Add(output);
                previous = output.CarState.WheelLoads;
            }

            return outputs;
        }

        /// <summary>
        /// Two room-unit girders along X from fixed nodes on surface 1 to fixed nodes on surface 2, joined
        /// by cross members, with a road fill and a hidden diagonal brace over every panel.
        /// </summary>
        private static BridgeModel DeckModel()
        {
            var model = new BridgeModel();
            int[] left = Girder(model, 0.0);
            int[] right = Girder(model, DeckWidth);
            for (int k = 1; k < Panels; k++)
            {
                RequireSucceeded(model.TryAddLine(new[] { LineVertex.AtNode(left[k]), LineVertex.AtNode(right[k]) }, SectionId));
            }

            for (int k = 0; k < Panels; k++)
            {
                RequireSucceeded(model.TryAddFill(new[] { left[k], left[k + 1], right[k + 1], right[k] }, true));
                RequireSucceeded(model.TryAddHiddenBrace(left[k], right[k + 1], SectionId));
            }

            return model;
        }

        private static int[] Girder(BridgeModel model, double z)
        {
            int start = model.AddFixedNode(new Vec3(0.0, DeckHeight, z), StartSurfaceId);
            int end = model.AddFixedNode(new Vec3(Panels * PanelLength, DeckHeight, z), FarSurfaceId);
            var vertices = new List<LineVertex> { LineVertex.AtNode(start) };
            for (int k = 1; k < Panels; k++)
            {
                vertices.Add(LineVertex.AtPosition(new Vec3(k * PanelLength, DeckHeight, z)));
            }

            vertices.Add(LineVertex.AtNode(end));
            EditResult line = model.TryAddLine(vertices, SectionId);
            RequireSucceeded(line);
            return new[] { start }.Concat(line.NodeIds).Concat(new[] { end }).ToArray();
        }

        private static void RequireSucceeded(EditResult result)
        {
            Assert.True(result.Succeeded, "Building the test deck failed: " + result.Reason + ".");
        }

        private static SectionProperties[] DeckSections()
        {
            return new[] { Steel(SectionId) };
        }

        private static CarParameters RealCar()
        {
            return new CarParameters
            {
                MassKg = Constants.Human.CarMassKg,
                WheelCount = 4,
                Wheelbase = 2.5,
                Track = 1.5,
                SpringStiffness = 40000.0,
                DamperCoefficient = 3000.0,
                RestLength = 0.3,
            };
        }

        /// <summary>The real car on surface 1 behind the deck, centered on the road, with its struts at static compression.</summary>
        private static CarState RealStartCar()
        {
            CarParameters car = RealCar();
            double compression = car.MassKg * Constants.Loads.Gravity / (car.WheelCount * car.SpringStiffness);
            return new CarState
            {
                Position = new Vec3(StartX, DeckHeight * Scale + car.RestLength - compression, 0.5 * DeckWidth * Scale),
                Heading = Vec3.UnitX,
                Speed = StartSpeed,
            };
        }

        private static SolverInput SpyRoomInput()
        {
            return new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 1, Position = new Vec3(0, 0.5, 0), Fixed = true, SurfaceId = StartSurfaceId },
                    new Node { Id = 2, Position = new Vec3(1, 0.5, 0) },
                    new Node { Id = 3, Position = new Vec3(1, 0.5, 1) },
                    new Node { Id = 4, Position = new Vec3(0, 0.5, 1), Fixed = true, SurfaceId = StartSurfaceId },
                },
                Members = new[]
                {
                    new Member { Id = 10, NodeA = 1, NodeB = 2, SectionId = SectionId, LineId = 1 },
                    new Member { Id = SideMemberId, NodeA = 2, NodeB = 3, SectionId = SectionId, LineId = 2 },
                    new Member { Id = 12, NodeA = 3, NodeB = 4, SectionId = SectionId, LineId = 3 },
                    new Member { Id = 13, NodeA = 4, NodeB = 1, SectionId = SectionId, LineId = 4 },
                    new Member { Id = BraceId, NodeA = 1, NodeB = 3, SectionId = SectionId, HiddenBrace = true },
                },
                Fills = new[] { new Fill { Id = 1, NodeIds = new[] { 1, 2, 3, 4 }, Road = true, Valid = true } },
                Sections = new[] { Steel(SectionId) },
                ScaleFactor = SpyScale,
            };
        }

        private static CarParameters SpyParameters()
        {
            return new CarParameters
            {
                MassKg = 1000.0,
                WheelCount = 2,
                Wheelbase = 0.0,
                Track = 1.0,
                SpringStiffness = 1.0e4,
                DamperCoefficient = 1.0e3,
                RestLength = 0.3,
            };
        }

        private static RunConfiguration SpyConfiguration()
        {
            return new RunConfiguration { StartSurfaceId = StartSurfaceId, Car = SpyParameters(), Grip = Grip, ScaleFactor = SpyScale };
        }

        /// <summary>The spy car at bridge-world (1, 1.3, 1) heading +X, so its wheels are over (1, 0.5) and (1, 1.5).</summary>
        private static CarState SpyStartCar()
        {
            return new CarState { Position = new Vec3(1.0, 1.3, 1.0), Heading = Vec3.UnitX, Speed = 4.0 };
        }

        /// <summary>
        /// The state the spy car returns from its call number <paramref name="call"/>: wheel 0 presses on the
        /// deck over node 2, and wheel 1 over (1, 1.5), which is 1/4 node 1, 1/2 node 3, and 1/4 node 4.
        /// </summary>
        private static CarState SpyCarOutput(int call)
        {
            return new CarState
            {
                Position = new Vec3(1.0, 1.3, 1.0),
                Heading = Vec3.UnitX,
                Speed = 4.0 + call,
                WheelLoads = new[]
                {
                    new WheelLoad { WheelIndex = 0, NormalLoad = FrontLoadPerCall * call, Position = new Vec3(2.0, 1.0, 0.0) },
                    new WheelLoad { WheelIndex = 1, NormalLoad = FrontLoadPerCall * call + RearLoadExtra, Position = new Vec3(1.0, 1.0, 1.5) },
                },
                SuspensionCompression = new[] { 0.1, 0.1 },
            };
        }

        /// <summary>Nodal forces, by node id, of the wheel loads from <see cref="SpyCarOutput"/> on a deck with this upward normal.</summary>
        private static Dictionary<int, Vec3> SpyWheelForces(int call, Vec3 deckNormal)
        {
            double front = FrontLoadPerCall * call;
            double rear = FrontLoadPerCall * call + RearLoadExtra;
            return new Dictionary<int, Vec3>
            {
                [1] = deckNormal * (-0.25 * rear),
                [2] = deckNormal * -front,
                [3] = deckNormal * (-0.5 * rear),
                [4] = deckNormal * (-0.25 * rear),
            };
        }

        private static StepOutput FlatDeck(SolverInput bridge, int step)
        {
            return DeckOutput(bridge, 0.0, NoneFailed);
        }

        private static StepOutput TiltedDeck(SolverInput bridge, int step)
        {
            return DeckOutput(bridge, Tilt * step, NoneFailed);
        }

        /// <summary>A bridge-world step whose deck height falls by <paramref name="tilt"/> per meter of X.</summary>
        private static StepOutput DeckOutput(SolverInput bridge, double tilt, int[] failed)
        {
            return new StepOutput
            {
                Displacements = bridge.Nodes.Select(n => new Vec3(0.0, -tilt * n.Position.X, 0.0)).ToArray(),
                Rotations = new Vec3[bridge.Nodes.Length],
                MemberForces = bridge.Members.Where(m => !failed.Contains(m.Id)).Select(m => new MemberForce { MemberId = m.Id }).ToArray(),
                FailedMemberIds = failed,
            };
        }

        /// <summary>Both spy wheels at x = 1 on the plane y = 1 - tilt * x: height 1 - tilt, slope tilt.</summary>
        private static void AssertTiltedDeckUnderSpyCar(DeckSample[] deck, double tilt)
        {
            Assert.Equal(new[] { 0, 1 }, deck.Select(s => s.WheelIndex));
            foreach (DeckSample sample in deck)
            {
                double z = sample.WheelIndex == 0 ? 0.5 : 1.5;
                AssertClose(1.0 - tilt, sample.Height, GeometryTolerance, "Height under wheel " + sample.WheelIndex);
                AssertClose(tilt, sample.Slope, GeometryTolerance, "Slope under wheel " + sample.WheelIndex);
                AssertClose(new Vec3(1.0, 1.0 - tilt, z), sample.Position, GeometryTolerance, "Sample point of wheel " + sample.WheelIndex);
            }
        }

        private static void AssertLoads(NodalLoad[] selfWeight, Dictionary<int, Vec3> wheelForces, NodalLoad[] actual, string name)
        {
            Assert.Equal(selfWeight.Select(l => l.NodeId), actual.Select(l => l.NodeId));
            for (int i = 0; i < selfWeight.Length; i++)
            {
                wheelForces.TryGetValue(selfWeight[i].NodeId, out Vec3 wheel);
                AssertClose(selfWeight[i].Force + wheel, actual[i].Force, LoadTolerance, name + ", node " + selfWeight[i].NodeId);
                Assert.Equal(Vec3.Zero, actual[i].Moment);
            }
        }

        private static void AssertWeightChange(SolverInput room, NodalLoad[] before, NodalLoad[] after, Dictionary<int, double> lighterBy)
        {
            foreach (Node node in room.Nodes)
            {
                int i = IndexOf(room, node.Id);
                lighterBy.TryGetValue(node.Id, out double lighter);
                AssertClose(before[i].Force.Y + lighter, after[i].Force.Y, LoadTolerance, "Self-weight change on node " + node.Id);
            }
        }

        private static double MemberWeight(SolverInput bridge, int memberId)
        {
            Member member = bridge.Members.Single(m => m.Id == memberId);
            SectionProperties section = bridge.Sections.Single(s => s.Id == member.SectionId);
            double length = Vec3.Distance(bridge.Nodes.Single(n => n.Id == member.NodeA).Position, bridge.Nodes.Single(n => n.Id == member.NodeB).Position);
            return section.Density * section.A * length * Constants.Loads.Gravity;
        }

        private static int IndexOf(SolverInput input, int nodeId)
        {
            int index = Array.FindIndex(input.Nodes, n => n.Id == nodeId);
            Assert.True(index >= 0, "Node " + nodeId + " is not in the input.");
            return index;
        }

        private static SectionProperties Steel(int id)
        {
            return new SectionProperties
            {
                Id = id, E = 2.0e11, G = 7.7e10, A = 0.01, Iy = 1.0e-4, Iz = 1.0e-4, J = 2.0e-4,
                YieldStrength = 2.5e8, Density = 7850.0, Cy = 0.1, Cz = 0.1,
            };
        }

        private static (int, Vec3, bool, int?, bool)[] NodeRecords(IEnumerable<Node> nodes)
        {
            return nodes.Select(n => (n.Id, n.Position, n.Fixed, n.SurfaceId, n.GroundAnchor)).ToArray();
        }

        private static (int, int, int, int, int, bool)[] MemberRecords(IEnumerable<Member> members)
        {
            return members.Select(m => (m.Id, m.NodeA, m.NodeB, m.SectionId, m.LineId, m.HiddenBrace)).ToArray();
        }

        private static double[] ForceValues(MemberForce force)
        {
            return new[]
            {
                force.Axial, force.ShearLocalYAtA, force.ShearLocalZAtA, force.MomentLocalYAtA, force.MomentLocalZAtA,
                force.ShearLocalYAtB, force.ShearLocalZAtB, force.MomentLocalYAtB, force.MomentLocalZAtB, force.Utilization,
            };
        }

        private static string ForceRecord(MemberForce force)
        {
            return force.MemberId + ":" + string.Join(",", ForceValues(force).Select(v => v.ToString("R")));
        }

        private static void AssertSameState(CarState expected, CarState actual)
        {
            Assert.Equal(expected.Position, actual.Position);
            Assert.Equal(expected.Heading, actual.Heading);
            Assert.Equal(expected.Speed, actual.Speed);
            Assert.Equal(expected.VerticalVelocity, actual.VerticalVelocity);
            Assert.Equal(expected.SuspensionCompression, actual.SuspensionCompression);
            Assert.Equal(
                expected.WheelLoads.Select(w => (w.WheelIndex, w.NormalLoad, w.Position)),
                actual.WheelLoads.Select(w => (w.WheelIndex, w.NormalLoad, w.Position)));
        }

        private static void AssertFinite(Vec3 value, string name)
        {
            Assert.True(double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z), "Non-finite " + name + ": " + value + ".");
        }

        private static void AssertClose(double expected, double actual, double tolerance, string name)
        {
            Assert.True(
                Math.Abs(actual - expected) <= tolerance,
                name + ": expected " + expected.ToString("R") + " within " + tolerance + " but got " + actual.ToString("R") + ".");
        }

        private static void AssertClose(Vec3 expected, Vec3 actual, double tolerance, string name)
        {
            AssertClose(expected.X, actual.X, tolerance, name + ".X");
            AssertClose(expected.Y, actual.Y, tolerance, name + ".Y");
            AssertClose(expected.Z, actual.Z, tolerance, name + ".Z");
        }

        /// <summary>Records each call and returns the scripted bridge-world output for step n (from 1).</summary>
        private sealed class SpyIntegrator : IBridgeIntegrator
        {
            private readonly List<string> log;
            private readonly Func<SolverInput, int, StepOutput> script;

            public SpyIntegrator(List<string> log, Func<SolverInput, int, StepOutput> script)
            {
                this.log = log;
                this.script = script;
            }

            public SolverInput ResetInput { get; private set; }

            public NodalLoad[] ResetLoads { get; private set; }

            public List<NodalLoad[]> StepLoads { get; } = new List<NodalLoad[]>();

            public void Reset(SolverInput bridgeInput, NodalLoad[] initialLoads)
            {
                ResetInput = bridgeInput;
                ResetLoads = initialLoads;
                StepLoads.Clear();
            }

            public StepOutput Step(NodalLoad[] loads, double dt)
            {
                log.Add("bridge");
                StepLoads.Add(loads);
                return script(ResetInput, StepLoads.Count);
            }
        }

        /// <summary>Records what each call reads and returns <see cref="SpyCarOutput"/> for call n (from 1).</summary>
        private sealed class SpyCar : ICarModel
        {
            private readonly List<string> log;

            public SpyCar(List<string> log)
            {
                this.log = log;
            }

            public List<CarState> States { get; } = new List<CarState>();

            public List<CarParameters> Parameters { get; } = new List<CarParameters>();

            public List<DeckSample[]> Decks { get; } = new List<DeckSample[]>();

            public List<Vec3> Forces { get; } = new List<Vec3>();

            public List<double> Dts { get; } = new List<double>();

            public List<CarState> Produced { get; } = new List<CarState>();

            public CarState Step(CarState state, CarParameters parameters, DeckSample[] deck, Vec3 externalForceBridge, double dt)
            {
                log.Add("car");
                States.Add(state);
                Parameters.Add(parameters);
                Decks.Add(deck);
                Forces.Add(externalForceBridge);
                Dts.Add(dt);
                CarState next = SpyCarOutput(States.Count);
                Produced.Add(next);
                return next;
            }
        }
    }
}
