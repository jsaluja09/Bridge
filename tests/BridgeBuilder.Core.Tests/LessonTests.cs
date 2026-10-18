using System;
using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>Every lesson is well formed: steps readable, every reference real, geometry sane.</summary>
    public class LessonCatalogTests
    {
        public static IEnumerable<object[]> Each() => Lessons.All().Select((l, i) => new object[] { i, l.Title });

        static Lesson Get(int i) => Lessons.All()[i];

        [Theory]
        [MemberData(nameof(Each))]
        public void Lesson_HasSixToEightReadableSteps(int i, string title)
        {
            var lesson = Get(i);
            Assert.Equal(title, lesson.Title);
            Assert.InRange(lesson.Steps.Count, 6, 8);
            Assert.False(string.IsNullOrWhiteSpace(lesson.Place));
            Assert.True(lesson.Year > 1800 && lesson.RealLength > 100);
            foreach (var s in lesson.Steps)
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Title));
                Assert.InRange(s.Text.Length, 40, 400);
                Assert.False(string.IsNullOrWhiteSpace(s.Formula), $"step '{s.Title}' has no formula");
            }
        }

        [Theory]
        [MemberData(nameof(Each))]
        public void Lesson_RefersOnlyToRealPartsAndGroups(int i, string title)
        {
            var lesson = Get(i);
            var groups = lesson.Bridge.Parts.Select(p => p.Group).ToHashSet();
            var ids = lesson.Bridge.Parts.Select(p => p.Id).ToHashSet();
            foreach (var s in lesson.Steps)
            {
                Assert.All(s.Builds, g => Assert.Contains(g, groups));
                Assert.All(s.Highlight, id => Assert.Contains(id, ids));
                Assert.All(s.Labels, l => Assert.Contains(l.PartId, ids));
            }
            var built = lesson.Steps.SelectMany(s => s.Builds).ToList();
            Assert.Equal(built.Count, built.Distinct().Count());                  // each group built once
            Assert.All(groups, g => Assert.Contains(g, built));                    // and every group is built
            Assert.Equal(ids.Count, lesson.Bridge.Parts.Count);                    // ids are unique
            Assert.Contains(title, lesson.Bridge.Name);
        }

        [Theory]
        [MemberData(nameof(Each))]
        public void Bridge_IsFiniteAndFitsTheTable(int i, string title)
        {
            var b = Get(i).Bridge;
            foreach (var part in b.Parts)
            {
                Assert.True(part.Segments.Count + part.Faces.Count > 0, $"{title}: {part.Id} is empty");
                Assert.All(part.Segments, s => Assert.True(s.A.IsFinite && s.B.IsFinite));
                Assert.All(part.Faces, f => Assert.True(f.A.IsFinite && f.B.IsFinite && f.C.IsFinite));
                Assert.True(part.Radius >= 0);
            }
            var (min, max) = b.Bounds();
            Assert.Equal(1.5, (max.X - min.X) * b.FitScale(1.5), 9);
            Assert.True(b.MainSpan > 0 && b.MainSpanStart >= min.X && b.MainSpanEnd <= max.X);
            Assert.True(b.Parts.Sum(p => p.Segments.Count) < 3000, "too many tubes to draw every frame");
        }

        [Theory]
        [MemberData(nameof(Each))]
        public void Plots_AreFinite(int i, string title)
        {
            foreach (var plot in Get(i).Steps.Select(s => s.Plot).Where(p => p != null))
            {
                Assert.False(string.IsNullOrWhiteSpace(plot.Title), title);
                Assert.NotEmpty(plot.Series);
                Assert.All(plot.Series, s => Assert.All(s.points, p => Assert.True(double.IsFinite(p.x) && double.IsFinite(p.y))));
            }
        }
    }

    public class GoldenGateTests
    {
        static readonly Lesson GoldenGate = GoldenGateLesson.Build();

        [Fact]
        public void Roles_MatchWhatEachPartDoes()
        {
            var b = GoldenGate.Bridge;
            Assert.Equal(PartRole.Push, b.Part("towers").Role);
            Assert.Equal(PartRole.Pull, b.Part("cables").Role);
            Assert.Equal(PartRole.Pull, b.Part("hangers").Role);
            Assert.Equal(PartRole.Hold, b.Part("anchorages").Role);
            Assert.Equal(PartRole.Bend, b.Part("truss").Role);
        }

        [Fact]
        public void Cables_RunOverTheTowerTops_AndSagANinthOfTheSpan()
        {
            var b = GoldenGate.Bridge;
            var points = b.Part("cables").Segments.SelectMany(s => new[] { s.A, s.B }).ToList();
            Assert.Contains(points, p => Math.Abs(p.X - 343) < 1e-6 && Math.Abs(p.Y - 227) < 1e-6);
            Assert.Contains(points, p => Math.Abs(p.X - 1623) < 1e-6 && Math.Abs(p.Y - 227) < 1e-6);
            var middle = points.Where(p => Math.Abs(p.X - 983) < 1).Min(p => p.Y);
            Assert.Equal(227 - 1280 / 9.0, middle, 6);
        }

        [Fact]
        public void Hangers_AreVertical_FromRoadToCable()
        {
            var b = GoldenGate.Bridge;
            var hangers = b.Part("hangers").Segments;
            Assert.InRange(hangers.Count, 200, 300);
            Assert.All(hangers, h =>
            {
                Assert.Equal(h.A.X, h.B.X, 9);
                Assert.Equal(67, h.A.Y, 9);
                Assert.True(h.B.Y > h.A.Y);
            });
        }

        [Fact]
        public void CablePlot_IsTheParabola()
        {
            var plot = GoldenGate.Steps.Select(s => s.Plot).Single(p => p != null);
            var pts = plot.Series.Single().points;
            Assert.Equal(227, pts.First().y, 6);
            Assert.Equal(227, pts.Last().y, 6);
            Assert.Equal(227 - 1280 / 9.0, pts.Min(p => p.y), 6);
        }
    }

    public class LessonPlayerTests
    {
        static LessonPlayer Player() => new LessonPlayer(Lessons.All());

        [Fact]
        public void NextAndBack_StayInBounds()
        {
            var p = Player();
            Assert.False(p.Back(1));
            Assert.Equal(0, p.StepIndex);
            int steps = p.Lesson.Steps.Count;
            for (int i = 1; i < steps; i++)
                Assert.True(p.Next(i));
            Assert.True(p.AtLastStep);
            Assert.False(p.Next(99));
            Assert.Equal(steps - 1, p.StepIndex);
            Assert.True(p.Back(100));
            Assert.Equal(100, p.StepStartedAt);
        }

        [Fact]
        public void Groups_BuildInTheirStep_OneAfterAnother()
        {
            var p = Player();
            var lesson = p.Lesson;
            int step = lesson.Steps.FindIndex(s => s.Builds.Length >= 2);
            Assert.True(step > 0, "a lesson step builds two groups");
            int first = lesson.Steps[step].Builds[0], second = lesson.Steps[step].Builds[1];
            int earlier = lesson.Steps[0].Builds[0];
            int later = lesson.Steps.Skip(step + 1).First(s => s.Builds.Length > 0).Builds[0];

            for (int i = 0; i < step; i++)
                p.Next(0);
            Assert.Equal(1, p.Built(earlier, 0));
            Assert.Equal(0, p.Built(later, 100));
            Assert.Equal(0, p.Built(first, 0));
            Assert.Equal(0.5, p.Built(first, LessonPlayer.BuildSeconds / 2), 9);
            Assert.Equal(0, p.Built(second, LessonPlayer.BuildStagger));
            Assert.Equal(1, p.Built(second, LessonPlayer.BuildStagger + LessonPlayer.BuildSeconds));
            Assert.Equal(1, p.Built(12345, 0));                                   // no step builds it: always there

            p.Back(10);
            Assert.Equal(0, p.Built(first, 10 + LessonPlayer.BuildSeconds * 5));  // going back takes it away
        }

        [Fact]
        public void Choose_StartsALessonFromItsFirstStep()
        {
            var p = Player();
            p.Next(1);
            p.Choose(0, 5);
            Assert.Equal(0, p.StepIndex);
            Assert.Equal(5, p.StepStartedAt);
            p.Choose(999, 6);
            Assert.Equal(p.Lessons.Count - 1, p.LessonIndex);
        }

        [Fact]
        public void NoLessons_Throws() => Assert.Throws<ArgumentException>(() => new LessonPlayer(new Lesson[0]));
    }

    public class LessonMotionTests
    {
        static readonly LessonBridge Bridge = GoldenGateLesson.Build().Bridge;

        static Vec3 MidDeck => new Vec3((Bridge.MainSpanStart + Bridge.MainSpanEnd) / 2, Bridge.DeckY, 0);

        [Fact]
        public void RigidPartsAndSideSpans_NeverMove()
        {
            var tower = Bridge.Part("towers");
            var deck = Bridge.Part("deck");
            var p = MidDeck;
            foreach (LoadDemo demo in Enum.GetValues(typeof(LoadDemo)))
                Assert.Equal(p, LessonMotion.Displace(Bridge, tower, p, demo, 1.7));     // not flexible, no pivot
            var side = new Vec3(Bridge.MainSpanStart - 10, Bridge.DeckY, 0);
            foreach (var demo in new[] { LoadDemo.Cars, LoadDemo.Wind })                // these move the main span only
                Assert.Equal(side, LessonMotion.Displace(Bridge, deck, side, demo, 1.7));
        }

        [Fact]
        public void Traffic_SagsTheMiddleByTheRealAmountMagnified()
        {
            var deck = Bridge.Part("deck");
            double total = 0;
            int n = 0;
            for (double t = 0; t < LessonMotion.CrossingSeconds; t += 0.05, n++)
            {
                double drop = MidDeck.Y - LessonMotion.Displace(Bridge, deck, MidDeck, LoadDemo.Cars, t).Y;
                Assert.True(drop > 0);
                total += drop;
            }
            Assert.Equal(Bridge.LiveLoadSag * LessonMotion.Exaggeration, total / n, 1);
        }

        [Fact]
        public void Wind_SwaysSidewaysWithinTheRealAmountMagnified()
        {
            var deck = Bridge.Part("deck");
            double most = 0;
            for (double t = 0; t < 2 * LessonMotion.SwayPeriodSeconds; t += 0.05)
            {
                var moved = LessonMotion.Displace(Bridge, deck, MidDeck, LoadDemo.Wind, t);
                Assert.Equal(MidDeck.Y, moved.Y, 9);
                most = Math.Max(most, Math.Abs(moved.Z));
            }
            Assert.InRange(most, 0.95 * Bridge.WindSway * LessonMotion.Exaggeration, Bridge.WindSway * LessonMotion.Exaggeration + 1e-9);
        }

        [Fact]
        public void Cars_LoopOverTheMainSpan()
        {
            for (int c = 0; c < LessonMotion.Cars; c++)
            {
                for (double t = 0; t < 20; t += 0.3)
                    Assert.InRange(LessonMotion.CarAlong(Bridge, c, t), Bridge.MainSpanStart, Bridge.MainSpanEnd);
            }
            Assert.Equal(LessonMotion.CarAlong(Bridge, 2, 1.0), LessonMotion.CarAlong(Bridge, 2, 1.0 + LessonMotion.CrossingSeconds), 6);
        }
    }

    public class LessonStreakTests
    {
        static readonly LessonBridge Bridge = GoldenGateLesson.Build().Bridge;

        [Fact]
        public void Streaks_BlowAcrossTheMainSpan_InsideTheModel()
        {
            for (int i = 0; i < LessonMotion.Streaks; i++)
            {
                for (double t = 0; t < 10; t += 0.31)
                {
                    var (tail, head) = LessonMotion.Streak(Bridge, i, t);
                    Assert.InRange(head.X, Bridge.MainSpanStart, Bridge.MainSpanEnd);
                    Assert.Equal(tail.X, head.X);
                    Assert.Equal(tail.Y, head.Y);
                    Assert.True(head.Z >= tail.Z);                       // pointing downwind
                    Assert.InRange(head.Z - tail.Z, 0, 40 + 1e-9);
                    Assert.InRange(head.Z, -160, 160);
                }
            }
        }

        [Fact]
        public void Streaks_MoveDownwindAndRepeat()
        {
            var a = LessonMotion.Streak(Bridge, 3, 1.0).head;
            var b = LessonMotion.Streak(Bridge, 3, 1.1).head;
            Assert.True(b.Z > a.Z || b.Z < a.Z - 100);                      // moved on, or wrapped around
            Assert.Equal(a, LessonMotion.Streak(Bridge, 3, 1.0).head);
        }
    }

    public class MillauTests
    {
        static readonly LessonBridge Millau = MillauLesson.Build().Bridge;

        [Fact]
        public void EightSpans_SevenPiers_TallestAbout245m()
        {
            var piers = Millau.Part("piers").Segments;
            var feet = piers.Select(s => s.A).GroupBy(p => Math.Round(p.X)).Select(g => g.Min(p => p.Y)).ToList();
            Assert.Equal(7, feet.Count);
            Assert.Equal(270 - 245, feet.Min(), 6);
            var (min, max) = Millau.Bounds();
            Assert.Equal(2460, Millau.MainSpanEnd - Millau.MainSpanStart, 6);
            Assert.InRange(max.Y, 270 + 87 - 1e-6, 270 + 87 + 1e-6);              // the mast tops are the highest point
        }

        [Fact]
        public void Stays_LieInOnePlaneDownTheMiddle_ElevenPairsPerMast()
        {
            var stays = Millau.Part("stays").Segments;
            Assert.Equal(7 * 11 * 2, stays.Count);
            Assert.All(stays, s => Assert.True(s.A.Z == 0 && s.B.Z == 0));
            Assert.All(stays, s => Assert.Equal(270, s.B.Y, 9));                   // every stay holds the deck
        }

        [Fact]
        public void Heat_GrowsTheDeckFromItsMiddle_AndBendsOnlyThePierTops()
        {
            var deck = Millau.Part("deck");
            var piers = Millau.Part("piers");
            double hot = LessonMotion.DaySeconds / 4;                              // the warmest moment of the cycle
            var end = new Vec3(2460, 270, 0);
            var middle = new Vec3(1230, 270, 0);
            double grow = LessonMotion.Displace(Millau, deck, end, LoadDemo.Temperature, hot).X - end.X;
            double expected = 1230 * LessonMotion.SteelExpansion * LessonMotion.TemperatureSwing * LessonMotion.ThermalExaggeration;
            Assert.Equal(expected, grow, 6);
            Assert.Equal(middle, LessonMotion.Displace(Millau, deck, middle, LoadDemo.Temperature, hot));
            var foot = new Vec3(204, 270 - 94, 0);                                  // a pier's foot doesn't move
            Assert.Equal(foot, LessonMotion.Displace(Millau, piers, foot, LoadDemo.Temperature, hot));
            var top = new Vec3(204, 260, 0);                                        // 10 m below the deck: most of the way
            double moved = LessonMotion.Displace(Millau, piers, top, LoadDemo.Temperature, hot).X - top.X;
            Assert.True(moved < 0 && moved > -1026 * LessonMotion.SteelExpansion * 20 * 100);
        }

        [Fact]
        public void TemperatureChange_SwingsEitherSideOfAverage()
        {
            Assert.Equal(0, LessonMotion.TemperatureChange(0), 9);
            Assert.Equal(LessonMotion.TemperatureSwing, LessonMotion.TemperatureChange(LessonMotion.DaySeconds / 4), 9);
            Assert.Equal(-LessonMotion.TemperatureSwing, LessonMotion.TemperatureChange(3 * LessonMotion.DaySeconds / 4), 9);
        }
    }

    public class TowerBridgeTests
    {
        static readonly LessonBridge Tower = TowerBridgeLesson.Build().Bridge;

        [Fact]
        public void Dimensions_MatchTheRealBridge()
        {
            var (min, max) = Tower.Bounds();
            Assert.Equal(244, max.X - min.X - 60, 6);                              // the river runs 30 m past each end
            Assert.Equal(61, Tower.MainSpan, 6);
            Assert.Equal(65, Tower.Part("towers").Segments.Max(s => Math.Max(s.A.Y, s.B.Y)), 6);
        }

        [Fact]
        public void Leaves_PivotAtTheTowers_AndMeetInTheMiddle()
        {
            var north = Tower.Part("leaf-north");
            var south = Tower.Part("leaf-south");
            Assert.Equal(Tower.MainSpanStart, north.Pivot.Value.X, 9);
            Assert.Equal(Tower.MainSpanEnd, south.Pivot.Value.X, 9);
            double northTip = north.Segments.Max(s => Math.Max(s.A.X, s.B.X));
            double southTip = south.Segments.Min(s => Math.Min(s.A.X, s.B.X));
            Assert.Equal(northTip, southTip, 9);
            Assert.Equal(1, north.Turn);
            Assert.Equal(-1, south.Turn);
        }

        [Fact]
        public void Bascules_LiftBothTipsTo86Degrees_AndLowerThemAgain()
        {
            var north = Tower.Part("leaf-north");
            var south = Tower.Part("leaf-south");
            double open = LessonMotion.BasculeSeconds / 2;
            Assert.Equal(86, LessonMotion.BasculeAngle(open), 9);
            Assert.Equal(0, LessonMotion.BasculeAngle(0), 9);
            Assert.Equal(0, LessonMotion.BasculeAngle(LessonMotion.BasculeSeconds), 9);

            var tip = new Vec3(Tower.MainSpanStart + 30.5, Tower.DeckY, 0);
            var lifted = LessonMotion.Displace(Tower, north, tip, LoadDemo.Bascules, open);
            Assert.Equal(30.5, Vec3.Distance(lifted, north.Pivot.Value), 9);        // turns, doesn't stretch
            Assert.True(lifted.Y > Tower.DeckY + 30);
            var southTip = new Vec3(Tower.MainSpanEnd - 30.5, Tower.DeckY, 0);
            Assert.True(LessonMotion.Displace(Tower, south, southTip, LoadDemo.Bascules, open).Y > Tower.DeckY + 30);

            var counterweight = new Vec3(Tower.MainSpanStart - 6, Tower.DeckY, 0);   // behind the pivot: goes down
            Assert.True(LessonMotion.Displace(Tower, north, counterweight, LoadDemo.Bascules, open).Y < Tower.DeckY - 5);
        }

        [Fact]
        public void Walkways_AreTiesThatPull() =>
            Assert.Equal(PartRole.Pull, Tower.Part("walkways").Role);
    }
}
