using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// The game loop, kept simple for students (spec 11): pick a challenge, tap the two ends of the bridge,
    /// hold the trigger to draw lines in the air, press A to send the car across. Flat lines side by side
    /// become the road automatically. Physics runs on a worker thread; this class turns input into model edits
    /// and draws whatever state is current. Split across partial files: flow (here), input, drawing, HUD.
    /// </summary>
    [RequireComponent(typeof(ControllerPointer), typeof(BridgeView), typeof(CarView))]
    public sealed partial class BridgeGame : MonoBehaviour
    {
        [Tooltip("Show the real room behind the game (Quest passthrough).")]
        [SerializeField] bool passthrough = true;

        enum Phase { Brief, Place, Build, Testing }

        sealed class UnityClock : IClock
        {
            public double Now => Time.realtimeSinceStartupAsDouble;
        }

        ControllerPointer _pointer;
        BridgeView _view;
        CarView _car;
        SimWorker _worker;
        PreviewScheduler _scheduler;
        FailureFx _fx;
        readonly List<FailureReport> _newFailures = new List<FailureReport>();

        readonly ChallengeConfig[] _challenges = ChallengeConfig.Defaults();
        int _challengeIndex;
        Phase _phase;
        readonly List<Vector3> _taps = new List<Vector3>();

        Pad[] _pads;
        List<Vec3> _groundDots = new List<Vec3>();
        float _floor;
        BridgeModel _model;
        StartSurfaceChoice _start;

        CompiledBridge _bridge;                   // the model compiled, refreshed on every edit
        CompiledBridge _previewBridge;
        PreviewResult _preview;

        CompiledBridge _runBridge;
        RunSnapshot _snapshot;
        int _lastLost;
        RunResult _shownResult;
        int _stars;

        ChallengeConfig Challenge => _challenges[_challengeIndex];
        static double Scale => Constants.Scale.RoomToBridge;

        void Awake()
        {
            _pointer = GetComponent<ControllerPointer>();
            _view = GetComponent<BridgeView>();
            _car = GetComponent<CarView>();
            _fx = GetComponent<FailureFx>();
            if (_fx == null)
                _fx = gameObject.AddComponent<FailureFx>();
            CreateFeedback();
            _worker = new SimWorker();
            _scheduler = new PreviewScheduler(new UnityClock(), () =>
            {
                if (_bridge != null)
                    _worker.RequestPreview(_bridge);
            });
        }

        void Start()
        {
            if (passthrough)
                Passthrough.Enable();
        }

        void OnDestroy() => _worker?.Dispose();

        void Update()
        {
            switch (_phase)
            {
                case Phase.Brief: UpdateBrief(); break;
                case Phase.Place: UpdatePlace(); break;
                case Phase.Build: UpdateBuild(); break;
                case Phase.Testing: UpdateTesting(); break;
            }
            Draw();
            UpdateHud();
            UpdateAudio();
        }

        // ---------- Brief: pick a challenge (spec 2.9) ----------

        void UpdateBrief()
        {
            if (_pointer.StickStep != 0)
            {
                _challengeIndex = (_challengeIndex + _pointer.StickStep + _challenges.Length) % _challenges.Length;
                Play(snapClip);
            }
            if (_pointer.PrimaryPressed || _pointer.TriggerPressed)
            {
                _taps.Clear();
                _phase = Phase.Place;
                Play(snapClip);
            }
        }

        // ---------- Place: two taps, one for each end of the bridge (spec 11.2) ----------

        void UpdatePlace()
        {
            if (_pointer.ClearPressed)
            {
                if (_taps.Count > 0)
                    _taps.RemoveAt(_taps.Count - 1);
                else
                    _phase = Phase.Brief;
                return;
            }
            if (!_pointer.TriggerPressed)
                return;

            _taps.Add(_pointer.Tip);
            Play(snapClip);
            if (_taps.Count < 2)
                return;

            Vector3 gap = _taps[1] - _taps[0];
            gap.y = 0f;
            if (gap.magnitude < 0.3f)
            {
                Flash("Put the two ends at least 30 cm apart");
                Play(errorClip);
                _taps.RemoveAt(1);
                return;
            }

            var (a, b) = Pad.FromTaps(_taps[0].ToCore(), _taps[1].ToCore());
            _pads = new[] { a, b };
            float lowest = Mathf.Min(_taps[0].y, _taps[1].y);
            _floor = Mathf.Min(_pointer.FloorY, lowest - 0.3f);
            _groundDots.Clear();
            if (_floor < lowest - 0.15f)
            {
                var ea = a.Corners();
                var eb = b.Corners();
                _groundDots = GroundPlane.Grid(ea[0], ea[1], eb[0], eb[1], _floor).Select(n => n.Position).ToList();
            }

            _model = new BridgeModel(new BudgetPolicy
            {
                CostPerMeter = Constants.Budget.CostPerMeter,
                CostPerSquareMeter = Constants.Budget.CostPerSquareMeter,
                MaterialBudget = Challenge.Budget,
                NodeCap = Constants.Human.NodeCap + Constants.Draw.SupportNodeAllowance,
            });
            _start = new StartSurfaceChoice(0, 1);
            _bridge = null;
            _preview = null;
            _phase = Phase.Build;
        }

        // ---------- Build ----------

        void UpdateBuild()
        {
            if (_bridge == null || _bridge.ModelVersion != _model.Version)
                _bridge = BridgeCompiler.Compile(_model, _pads, Scale);

            UpdateBuildInput();
            _scheduler.Tick();
            if (_worker.TakePreview(out var previewBridge, out var preview))
            {
                _previewBridge = previewBridge;
                _preview = preview;
            }

            if (_pointer.PrimaryPressed && !Drawing)
                StartTest();
        }

        bool PreviewCurrent => _preview != null && _previewBridge == _bridge;

        void StartTest()
        {
            if (!_bridge.Road.Ok)
            {
                Flash(_bridge.Road.TooSteepFillIds.Count > 0
                    ? "Part of the road is too steep for the car"
                    : "No road yet: draw two flat lines side by side, from one end pad to the other");
                Play(errorClip);
                _roadWarningUntil = Time.time + 3f;
                return;
            }

            ResetInput();
            _runBridge = BridgeCompiler.Compile(_model.Copy(), _pads, Scale);
            var config = Challenge.ToRunConfiguration(_start, _pads[_start.Start].Across, Scale);
            _worker.StartRun(_runBridge, config, _floor);
            _fx.Begin(_runBridge, _floor);
            _snapshot = null;
            _lastLost = 0;
            _shownResult = RunResult.Running;
            _stars = 0;
            _phase = Phase.Testing;
        }

        // ---------- Testing ----------

        void UpdateTesting()
        {
            var latest = _worker.Latest();
            _snapshot = latest.Bridge == _runBridge ? latest : null;
            _newFailures.Clear();
            _worker.TakeFailures(_runBridge, _newFailures);
            _fx.Advance(_snapshot, _newFailures);

            if (_snapshot != null)
            {
                if (_snapshot.MembersLost > _lastLost)
                {
                    _lastLost = _snapshot.MembersLost;
                    Play(breakClip);
                }
                if (_snapshot.Result != _shownResult)
                {
                    _shownResult = _snapshot.Result;
                    var metrics = new RunMetrics
                    {
                        MembersLost = _snapshot.MembersLost,
                        PeakUtilization = _snapshot.PeakUtilization,
                        BudgetRemaining = _model.Remaining,
                    };
                    _stars = _shownResult == RunResult.Passed ? Scoring.Stars(metrics, StarPolicy) : 0;
                    Play(_shownResult == RunResult.Passed ? passClip : failClip);
                }
            }

            // Tests run on a copy: going back restores the bridge as built (spec 2.11).
            if (_pointer.PrimaryPressed || _pointer.ClearPressed)
            {
                _worker.StopRun();
                _car.Hide();
                _fx.Clear();
                HudMessages.Clear();
                _snapshot = null;
                _phase = Phase.Build;
            }
        }

        /// <summary>Three-star thresholds. HG-stars has not set them, so these are the game's GUESS.</summary>
        StarPolicy StarPolicy => new StarPolicy { MaxLostForThree = 0, MaxUtilForThree = 0.8, MinBudgetForThree = Challenge.Budget * 0.25 };
    }
}
