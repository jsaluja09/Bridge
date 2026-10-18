using System;
using System.Collections.Generic;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>A finished test as its results appear, for anything that reacts to it (BridgeGame.TestFinished).</summary>
    public sealed class TestOutcome
    {
        public ChallengeConfig Level { get; set; }
        /// <summary>Every phase that ran, in order. The run values below are the last phase's.</summary>
        public IReadOnlyList<PhaseOutcome> Phases { get; set; }
        public RunConfiguration LastRun { get; set; }
        public bool Passed { get; set; }
        public int Stars { get; set; }
        public int MembersLost { get; set; }
        public double PeakUtilization { get; set; }
        public double MaterialSpent { get; set; }
        /// <summary>The last phase's failures, or null when nothing broke.</summary>
        public FailureSummary Failures { get; set; }
    }

    /// <summary>
    /// The game loop, kept simple for students (spec 11): read the level, tap the two ends of the bridge, hold
    /// the trigger to draw lines in the air, press A to test. The test runs in phases (calm, wind, snow, traffic,
    /// a jam), each on a fresh copy of the bridge, and stops at the first failure; the stars show how far it got.
    /// Flat lines side by side become the road automatically. Physics runs on a worker thread; this class turns
    /// input into model edits and draws whatever state is current. Split across partial files: flow (here),
    /// input, drawing, HUD.
    /// </summary>
    [RequireComponent(typeof(ControllerPointer), typeof(BridgeView), typeof(CarView))]
    [RequireComponent(typeof(CanyonView))]
    public sealed partial class BridgeGame : MonoBehaviour
    {
        [Tooltip("Show the real room behind the game (Quest passthrough).")]
        [SerializeField] bool passthrough = true;
        [Tooltip("How long a passed phase's result shows before the next phase starts.")]
        [SerializeField] float passPauseSeconds = 2.5f;
        [Tooltip("How long a failed phase plays out before the results.")]
        [SerializeField] float failPauseSeconds = 4f;

        enum Phase { Brief, Place, Build, Testing, Results }

        sealed class UnityClock : IClock
        {
            public double Now => Time.realtimeSinceStartupAsDouble;
        }

        ControllerPointer _pointer;
        BridgeView _view;
        CarView _car;
        CanyonView _canyonView;
        SimWorker _worker;
        PreviewScheduler _scheduler;
        FailureFx _fx;
        readonly List<FailureReport> _newFailures = new List<FailureReport>();

        readonly ChallengeConfig _level = ChallengeConfig.Level();
        Phase _phase;
        readonly List<Vector3> _taps = new List<Vector3>();

        Pad[] _pads;
        CanyonLayout _canyon;
        List<Vec3> _groundDots = new List<Vec3>();
        float _floor;
        BridgeModel _model;
        StartSurfaceChoice _start;

        CompiledBridge _bridge;                   // the model compiled, refreshed on every edit
        CompiledBridge _previewBridge;
        PreviewResult _preview;

        PhaseSequence _sequence;                  // the test in progress, or the last one
        TestPhase _runningPhase;
        int _runningIndex;
        string _resultsText = "";
        CompiledBridge _runBridge;
        RunConfiguration _runConfig;
        RunSnapshot _snapshot;
        int _lastLost;
        float _phaseEndedAt = -1f;                // when the running phase's result came in, or -1
        float _resultsShownAt;
        int _bestStars;

        /// <summary>Raised once per test, when its results appear.</summary>
        public event Action<TestOutcome> TestFinished;
        /// <summary>Raised when the player leaves a test or its results and goes back to building.</summary>
        public event Action BackToBuilding;
        /// <summary>One optional extra line on the results (TextMeshPro rich text); cleared on going back.</summary>
        public string ResultNote { get; set; }

        int Stars => _sequence?.Stars ?? 0;
        static double Scale => Constants.Scale.RoomToBridge;

        void Awake()
        {
            _pointer = GetComponent<ControllerPointer>();
            _view = GetComponent<BridgeView>();
            _car = GetComponent<CarView>();
            _canyonView = GetComponent<CanyonView>();
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
                case Phase.Results: UpdateResults(); break;
            }
            Draw();
            UpdateHud();
            UpdateAudio();
        }

        // ---------- Brief: the level (spec 2.9) ----------

        void UpdateBrief()
        {
            if (_pointer.PrimaryPressed || _pointer.TriggerPressed)
            {
                _taps.Clear();
                _canyonView.Hide();
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

            // Ends tapped too low are lifted so the whole canyon fits above the real floor.
            float lift = (float)CanyonLayout.LiftNeeded(_taps[0].ToCore(), _taps[1].ToCore(), _pointer.FloorY);
            if (lift > 0f)
            {
                _taps[0] += Vector3.up * lift;
                _taps[1] += Vector3.up * lift;
                Flash("Raised the bridge ends so the canyon fits above the floor");
            }
            var (a, b) = Pad.FromTaps(_taps[0].ToCore(), _taps[1].ToCore());
            _pads = new[] { a, b };
            _canyon = new CanyonLayout(a, b);
            _canyonView.Show(_canyon);
            _floor = (float)_canyon.FloorY;            // debris and fallen cars land on the canyon floor
            _groundDots = _canyon.FloorDots();

            _model = new BridgeModel(new BudgetPolicy
            {
                CostPerMeter = Constants.Budget.CostPerMeter,
                CostPerSquareMeter = Constants.Budget.CostPerSquareMeter,
                MaterialBudget = _level.Budget,
                NodeCap = Constants.Human.NodeCap + Constants.Draw.SupportNodeAllowance,
            });
            _start = new StartSurfaceChoice(0, 1);
            _bridge = null;
            _preview = null;
            _sequence = null;
            _bestStars = 0;
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
            _sequence = new PhaseSequence();
            StartPhase();
        }

        /// <summary>Runs the sequence's current phase on a fresh copy of the bridge as built (spec 2.11).</summary>
        void StartPhase()
        {
            _runningPhase = _sequence.Current;
            _runningIndex = _sequence.Index;
            _runBridge = BridgeCompiler.Compile(_model.Copy(), _pads, Scale);
            _runConfig = _runningPhase.Configure(_level, _start, _pads[_start.Start].Across, Scale);
            _worker.StartRun(_runBridge, _runConfig, _floor);
            _fx.Begin(_runBridge, _floor);
            _canyonView.SetSnow(_runningPhase.Kind == PhaseKind.Snow);
            _canyonView.SetLights(false);
            HudMessages.Show(_runningPhase.Intro, passPauseSeconds + 1f);
            _snapshot = null;
            _lastLost = 0;
            _phaseEndedAt = -1f;
            _phase = Phase.Testing;
        }

        // ---------- Testing: one phase at a time ----------

        void UpdateTesting()
        {
            PollRun();
            if (_snapshot != null && _snapshot.Result != RunResult.Running && _phaseEndedAt < 0f)
                FinishPhase();

            if (_phaseEndedAt >= 0f)
            {
                bool passed = _sequence.Outcomes[_sequence.Outcomes.Count - 1].Passed;
                float waited = Time.time - _phaseEndedAt;
                if (_sequence.Finished && waited > (passed ? passPauseSeconds : failPauseSeconds))
                    ShowResults();
                else if (!_sequence.Finished && waited > passPauseSeconds)
                    StartPhase();
            }

            if (_pointer.PrimaryPressed || _pointer.ClearPressed)
                BackToBuild();
        }

        /// <summary>Takes the newest snapshot of this run, its failures, and the break sound.</summary>
        void PollRun()
        {
            var latest = _worker.Latest();
            _snapshot = latest.Bridge == _runBridge ? latest : null;
            _newFailures.Clear();
            _worker.TakeFailures(_runBridge, _newFailures);
            _fx.Advance(_snapshot, _newFailures);
            if (_snapshot != null && _snapshot.MembersLost > _lastLost)
            {
                _lastLost = _snapshot.MembersLost;
                Play(breakClip);
            }
        }

        void FinishPhase()
        {
            _phaseEndedAt = Time.time;
            bool passed = _snapshot.Result == RunResult.Passed;
            var result = new PhaseResult(passed, _snapshot.FirstBreak, _snapshot.Stuck, _snapshot.PeakUtilization);
            _sequence.Record(new PhaseOutcome
            {
                Phase = _runningPhase,
                Passed = passed,
                Message = _snapshot.Message,
                Tip = PhaseFeedback.Tip(_runningPhase.Kind, result),
            });
            Play(passed ? passClip : failClip);
        }

        // ---------- Results: stars, what held, what to fix ----------

        void ShowResults()
        {
            _bestStars = Mathf.Max(_bestStars, Stars);
            _resultsShownAt = Time.time;
            _resultsText = BuildResultsText();
            _canyonView.SetLights(Stars == TestPhases.MaxStars);     // the town lights up when it is connected
            _phase = Phase.Results;
            TestFinished?.Invoke(new TestOutcome
            {
                Level = _level,
                Phases = _sequence.Outcomes,
                LastRun = _runConfig,
                Passed = _sequence.Outcomes[_sequence.Outcomes.Count - 1].Passed,
                Stars = Stars,
                MembersLost = _snapshot?.MembersLost ?? 0,
                PeakUtilization = _snapshot?.PeakUtilization ?? 0,
                MaterialSpent = _model.Spent,
                Failures = _fx.Summary,
            });
        }

        void UpdateResults()
        {
            PollRun();                         // a collapse keeps playing out behind the results
            float before = Time.time - Time.deltaTime - _resultsShownAt, now = Time.time - _resultsShownAt;
            for (int i = 0; i < TestPhases.MaxStars; i++)
            {
                if (StarReveal.StartsBetween(i, Stars, before, now))
                    Play(i == TestPhases.MaxStars - 1 ? passClip : snapClip);
            }
            if (_pointer.PrimaryPressed || _pointer.ClearPressed)
                BackToBuild();
        }

        /// <summary>Tests run on a copy: going back restores the bridge as built (spec 2.11).</summary>
        void BackToBuild()
        {
            _worker.StopRun();
            _car.Hide();
            _fx.Clear();
            _canyonView.SetSnow(false);
            _canyonView.SetLights(_bestStars == TestPhases.MaxStars);
            HudMessages.Clear();
            ResultNote = null;
            _snapshot = null;
            _phase = Phase.Build;
            BackToBuilding?.Invoke();
        }
    }
}
