using System.Collections.Generic;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Building input (spec 11.1, 11.4): hold the trigger to draw a line anywhere, release to place it;
    /// flick the stick to change material; squeeze grip on a line to delete it; B undoes, X clears, Y turns
    /// mirroring across the road on and off.
    /// </summary>
    public sealed partial class BridgeGame
    {
        const string OutOfMaterial = "Out of material. Delete or undo something first.";

        BridgeMaterial _material = BridgeMaterial.Wood;     // what new lines are made of
        bool _drawing;
        readonly List<Vector3> _stroke = new List<Vector3>();
        readonly StrokeFeedback _strokeFx = new StrokeFeedback();
        Vector3? _hover;              // the dot (or pad / floor spot) a stroke would join here
        int _hoverLine = -1;
        List<int> _blockedMembers = new List<int>();
        float _blockedUntil;
        float _clearArmedUntil;
        float _roadWarningUntil;

        bool _mirror = true;          // copy each beam across the road's centerline

        bool Drawing => _drawing;
        static double SnapRadius => Constants.Draw.SnapRadiusMeters;
        MirrorPlane? MirrorPlaneNow => _mirror && _pads != null ? MirrorPlane.Centerline(_pads[0], _pads[1]) : (MirrorPlane?)null;

        void ResetInput()
        {
            _drawing = false;
            _stroke.Clear();
            _strokeFx.Clear();
            _hover = null;
            _hoverLine = -1;
        }

        void UpdateBuildInput()
        {
            Vector3 tip = _pointer.Tip;

            if (_pointer.TriggerPressed)
            {
                _drawing = true;
                _stroke.Clear();
                _stroke.Add(HoverPoint(tip) ?? tip);
                _strokeFx.Clear();
                _scheduler.NotifyDrawStarted();
            }
            else if (_drawing && _pointer.TriggerHeld)
            {
                if (Vector3.Distance(_stroke[_stroke.Count - 1], tip) > 0.005f)
                    _stroke.Add(tip);
                _strokeFx.UpdateGhost(_stroke, _model, MirrorPlaneNow, Time.time);      // W4-04: ghost of the resolved, snapped shape
            }
            else if (_drawing)
            {
                _drawing = false;
                FinishStroke();
            }

            _hover = _drawing ? null : HoverPoint(tip);
            _hoverLine = _drawing ? -1 : PickLine(tip.ToCore());

            if (_pointer.StickStep != 0 && !_drawing)
            {
                int count = Materials.All.Length;
                int next = System.Array.IndexOf(Materials.All, _material) + _pointer.StickStep;
                _material = Materials.All[(next % count + count) % count];
                Play(snapClip);
            }

            if (_pointer.GripPressed && !_drawing)
                DeleteHovered();

            if (_pointer.SecondaryPressed && !_drawing && _model.Undo())
            {
                Play(snapClip);
                _scheduler.NotifyPaused();
            }

            if (_pointer.MirrorPressed)
            {
                _mirror = !_mirror;
                Flash(_mirror ? "Mirror on: beams are copied to the other side of the road" : "Mirror off");
            }

            if (_pointer.ClearPressed && !_drawing)
            {
                if (_model.Lines.Count == 0)
                {
                    _phase = Phase.Brief;
                }
                else if (Time.time < _clearArmedUntil)
                {
                    _model.Reset();
                    _clearArmedUntil = 0f;
                    _scheduler.NotifyPaused();
                }
                else
                {
                    _clearArmedUntil = Time.time + 2f;
                    Flash("Press X again to clear the bridge (B brings it back)");
                }
            }
        }

        /// <summary>The dot, pad top or floor dot the tip would join, if any.</summary>
        Vector3? HoverPoint(Vector3 tip)
        {
            Vec3 p = tip.ToCore();
            Node best = null;
            double bestDist = SnapRadius;
            foreach (var n in _model.Nodes)
            {
                double d = Vec3.Distance(n.Position, p);
                if (d <= bestDist)
                {
                    best = n;
                    bestDist = d;
                }
            }
            if (best != null)
                return best.Position.ToUnity();
            foreach (var pad in _pads)
            {
                if (pad.TrySupportPoint(p, SnapRadius, out Vec3 support))
                    return support.ToUnity();
            }
            foreach (var g in _groundDots)
            {
                if (Vec3.Distance(g, p) <= SnapRadius)
                    return g.ToUnity();
            }
            return null;
        }

        /// <summary>
        /// Builds the resolved shape (W4-04), not the raw stroke: one piece for a line or arc, one per side of a
        /// polyline so its corners land exactly. A stroke that fits no shape is built as a straight beam; one too
        /// short for a beam flashes red with no message. The whole stroke comes back out if the lines plus the
        /// decks they make cost more than the budget.
        /// </summary>
        void FinishStroke()
        {
            var pieces = StrokeFeedback.Pieces(_stroke, _model);
            if (pieces == null)
            {
                Reject(null);
                return;
            }

            var result = AddStroke(pieces);
            if (result.Ok)
            {
                var compiled = BridgeCompiler.Compile(_model, _pads, Scale);
                if (TotalSpent(compiled) > _model.Policy.MaterialBudget + 1e-9)
                {
                    _model.Undo();
                    _scheduler.NotifyPaused();
                    result = new StrokeResult(0, EditReason.BudgetExceeded);
                }
                else
                {
                    _bridge = compiled;
                }
            }
            if (!result.Ok)
            {
                Reject(result.Reason == EditReason.BudgetExceeded ? OutOfMaterial : null);
                return;
            }
            _stroke.Clear();
            _strokeFx.Clear();
            Play(snapClip);
            _scheduler.NotifyPaused();
        }

        /// <summary>
        /// StrokeBuilder.Build in the chosen material: each piece, then its mirror copy, goes through FreeDraw, and
        /// the whole stroke is one undo step that goes in whole or not at all.
        /// </summary>
        StrokeResult AddStroke(List<List<Vec3>> pieces)
        {
            var all = new List<IReadOnlyList<Vec3>>(pieces);
            var mirror = MirrorPlaneNow;
            if (mirror.HasValue)
            {
                foreach (var piece in pieces)
                {
                    var copy = MirrorAssist.Mirror(piece, mirror.Value);
                    if (copy != null)
                        all.Add(copy);
                }
            }

            int added = 0;
            _model.BeginGroup();
            try
            {
                foreach (var piece in all)
                {
                    var draw = new FreeDraw.Context { Nodes = _model.Nodes, Joined = JoinedLookup(), Pads = _pads, GroundDots = _groundDots };
                    foreach (var chain in FreeDraw.Build(piece, draw, out _))
                    {
                        var result = _model.TryAddFreeLine(LineVertices(chain), (int)_material);
                        if (!result.Ok)
                        {
                            if (added > 0)
                                _model.Undo();          // the group saved one snapshot before its first line
                            return new StrokeResult(0, result.Reason);
                        }
                        added++;
                    }
                }
            }
            finally
            {
                _model.EndGroup();
            }
            return new StrokeResult(added, EditReason.None);
        }

        List<LineVertex> LineVertices(List<DrawVertex> chain)
        {
            var vertices = new List<LineVertex>(chain.Count);
            foreach (var v in chain)
            {
                if (v.IsExisting)
                    vertices.Add(LineVertex.Existing(v.ExistingId));
                else if (v.NewSupport)
                    vertices.Add(LineVertex.Existing(_model.AddFixedNode(v.Position, v.SurfaceId, v.Ground)));
                else
                    vertices.Add(LineVertex.New(v.Position));
            }
            return vertices;
        }

        /// <summary>"Are these two nodes already joined?" from one pass over the members (FreeDraw asks per segment).</summary>
        System.Func<int, int, bool> JoinedLookup()
        {
            var joined = new HashSet<(int, int)>();
            foreach (var m in _model.Members)
                joined.Add(m.NodeA < m.NodeB ? (m.NodeA, m.NodeB) : (m.NodeB, m.NodeA));
            return (a, b) => joined.Contains(a < b ? (a, b) : (b, a));
        }

        void Reject(string message)
        {
            if (message != null)
                Flash(message);
            Play(errorClip);
            _strokeFx.Reject(_stroke, Time.time);
            _stroke.Clear();
        }

        /// <summary>The drawn line closest to p within 3 cm, or -1.</summary>
        int PickLine(Vec3 p)
        {
            int best = -1;
            double bestDist = 0.03;
            foreach (var m in _model.Members)
            {
                if (m.LineId < 0)
                    continue;
                double d = Geometry.DistanceToSegment(p, _model.GetNode(m.NodeA).Position, _model.GetNode(m.NodeB).Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = m.LineId;
                }
            }
            return best;
        }

        void DeleteHovered()
        {
            if (_hoverLine < 0)
            {
                Flash("Touch a line with the controller tip, then squeeze grip to delete it");
                return;
            }
            var result = _model.TryDeleteLine(_hoverLine);
            if (!result.Ok)
            {
                _blockedMembers = result.BlockingMemberIds;
                _blockedUntil = Time.time + 2f;
                Flash("Other lines are attached to this one. Delete those first.");
                Play(errorClip);
                return;
            }
            Play(snapClip);
            _scheduler.NotifyPaused();
        }
    }
}
