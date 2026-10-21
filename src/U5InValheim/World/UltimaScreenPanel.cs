using System;
using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Managers;
using U5InValheim.Data;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Screen = U5InValheim.Data.Screen;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// A whole-screen panel showing one of the original's 320×200 EGA screens (the introduction,
    /// the gypsy, the cut-scenes of the story), sharp-pixelled at the original's 4:3, with a line of
    /// hints and buttons under it. Space, Enter or a click go on; Escape skips; letters answer when
    /// the screen asks; a typed line (<see cref="Ask"/>) when it asks for words.
    /// </summary>
    public static class UltimaScreenPanel
    {
        static GameObject panel;
        static RawImage image;
        static Text hint;
        static InputField field;
        static Action<string> onTyped;
        static Texture2D texture;
        static readonly List<GameObject> buttons = new List<GameObject>();
        static Action onNext, onSkip;
        static Action<char> onLetter;
        static string letters = "";
        static int openedFrame;

        public static bool IsOpen => panel != null && panel.activeSelf;
        /// <summary>A line is being typed in (<see cref="Ask"/>).</summary>
        public static bool Asking => IsOpen && onTyped != null;

        public static void Install(Harmony harmony)
        {
            // Escape skips the screens, not into the game menu behind them
            var update = AccessTools.Method(typeof(Menu), "Update");
            if (update != null) harmony.Patch(update, prefix: new HarmonyMethod(typeof(UltimaScreenPanel), nameof(MenuUpdate_Prefix)));
        }

        static bool MenuUpdate_Prefix() => !IsOpen;

        /// <summary>
        /// Shows a screen. <paramref name="next"/>: Space/Enter/click (null: not offered);
        /// <paramref name="skip"/>: Escape (null: not offered); <paramref name="letter"/> with
        /// <paramref name="answers"/>: the letters that answer (as buttons too).
        /// </summary>
        public static bool Show(Screen screen, string hintText, Action next, Action skip, string answers = "", Action<char> letter = null)
        {
            if (!Build()) return false;
            Load(screen);
            hint.text = hintText ?? "";
            onTyped = null;
            field.gameObject.SetActive(false);
            onNext = next;
            onSkip = skip;
            onLetter = letter;
            letters = (answers ?? "").ToUpperInvariant();
            foreach (var b in buttons) Object.Destroy(b);
            buttons.Clear();
            var g = GUIManager.Instance;
            var choices = new List<(string, Action)>();
            foreach (char c in letters) { char k = c; choices.Add((k.ToString(), () => Letter(k))); }
            if (next != null) choices.Add(("Next", () => Next()));
            if (skip != null) choices.Add(("Skip", () => Skip()));
            for (int i = 0; i < choices.Count; i++)
            {
                var (text, click) = choices[i];
                var b = g.CreateButton(text, panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2((i - (choices.Count - 1) / 2f) * 130, 30), 120, 36);
                b.GetComponent<Button>().onClick.AddListener(() => click());
                buttons.Add(b);
            }
            if (!panel.activeSelf)
            {
                panel.SetActive(true);
                GUIManager.BlockInput(true);
            }
            openedFrame = Time.frameCount;
            return true;
        }

        /// <summary>The open screen's picture changed (a cut-scene going on), its hints and buttons as they are.</summary>
        public static void Redraw(Screen screen)
        {
            if (IsOpen) Load(screen);
        }

        static void Load(Screen screen)
        {
            var rgba = screen.Rgba();
            // Unity's rows run from the bottom
            var flipped = new byte[rgba.Length];
            int row = Screen.W * 4;
            for (int y = 0; y < Screen.H; y++) Buffer.BlockCopy(rgba, y * row, flipped, (Screen.H - 1 - y) * row, row);
            texture.LoadRawTextureData(flipped);
            texture.Apply(false);
        }

        /// <summary>A line to type under the screen (Blackthorn's "Your response?"); Enter hands it to <paramref name="typed"/>, Escape an empty one.</summary>
        public static bool Ask(Screen screen, string prompt, Action<string> typed)
        {
            if (!Show(screen, prompt + "      Enter: answer", null, null)) return false;
            onTyped = typed;
            field.text = "";
            field.gameObject.SetActive(true);
            field.Select();
            field.ActivateInputField();
            return true;
        }

        /// <summary>Answers the open question as if typed (the console's u5 type, for tests).</summary>
        public static bool Type(string text)
        {
            if (!Asking) return false;
            Typed(text);
            return true;
        }

        static void Typed(string text)
        {
            var a = onTyped;
            onTyped = null;
            field.gameObject.SetActive(false);
            a?.Invoke(text ?? "");
        }

        public static void Close()
        {
            if (!IsOpen) return;
            panel.SetActive(false);
            GUIManager.BlockInput(false);
            onNext = onSkip = null;
            onLetter = null;
            onTyped = null;
        }

        /// <summary>What a key or button does, by name: "next", "skip" or an answer's letter (the console's u5 press, for tests).</summary>
        public static bool Press(string what)
        {
            if (!IsOpen || string.IsNullOrEmpty(what)) return false;
            if (what == "next" && onNext != null) Next();
            else if (what == "skip" && onSkip != null) Skip();
            else if (what.Length == 1 && letters.IndexOf(char.ToUpperInvariant(what[0])) >= 0) Letter(char.ToUpperInvariant(what[0]));
            else return false;
            return true;
        }

        static void Next() { var a = onNext; a?.Invoke(); }
        static void Skip() { var a = onSkip; a?.Invoke(); }
        static void Letter(char c) { var a = onLetter; a?.Invoke(c); }

        static bool Build()
        {
            if (panel != null) return true;
            var gui = GUIManager.CustomGUIFront;
            if (gui == null) return false;
            var g = GUIManager.Instance;
            panel = new GameObject("U5Screen", typeof(RectTransform));
            panel.transform.SetParent(gui.transform, false);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            panel.AddComponent<Image>().color = Color.black;
            // (the fitter fills its parent, whatever its own anchors say: the frame keeps the strip
            // under the picture free for the hints and buttons)
            var frame = new GameObject("frame", typeof(RectTransform));
            frame.transform.SetParent(panel.transform, false);
            var frt = frame.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0.03f, 0f); frt.anchorMax = new Vector2(0.97f, 1f); frt.offsetMin = new Vector2(0, 110); frt.offsetMax = new Vector2(0, -10);
            var pic = new GameObject("picture", typeof(RectTransform));
            pic.transform.SetParent(frame.transform, false);
            texture = new Texture2D(Screen.W, Screen.H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            image = pic.AddComponent<RawImage>();
            image.texture = texture;
            var fit = pic.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 4f / 3f; // the original's screen
            hint = g.CreateText("", panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 75), g.AveriaSerifBold, 18, g.ValheimOrange, true, Color.black, 900, 30, false).GetComponent<Text>();
            hint.alignment = TextAnchor.MiddleCenter;
            var input = g.CreateInputField(panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 30), InputField.ContentType.Standard, "type, then Enter", 18, 380, 34);
            field = input.GetComponent<InputField>();
            field.characterLimit = 30;
            field.onEndEdit.AddListener(text =>
            {
                if (onTyped != null && (ZInput.GetKeyDown(KeyCode.Return) || ZInput.GetKeyDown(KeyCode.KeypadEnter))) Typed(text);
            });
            input.SetActive(false);
            panel.AddComponent<Keys>();
            panel.SetActive(false);
            return true;
        }

        class Keys : MonoBehaviour
        {
            void Update()
            {
                // (the key that opened it is not the first answer)
                if (Time.frameCount <= openedFrame + 1) return;
                if (onTyped != null)
                {
                    // (the letters go into the line; Escape is the empty answer)
                    if (ZInput.GetKeyDown(KeyCode.Escape)) Typed("");
                    else if (!field.isFocused) { field.Select(); field.ActivateInputField(); }
                    return;
                }
                if (ZInput.GetKeyDown(KeyCode.Escape)) { if (onSkip != null) Skip(); else if (onNext != null && letters.Length == 0) Next(); return; }
                foreach (char c in letters)
                    if (ZInput.GetKeyDown((KeyCode)char.ToLowerInvariant(c))) { Letter(c); return; }
                if (onNext != null && (ZInput.GetKeyDown(KeyCode.Space) || ZInput.GetKeyDown(KeyCode.Return) || ZInput.GetKeyDown(KeyCode.KeypadEnter))) Next();
            }
        }
    }
}
