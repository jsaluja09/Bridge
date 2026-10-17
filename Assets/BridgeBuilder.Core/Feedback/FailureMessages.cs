using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>What a student is told about one failure: why it broke, and what to try.</summary>
    public readonly struct FailureMessage
    {
        public readonly string Reason;
        public readonly string Hint;

        public FailureMessage(string reason, string hint)
        {
            Reason = reason;
            Hint = hint;
        }

        public string Text => Hint.Length > 0 ? $"{Reason} {Hint}" : Reason;
    }

    /// <summary>A run's failures boiled down for the HUD: the first one, and the mode most of them shared.</summary>
    public sealed class FailureSummary
    {
        public MemberFailure First { get; set; }
        public FailureMode MostCommon { get; set; }
        public int Count { get; set; }
        public FailureMessage Message { get; set; }
        /// <summary>"Most of the 5 broken pieces buckled." (or "All 5 ..."); empty when only one piece broke.</summary>
        public string MostCommonLine { get; set; } = "";

        public string Text => MostCommonLine.Length > 0 ? $"{Message.Text}\n{MostCommonLine}" : Message.Text;
    }

    /// <summary>
    /// Failure messages in middle-school language (W4-06): why the physics would not allow the member, plus a fix.
    /// "Too long" wording is used only for slender members (Constants.FailureFeedback.SlendernessMessageThreshold).
    /// </summary>
    public static class FailureMessages
    {
        public static FailureMessage For(MemberFailure failure) =>
            For(failure.Mode, failure.Slenderness > Constants.FailureFeedback.SlendernessMessageThreshold);

        public static FailureMessage For(FailureMode mode, bool slender) => mode switch
        {
            FailureMode.Buckling when slender => new FailureMessage(
                "This beam was too long and thin. When it was squeezed, it bent sideways and buckled.",
                "Try shorter pieces or a triangle brace."),
            FailureMode.Buckling => new FailureMessage(
                "This beam was squeezed so hard that it bent sideways and buckled.",
                "Add a triangle brace to keep it straight."),
            FailureMode.Bending when slender => new FailureMessage(
                "This span was too long with nothing under it, so it bent until it broke.",
                "Add a support or a truss."),
            FailureMode.Bending => new FailureMessage(
                "This piece was bent too far and broke.",
                "Add a support under it, or a triangle to share the load."),
            FailureMode.Tension => new FailureMessage(
                "This piece was pulled apart.",
                "Share the load with another member."),
            FailureMode.Compression => new FailureMessage(
                "This piece was squeezed until it crushed.",
                "Add another member beside it to share the push."),
            _ => new FailureMessage("", ""),
        };

        /// <summary>How a group of pieces failed, for "Most of the 5 broken pieces ___."</summary>
        public static string Verb(FailureMode mode) => mode switch
        {
            FailureMode.Buckling => "buckled",
            FailureMode.Bending => "bent and broke",
            FailureMode.Tension => "were pulled apart",
            FailureMode.Compression => "were crushed",
            _ => "broke",
        };

        /// <summary>
        /// The first failure (earliest step, then worst, then lowest member id) and the most common mode (ties go
        /// to the first failure's mode, then to the lower mode). Null if nothing failed.
        /// </summary>
        public static FailureSummary Summarize(IEnumerable<FailureReport> reports)
        {
            MemberFailure first = null;
            var counts = new Dictionary<FailureMode, int>();
            int total = 0;
            foreach (var report in reports)
            {
                foreach (var f in report.Failures)
                {
                    if (first == null || Earlier(f, first))
                        first = f;
                    counts.TryGetValue(f.Mode, out int c);
                    counts[f.Mode] = c + 1;
                    total++;
                }
            }
            if (first == null)
                return null;

            // Modes in a fixed order; only a strictly higher count displaces the current pick.
            var common = first.Mode;
            int best = counts[common];
            foreach (var mode in Modes)
            {
                if (counts.TryGetValue(mode, out int c) && c > best)
                {
                    common = mode;
                    best = c;
                }
            }

            var summary = new FailureSummary { First = first, MostCommon = common, Count = total, Message = For(first) };
            if (total > 1)
                summary.MostCommonLine = counts.Count == 1
                    ? $"All {total} broken pieces {Verb(common)}."
                    : $"Most of the {total} broken pieces {Verb(common)}.";
            return summary;
        }

        static readonly FailureMode[] Modes = { FailureMode.Tension, FailureMode.Compression, FailureMode.Buckling, FailureMode.Bending };

        static bool Earlier(MemberFailure a, MemberFailure b)
        {
            if (a.Step != b.Step)
                return a.Step < b.Step;
            if (a.Utilization != b.Utilization)
                return a.Utilization > b.Utilization;
            return a.MemberId < b.MemberId;
        }
    }
}
