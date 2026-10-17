using System.Collections.Generic;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// How members fail on screen (W4-07). Before failure a squeezed member bows toward buckling and pulses near
    /// its limit (spec 2.10). At failure it flashes red and splits at the failure point into two halves; each half
    /// hangs from its end joint and swings down under gravity. Joints that come loose ride the existing debris
    /// simulation (their positions arrive in the snapshot), so a half on a falling piece, and a deck on a failed
    /// rail, fall with it. The W4-06 message goes to HudMessages. Everything is timed by simulation time, which
    /// advances in the solver's fixed dt, never by the wall clock.
    /// </summary>
    public sealed class FailureFx : MonoBehaviour
    {
        [Tooltip("Simulation seconds a broken member flashes red.")]
        [SerializeField] float flashSeconds = 0.6f;                 // GUESS
        [Tooltip("Seconds the failure message stays on the HUD.")]
        [SerializeField] float messageSeconds = HudMessages.DefaultSeconds;
        [SerializeField] Color flashColor = new Color(1f, 0.1f, 0.1f);
        [SerializeField] Color brokenColor = new Color(0.5f, 0.5f, 0.55f);
        [Tooltip("Share of a swinging half's speed kept each solver step.")]
        [Range(0.9f, 1f)] [SerializeField] float swingDamping = 0.995f;  // GUESS

        const float MaxBowFraction = 0.15f;   // GUESS: a bow never exceeds this share of the member's length
        const int BowSegments = 6;
        const float PulseHz = 1.6f;           // GUESS
        const float FlashHz = 6f;             // GUESS

        sealed class Half
        {
            public int Pivot;                 // node index the half hangs from
            public float Length;
            public Vector3 Tip, PreviousTip;
        }

        sealed class Broken
        {
            public float BrokeAt;             // simulation seconds
            public Half A, B;
        }

        readonly List<Broken> _broken = new List<Broken>();
        readonly List<FailureReport> _reports = new List<FailureReport>();
        readonly List<FailureReport> _pending = new List<FailureReport>();
        readonly Dictionary<int, int> _memberIndex = new Dictionary<int, int>();
        CompiledBridge _bridge;
        float _floor;
        float _time;                          // simulation seconds, from the snapshot
        float _integrated;                    // simulation seconds the halves have been stepped to
        string _shown;

        static float Dt => (float)(1.0 / Constants.Dynamics.SolverRateHz);
        static float Gravity => (float)(Constants.Loads.Gravity / Constants.Scale.RoomToBridge);   // room m/s^2, DERIVED

        /// <summary>The run's failures so far for the HUD (W4-06), or null while nothing has failed.</summary>
        public FailureSummary Summary { get; private set; }

        public void Begin(CompiledBridge bridge, float floorY)
        {
            Clear();
            _bridge = bridge;
            _floor = floorY;
            for (int k = 0; k < bridge.Input.Members.Count; k++)
                _memberIndex[bridge.Input.Members[k].Id] = k;
        }

        public void Clear()
        {
            _broken.Clear();
            _reports.Clear();
            _pending.Clear();
            _memberIndex.Clear();
            _bridge = null;
            _time = _integrated = 0f;
            _shown = null;
            Summary = null;
        }

        /// <summary>Takes this frame's snapshot and any new failure reports, and steps the halves.</summary>
        public void Advance(RunSnapshot snap, List<FailureReport> newReports)
        {
            if (_bridge == null)
                return;
            _pending.AddRange(newReports);
            if (snap == null || snap.Bridge != _bridge)
                return;                       // hold reports until this run's first snapshot arrives
            _time = (float)snap.Time;

            foreach (var report in _pending)
            {
                _reports.Add(report);
                foreach (var f in report.Failures)
                    Split(snap, f);
            }
            if (_pending.Count > 0)
                UpdateMessage();
            _pending.Clear();

            // Fixed steps up to the simulation time.
            while (_integrated + Dt <= _time)
            {
                _integrated += Dt;
                foreach (var b in _broken)
                {
                    Swing(b.A, snap);
                    Swing(b.B, snap);
                }
            }
        }

        void Split(RunSnapshot snap, MemberFailure f)
        {
            int k = _memberIndex[f.MemberId];
            var m = _bridge.Input.Members[k];
            if (m.HiddenBrace)
                return;                       // deck braces aren't drawn; their triangles hide instead
            int a = _bridge.NodeIndex[m.NodeA], b = _bridge.NodeIndex[m.NodeB];
            Vector3 pa = snap.Positions[a].ToUnity(), pb = snap.Positions[b].ToUnity();
            Vector3 at = Vector3.Lerp(pa, pb, (float)f.FailurePoint);
            _broken.Add(new Broken
            {
                BrokeAt = (float)(f.Step * Dt),
                A = NewHalf(a, pa, at),
                B = NewHalf(b, pb, at),
            });
        }

        static Half NewHalf(int pivot, Vector3 from, Vector3 tip)
        {
            float length = Vector3.Distance(from, tip);
            return length < 1e-4f ? null : new Half { Pivot = pivot, Length = length, Tip = tip, PreviousTip = tip };
        }

        /// <summary>One Verlet step of a half hanging from its joint, kept off the floor.</summary>
        void Swing(Half h, RunSnapshot snap)
        {
            if (h == null)
                return;
            Vector3 pivot = snap.Positions[h.Pivot].ToUnity();
            Vector3 velocity = (h.Tip - h.PreviousTip) * swingDamping;
            h.PreviousTip = h.Tip;
            h.Tip += velocity + Vector3.down * (Gravity * Dt * Dt);
            h.Tip = pivot + (h.Tip - pivot).normalized * h.Length;
            if (h.Tip.y < _floor)
                h.Tip.y = _floor;
        }

        void UpdateMessage()
        {
            Summary = FailureMessages.Summarize(_reports);
            if (Summary == null || Summary.Text == _shown)
                return;
            _shown = Summary.Text;
            HudMessages.Show(_shown, messageSeconds);
        }

        float Wave(float hz) => 0.5f + 0.5f * Mathf.Sin(_time * hz * Mathf.PI * 2f);

        /// <summary>
        /// Draws a standing member: bowed sideways by its buckling utilization and pulsing near failure.
        /// </summary>
        public void Member(BridgeView view, Vector3 a, Vector3 b, float radius, Color color, float utilization, float buckling)
        {
            if (FeedbackMapping.Pulse(utilization))
                color = Color.Lerp(color, Color.white, Wave(PulseHz) * 0.6f);
            float length = Vector3.Distance(a, b);
            float bow = Mathf.Min((float)FeedbackMapping.BowAmplitude(Mathf.Clamp01(buckling)), length * MaxBowFraction);
            if (bow < 1e-4f)
            {
                view.Tube(a, b, radius, color, utilization);
                return;
            }
            Vector3 axis = (b - a) / length;
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 previous = a;
            for (int i = 1; i <= BowSegments; i++)
            {
                float s = i / (float)BowSegments;
                Vector3 p = Vector3.Lerp(a, b, s) + side * (bow * Mathf.Sin(Mathf.PI * s));
                view.Tube(previous, p, radius, color, utilization);
                previous = p;
            }
        }

        /// <summary>Draws every broken member's two halves: red and flashing at first, then grey.</summary>
        public void DrawBroken(BridgeView view, RunSnapshot snap, float radius, float crackRadius)
        {
            if (_bridge == null || snap == null || snap.Bridge != _bridge)
                return;
            foreach (var b in _broken)
            {
                float age = _time - b.BrokeAt;
                bool flashing = age < flashSeconds;
                Color c = flashing ? Color.Lerp(flashColor, Color.white, Wave(FlashHz) * 0.4f) : brokenColor;
                DrawHalf(view, b.A, snap, radius, c, flashing, crackRadius);
                DrawHalf(view, b.B, snap, radius, c, flashing, crackRadius);
            }
        }

        static void DrawHalf(BridgeView view, Half h, RunSnapshot snap, float radius, Color c, bool crack, float crackRadius)
        {
            if (h == null)
                return;
            view.Tube(snap.Positions[h.Pivot].ToUnity(), h.Tip, radius, c);
            if (crack)
                view.Dot(h.Tip, crackRadius, c);
        }
    }
}
