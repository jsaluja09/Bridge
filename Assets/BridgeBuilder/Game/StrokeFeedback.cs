using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// What a stroke turns into (W4-04, spec 11.1, 11.6). FreeDraw shapes the stroke; when that shape is a line, arc or
    /// polyline drawn beside a matching line at road spacing, the exact shifted copy of that line is built instead
    /// (W4-02). While the trigger is held, a ghost shows what will be built and the line it copies is highlighted. A
    /// rejected stroke flashes red and fades.
    /// </summary>
    public sealed class StrokeFeedback
    {
        const float GhostRefreshSeconds = 0.1f;   // GUESS: how often the ghost re-resolves while drawing
        const float RejectSeconds = 1f;           // GUESS: how long a rejected stroke takes to fade

        readonly List<Vector3> _rejected = new List<Vector3>();
        float _rejectedAt = float.NegativeInfinity;
        float _ghostAt = float.NegativeInfinity;
        int _ghostSamples;
        readonly List<List<Vec3>> _ghost = new List<List<Vec3>>();

        /// <summary>The line the ghost copies, or -1 (drawn highlighted so you see the pairing).</summary>
        public int PartnerLine { get; private set; } = -1;

        public void Clear()
        {
            _ghost.Clear();
            PartnerLine = -1;
            _ghostSamples = 0;
        }

        /// <summary>Works out what the stroke so far would build, a few times a second.</summary>
        public void UpdateGhost(List<Vector3> stroke, FreeDraw.Context ctx, BridgeModel model, float now)
        {
            if (now - _ghostAt < GhostRefreshSeconds || stroke.Count == _ghostSamples)
                return;
            _ghostAt = now;
            _ghostSamples = stroke.Count;
            _ghost.Clear();
            var chains = Shape(stroke, ctx, model, out _, out var copy);
            PartnerLine = copy != null ? copy.PartnerLineId : -1;
            if (copy != null)
                _ghost.Add(copy.Shape.Path(0.01));
            else
                _ghost.AddRange(chains.Select(c => c.Select(v => v.Position).ToList()));
        }

        /// <summary>
        /// FreeDraw's chains for the stroke (none, with the reason, if it was rejected), and the copy to build instead
        /// when it was drawn beside a matching line, else null.
        /// </summary>
        public static List<List<DrawVertex>> Shape(List<Vector3> stroke, FreeDraw.Context ctx, BridgeModel model, out RejectionReason reason, out ParallelResult copy)
        {
            copy = null;
            var chains = FreeDraw.Build(stroke.Select(v => v.ToCore()).ToList(), ctx, out var kind, out reason);
            if (chains.Count != 1 || (kind != StrokeKind.Line && kind != StrokeKind.Arc && kind != StrokeKind.Polyline))
                return chains;
            var snapped = ParallelAssist.Snap(ShapeResolver.Resolve(chains[0].Select(v => v.Position).ToList()), model);
            if (snapped.Snapped)
                copy = snapped;
            return chains;
        }

        /// <summary>A snapped shape as pieces for FreeDraw: one per side of a polyline so every corner lands exactly, else one.</summary>
        public static List<List<Vec3>> Pieces(ResolvedShape shape)
        {
            if (shape.Kind == ResolvedKind.Polyline)
                return Enumerable.Range(0, shape.Points.Count - 1).Select(i => new List<Vec3> { shape.Points[i], shape.Points[i + 1] }).ToList();
            return new List<List<Vec3>> { shape.Path(0.01) };
        }

        /// <summary>Shows a rejected stroke in red, fading out.</summary>
        public void Reject(List<Vector3> stroke, float now)
        {
            _rejected.Clear();
            _rejected.AddRange(stroke);
            _rejectedAt = now;
            Clear();
        }

        public void Draw(BridgeView view, float radius, Color ghostColor, Color rejectColor, bool drawing, float now)
        {
            if (drawing)
            {
                Color c = PartnerLine >= 0 ? ghostColor : Color.Lerp(ghostColor, Color.white, 0.5f);
                foreach (var path in _ghost)
                {
                    for (int i = 1; i < path.Count; i++)
                        view.Tube(path[i - 1].ToUnity(), path[i].ToUnity(), radius * 0.6f, c);
                }
            }

            float age = now - _rejectedAt;
            if (age < RejectSeconds && _rejected.Count > 1)
            {
                float fade = 1f - age / RejectSeconds;
                for (int i = 2; i < _rejected.Count; i += 2)
                    view.Tube(_rejected[i - 2], _rejected[i], radius * 0.7f * fade, rejectColor);
            }
        }
    }
}
