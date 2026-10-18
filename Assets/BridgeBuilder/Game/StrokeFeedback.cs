using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// What a stroke turns into (W4-04, spec 11.1, 11.6), as StrokeBuilder.Shape reads it: FreeDraw's shape (a squiggle
    /// straightened), or the exact shifted copy of a matching line it was drawn beside (W4-02). While the trigger is
    /// held, a ghost shows what will be built, its mirror copy across the road, and the line it copies highlighted. A
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
        readonly List<List<Vec3>> _mirrorGhost = new List<List<Vec3>>();

        /// <summary>The line the ghost copies, or -1 (drawn highlighted so you see the pairing).</summary>
        public int PartnerLine { get; private set; } = -1;

        public void Clear()
        {
            _ghost.Clear();
            _mirrorGhost.Clear();
            PartnerLine = -1;
            _ghostSamples = 0;
        }

        /// <summary>Works out what the stroke so far would build, a few times a second.</summary>
        public void UpdateGhost(List<Vector3> stroke, BridgeModel model, StrokeBuilder.Context ctx, float now)
        {
            if (now - _ghostAt < GhostRefreshSeconds || stroke.Count == _ghostSamples)
                return;
            _ghostAt = now;
            _ghostSamples = stroke.Count;
            var shaped = StrokeBuilder.Shape(stroke.Select(v => v.ToCore()).ToList(), model, ctx);
            PartnerLine = shaped.Copy != null ? shaped.Copy.PartnerLineId : -1;
            _ghost.Clear();
            _ghost.AddRange(shaped.Paths());
            _mirrorGhost.Clear();
            if (ctx.Mirror.HasValue)
                _mirrorGhost.AddRange(_ghost.Select(p => MirrorAssist.Mirror(p, ctx.Mirror.Value)).Where(p => p != null));
        }

        /// <summary>Shows a rejected stroke in red, fading out.</summary>
        public void Reject(List<Vector3> stroke, float now)
        {
            _rejected.Clear();
            _rejected.AddRange(stroke);
            _rejectedAt = now;
            Clear();
        }

        static void DrawPaths(BridgeView view, List<List<Vec3>> paths, float radius, Color color)
        {
            foreach (var path in paths)
            {
                for (int i = 1; i < path.Count; i++)
                    view.Tube(path[i - 1].ToUnity(), path[i].ToUnity(), radius, color);
            }
        }

        public void Draw(BridgeView view, float radius, Color ghostColor, Color rejectColor, bool drawing, float now)
        {
            if (drawing)
            {
                Color c = PartnerLine >= 0 ? ghostColor : Color.Lerp(ghostColor, Color.white, 0.5f);
                DrawPaths(view, _ghost, radius * 0.6f, c);
                DrawPaths(view, _mirrorGhost, radius * 0.5f, Color.Lerp(c, Color.white, 0.5f));
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
