using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Building input (spec 11.1, 11.4): hold the trigger to draw a line anywhere, release to place it;
    /// squeeze grip on a line to delete it; B undoes, X clears, Y swaps the car's start side.
    /// </summary>
    public sealed partial class BridgeGame
    {
        bool _drawing;
        readonly List<Vector3> _stroke = new List<Vector3>();
        readonly List<Vector3> _rejected = new List<Vector3>();
        float _rejectedUntil;
        Vector3? _hover;              // the dot (or pad / floor spot) a stroke would join here
        int _hoverLine = -1;
        List<int> _blockedMembers = new List<int>();
        float _blockedUntil;
        float _clearArmedUntil;
        float _roadWarningUntil;

        bool Drawing => _drawing;
        static double SnapRadius => Constants.Draw.SnapRadiusMeters;

        void ResetInput()
        {
            _drawing = false;
            _stroke.Clear();
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
                _scheduler.NotifyDrawStarted();
            }
            else if (_drawing && _pointer.TriggerHeld)
            {
                if (Vector3.Distance(_stroke[_stroke.Count - 1], tip) > 0.005f)
                    _stroke.Add(tip);
            }
            else if (_drawing)
            {
                _drawing = false;
                FinishStroke();
            }

            _hover = _drawing ? null : HoverPoint(tip);
            _hoverLine = _drawing ? -1 : PickLine(tip.ToCore());

            if (_pointer.GripPressed && !_drawing)
                DeleteHovered();

            if (_pointer.SecondaryPressed && !_drawing && _model.Undo())
            {
                Play(snapClip);
                _scheduler.NotifyPaused();
            }

            if (_pointer.SwapPressed)
            {
                _start.Swap();
                Flash("The car now starts from the other end");
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

        void FinishStroke()
        {
            var raw = _stroke.Select(v => v.ToCore()).ToList();
            var ctx = new FreeDraw.Context
            {
                Nodes = _model.Nodes,
                Joined = (a, b) => _model.Members.Any(m => (m.NodeA == a && m.NodeB == b) || (m.NodeA == b && m.NodeB == a)),
                Pads = _pads,
                GroundDots = _groundDots,
            };
            var chains = FreeDraw.Build(raw, ctx, out _);
            if (chains.Count == 0)
            {
                Reject("Draw a little longer");
                return;
            }

            foreach (var chain in chains)
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
                var result = _model.TryAddFreeLine(vertices);
                if (!result.Ok)
                {
                    Reject(result.Reason == EditReason.BudgetExceeded ? "Out of material. Delete or undo something first." : "That line didn't work. Try again.");
                    return;
                }
            }
            _stroke.Clear();
            Play(snapClip);
            _scheduler.NotifyPaused();
        }

        void Reject(string message)
        {
            Flash(message);
            Play(errorClip);
            _rejected.Clear();
            _rejected.AddRange(_stroke);
            _rejectedUntil = Time.time + 1f;
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
