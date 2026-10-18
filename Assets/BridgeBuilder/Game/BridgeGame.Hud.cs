using BridgeBuilder.Core;
using TMPro;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>The text panel, written for students, and sound (including the load creak, spec 2.10).</summary>
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

        TextMeshPro _hud;
        AudioSource _sfx;
        AudioSource _creak;

        void CreateFeedback()
        {
            var go = new GameObject("HUD");
            go.transform.SetParent(transform, false);
            _hud = go.AddComponent<TextMeshPro>();
            _hud.alignment = TextAlignmentOptions.Center;
            _hud.rectTransform.sizeDelta = new Vector2(1.6f, 0.6f);
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

            string text = _phase switch
            {
                Phase.Brief => BriefText(),
                Phase.Place => _taps.Count == 0
                    ? "Touch the spot where the bridge STARTS\nand pull the trigger"
                    : "Now touch where the bridge ENDS\n<size=70%>X: undo</size>",
                Phase.Build => BuildText(),
                _ => TestText(),
            };
            string message = HudMessages.Current;
            if (message != null)
                text = $"<color=#FFD24A>{message}</color>\n{text}";
            if (_worker.Error != null)
                text += $"\n<color=#FF5050>Simulation error: {_worker.Error}</color>";
            _hud.text = text;
            _hud.fontSize = hudFontSize;

            Vector3 anchor;
            if (_phase == Phase.Build || _phase == Phase.Testing)
                anchor = (V(_pads[0].FrontCenter) + V(_pads[1].FrontCenter)) * 0.5f + Vector3.up * 0.45f;
            else
                anchor = cam.transform.position + Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized * 0.8f - Vector3.up * 0.1f;
            _hud.transform.position = anchor;
            _hud.transform.rotation = Quaternion.LookRotation(anchor - cam.transform.position, Vector3.up);
        }

        string BriefText()
        {
            var c = Challenge;
            string wind = c.WindSpeed > 0 ? $"{c.WindSpeed:F0} m/s wind" : "no wind";
            return $"<b>{c.Name}</b>  ({_challengeIndex + 1}/{_challenges.Length})\n{c.Brief}\n" +
                   $"Vehicle {c.CarWeightKg / 1000:F1} t  ·  {wind}  ·  Material {c.Budget:F0} m\n" +
                   "<size=70%>Stick: choose   ·   A: start</size>";
        }

        string BuildText()
        {
            string material = $"Material {_model.Spent:F1} / {Challenge.Budget:F0} m";
            string road = _bridge.Road.Ok ? "<color=#5CE65C>Road ready</color>" : "Road: draw two flat lines side by side, pad to pad";
            string text = $"{material}   ·   {road}\n" +
                          "<size=70%>Hold trigger: draw   ·   Grip on a line: delete   ·   A: test   ·   B: undo   ·   X: clear   ·   Y: swap start</size>";
            if (PreviewCurrent)
                text += "\n<size=60%>Colors show how hard the bridge works to hold itself up</size>";
            return text + SolverStats();
        }

        string TestText()
        {
            var snap = _snapshot;
            if (snap == null)
                return "Starting...";
            // Failure messages arrive through HudMessages while testing (W4-07).
            if (snap.Result == RunResult.Running)
                return $"Testing...   worst load {Mathf.RoundToInt((float)snap.WorstUtilization * 100)}%" + SolverStats();

            bool pass = snap.Result == RunResult.Passed;
            string verdict = pass ? "<color=#5CE65C>PASS</color>" : "<color=#FF5050>FAIL</color>";
            string stars = pass && _stars > 0 ? $"  <color=#FFD24A>{_stars} of 3 stars{(_stars == 3 ? "!" : "")}</color>" : "";
            // Keep the summary on the result panel once the HUD message has gone.
            var summary = _fx.Summary;
            string lesson = summary != null && HudMessages.Current != summary.Text ? $"\n<color=#FFD24A>{summary.Text}</color>" : "";
            return $"{verdict}{stars}  {snap.Message}{lesson}\n" +
                   $"Members broken: {snap.MembersLost}   ·   Highest load: {Mathf.RoundToInt((float)snap.PeakUtilization * 100)}%\n" +
                   "<size=70%>A: back to building</size>" + SolverStats();
        }

        string SolverStats()
        {
            if (!showSolverStats)
                return "";
            return $"\n<size=45%><color=#9A9A9A>{_worker.Members} members · step {_worker.StepMs:F2} ms · start {_worker.StartMs:F0} ms · preview {_worker.PreviewMs:F0} ms</color></size>";
        }

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
