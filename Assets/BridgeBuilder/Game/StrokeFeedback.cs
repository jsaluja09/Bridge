using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// What a stroke turns into (W4-04). While the trigger is held, a ghost shows the shape the stroke resolves to
    /// (W4-01), already snapped parallel to its partner rail if it has one (W4-02). On release the resolved shape
    /// is built, not the raw stroke; a stroke that fits no shape becomes a straight beam between its ends, and
    /// only one too short to be a beam flashes red and fades. No HUD text here.
    /// </summary>
    public sealed class StrokeFeedback
    {
        const float GhostRefreshSeconds = 0.1f;   // GUESS: how often the ghost re-resolves while drawing
        const float RejectSeconds = 1f;           // GUESS: how long a rejected stroke takes to fade

        readonly List<Vector3> _rejected = new List<Vector3>();
        float _rejectedAt = float.NegativeInfinity;
        float _ghostAt = float.NegativeInfinity;
        int _ghostSamples;
        List<Vec3> _ghostPath;
        List<Vec3> _ghostMirror;      // where the mirror copy will go, or null

        /// <summary>The line the ghost is snapped to, or -1 (drawn highlighted so you see the pairing).</summary>
        public int PartnerLine { get; private set; } = -1;

        public void Clear()
        {
            _ghostPath = null;
            _ghostMirror = null;
            PartnerLine = -1;
            _ghostSamples = 0;
        }

        /// <summary>Resolves the stroke so far for the ghost (and its mirror copy), a few times a second.</summary>
        public void UpdateGhost(List<Vector3> stroke, BridgeModel model, MirrorPlane? mirror, float now)
        {
            if (now - _ghostAt < GhostRefreshSeconds || stroke.Count == _ghostSamples)
                return;
            _ghostAt = now;
            _ghostSamples = stroke.Count;
            var shape = Resolve(stroke, model, out int partner);
            _ghostPath = shape != null ? shape.Path(0.01) : null;
            _ghostMirror = _ghostPath != null && mirror.HasValue ? MirrorAssist.Mirror(_ghostPath, mirror.Value) : null;
            PartnerLine = partner;
        }

        /// <summary>
        /// The finished stroke as straight or curved pieces ready for FreeDraw: one piece for a line or an arc,
        /// one per side of a polyline so every corner lands exactly. Null only if the stroke is too short for a beam.
        /// </summary>
        public static List<List<Vec3>> Pieces(List<Vector3> stroke, BridgeModel model)
        {
            var shape = Resolve(stroke, model, out _);
            if (shape == null)
                return null;
            if (shape.Kind == ResolvedKind.Polyline)
                return Enumerable.Range(0, shape.Points.Count - 1).Select(i => new List<Vec3> { shape.Points[i], shape.Points[i + 1] }).ToList();
            return new List<List<Vec3>> { shape.Path(0.01) };
        }

        static ResolvedShape Resolve(List<Vector3> stroke, BridgeModel model, out int partner)
        {
            partner = -1;
            var shape = ShapeResolver.ResolveOrStraighten(stroke.Select(v => v.ToCore()).ToList());
            if (!shape.Valid)
                return null;
            var snapped = ParallelAssist.Snap(shape, model);
            partner = snapped.PartnerLineId;
            return snapped.Shape;
        }

        /// <summary>Shows a stroke that fits no shape in red, fading out.</summary>
        public void Reject(List<Vector3> stroke, float now)
        {
            _rejected.Clear();
            _rejected.AddRange(stroke);
            _rejectedAt = now;
            Clear();
        }

        static void DrawPath(BridgeView view, List<Vec3> path, float radius, Color color)
        {
            for (int i = 1; i < path.Count; i++)
                view.Tube(path[i - 1].ToUnity(), path[i].ToUnity(), radius, color);
        }

        public void Draw(BridgeView view, float radius, Color ghostColor, Color rejectColor, bool drawing, float now)
        {
            if (drawing && _ghostPath != null)
            {
                Color c = PartnerLine >= 0 ? ghostColor : Color.Lerp(ghostColor, Color.white, 0.5f);
                DrawPath(view, _ghostPath, radius * 0.6f, c);
                if (_ghostMirror != null)
                    DrawPath(view, _ghostMirror, radius * 0.5f, Color.Lerp(c, Color.white, 0.5f));
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
