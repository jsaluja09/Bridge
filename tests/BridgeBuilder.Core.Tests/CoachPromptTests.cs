using System.Globalization;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>Spec 11.7: the coach's prompt text and the cleanup of its reply.</summary>
    public class CoachPromptTests
    {
        static CoachFacts Facts(bool passed, FailureSummary failures = null) => new CoachFacts
        {
            ChallengeName = "Icy and windy",
            ChallengeBrief = "Slippery tires and a 10 m/s wind.",
            CarWeightKg = 1500,
            WindSpeed = 10.5,
            Grip = 0.35,
            Passed = passed,
            Stars = passed ? 2 : 0,
            MembersLost = passed ? 0 : 3,
            PeakUtilization = passed ? 0.734 : 1,
            MaterialSpent = 12.4,
            Budget = 16,
            Message = passed ? "The car made it across!" : "The car fell.",
            Failures = failures,
        };

        static FailureSummary Summary(params FailureMode[] modes)
        {
            var report = new FailureReport();
            report.Failures.AddRange(modes.Select((mode, id) => new MemberFailure { MemberId = id, Mode = mode, Utilization = 1.2, Slenderness = 50 }));
            return FailureMessages.Summarize(new[] { report });
        }

        [Fact]
        public void SystemInstruction_AsksForShortPlainText()
        {
            Assert.Contains($"at most {Constants.Coach.MaxReplyWords} words", CoachPrompt.SystemInstruction);
            Assert.Contains("no markdown", CoachPrompt.SystemInstruction);
            Assert.Contains("Use only the facts given", CoachPrompt.SystemInstruction);
        }

        [Fact]
        public void PassAndFail_UseDifferentVerdicts()
        {
            string pass = CoachPrompt.Build(Facts(true));
            string fail = CoachPrompt.Build(Facts(false, Summary(FailureMode.Bending)));

            Assert.Contains("Result: PASSED, 2 of 3 stars. The car made it across!", pass);
            Assert.Contains("why this bridge held", pass);
            Assert.DoesNotContain("FAILED", pass);
            Assert.Contains("Result: FAILED, 0 of 3 stars. The car fell.", fail);
            Assert.Contains("why this bridge failed", fail);
            Assert.DoesNotContain("PASSED", fail);
        }

        [Fact]
        public void Phases_ListWhatHeldAndDescribeTheLastOne()
        {
            var phases = TestPhases.All;
            var facts = Facts(false);
            facts.Phases = new[]
            {
                new PhaseOutcome { Phase = phases[0], Passed = true, Tip = "Solid." },
                new PhaseOutcome { Phase = phases[1], Passed = false, Message = "The car fell.", Tip = "Brace the two sides together." },
            };
            string prompt = CoachPrompt.Build(facts);

            Assert.Contains($"Tests, in order: {phases[0].Name}: held. {phases[1].Name}: FAILED.", prompt);
            Assert.Contains($"Last test: {phases[1].Name}. {phases[1].Intro}", prompt);
            Assert.Contains("Advice for this test: Brace the two sides together.", prompt);
            Assert.DoesNotContain("Solid.", prompt);
            Assert.DoesNotContain(phases[2].Name, prompt);
        }

        [Fact]
        public void NoPhases_LeavesOutThePhaseLines()
        {
            string prompt = CoachPrompt.Build(Facts(true));

            Assert.DoesNotContain("Tests, in order", prompt);
            Assert.DoesNotContain("Last test", prompt);
            Assert.DoesNotContain("Advice for this test", prompt);
        }

        [Fact]
        public void FailureSummary_ReasonTipAndCountAppear()
        {
            var summary = Summary(FailureMode.Buckling, FailureMode.Buckling);
            string prompt = CoachPrompt.Build(Facts(false, summary));

            Assert.Contains("What broke first: " + summary.Message.Reason, prompt);
            Assert.Contains("The game's tip: " + summary.Message.Hint, prompt);
            Assert.Contains("All 2 broken pieces buckled.", prompt);
        }

        [Fact]
        public void NoFailureSummary_LeavesOutTheFailureLines()
        {
            string prompt = CoachPrompt.Build(Facts(false));

            Assert.DoesNotContain("What broke first", prompt);
            Assert.DoesNotContain("The game's tip", prompt);
            Assert.Contains("Pieces broken: 3.", prompt);
            Assert.Contains("Challenge: Icy and windy. Slippery tires and a 10 m/s wind.", prompt);
        }

        [Fact]
        public void Numbers_AreWrittenTheSameInEveryCulture()
        {
            var before = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                string invariant = CoachPrompt.Build(Facts(true));
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal("10,5", 10.5.ToString(CultureInfo.CurrentCulture));
                string german = CoachPrompt.Build(Facts(true));

                Assert.Equal(invariant, german);
                Assert.Contains("Vehicle: 1500 kg. Wind: 10.5 m/s. Tire grip: 0.35", german);
                Assert.Contains("Highest load on any piece: 73%", german);
                Assert.Contains("Budget used: $12 of $16, for lines and road decks.", german);
                Assert.DoesNotContain("10,5", german);
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
            }
        }

        [Fact]
        public void CleanReply_StripsMarkdownAndCollapsesWhitespace()
        {
            Assert.Equal("Great job! Try a triangle under the road. Tip: use shorter pieces.",
                CoachPrompt.CleanReply("  **Great job!**\n\nTry `a triangle`   under the road.\r\n# Tip: use\tshorter pieces.  "));
        }

        [Fact]
        public void CleanReply_CapsTheWordCount()
        {
            int max = Constants.Coach.MaxReplyWords;
            string exact = string.Join(" ", Enumerable.Range(1, max).Select(i => $"w{i}"));
            Assert.Equal(exact, CoachPrompt.CleanReply(exact));

            string clean = CoachPrompt.CleanReply(string.Join(" ", Enumerable.Range(1, max + 10).Select(i => $"word{i}.")));
            string[] words = clean.Split(' ');
            Assert.Equal(max, words.Length);
            Assert.Equal($"word{max}...", words.Last());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   \n\t ")]
        [InlineData("** ## ``")]
        public void CleanReply_NothingToShow_IsEmpty(string reply) => Assert.Equal("", CoachPrompt.CleanReply(reply));
    }
}
