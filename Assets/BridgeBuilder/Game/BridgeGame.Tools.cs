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
        DrawVertex _strokeStart;      // what the stroke being drawn starts on
        readonly StrokeFeedback _strokeFx = new StrokeFeedback();
        Vector3? _hover;              // the dot, point on a line, or pad / floor spot a stroke would join here
        int _hoverLine = -1;
        List<int> _blockedMembers = new List<int>();
        float _blockedUntil;
        float _clearArmedUntil;
        float _roadWarningUntil;

        bool Drawing => _drawing;

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
                _strokeStart = FreeDraw.Target(tip.ToCore(), DrawContext());
                _stroke.Add(_strokeStart.Joins ? _strokeStart.Position.ToUnity() : tip);
                _strokeFx.Clear();
                _scheduler.NotifyDrawStarted();
            }
            else if (_drawing && _pointer.TriggerHeld)
            {
                if (Vector3.Distance(_stroke[_stroke.Count - 1], tip) > 0.005f)
                    _stroke.Add(tip);
                _strokeFx.UpdateGhost(_stroke, DrawContext(), _model, Time.time);      // W4-04: ghost of what the stroke will build
            }
            else if (_drawing)
            {
                _drawing = false;
                if (Vector3.Distance(_stroke[_stroke.Count - 1], tip) > 0.0005f)
                    _stroke.Add(tip);
                FinishStroke();
            }

            _hover = HoverPoint(tip);
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

        FreeDraw.Context DrawContext() => new FreeDraw.Context
        {
            Nodes = _model.Nodes,
            Members = _model.Members,
            Joined = (a, b) => _model.Members.Any(m => (m.NodeA == a && m.NodeB == b) || (m.NodeA == b && m.NodeB == a)),
            Pads = _pads,
            GroundDots = _groundDots,
        };

        /// <summary>
        /// The dot, point on a line, pad top or floor dot the tip would join, if any. While drawing, this is
        /// where the stroke's end would join (spec 11.5).
        /// </summary>
        Vector3? HoverPoint(Vector3 tip)
        {
            var target = FreeDraw.Target(tip.ToCore(), DrawContext(), _drawing ? _strokeStart : (DrawVertex?)null);
            return target.Joins ? target.Position.ToUnity() : (Vector3?)null;
        }

        /// <summary>
        /// Builds what the stroke turns into (W4-04): FreeDraw's chains, or the shifted copy of the line it was drawn
        /// beside (W4-02), one piece at a time so a polyline's corners are shared dots. It goes in whole or not at all.
        /// </summary>
        void FinishStroke()
        {
            var chains = StrokeFeedback.Shape(_stroke, DrawContext(), _model, out var reason, out var copy);
            if (chains.Count == 0)
            {
                Reject(reason == RejectionReason.Unrecognized ? "Try a smoother shape" : "Draw a little longer");
                return;
            }

            int added = 0;
            string failure = null;
            if (copy == null)
                failure = AddChains(chains, ref added);
            else
            {
                foreach (var piece in StrokeFeedback.Pieces(copy.Shape))
                {
                    failure = AddChains(FreeDraw.Build(piece, DrawContext(), out _), ref added);
                    if (failure != null)
                        break;
                }
            }
            if (failure == null && added == 0)
                failure = "That line didn't work. Try again.";
            if (failure != null)
            {
                for (; added > 0; added--)
                    _model.Undo();
                Reject(failure);
                return;
            }
            _stroke.Clear();
            _strokeFx.Clear();
            Play(snapClip);
            _scheduler.NotifyPaused();
        }

        /// <summary>Adds each chain as a line, counting them in added. The message to show if one fails, else null.</summary>
        string AddChains(List<List<DrawVertex>> chains, ref int added)
        {
            foreach (var chain in chains)
            {
                var vertices = new List<LineVertex>(chain.Count);
                foreach (var v in chain)
                {
                    if (v.IsExisting)
                        vertices.Add(LineVertex.Existing(v.ExistingId));
                    else if (v.IsOnMember)
                        vertices.Add(LineVertex.OnMember(v.MemberId, v.Position));
                    else if (v.NewSupport)
                        vertices.Add(LineVertex.Existing(_model.AddFixedNode(v.Position, v.SurfaceId, v.Ground)));
                    else
                        vertices.Add(LineVertex.New(v.Position));
                }
                var result = _model.TryAddFreeLine(vertices);
                if (!result.Ok)
                    return result.Reason == EditReason.BudgetExceeded ? "Out of material. Delete or undo something first." : "That line didn't work. Try again.";
                added++;
            }
            return null;
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
