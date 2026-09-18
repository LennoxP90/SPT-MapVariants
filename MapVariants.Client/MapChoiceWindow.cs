using System;
using System.Linq;
using MapVariants.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MapVariants.Client
{
    /// <summary>
    /// The map-version prompt, built from uGUI at runtime on its own overlay
    /// canvas in the game's faction-select look. Accept resolves with the
    /// selection, Back and Escape with null. True means BACKPORT throughout.
    /// </summary>
    internal sealed class MapChoiceWindow : MonoBehaviour
    {
        static MapChoiceWindow _open;

        Action<bool?> _done;
        bool _backport = true;
        Tile _left, _right;
        TMP_FontAsset _font;

        TextMeshProUGUI _accept;

        // Things the player should know before choosing the BACKPORT, supplied by a registered
        // provider. Empty is the normal case, and then nothing about this window changes.
        string[] _warnings = new string[0];
        TextMeshProUGUI _warning;

        sealed class Tile
        {
            public bool Backport;
            public Image Picture, Tag;
            public RectTransform Root;
            public TextMeshProUGUI Name, Description;
            public string Text;
        }

        static readonly Color Lit = Color.white;
        static readonly Color Dimmed = new Color(0.26f, 0.28f, 0.30f, 1f);
        static readonly Color Hovered = new Color(0.6f, 0.62f, 0.64f, 1f);
        static readonly Color Parchment = new Color(0.80f, 0.76f, 0.64f, 1f);
        static readonly Color Ink = new Color(0.12f, 0.11f, 0.09f, 1f);
        static readonly Color Muted = new Color(0.62f, 0.64f, 0.66f, 1f);
        static readonly Color Bright = new Color(0.93f, 0.94f, 0.95f, 1f);
        static readonly Color Caution = new Color(0.86f, 0.62f, 0.26f, 1f);
        static readonly Color BackdropColor = new Color(0.02f, 0.03f, 0.04f, 1f);
        static Sprite _edgeFade;
        static Sprite _tape;
        static bool _tapeLooked;

        // The game's own button tape, borrowed by name from the loaded UI atlas; a miss falls back to flat parchment.
        const string TapeSprite = "mainbutton_small_idle";
        const float TapeHeight = 33f;

        static Sprite Tape()
        {
            if (_tapeLooked) return _tape;
            _tapeLooked = true;
            _tape = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(sp => sp != null && sp.name == TapeSprite);
            Plugin.Log.LogDebug(_tape == null ? $"[MV] sprite '{TapeSprite}' not loaded; tags use a flat parchment block"
                                             : $"[MV] tag texture '{TapeSprite}' {_tape.rect.width}x{_tape.rect.height}, border {_tape.border}");
            return _tape;
        }

        static void DressAsTape(Image image, float height)
        {
            var tape = Tape();
            if (tape == null) { image.color = Parchment; return; }
            image.sprite = tape;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = TapeHeight / height;
            image.color = Color.white;
        }

        // Alpha mask: dark at the border, clearing quickly. The two axes multiply so the corners stay square.
        static Sprite EdgeFade()
        {
            if (_edgeFade != null) return _edgeFade;
            const int size = 256;
            const float bandX = 0.18f, bandY = 0.10f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var tx = 1f - Mathf.Clamp01(Mathf.Min(x, size - 1 - x) / (size * bandX));
                    var ty = 1f - Mathf.Clamp01(Mathf.Min(y, size - 1 - y) / (size * bandY));
                    var alpha = 1f - (1f - tx * tx) * (1f - ty * ty);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            texture.hideFlags = HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
            _edgeFade = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            _edgeFade.hideFlags = HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
            return _edgeFade;
        }

        internal static void Show(string title, Sprite originalPreview, string originalLabel, Sprite backportPreview, string backportLabel, string[] backportWarnings, Action<bool?> done)
        {
            if (_open != null) { Destroy(_open.gameObject); _open = null; }
            var host = new GameObject("MV_MapChoice", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (EventSystem.current == null)
                host.AddComponent<EventSystem>().gameObject.AddComponent<StandaloneInputModule>();
            _open = host.AddComponent<MapChoiceWindow>();
            _open._done = done;
            _open._warnings = WarningPolicy.Normalise(backportWarnings);
            _open.Build(title, originalPreview, originalLabel, backportPreview, backportLabel);
        }

        void Build(string title, Sprite originalPreview, string originalLabel, Sprite backportPreview, string backportLabel)
        {
            _font = GameFont();

            var backdrop = Rect("Backdrop", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            backdrop.gameObject.AddComponent<Image>().color = new Color(BackdropColor.r, BackdropColor.g, BackdropColor.b, 0.94f);

            Label("Title", transform, title, 44, Bright, new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(1400f, 60f), FontStyles.UpperCase);
            _left = MakeTile(originalPreview, originalLabel.ToUpperInvariant(), "The map SPT ships", new Vector2(-400f, 40f), backport: false);
            _right = MakeTile(backportPreview, backportLabel.ToUpperInvariant(), "The backported map", new Vector2(400f, 40f), backport: true);

            // Word wrapping, unlike every other label here, because this one is a sentence rather than
            // a caption and its length depends on what the provider has to say.
            _warning = Label("Warning", transform, WarningPolicy.Text(_warnings), 20, Caution,
                             new Vector2(0.5f, 0f), new Vector2(0f, 168f), new Vector2(1240f, 96f), FontStyles.Normal);
            _warning.enableWordWrapping = true;
            _warning.characterSpacing = 0f;

            TextButton("BACK", new Vector2(-160f, 58f), () => Close(null));
            _accept = TextButton("NEXT", new Vector2(160f, 58f), () => Close(_backport));

            Select(true);
        }

        Tile MakeTile(Sprite preview, string name, string description, Vector2 offset, bool backport)
        {
            var tile = new Tile { Backport = backport, Text = name };
            var root = Rect("Tile_" + name, transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(720f, 620f), offset);
            tile.Root = root;

            var pictureRect = Rect("Picture", root, new Vector2(0f, 0.3f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            tile.Picture = pictureRect.gameObject.AddComponent<Image>();
            tile.Picture.preserveAspect = true;
            if (preview != null) tile.Picture.sprite = preview;
            else tile.Picture.color = new Color(0.2f, 0.22f, 0.25f, 1f);

            var fadeRect = Rect("Fade", pictureRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var fade = fadeRect.gameObject.AddComponent<Image>();
            fade.sprite = EdgeFade();
            fade.color = BackdropColor;
            fade.raycastTarget = false;
            fade.type = Image.Type.Simple;

            var tagRect = Rect("Tag", root, new Vector2(0.5f, 0.3f), new Vector2(0.5f, 0.3f), new Vector2(300f, 48f), new Vector2(0f, -36f));
            tile.Tag = tagRect.gameObject.AddComponent<Image>();
            DressAsTape(tile.Tag, 48f);
            tile.Tag.raycastTarget = false;

            tile.Name = Label("Name", root, name, 26, Muted, new Vector2(0.5f, 0.3f), new Vector2(0f, -36f), new Vector2(680f, 48f), FontStyles.UpperCase);
            tagRect.sizeDelta = new Vector2(tile.Name.GetPreferredValues(name.ToUpperInvariant()).x + 96f, 48f);

            tile.Description = Label("Description", root, description, 20, Muted, new Vector2(0.5f, 0.3f), new Vector2(0f, -84f), new Vector2(640f, 32f), FontStyles.Normal);

            var button = root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => Select(backport));
            var hover = root.gameObject.AddComponent<HoverRelay>();
            hover.Enter = () => { if (_backport != backport) tile.Picture.color = Hovered; };
            hover.Exit = () => { if (_backport != backport) tile.Picture.color = Dimmed; };
            root.gameObject.AddComponent<Image>().color = Color.clear;   // the click target over the whole tile
            return tile;
        }

        void Select(bool backport)
        {
            _backport = backport;
            foreach (var tile in new[] { _left, _right })
            {
                var on = tile.Backport == backport;
                tile.Picture.color = tile.Picture.sprite == null ? new Color(0.2f, 0.22f, 0.25f, 1f) : (on ? Lit : Dimmed);
                tile.Root.localScale = Vector3.one * (on ? 1.04f : 0.96f);
                tile.Tag.enabled = on;
                tile.Name.color = on ? Ink : Muted;
                tile.Description.color = on ? Bright : Muted;
            }

            // The warning is about the backported tile only, and the button asks for a deliberate yes
            // while it is showing.
            if (_warning != null) _warning.enabled = WarningPolicy.Shows(backport, _warnings);
            if (_accept != null) _accept.text = WarningPolicy.AcceptCaption(backport, _warnings);
        }

        TextMeshProUGUI TextButton(string caption, Vector2 offset, Action onClick)
        {
            var rect = Rect("Button_" + caption, transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(240f, 56f), offset);
            rect.gameObject.AddComponent<Image>().color = Color.clear;   // the click target
            var tapeRect = Rect("Tape", rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var tape = tapeRect.gameObject.AddComponent<Image>();
            DressAsTape(tape, 56f);
            tape.raycastTarget = false;
            tape.enabled = false;
            var label = Label("Label", rect, caption, 34, Bright, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 56f), FontStyles.UpperCase);
            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => onClick());
            var hover = rect.gameObject.AddComponent<HoverRelay>();
            hover.Enter = () => { tape.enabled = true; label.color = Ink; };
            hover.Exit = () => { tape.enabled = false; label.color = Bright; };
            return label;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Close(null);
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Close(_backport);
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) Select(false);
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) Select(true);
        }

        void Close(bool? result)
        {
            var done = _done;
            _done = null;
            if (_open == this) _open = null;
            Destroy(gameObject);
            done?.Invoke(result);
        }

        // Every EFT screen title is set in Bender; upright, not the italic asset flagged "DON'T USE".
        static TMP_FontAsset GameFont()
        {
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Where(f => f != null && f.name != null).ToList();
            bool Usable(TMP_FontAsset f) => f.name.IndexOf("DON'T USE", StringComparison.OrdinalIgnoreCase) < 0
                                            && f.name.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) < 0;
            bool Bender(TMP_FontAsset f) => f.name.IndexOf("Bender", StringComparison.OrdinalIgnoreCase) >= 0;
            var pick = fonts.FirstOrDefault(f => Usable(f) && Bender(f) && f.name.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) < 0)
                    ?? fonts.FirstOrDefault(f => Usable(f) && Bender(f))
                    ?? fonts.FirstOrDefault(Usable)
                    ?? fonts.FirstOrDefault();
            Plugin.Log.LogDebug($"[MV] font '{pick?.name ?? "none"}'; loaded: {string.Join(" | ", fonts.Select(f => f.name))}");
            return pick;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta, Vector2 anchoredPosition)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
            return rect;
        }

        TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color, Vector2 anchor, Vector2 offset, Vector2 sizeDelta, FontStyles style)
        {
            var rect = Rect(name, parent, anchor, anchor, sizeDelta, offset);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) label.font = _font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.characterSpacing = 4f;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            return label;
        }

        sealed class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Action Enter, Exit;
            public void OnPointerEnter(PointerEventData eventData) => Enter?.Invoke();
            public void OnPointerExit(PointerEventData eventData) => Exit?.Invoke();
        }
    }
}
