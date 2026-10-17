using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class FailureMessagesTests
    {
        static readonly double Slender = Constants.FailureFeedback.SlendernessMessageThreshold * 2;
        static readonly double Stocky = Constants.FailureFeedback.SlendernessMessageThreshold / 2;

        static MemberFailure Failure(int id, FailureMode mode, long step = 0, double utilization = 1.2, double slenderness = 50) =>
            new MemberFailure { MemberId = id, Mode = mode, Step = step, Utilization = utilization, Slenderness = slenderness };

        static FailureReport Report(long step, params MemberFailure[] failures)
        {
            var report = new FailureReport { Step = step };
            report.Failures.AddRange(failures);
            return report;
        }

        [Theory]
        [InlineData(FailureMode.Buckling, "buckled", "triangle brace")]
        [InlineData(FailureMode.Bending, "bent", "support")]
        [InlineData(FailureMode.Tension, "pulled apart", "Share the load")]
        [InlineData(FailureMode.Compression, "crushed", "share the push")]
        public void EachMode_GetsItsMessageAndHint(FailureMode mode, string reason, string hint)
        {
            foreach (double slenderness in new[] { Slender, Stocky })
            {
                var m = FailureMessages.For(Failure(0, mode, slenderness: slenderness));
                Assert.Contains(reason, m.Reason);
                Assert.Contains(hint, m.Hint);
                Assert.StartsWith(m.Reason, m.Text);
                Assert.EndsWith(m.Hint, m.Text);
            }
        }

        [Fact]
        public void Buckling_Slender_MatchesTheIssueWording()
        {
            var m = FailureMessages.For(Failure(0, FailureMode.Buckling, slenderness: Slender));
            Assert.Equal("This beam was too long and thin. When it was squeezed, it bent sideways and buckled. Try shorter pieces or a triangle brace.", m.Text);
        }

        [Theory]
        [InlineData(FailureMode.Buckling)]
        [InlineData(FailureMode.Bending)]
        public void TooLong_OnlyAboveTheSlendernessThreshold(FailureMode mode)
        {
            Assert.Contains("too long", FailureMessages.For(Failure(0, mode, slenderness: Slender)).Text);
            Assert.DoesNotContain("too long", FailureMessages.For(Failure(0, mode, slenderness: Stocky)).Text);
            Assert.DoesNotContain("too long", FailureMessages.For(
                Failure(0, mode, slenderness: Constants.FailureFeedback.SlendernessMessageThreshold)).Text);
        }

        [Theory]
        [InlineData(FailureMode.Tension)]
        [InlineData(FailureMode.Compression)]
        public void AxialModes_NeverSayTooLong(FailureMode mode) =>
            Assert.DoesNotContain("too long", FailureMessages.For(Failure(0, mode, slenderness: Slender)).Text);

        [Fact]
        public void NoFailures_SummarizesToNull()
        {
            Assert.Null(FailureMessages.Summarize(new List<FailureReport>()));
            Assert.Null(FailureMessages.Summarize(new[] { Report(4) }));
        }

        [Fact]
        public void SingleFailure_HasNoCountLine()
        {
            var s = FailureMessages.Summarize(new[] { Report(2, Failure(5, FailureMode.Tension)) });
            Assert.Equal(5, s.First.MemberId);
            Assert.Equal(FailureMode.Tension, s.MostCommon);
            Assert.Equal(1, s.Count);
            Assert.Equal("", s.MostCommonLine);
            Assert.Equal(s.Message.Text, s.Text);
        }

        [Fact]
        public void ManyFailures_ReportFirstAndMostCommon()
        {
            var reports = new[]
            {
                Report(10, Failure(1, FailureMode.Buckling), Failure(2, FailureMode.Buckling)),
                Report(4, Failure(9, FailureMode.Tension, utilization: 1.1), Failure(8, FailureMode.Bending, utilization: 1.5)),
                Report(12, Failure(3, FailureMode.Buckling)),
            };
            var s = FailureMessages.Summarize(reports);

            Assert.Equal(8, s.First.MemberId);                        // earliest step, then worst
            Assert.Equal(FailureMode.Buckling, s.MostCommon);
            Assert.Equal(5, s.Count);
            Assert.Equal("Most of the 5 broken pieces buckled.", s.MostCommonLine);
            Assert.StartsWith(FailureMessages.For(s.First).Text, s.Text);
        }

        [Fact]
        public void AllOneMode_SaysAll()
        {
            var s = FailureMessages.Summarize(new[] { Report(1, Failure(1, FailureMode.Bending), Failure(2, FailureMode.Bending)) });
            Assert.Equal("All 2 broken pieces bent and broke.", s.MostCommonLine);
        }

        [Fact]
        public void Ties_GoToTheFirstFailuresMode_ThenTheLowerMode()
        {
            var tieWithFirst = FailureMessages.Summarize(new[]
            {
                Report(1, Failure(1, FailureMode.Bending)),
                Report(2, Failure(2, FailureMode.Tension)),
            });
            Assert.Equal(FailureMode.Bending, tieWithFirst.MostCommon);

            var tieWithoutFirst = FailureMessages.Summarize(new[]
            {
                Report(1, Failure(1, FailureMode.Bending)),
                Report(2, Failure(2, FailureMode.Buckling), Failure(3, FailureMode.Buckling)),
                Report(3, Failure(4, FailureMode.Tension), Failure(5, FailureMode.Tension)),
            });
            Assert.Equal(FailureMode.Tension, tieWithoutFirst.MostCommon);
        }

        [Fact]
        public void Summary_IsDeterministic_RegardlessOfOrder()
        {
            var failures = new[]
            {
                Failure(4, FailureMode.Tension, step: 3, utilization: 1.3),
                Failure(2, FailureMode.Bending, step: 3, utilization: 1.3),
                Failure(7, FailureMode.Buckling, step: 5),
                Failure(1, FailureMode.Tension, step: 6),
            };
            var forward = FailureMessages.Summarize(failures.Select(f => Report(f.Step, f)));
            var backward = FailureMessages.Summarize(failures.Reverse().Select(f => Report(f.Step, f)));

            Assert.Equal(2, forward.First.MemberId);                  // same step and load: lower id
            Assert.Equal(forward.First.MemberId, backward.First.MemberId);
            Assert.Equal(forward.MostCommon, backward.MostCommon);
            Assert.Equal(forward.Text, backward.Text);
        }
    }
}
