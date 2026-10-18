using System.Collections.Generic;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// How members fail on screen (W4-07). Before failure a squeezed member bows toward buckling and pulses near
    /// its limit (spec 2.10). At failure it flashes red and splits at the failure point into two halves that
    /// tumble down; a torn piece of deck drops as a plate. Pieces land, lie on the floor for a moment, then
    /// shrink away, and so do the parts the solver drops as debris (CollapseEffects does the falling). The W4-06
    /// message goes to HudMessages. Everything is timed by simulation time, never by the wall clock.
    /// </summary>
    public sealed class FailureFx : MonoBehaviour
    {
        [Tooltip("Simulation seconds a new piece flashes red.")]
        [SerializeField] float flashSeconds = 0.6f;                 // GUESS
        [Tooltip("Seconds the failure message stays on the HUD.")]
        [SerializeField] float messageSeconds = HudMessages.DefaultSeconds;
        [SerializeField] Color flashColor = new Color(1f, 0.1f, 0.1f);
        [SerializeField] Color brokenColor = new Color(0.62f, 0.58f, 0.52f);
        [SerializeField] Color deckColor = new Color(0.3f, 0.3f, 0.33f);
        [Tooltip("How much the flash and broken colors cover a falling piece's material.")]
        [Range(0f, 1f)] [SerializeField] float pieceTint = 0.75f;   // GUESS

        const float MaxBowFraction = 0.15f;   // GUESS: a bow never exceeds this share of the member's length
        const int BowSegments = 6;
        const float PulseHz = 1.6f;           // GUESS
        const float FlashHz = 6f;             // GUESS

        readonly List<FailureReport> _reports = new List<FailureReport>();
        readonly List<FailureReport> _pending = new List<FailureReport>();
        CompiledBridge _bridge;
        CollapseEffects _effects;
        float _time;                          // simulation seconds, from the snapshot
        string _shown;

        /// <summary>The run's failures so far for the HUD (W4-06), or null while nothing has failed.</summary>
        public FailureSummary Summary { get; private set; }

        public void Begin(CompiledBridge bridge, float floorY)
        {
            Clear();
            _bridge = bridge;
            _effects = new CollapseEffects(bridge, floorY, Constants.Scale.RoomToBridge);
        }

        public void Clear()
        {
            _reports.Clear();
            _pending.Clear();
            _bridge = null;
            _effects = null;
            _time = 0f;
            _shown = null;
            Summary = null;
        }

        /// <summary>Takes this frame's snapshot and any new failure reports, and steps the falling pieces.</summary>
        public void Advance(RunSnapshot snap, List<FailureReport> newReports)
        {
            if (_bridge == null)
                return;
            _pending.AddRange(newReports);
            if (snap == null || snap.Bridge != _bridge)
                return;                       // hold reports until this run's first snapshot arrives
            _time = (float)snap.Time;
            _effects.Update(snap.Time, snap.Positions, snap.State, _pending);
            if (_pending.Count > 0)
            {
                _reports.AddRange(_pending);
                UpdateMessage();
            }
            _pending.Clear();
        }

        /// <summary>1 for joints in the bridge or still falling; down to 0 as fallen debris shrinks away.</summary>
        public float NodeFade(int node) => _effects == null ? 1f : (float)_effects.NodeFade(node);

        /// <summary>True once this deck triangle tore off and falls as a piece instead.</summary>
        public bool Torn(int deck, int triangleStart) => _effects != null && _effects.Torn(deck, triangleStart);

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
        /// Draws a standing member in its material, the load color tinted over it: bowed sideways by its buckling
        /// utilization and pulsing near failure.
        /// </summary>
        public void Member(BridgeView view, Vector3 a, Vector3 b, BridgeMaterial material, Color color, float tint, float utilization, float buckling)
        {
            if (FeedbackMapping.Pulse(utilization))
                color = Color.Lerp(color, Color.white, Wave(PulseHz) * 0.6f);
            float length = Vector3.Distance(a, b);
            float bow = Mathf.Min((float)FeedbackMapping.BowAmplitude(Mathf.Clamp01(buckling)), length * MaxBowFraction);
            if (bow < 1e-4f)
            {
                view.Beam(a, b, material, color, tint, utilization);
                return;
            }
            Vector3 axis = (b - a) / length;
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 previous = a;
            float along = 0f;
            for (int i = 1; i <= BowSegments; i++)
            {
                float s = i / (float)BowSegments;
                Vector3 p = Vector3.Lerp(a, b, s) + side * (bow * Mathf.Sin(Mathf.PI * s));
                view.Beam(previous, p, material, color, tint, utilization, 1f, along);
                along += Vector3.Distance(previous, p);
                previous = p;
            }
        }

        /// <summary>Draws every falling piece: red and flashing when new, then broken grey over its material (or deck grey).</summary>
        public void DrawPieces(BridgeView view)
        {
            if (_effects == null)
                return;
            foreach (var f in _effects.Fragments)
            {
                bool flashing = _time - f.BornAt < flashSeconds;
                Color settled = f.Plate ? deckColor : brokenColor;
                Color c = flashing ? Color.Lerp(flashColor, Color.white, Wave(FlashHz) * 0.4f) : settled;
                if (f.Plate)
                {
                    view.Triangle(f.Point(0).ToUnity(), f.Point(1).ToUnity(), f.Point(2).ToUnity(), c);
                    continue;
                }
                var material = Materials.Of(_bridge.Input.Members[f.SourceMember].SectionId);
                view.Beam(f.Point(0).ToUnity(), f.Point(1).ToUnity(), material, c, pieceTint, 0f, (float)f.Scale);
            }
        }
    }
}
