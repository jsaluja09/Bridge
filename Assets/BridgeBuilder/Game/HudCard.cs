using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace BridgeBuilder.Game
{
    /// <summary>What a HUD card is about. Each kind has its own accent color; switching kind fades the new card in.</summary>
    public enum HudCardKind { Intro, Instructions, Testing, Passed, Failed, Results, Menu, Learn }

    /// <summary>
    /// What one HUD card shows, top to bottom. A null row is left out. Every row but Header and Title may hold
    /// rich text; the card colors those two itself.
    /// </summary>
    public struct HudCardContent
    {
        public HudCardKind Kind;
        public int Page;                  // changing it fades the card in, as a new kind does
        public string Toast;              // a short warning, in the amber strip across the top
        public string Header;             // small, above the title
        public string Title;
        public string Body;
        public string Formula;            // in a strip tinted with the accent, below the body
        public string[] Choices;          // one row each; keep the same array while the options stay the same
        public int Selected;              // the row filled with the accent
        public string BarLabel;           // null: no progress bar
        public float BarFill;             // 0..1
        public string Details;            // below the bar
        public string Controls;           // HudCard.Key badges
        public string Error;
        public string Footer;             // small and faint, at the bottom
    }

    /// <summary>
    /// A floating HUD card: a rounded, dark, slightly see-through panel with a soft shadow, a band in the card's
    /// accent color along the top, and optional choice rows, formula strip and progress bar, sized to fit its text.
    /// Built in code: one vertex-colored mesh for the shapes and TextMeshPro for the text. A new kind of card (or a
    /// new page) fades in with a small pop. Lengths are meters at font size 0.22 and scale with the font size.
    /// </summary>
    public sealed class HudCard : MonoBehaviour
    {
        [Header("Size")]
        [Tooltip("Text wider than this wraps.")]
        [SerializeField] float maxTextWidth = 1.1f;                                  // GUESS
        [SerializeField] float minTextWidth = 0.3f;                                  // GUESS
        [SerializeField] float padding = 0.04f;                                      // GUESS
        [SerializeField] float cornerRadius = 0.035f;                                // GUESS
        [SerializeField] float bandHeight = 0.012f;                                  // GUESS
        [Tooltip("Above and below the warning text, inside its strip.")]
        [SerializeField] float toastPadding = 0.014f;                                // GUESS
        [Tooltip("Between the text blocks, the bar and the footer.")]
        [SerializeField] float blockGap = 0.012f;                                    // GUESS
        [Tooltip("Between the bar's label and the bar.")]
        [SerializeField] float barGap = 0.008f;                                      // GUESS
        [SerializeField] float barHeight = 0.014f;                                   // GUESS
        [Tooltip("Around the text inside a choice row or the formula strip.")]
        [SerializeField] float rowPadding = 0.012f;                                  // GUESS
        [Tooltip("Between choice rows.")]
        [SerializeField] float rowGap = 0.008f;                                      // GUESS
        [SerializeField] Vector2 shadowOffset = new Vector2(0.006f, -0.01f);         // GUESS
        [SerializeField] float shadowSoftness = 0.03f;                               // GUESS
        [Tooltip("How far the panel sits behind the text. Local +Z points away from the player.")]
        [SerializeField] float panelDepth = 0.004f;                                  // GUESS
        [SerializeField] float shadowDepth = 0.008f;                                 // GUESS

        [Header("Text, as shares of the font size")]
        [SerializeField] float headerSize = 0.7f;                                    // GUESS
        [SerializeField] float titleSize = 1.1f;                                     // GUESS
        [SerializeField] float barLabelSize = 0.7f;                                  // GUESS
        [SerializeField] float controlsSize = 0.75f;                                 // GUESS
        [SerializeField] float toastSize = 0.9f;                                     // GUESS
        [SerializeField] float choiceSize = 0.95f;                                   // GUESS
        [SerializeField] float formulaSize = 0.9f;                                   // GUESS
        [SerializeField] float footerSize = 0.45f;
        [SerializeField] Color textColor = new Color(0.93f, 0.95f, 0.98f);           // GUESS
        [SerializeField] Color toastTextColor = new Color32(0x2A, 0x1A, 0x00, 0xFF); // GUESS
        [Tooltip("Text on the selected choice row, over the accent.")]
        [SerializeField] Color selectedTextColor = new Color32(0x14, 0x16, 0x1C, 0xFF); // GUESS
        [SerializeField] Color footerColor = new Color32(0x9A, 0x9A, 0x9A, 0xFF);

        [Header("Panel")]
        [SerializeField] Color bodyColor = new Color32(0x16, 0x1A, 0x24, 0xE0);      // GUESS: alpha 0.88
        [Tooltip("How much of the accent color tints the body.")]
        [Range(0f, 1f)] [SerializeField] float bodyTint = 0.08f;                     // GUESS
        [SerializeField] Color shadowColor = new Color(0f, 0f, 0f, 0.35f);           // GUESS
        [SerializeField] Color toastColor = new Color32(0xFF, 0xB5, 0x47, 0xF2);     // GUESS: alpha 0.95
        [SerializeField] Color barTrackColor = new Color(1f, 1f, 1f, 0.12f);         // GUESS
        [Tooltip("Choice rows that are not selected.")]
        [SerializeField] Color rowColor = new Color(1f, 1f, 1f, 0.07f);              // GUESS
        [Tooltip("How strongly the accent shows behind the formula.")]
        [Range(0f, 1f)] [SerializeField] float formulaTint = 0.18f;                  // GUESS

        [Header("Accents")]
        [SerializeField] Color introColor = new Color32(0x8B, 0x7C, 0xF6, 0xFF);        // GUESS: design pick
        [SerializeField] Color instructionsColor = new Color32(0x4D, 0xA8, 0xF7, 0xFF); // GUESS: design pick
        [SerializeField] Color testingColor = new Color32(0x2E, 0xC4, 0xB6, 0xFF);      // GUESS: design pick
        [SerializeField] Color passedColor = new Color32(0x5C, 0xD2, 0x7E, 0xFF);       // GUESS: design pick
        [SerializeField] Color failedColor = new Color32(0xFF, 0x6B, 0x6B, 0xFF);       // GUESS: design pick
        [SerializeField] Color resultsColor = new Color32(0xFF, 0xC9, 0x4A, 0xFF);      // GUESS: design pick
        [SerializeField] Color menuColor = new Color32(0xFF, 0x8A, 0x3D, 0xFF);         // GUESS: design pick
        [SerializeField] Color learnColor = new Color32(0xA3, 0xE6, 0x35, 0xFF);        // GUESS: design pick

        [Header("Motion")]
        [SerializeField] float fadeSeconds = 0.2f;                                   // GUESS
        [Tooltip("The scale a new card starts at before it settles to full size.")]
        [SerializeField] float popScale = 0.92f;                                     // GUESS

        const float ReferenceFontSize = 0.22f;
        const int CornerSteps = 6;
        const float Unbounded = 100f;
        // TMP wraps a line exactly as wide as its box; the slack keeps a measured line on one line.
        const float WrapSlack = 0.002f;
        const string BadgeOpen = "<nobr><mark=#FFFFFF33>\u00A0<b>";                // GUESS: white at 20% over the panel
        const string ErrorOpen = "<color=#FF5050>";

        static readonly float[] ArcSin = ArcSines();

        TextMeshPro _toast, _upper, _formula, _barText, _lower, _footer;
        Vector2 _toastSize, _upperSize, _formulaSize, _barTextSize, _lowerSize, _footerSize;
        readonly List<TextMeshPro> _rows = new List<TextMeshPro>();
        readonly List<Vector2> _rowSizes = new List<Vector2>();
        readonly List<float> _rowTops = new List<float>();
        Mesh _mesh;
        Material _material;
        bool _linear;
        readonly List<Vector3> _vertices = new List<Vector3>(512);
        readonly List<Color32> _colors = new List<Color32>(512);
        readonly List<int> _indices = new List<int>(1536);
        readonly StringBuilder _text = new StringBuilder(512);

        HudCardContent _shown;
        string _accentHex;
        bool _started;
        float _fontSize;
        float _unit = 1f;                     // font size over the reference font size
        float _fill;
        float _kindChangedAt;
        float _fade = -1f;
        float _maxWidthMeters;                // 0: maxTextWidth at the font size

        // Card-local meters with the origin in the card's middle; tops are measured down from the card's top edge.
        float _width, _height, _toastHeight, _formulaTop, _barTop;
        bool _bar;

        /// <summary>The middle of the card's top edge in world space, as last shown (for things drawn above it).</summary>
        public Vector3 TopCenter => transform.TransformPoint(0f, _height * 0.5f, 0f);

        /// <summary>Widest the text gets before it wraps, in meters whatever the font size; 0 goes back to maxTextWidth.</summary>
        public float MaxTextWidthMeters
        {
            set
            {
                _maxWidthMeters = value;
                _started = false;
            }
        }

        /// <summary>A controller button as a small key badge followed by what it does, kept on one line.</summary>
        public static string Key(string key, string action) => BadgeOpen + key + "</b>\u00A0</mark> " + action + "</nobr>";

        void Awake()
        {
            _linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            _material = new Material(Shader.Find("BridgeBuilder/HudCard") ?? Shader.Find("Sprites/Default"));
            _mesh = new Mesh { name = "HUD Card" };
            _mesh.MarkDynamic();
            var panel = new GameObject("Card Panel");
            panel.transform.SetParent(transform, false);
            panel.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = panel.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            _toast = NewText("Card Warning", toastTextColor);
            _upper = NewText("Card Text", textColor);
            _formula = NewText("Card Formula", textColor);
            _barText = NewText("Card Bar Label", textColor);
            _lower = NewText("Card Controls", textColor);
            _footer = NewText("Card Footer", footerColor);
        }

        /// <summary>A card shown again (say, back at the menu) is laid out afresh and fades in.</summary>
        void OnEnable() => _started = false;

        void OnDestroy()
        {
            Destroy(_mesh);
            Destroy(_material);
        }

        TextMeshPro NewText(string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var text = go.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.color = color;
            text.rectTransform.pivot = new Vector2(0.5f, 1f);
            go.SetActive(false);
            return text;
        }

        /// <summary>Shows this content at this font size. Call every frame: only what changed is laid out again.</summary>
        public void Show(in HudCardContent content, float fontSize)
        {
            bool all = !_started || fontSize != _fontSize;
            bool kind = all || content.Kind != _shown.Kind;
            bool toast = all || content.Toast != _shown.Toast;
            bool upper = kind || content.Header != _shown.Header || content.Title != _shown.Title || content.Body != _shown.Body;
            bool formula = all || content.Formula != _shown.Formula;
            bool rows = all || !ReferenceEquals(content.Choices, _shown.Choices) || content.Selected != _shown.Selected;
            bool barText = all || content.BarLabel != _shown.BarLabel;
            bool lower = all || content.Details != _shown.Details || content.Controls != _shown.Controls
                         || content.Error != _shown.Error;
            bool footer = all || content.Footer != _shown.Footer;
            float fill = float.IsNaN(content.BarFill) ? 0f : Mathf.Clamp01(content.BarFill);
            bool layout = toast || upper || formula || rows || barText || lower || footer;

            if (!_started || content.Kind != _shown.Kind || content.Page != _shown.Page)
                _kindChangedAt = Time.time;
            _started = true;
            _shown = content;
            _fontSize = fontSize;
            _unit = fontSize / ReferenceFontSize;
            bool panel = layout || fill != _fill;
            _fill = fill;
            _bar = !string.IsNullOrEmpty(content.BarLabel);

            if (toast)
            {
                _text.Clear();
                if (!string.IsNullOrEmpty(content.Toast))
                    _text.Append(content.Toast);
                _toastSize = Fill(_toast, toastSize);
            }
            if (kind)
                _accentHex = ColorUtility.ToHtmlStringRGB(Accent(content.Kind));
            if (upper)
            {
                _text.Clear();
                if (!string.IsNullOrEmpty(content.Header))
                    AppendSize(headerSize).Append("<color=#").Append(_accentHex).Append('>').Append(content.Header).Append("</color></size>");
                NextLine();
                AppendSize(titleSize).Append("<b><color=#").Append(_accentHex).Append('>').Append(content.Title).Append("</color></b></size>");
                if (!string.IsNullOrEmpty(content.Body))
                    NextLine().Append(content.Body);
                _upperSize = Fill(_upper, 1f);
            }
            if (formula)
            {
                _text.Clear();
                if (!string.IsNullOrEmpty(content.Formula))
                    _text.Append(content.Formula);
                _formulaSize = Fill(_formula, formulaSize, rowPadding * _unit);
            }
            if (rows)
                FillRows(content.Choices, content.Selected);
            if (barText)
            {
                _text.Clear();
                if (_bar)
                    _text.Append(content.BarLabel);
                _barTextSize = Fill(_barText, barLabelSize);
            }
            if (lower)
            {
                _text.Clear();
                if (!string.IsNullOrEmpty(content.Details))
                    _text.Append(content.Details);
                if (!string.IsNullOrEmpty(content.Controls))
                {
                    NextLine();
                    AppendSize(controlsSize).Append(content.Controls).Append("</size>");
                }
                if (!string.IsNullOrEmpty(content.Error))
                    NextLine().Append(ErrorOpen).Append(content.Error).Append("</color>");
                _lowerSize = Fill(_lower, 1f);
            }
            if (footer)
            {
                _text.Clear();
                if (!string.IsNullOrEmpty(content.Footer))
                    _text.Append(content.Footer);
                _footerSize = Fill(_footer, footerSize);
            }

            if (layout)
                Layout();
            if (panel)
                BuildPanel();
            Fade();
        }

        StringBuilder NextLine() => _text.Length > 0 ? _text.Append('\n') : _text;

        StringBuilder AppendSize(float share) => _text.Append("<size=").Append(Mathf.RoundToInt(share * 100f)).Append("%>");

        int RowCount => _shown.Choices?.Length ?? 0;

        /// <summary>One text block per choice (made as needed); the selected one is bold, in dark text over the accent.</summary>
        void FillRows(string[] choices, int selected)
        {
            int count = choices?.Length ?? 0;
            while (_rows.Count < count)
                _rows.Add(NewText("Card Choice", textColor));
            _rowSizes.Clear();
            for (int i = 0; i < _rows.Count; i++)
            {
                _text.Clear();
                if (i < count)
                    _text.Append(i == selected ? "<b>" : "").Append(choices[i]).Append(i == selected ? "</b>" : "");
                _rowSizes.Add(Fill(_rows[i], choiceSize, rowPadding * _unit));
                _rows[i].color = i == selected ? selectedTextColor : textColor;
            }
            _fade = -1f;                      // setting a color resets the text's alpha; Fade puts it back
        }

        /// <summary>Puts the composed text into a block, or hides the block when there is none; returns the text's size.</summary>
        Vector2 Fill(TextMeshPro block, float sizeShare, float inset = 0f)
        {
            bool show = _text.Length > 0;
            block.gameObject.SetActive(show);
            if (!show)
                return Vector2.zero;
            float maxWidth = (_maxWidthMeters > 0f ? _maxWidthMeters : maxTextWidth * _unit) - 2f * inset;
            block.fontSize = _fontSize * sizeShare;
            block.text = _text.ToString();
            Vector2 size = block.GetPreferredValues(maxWidth + WrapSlack, Unbounded);
            return new Vector2(Mathf.Min(size.x, maxWidth), size.y);
        }

        /// <summary>Stacks the blocks top to bottom: warning strip, band, text, formula, choices, bar, controls, footer.</summary>
        void Layout()
        {
            float u = _unit, pad = padding * u, gap = blockGap * u, inner = rowPadding * u;
            int rows = RowCount;
            float width = Mathf.Max(Mathf.Max(minTextWidth * u, _toastSize.x), Mathf.Max(Mathf.Max(_upperSize.x, _lowerSize.x), _footerSize.x));
            width = Mathf.Max(width, _barTextSize.x);
            if (_formulaSize.y > 0f)
                width = Mathf.Max(width, _formulaSize.x + 2f * inner);
            for (int i = 0; i < rows; i++)
                width = Mathf.Max(width, _rowSizes[i].x + 2f * inner);
            _width = width + 2f * pad;

            float down = 0f, toastTop = 0f, barTextTop = 0f, lowerTop = 0f, footerTop = 0f;
            _toastHeight = 0f;
            if (_toastSize.y > 0f)
            {
                toastTop = toastPadding * u;
                _toastHeight = _toastSize.y + 2f * toastTop;
                down = _toastHeight;
            }
            down += bandHeight * u + pad;
            float upperTop = down;
            down += _upperSize.y;
            if (_formulaSize.y > 0f)
            {
                down += gap;
                _formulaTop = down;
                down += _formulaSize.y + 2f * inner;
            }
            _rowTops.Clear();
            for (int i = 0; i < rows; i++)
            {
                down += i == 0 ? gap : rowGap * u;
                _rowTops.Add(down);
                down += _rowSizes[i].y + 2f * inner;
            }
            if (_bar)
            {
                down += gap;
                barTextTop = down;
                down += _barTextSize.y + barGap * u;
                _barTop = down;
                down += barHeight * u;
            }
            if (_lowerSize.y > 0f)
            {
                down += gap;
                lowerTop = down;
                down += _lowerSize.y;
            }
            if (_footerSize.y > 0f)
            {
                down += gap;
                footerTop = down;
                down += _footerSize.y;
            }
            _height = down + pad;

            Place(_toast, toastTop, _toastSize.y, width);
            Place(_upper, upperTop, _upperSize.y, width);
            Place(_formula, _formulaTop + inner, _formulaSize.y, width - 2f * inner);
            for (int i = 0; i < rows; i++)
                Place(_rows[i], _rowTops[i] + inner, _rowSizes[i].y, width - 2f * inner);
            Place(_barText, barTextTop, _barTextSize.y, width);
            Place(_lower, lowerTop, _lowerSize.y, width);
            Place(_footer, footerTop, _footerSize.y, width);
        }

        void Place(TextMeshPro block, float top, float height, float width)
        {
            if (height <= 0f)
                return;
            block.rectTransform.sizeDelta = new Vector2(width + WrapSlack, height);
            block.transform.localPosition = new Vector3(0f, _height * 0.5f - top, 0f);
        }

        /// <summary>The shapes, back to front: shadow, body, warning strip, accent band, formula strip, choice rows, bar track, bar fill.</summary>
        void BuildPanel()
        {
            _vertices.Clear();
            _colors.Clear();
            _indices.Clear();
            float u = _unit, halfW = _width * 0.5f, halfH = _height * 0.5f, radius = cornerRadius * u;
            Color accent = Accent(_shown.Kind);
            accent.a = 1f;
            Color body = Color.Lerp(bodyColor, accent, bodyTint);
            body.a = bodyColor.a;

            Vector2 s = shadowOffset * u;
            SoftRect(s.x - halfW, s.x + halfW, s.y - halfH, s.y + halfH, radius, shadowSoftness * u, shadowColor, shadowDepth);
            Section(-halfW, halfW, -halfH, halfH, radius, -halfH, halfH, body, panelDepth);
            float bandTop = halfH - _toastHeight;
            if (_toastHeight > 0f)
                Section(-halfW, halfW, -halfH, halfH, radius, bandTop, halfH, toastColor, panelDepth);
            Section(-halfW, halfW, -halfH, halfH, radius, bandTop - bandHeight * u, bandTop, accent, panelDepth);

            float left = -halfW + padding * u, right = halfW - padding * u, inner = rowPadding * u, rowRadius = radius * 0.5f;
            if (_formulaSize.y > 0f)
            {
                float top = halfH - _formulaTop, bottom = top - _formulaSize.y - 2f * inner;
                Color tint = accent;
                tint.a = formulaTint;
                Section(left, right, bottom, top, rowRadius, bottom, top, tint, panelDepth);
            }
            for (int i = 0; i < RowCount; i++)
            {
                float top = halfH - _rowTops[i], bottom = top - _rowSizes[i].y - 2f * inner;
                Section(left, right, bottom, top, rowRadius, bottom, top, i == _shown.Selected ? accent : rowColor, panelDepth);
            }

            if (_bar)
            {
                float top = halfH - _barTop, height = barHeight * u, bottom = top - height;
                Section(left, right, bottom, top, height * 0.5f, bottom, top, barTrackColor, panelDepth);
                if (_fill > 0f)
                    Section(left, Mathf.Lerp(left + height, right, _fill), bottom, top, height * 0.5f, bottom, top, accent, panelDepth);
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_indices, 0);
        }

        /// <summary>The slice of a rounded rectangle between heights y0 and y1, in one flat color.</summary>
        void Section(float left, float right, float bottom, float top, float radius, float y0, float y1, Color color, float z)
        {
            radius = Mathf.Max(0f, Mathf.Min(radius, Mathf.Min(right - left, top - bottom) * 0.5f));
            y0 = Mathf.Max(y0, bottom);
            y1 = Mathf.Min(y1, top);
            if (y1 - y0 < 1e-5f)
                return;
            float middle = (left + right) * 0.5f, half = (right - left) * 0.5f;
            Color32 c = ToVertex(color);
            float previous = y0;
            for (int i = CornerSteps; i >= 0; i--)
                Cut(bottom + radius - radius * ArcSin[i]);
            for (int i = 0; i <= CornerSteps; i++)
                Cut(top - radius + radius * ArcSin[i]);
            Row(previous, y1);

            void Cut(float y)
            {
                if (y <= previous || y >= y1)
                    return;
                Row(previous, y);
                previous = y;
            }

            void Row(float ya, float yb)
            {
                float wa = HalfWidth(ya), wb = HalfWidth(yb);
                Quad(new Vector3(middle - wa, ya, z), new Vector3(middle + wa, ya, z),
                     new Vector3(middle + wb, yb, z), new Vector3(middle - wb, yb, z), c, c);
            }

            float HalfWidth(float y)
            {
                float dy = y - Mathf.Clamp(y, bottom + radius, top - radius);
                return half - radius + Mathf.Sqrt(Mathf.Max(0f, radius * radius - dy * dy));
            }
        }

        /// <summary>A rounded rectangle whose edge fades to nothing over `soft` meters: the shadow.</summary>
        void SoftRect(float left, float right, float bottom, float top, float radius, float soft, Color color, float z)
        {
            radius = Mathf.Max(0f, Mathf.Min(radius, Mathf.Min(right - left, top - bottom) * 0.5f));
            Section(left, right, bottom, top, radius, bottom, top, color, z);
            if (soft <= 0f)
                return;
            Color32 inner = ToVertex(color), outer = inner;
            outer.a = 0;
            int count = 4 * (CornerSteps + 1);
            for (int k = 0; k < count; k++)
            {
                Edge(k, out var a, out var aOut);
                Edge((k + 1) % count, out var b, out var bOut);
                Quad(a, b, bOut, aOut, inner, outer);
            }

            // Perimeter point k, corners counter-clockwise from the top right, and the same point pushed out by soft.
            void Edge(int k, out Vector3 inside, out Vector3 outside)
            {
                int corner = k / (CornerSteps + 1), step = k % (CornerSteps + 1);
                float angle = (corner + step / (float)CornerSteps) * Mathf.PI * 0.5f;
                float cx = corner == 0 || corner == 3 ? right - radius : left + radius;
                float cy = corner < 2 ? top - radius : bottom + radius;
                float dx = Mathf.Cos(angle), dy = Mathf.Sin(angle);
                inside = new Vector3(cx + dx * radius, cy + dy * radius, z);
                outside = new Vector3(cx + dx * (radius + soft), cy + dy * (radius + soft), z);
            }
        }

        /// <summary>Corners a, b, c, d in order; a and b take the first color, c and d the second.</summary>
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 ab, Color32 cd)
        {
            int start = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _vertices.Add(d);
            _colors.Add(ab);
            _colors.Add(ab);
            _colors.Add(cd);
            _colors.Add(cd);
            _indices.Add(start);
            _indices.Add(start + 1);
            _indices.Add(start + 2);
            _indices.Add(start);
            _indices.Add(start + 2);
            _indices.Add(start + 3);
        }

        /// <summary>Alpha 0 to 1 and a small pop in scale over fadeSeconds after the card changes kind or page.</summary>
        void Fade()
        {
            float t = fadeSeconds > 0f ? Mathf.Clamp01((Time.time - _kindChangedAt) / fadeSeconds) : 1f;
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            if (eased == _fade)
                return;
            _fade = eased;
            transform.localScale = Vector3.one * Mathf.LerpUnclamped(popScale, 1f, eased);
            _material.color = new Color(1f, 1f, 1f, eased);
            _toast.alpha = eased;
            _upper.alpha = eased;
            _formula.alpha = eased;
            _barText.alpha = eased;
            _lower.alpha = eased;
            _footer.alpha = eased;
            foreach (var row in _rows)
                row.alpha = eased;
        }

        /// <summary>TMP turns its colors linear in a linear-space project; the panel matches so accents agree.</summary>
        Color32 ToVertex(Color color) => _linear ? color.linear : color;

        Color Accent(HudCardKind kind) => kind switch
        {
            HudCardKind.Intro => introColor,
            HudCardKind.Instructions => instructionsColor,
            HudCardKind.Testing => testingColor,
            HudCardKind.Passed => passedColor,
            HudCardKind.Failed => failedColor,
            HudCardKind.Results => resultsColor,
            HudCardKind.Menu => menuColor,
            _ => learnColor,
        };

        static float[] ArcSines()
        {
            var sines = new float[CornerSteps + 1];
            for (int i = 0; i <= CornerSteps; i++)
                sines[i] = Mathf.Sin(i * Mathf.PI * 0.5f / CornerSteps);
            return sines;
        }
    }
}
