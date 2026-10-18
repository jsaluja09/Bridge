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
            {
                Assert.Equal(p, LessonMotion.Displace(Bridge, tower, p, demo, 1.7));
                var side = new Vec3(Bridge.MainSpanStart - 10, Bridge.DeckY, 0);
                Assert.Equal(side, LessonMotion.Displace(Bridge, deck, side, demo, 1.7));
            }
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
}
