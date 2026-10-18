using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static System.FormattableString;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// What the coach is told about one finished test (spec 11.7). The car, wind, grip, load and message are from
    /// the last phase that ran. Material is in budget price units: the lines plus the decks they make.
    /// </summary>
    public sealed class CoachFacts
    {
        public string ChallengeName { get; set; } = "";
        public string ChallengeBrief { get; set; } = "";
        public double CarWeightKg { get; set; }
        public double WindSpeed { get; set; }               // m/s
        public double Grip { get; set; }
        /// <summary>True when every phase held.</summary>
        public bool Passed { get; set; }
        public int Stars { get; set; }
        public int MembersLost { get; set; }
        public double PeakUtilization { get; set; }
        public double MaterialSpent { get; set; }
        public double Budget { get; set; }
        /// <summary>The run's own result line, such as "The car fell."</summary>
        public string Message { get; set; } = "";
        /// <summary>The run's failures (FailureMessages.Summarize), or null when nothing broke.</summary>
        public FailureSummary Failures { get; set; }
        /// <summary>The phases that ran, in order; empty for a single run.</summary>
        public IReadOnlyList<PhaseOutcome> Phases { get; set; } = Array.Empty<PhaseOutcome>();
    }

    /// <summary>
    /// The text sent to the online coach, and the cleanup of its reply (spec 11.7). The game layer does the request;
    /// this stays pure so it can be tested. Numbers are written the same in every culture.
    /// </summary>
    public static class CoachPrompt
    {
        public static readonly string SystemInstruction =
            "You are a friendly bridge-building coach for middle school students playing a mixed-reality bridge game. " +
            "After each test you get the facts about how it went. In simple, encouraging words, say why the bridge held or failed, " +
            "then give one concrete thing to try next. " +
            Invariant($"Use at most {Constants.Coach.MaxReplyWords} words. ") +
            "Write plain text only: no markdown, lists, headings, or emoji. " +
            "Use only the facts given, and never invent numbers, parts, or events.";

        /// <summary>The user prompt for one test.</summary>
        public static string Build(CoachFacts facts)
        {
            var lines = new List<string> { $"Challenge: {facts.ChallengeName}. {facts.ChallengeBrief}" };
            var phases = facts.Phases;
            var last = phases.Count > 0 ? phases[phases.Count - 1] : null;
            if (last != null)
            {
                lines.Add("Tests, in order: " + string.Join(" ", phases.Select(p => $"{p.Phase.Name}: {(p.Passed ? "held" : "FAILED")}.")));
                lines.Add($"Last test: {last.Phase.Name}. {last.Phase.Intro}");
            }
            lines.Add(Invariant($"Vehicle: {facts.CarWeightKg:0} kg. Wind: {facts.WindSpeed:0.#} m/s. Tire grip: {facts.Grip:0.##} (lower is more slippery)."));
            lines.Add(Invariant($"Result: {(facts.Passed ? "PASSED" : "FAILED")}, {facts.Stars} of {TestPhases.MaxStars} stars. {facts.Message}"));
            lines.Add(Invariant($"Pieces broken: {facts.MembersLost}. Highest load on any piece: {facts.PeakUtilization * 100:0}% of what it can hold (100% breaks it)."));
            lines.Add(Invariant($"Budget used: ${facts.MaterialSpent:0} of ${facts.Budget:0}, for lines and road decks."));
            var failures = facts.Failures;
            if (failures != null)
            {
                AddLine(lines, "What broke first: ", failures.Message.Reason);
                AddLine(lines, "", failures.MostCommonLine);
                AddLine(lines, "The game's tip: ", failures.Message.Hint);
            }
            if (last != null)
                AddLine(lines, "Advice for this test: ", last.Tip);
            lines.Add(facts.Passed
                ? "Explain why this bridge held, and suggest one thing to try next."
                : "Explain why this bridge failed, and suggest one thing to try next.");
            return string.Join("\n", lines);
        }

        /// <summary>
        /// The reply as one plain line for the HUD: the markdown marks TextMeshPro would show as-is (* # `) removed,
        /// whitespace collapsed, and cut to MaxReplyWords. "" for a null or empty reply.
        /// </summary>
        public static string CleanReply(string reply)
        {
            if (string.IsNullOrEmpty(reply))
                return "";
            var kept = new StringBuilder(reply.Length);
            foreach (char c in reply)
            {
                if (c != '*' && c != '#' && c != '`')
                    kept.Append(c);
            }
            string[] words = kept.ToString().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            int max = Constants.Coach.MaxReplyWords;
            if (words.Length <= max)
                return string.Join(" ", words);
            words[max - 1] = words[max - 1].TrimEnd('.', ',', ';', ':') + "...";
            return string.Join(" ", words, 0, max);
        }

        static void AddLine(List<string> lines, string label, string text)
        {
            if (!string.IsNullOrEmpty(text))
                lines.Add(label + text);
        }
    }
}
