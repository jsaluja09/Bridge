using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Learn mode, separate from the game: pick a famous bridge and click through how and why it was built. The
    /// bridge assembles on a tabletop in front of you, step by step, with highlights, labels, a chart and load
    /// demos. Stick + A picks a bridge; then A is next, B is back, X returns to the bridges (and X there returns to
    /// the start menu). Split across partial files: flow and text (here), drawing (LearnMode.Draw).
    /// </summary>
    [RequireComponent(typeof(ControllerPointer), typeof(BridgeView))]
    public sealed partial class LearnMode : MonoBehaviour
    {
        [SerializeField] float panelFontSize = 0.16f;
        [Tooltip("Widest the card's text gets before it wraps, room meters.")]
        [SerializeField] float panelTextWidth = 1.2f;                // GUESS
        [SerializeField] float labelFontSize = 0.12f;
        [Tooltip("Length of the tabletop each bridge is fitted to, room meters.")]
        [SerializeField] float tableLength = 1.5f;

        /// <summary>Raised when the player leaves Learn mode (X on the bridge list).</summary>
        public event System.Action ExitRequested;

        static readonly string PickerKeys = HudCard.Key("Stick", "Choose") + "   " + HudCard.Key("A", "Start") + "   " + HudCard.Key("X", "Menu");
        static readonly string StepKeys = HudCard.Key("A", "Next") + "   " + HudCard.Key("B", "Back") + "   " + HudCard.Key("X", "Bridges");
        static readonly string LastStepKeys = HudCard.Key("A", "Back to the bridges") + "   " + HudCard.Key("B", "Back") + "   " + HudCard.Key("X", "Bridges");

        ControllerPointer _pointer;
        BridgeView _view;
        HudCard _card;
        LabelPool _labels;
        LessonPlayer _player;
        IReadOnlyList<Lesson> _lessons;
        string[] _pickerRows;
        bool _inLesson;
        int _choice;
        HudCardContent _panelCard;
        int _panelFor = int.MinValue;   // which lesson step (or picker choice) the card was built for

        void Awake()
        {
            _pointer = GetComponent<ControllerPointer>();
            _view = GetComponent<BridgeView>();
            _lessons = Lessons.All();
            _pickerRows = _lessons.Select(l => $"{l.Title}   <size=75%>{l.Place}, {l.Year}</size>").ToArray();
            _player = new LessonPlayer(_lessons);
            _labels = new LabelPool(transform, labelFontSize);

            var go = new GameObject("Lesson Card");
            go.transform.SetParent(transform, false);
            _card = go.AddComponent<HudCard>();
            _card.MaxTextWidthMeters = panelTextWidth;
        }

        void OnEnable()
        {
            _inLesson = false;
            _panelFor = int.MinValue;
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            double now = Time.timeAsDouble;
            if (_inLesson)
                UpdateLesson(now, cam);
            else
                UpdatePicker(now, cam);
            Draw(now, cam);
        }

        // ---------- Picking a bridge ----------

        void UpdatePicker(double now, Camera cam)
        {
            if (_pointer.ClearPressed)
            {
                ExitRequested?.Invoke();
                return;
            }
            if (_pointer.StickStep != 0)
                _choice = (_choice + _lessons.Count + _pointer.StickStep) % _lessons.Count;
            if (_pointer.PrimaryPressed || _pointer.TriggerPressed)
            {
                _player.Choose(_choice, now);
                PlaceTable(_player.Lesson.Bridge, cam);
                _inLesson = true;
            }
        }

        HudCardContent PickerCard() => new HudCardContent
        {
            Kind = HudCardKind.Learn,
            Page = -1,
            Title = "Learn from famous bridges",
            Choices = _pickerRows,
            Selected = _choice,
            Controls = PickerKeys,
        };

        // ---------- A lesson ----------

        void UpdateLesson(double now, Camera cam)
        {
            if (_pointer.ClearPressed)
            {
                _inLesson = false;
                return;
            }
            if (_pointer.PrimaryPressed || _pointer.TriggerPressed)
            {
                if (!_player.Next(now))
                    _inLesson = false;          // finished: back to the bridges
            }
            else if (_pointer.SecondaryPressed)
            {
                _player.Back(now);
            }
        }

        /// <summary>A lesson step: where it is in the lesson, what to read, its formula, and a bar of steps done.</summary>
        HudCardContent StepCard(int page)
        {
            var lesson = _player.Lesson;
            var step = _player.Step;
            int count = lesson.Steps.Count, index = _player.StepIndex;
            return new HudCardContent
            {
                Kind = HudCardKind.Learn,
                Page = page,
                Header = $"{lesson.Title} · {lesson.Place}, {lesson.Year}",
                Title = step.Title,
                Body = step.Text,
                Formula = step.Formula,
                BarLabel = $"Step {index + 1} of {count}",
                BarFill = (index + 1) / (float)count,
                Controls = _player.AtLastStep ? LastStepKeys : StepKeys,
            };
        }

        /// <summary>The card above the table (or in front of you on the picker); its content is rebuilt only when the step or choice changes.</summary>
        void UpdatePanel(Camera cam)
        {
            int key = _inLesson ? _player.LessonIndex * 100 + _player.StepIndex : -1 - _choice;
            if (key != _panelFor)
            {
                _panelCard = _inLesson ? StepCard(key) : PickerCard();
                _panelFor = key;
            }
            _card.Show(_panelCard, panelFontSize);
            Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Vector3 at = _inLesson ? _table.Center + Vector3.up * 0.55f : cam.transform.position + forward * 0.9f;
            _card.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - cam.transform.position, Vector3.up));
        }
    }
}
