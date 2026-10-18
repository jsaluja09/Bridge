using System.Linq;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>The HUD card, written for students, and sound (including the load creak, spec 2.10).</summary>
    public sealed partial class BridgeGame
    {
        [Header("Feedback")]
        [SerializeField] AudioClip snapClip;
        [SerializeField] AudioClip errorClip;
        [SerializeField] AudioClip breakClip;
        [SerializeField] AudioClip passClip;
        [SerializeField] AudioClip failClip;
        [Tooltip("Looped while testing; pitch and volume rise as the worst member nears failure.")]
        [SerializeField] AudioClip creakClip;
        [SerializeField] float hudFontSize = 0.22f;
        [Tooltip("Show solver timing: the headset benchmark for the node cap (spec 2.12).")]
        [SerializeField] bool showSolverStats = true;

        HudCard _card;
        AudioSource _sfx;
        AudioSource _creak;

        void CreateFeedback()
        {
            var go = new GameObject("HUD");
            go.transform.SetParent(transform, false);
            _card = go.AddComponent<HudCard>();
            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _creak = gameObject.AddComponent<AudioSource>();
            _creak.playOnAwake = false;
            _creak.loop = true;
            _creak.volume = 0f;
        }

        void Flash(string message) => HudMessages.Show(message, 2.5f);

        void Play(AudioClip clip)
        {
            if (clip != null)
                _sfx.PlayOneShot(clip);
        }

        void UpdateHud()
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            var card = _phase switch
            {
                Phase.Brief => BriefCard(),
                Phase.Place => PlaceCard(),
                Phase.Build => BuildCard(),
                Phase.Results => ResultsCard(),
                _ => TestCard(),
            };
            card.Toast = HudMessages.Current;
            if (_worker.Error != null)
                card.Error = $"Simulation error: {_worker.Error}";
            _card.Show(card, hudFontSize);

            Vector3 position = HudAnchor(cam);
            _card.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - cam.transform.position, Vector3.up));
        }

        /// <summary>Above the middle of the bridge once it exists, otherwise in front of the player.</summary>
        Vector3 HudAnchor(Camera cam) => _phase == Phase.Brief || _phase == Phase.Place
            ? cam.transform.position + Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized * 0.8f - Vector3.up * 0.1f
            : (V(_pads[0].FrontCenter) + V(_pads[1].FrontCenter)) * 0.5f + Vector3.up * 0.45f;

        static readonly string PhaseNames = string.Join("   ·   ", TestPhases.All.Select(p => p.Name));
        static readonly string TestCount = $"{TestPhases.All.Count} tests";
        static readonly string StartKeys = HudCard.Key("A", "Start") + "   " + HudCard.Key("X", "Menu");
        static readonly string UndoTapKeys = HudCard.Key("X", "Undo");
        static readonly string BuildKeysMirrorOn = BuildKeys("Mirror <b>on</b>");
        static readonly string BuildKeysMirrorOff = BuildKeys("Mirror off");
        static readonly string ResultsKeys = HudCard.Key("A", "Back to building");

        static string BuildKeys(string mirror) =>
            HudCard.Key("Trigger", "Hold to draw") + "   " + HudCard.Key("Stick", "Material") + "   " +
            HudCard.Key("Grip", "On a line: delete") + "   " + HudCard.Key("A", "Test") + "   " + HudCard.Key("B", "Undo") + "   " +
            HudCard.Key("X", "Clear") + "   " + HudCard.Key("Y", mirror);

        string _briefBody;

        HudCardContent BriefCard() => new HudCardContent
        {
            Kind = HudCardKind.Intro,
            Header = TestCount,
            Title = _level.Name,
            Body = _briefBody ??= $"{_level.Brief}\nBudget ${_level.Budget:F0}\n<size=80%>{PhaseNames}</size>",
            Controls = StartKeys,
        };

        HudCardContent PlaceCard() => _taps.Count == 0
            ? new HudCardContent
            {
                Kind = HudCardKind.Instructions,
                Title = "Place the bridge",
                Body = "Touch where the road ends on THIS side of the canyon\nand pull the trigger",
            }
            : new HudCardContent
            {
                Kind = HudCardKind.Instructions,
                Title = "Place the bridge",
                Body = "Now touch where the road starts again ACROSS the canyon\n<size=70%>The canyon follows your hand</size>",
                Controls = UndoTapKeys,
            };

        HudCardContent BuildCard()
        {
            string material = $"{Materials.Name(_material)}  ${Materials.CostPerMeter(_material):F0}/m   ·   " +
                              $"Spent ${TotalSpent(_bridge):F0} / ${_level.Budget:F0}";
            string road = _bridge.Road.Ok ? "<color=#5CE65C>Road ready</color>" : "Road: draw two flat lines side by side, pad to pad";
            string best = _bestStars > 0 ? $"   ·   <color=#FFD24A>Best: {_bestStars} of {TestPhases.MaxStars} stars</color>" : "";
            string body = $"{material}   ·   {road}{best}";
            if (PreviewCurrent)
                body += "\n<size=60%>Colors show how hard the bridge works to hold itself up</size>";
            return new HudCardContent
            {
                Kind = HudCardKind.Instructions,
                Title = "Build the bridge",
                Body = body,
                Controls = _mirror ? BuildKeysMirrorOn : BuildKeysMirrorOff,
                Footer = SolverStats(),
            };
        }

        /// <summary>
        /// One card for the running phase and its result: the phase's intro, worst load and a progress bar that
        /// follows the run's own end rule, then PASSED or FAILED with the bar frozen where the run stopped.
        /// </summary>
        HudCardContent TestCard()
        {
            var snap = _snapshot;
            var card = new HudCardContent
            {
                Header = _testHeader,
                BarLabel = ProgressLabel(snap),
                BarFill = snap == null ? 0f : (float)snap.Progress,
                Footer = SolverStats(),
            };
            // Failure messages arrive through HudMessages while testing (W4-07).
            if (snap == null || snap.Result == RunResult.Running)
            {
                card.Kind = HudCardKind.Testing;
                card.Title = "Testing...";
                card.Body = snap == null
                    ? _runningPhase.Intro
                    : $"{_runningPhase.Intro}\nWorst load {Mathf.RoundToInt((float)snap.WorstUtilization * 100)}%";
                return card;
            }

            var outcome = _sequence.Outcomes[_sequence.Outcomes.Count - 1];
            card.Kind = outcome.Passed ? HudCardKind.Passed : HudCardKind.Failed;
            card.Title = outcome.Passed ? "PASSED" : "FAILED";
            card.Body = snap.Message;
            string next = outcome.Passed && !_sequence.Finished ? $"\n<size=70%>Next: {_sequence.Current.Name}</size>" : "";
            card.Details = $"<color=#FFD24A>{outcome.Tip}</color>{next}";
            return card;
        }

        /// <summary>How far the run is toward its end rule: the car across, the traffic through, the jam held.</summary>
        string ProgressLabel(RunSnapshot snap)
        {
            if (snap == null)
                return "Starting...";
            if (_runningPhase.Kind == PhaseKind.Jam)
            {
                if (snap.JamHeldSeconds <= 0)
                    return $"Filling the bridge: {snap.CarsSent} of {snap.CarsTotal} cars";
                double hold = Constants.Traffic.JamHoldSeconds;
                return $"Holding the jam: {System.Math.Min(snap.JamHeldSeconds, hold):F0} of {hold:F0} s";
            }
            if (snap.CarsTotal > 0)
                return $"{snap.CarsCrossed} of {snap.CarsTotal} cars across";
            return snap.Result == RunResult.Passed ? "Across" : "Crossing";
        }

        string _resultsBody, _resultsBodyText, _resultsBodyNote;   // the results card's body, and what it was built from

        /// <summary>The results, with the optional ResultNote (the coach's explanation) under them.</summary>
        HudCardContent ResultsCard()
        {
            if (!ReferenceEquals(_resultsBodyText, _resultsText) || _resultsBodyNote != ResultNote)
            {
                _resultsBodyText = _resultsText;
                _resultsBodyNote = ResultNote;
                _resultsBody = string.IsNullOrEmpty(ResultNote) ? _resultsText : $"{_resultsText}\n{ResultNote}";
            }
            return new HudCardContent
            {
                Kind = HudCardKind.Results,
                Header = _testHeader,
                Title = _resultsTitle,
                Body = _resultsBody,
                Controls = ResultsKeys,
            };
        }

        /// <summary>
        /// Built once when the results appear (the stars are drawn above the card): each phase's verdict, why the
        /// last one failed, the advice, and where the weak spot is.
        /// </summary>
        string BuildResultsText()
        {
            var lines = new System.Text.StringBuilder();
            foreach (var phase in TestPhases.All)
            {
                var outcome = _sequence.Outcomes.FirstOrDefault(o => o.Phase == phase);
                string mark = outcome == null ? "<color=#808080>not reached</color>"
                            : outcome.Passed ? "<color=#5CE65C>held</color>" : "<color=#FF5050>failed</color>";
                if (lines.Length > 0)
                    lines.Append("   ");
                lines.Append($"<nobr>{phase.Name}: {mark}</nobr>");
            }
            var last = _sequence.Outcomes[_sequence.Outcomes.Count - 1];
            string summary = !last.Passed && _fx.Summary != null ? $"\n{_fx.Summary.Text}" : "";
            string point = _snapshot != null && _snapshot.WeakMember >= 0 ? "\n<size=70%>The flashing beam is the weakest spot.</size>" : "";
            return $"<size=70%>{lines}</size>{summary}\n<color=#FFD24A>{last.Tip}</color>{point}";
        }

        string SolverStats() => showSolverStats
            ? $"{_worker.Members} members · step {_worker.StepMs:F2} ms · start {_worker.StartMs:F0} ms · preview {_worker.PreviewMs:F0} ms"
            : null;

        void UpdateAudio()
        {
            if (creakClip == null)
                return;
            double worst = _phase == Phase.Testing && _snapshot != null && _snapshot.Result == RunResult.Running ? _snapshot.WorstUtilization : 0;
            float volume = Mathf.Clamp01((float)(worst - 0.6) / 0.4f);
            if (volume > 0f && !_creak.isPlaying)
            {
                _creak.clip = creakClip;
                _creak.Play();
            }
            _creak.volume = volume;
            // Spec 2.10: pitch rises as the worst member nears failure (80 to 400 Hz mapped onto playback rate).
            _creak.pitch = (float)(FeedbackMapping.CreakPitch(worst) / Constants.Feedback.CreakPitchMinHz) * 0.5f;
        }
    }
}
