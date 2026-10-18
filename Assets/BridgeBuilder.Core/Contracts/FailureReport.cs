using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>One member that failed, and why (W4-05). Utilizations are the ratios of each check at failure.</summary>
    public sealed class MemberFailure
    {
        public int MemberId { get; set; }
        public long Step { get; set; }
        public FailureMode Mode { get; set; }
        public double Utilization { get; set; }               // the governing check
        public double TensionUtilization { get; set; }
        public double CompressionUtilization { get; set; }
        public double BucklingUtilization { get; set; }
        public double BendingUtilization { get; set; }
        /// <summary>Unbraced (buckling) length over the radius of gyration of the weaker axis.</summary>
        public double Slenderness { get; set; }
        /// <summary>Where along the member it gave way: 0 at NodeA, 1 at NodeB.</summary>
        public double FailurePoint { get; set; }
    }

    /// <summary>
    /// Every member that failed in one step, worst first (W4-05). Built from the step's output and the
    /// MemberCapacity checks, so the governing mode matches the one the solver used to remove the member.
    /// </summary>
    public sealed class FailureReport
    {
        public long Step { get; set; }
        public List<MemberFailure> Failures { get; } = new List<MemberFailure>();

        /// <summary>
        /// Reports output.FailedMemberIds. MemberForces must hold each member's forces at failure (bridge-world
        /// newtons), indexed like input.Members. scale converts input positions to bridge-world meters.
        /// </summary>
        public static FailureReport Build(StepOutput output, SolverInput input, long step, double scale) =>
            Build(output, input, step, BucklingLengths.Compute(input, scale));

        /// <summary>As Build, with buckling lengths already computed (bridge-world meters, indexed like input.Members).</summary>
        public static FailureReport Build(StepOutput output, SolverInput input, long step, double[] bucklingLengths)
        {
            var report = new FailureReport { Step = step };
            var index = new Dictionary<int, int>();
            for (int k = 0; k < input.Members.Count; k++)
                index[input.Members[k].Id] = k;
            var sections = new Dictionary<int, SectionProperties>();
            foreach (var s in input.Sections)
                sections[s.Id] = s;

            foreach (int id in output.FailedMemberIds)
            {
                if (!index.TryGetValue(id, out int k))
                    throw new ArgumentException($"Failed member {id} is not in the input", nameof(output));
                var failure = Evaluate(output.MemberForces[k], sections[input.Members[k].SectionId], bucklingLengths[k]);
                failure.MemberId = id;
                failure.Step = step;
                report.Failures.Add(failure);
            }
            report.Failures.Sort(WorstFirst);
            return report;
        }

        /// <summary>Every check for one member force against its section.</summary>
        public static MemberFailure Evaluate(MemberForce force, SectionProperties section, double bucklingLength)
        {
            var s = section;
            double myA = Math.Abs(force.MomentLocalYAtA), myB = Math.Abs(force.MomentLocalYAtB);
            double mzA = Math.Abs(force.MomentLocalZAtA), mzB = Math.Abs(force.MomentLocalZAtB);
            var governing = MemberCapacity.Evaluate(force, s, bucklingLength);
            var bending = MemberCapacity.Evaluate(0, Math.Max(myA, myB), Math.Max(mzA, mzB), s.E, s.Iy, s.Iz, s.A,
                                                  s.YieldStrength, s.OuterFiberY, s.OuterFiberZ, bucklingLength);
            double r = Math.Sqrt(Math.Min(s.Iy, s.Iz) / s.A);

            var failure = new MemberFailure
            {
                Mode = governing.Governing,
                Utilization = governing.Utilization,
                TensionUtilization = Math.Max(force.Axial, 0) / (MemberCapacity.TensionStrength(s) * s.A),
                CompressionUtilization = Math.Max(-force.Axial, 0) / (MemberCapacity.CompressionStrength(s) * s.A),
                BucklingUtilization = governing.BucklingUtilization,
                BendingUtilization = bending.Utilization,
                Slenderness = bucklingLength / r,
                FailurePoint = FailurePoint(governing.Governing, myA * s.OuterFiberY / s.Iy, mzA * s.OuterFiberZ / s.Iz,
                                            myB * s.OuterFiberY / s.Iy, mzB * s.OuterFiberZ / s.Iz),
            };
            Check(failure);
            return failure;
        }

        /// <summary>
        /// Bending peaks at an end (end loads only, so the moment is linear along the member); axial load is the
        /// same all along, and a buckled member bows most at its middle.
        /// </summary>
        static double FailurePoint(FailureMode mode, double stressYA, double stressZA, double stressYB, double stressZB)
        {
            if (mode != FailureMode.Bending)
                return 0.5;
            return Math.Max(stressYA, stressZA) >= Math.Max(stressYB, stressZB) ? 0 : 1;
        }

        static int WorstFirst(MemberFailure x, MemberFailure y)
        {
            int c = y.Utilization.CompareTo(x.Utilization);
            return c != 0 ? c : x.MemberId.CompareTo(y.MemberId);
        }

        static void Check(MemberFailure f)
        {
            if (!double.IsFinite(f.Utilization) || !double.IsFinite(f.TensionUtilization) || !double.IsFinite(f.CompressionUtilization)
                || !double.IsFinite(f.BucklingUtilization) || !double.IsFinite(f.BendingUtilization)
                || !double.IsFinite(f.Slenderness) || !double.IsFinite(f.FailurePoint))
                throw new ArgumentOutOfRangeException(nameof(f), "Failure report value is NaN or Infinity");
        }
    }

    /// <summary>
    /// Watches a running frame and reports members as they fail (W4-05). A broken member's forces are left as they
    /// were at failure, so they are read straight from the frame after the step that removed it.
    /// </summary>
    public sealed class FailureTracker
    {
        readonly SolverInput _input;
        readonly double[] _bucklingLengths;
        readonly bool[] _reported;
        readonly StepOutput _output = new StepOutput();

        public FailureTracker(SolverInput input, double scale)
        {
            _input = input;
            _bucklingLengths = BucklingLengths.Compute(input, scale);
            _reported = new bool[input.Members.Count];
        }

        /// <summary>The members that broke since the last call, or null if none did.</summary>
        public FailureReport Observe(IFrameIntegrator frame, long step)
        {
            _output.FailedMemberIds.Clear();
            for (int k = 0; k < _reported.Length; k++)
            {
                if (!frame.Active[k] && !_reported[k])
                {
                    _reported[k] = true;
                    _output.FailedMemberIds.Add(_input.Members[k].Id);
                }
            }
            if (_output.FailedMemberIds.Count == 0)
                return null;
            _output.MemberForces = frame.Forces;
            return FailureReport.Build(_output, _input, step, _bucklingLengths);
        }
    }
}
