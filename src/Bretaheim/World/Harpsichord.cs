using System.Collections;
using System.Collections.Generic;
using Bretaheim.Data;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// The harpsichord (tile 0x8d, Valheim's black forge; look "harpsichord"): E asks for notes,
    /// the digits 1..9 as the original's number keys, and plays them. The melody (<see cref="Melody"/>)
    /// played in full opens the wall the harpsichord guards (look "tune", Lord British's castle,
    /// third floor) with a quake, or shuts it again, as the original toggles that tile. Its forge
    /// is gone: it is no crafting station.
    /// </summary>
    public class Harpsichord : MonoBehaviour, Hoverable, Interactable, TextReceiver
    {
        /// <summary>How far the walls it opens may be (the castle's harpsichord and its wall share a floor).</summary>
        const float Reach = 40f;
        const float NoteGap = 0.32f;
        static Melody melody;
        static readonly Dictionary<int, AudioClip> notes = new Dictionary<int, AudioClip>();
        bool playing;
        AudioSource source;

        void Awake()
        {
            var station = GetComponent<CraftingStation>();
            if (station != null) Destroy(station);
        }

        public string GetHoverText() => Localization.instance.Localize("Harpsichord\n[<color=yellow><b>$KEY_Use</b></color>] Play");
        public string GetHoverName() => "Harpsichord";
        public float GetHoverOffset() => 1f;
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player player) || player != Player.m_localPlayer || playing) return false;
            TextInput.instance?.RequestText(this, "Play the harpsichord (keys 1 to 9)", 40);
            return true;
        }

        public string GetText() => "";

        public void SetText(string text)
        {
            var keys = new List<int>();
            foreach (char ch in text ?? "") if (ch >= '0' && ch <= '9') keys.Add(ch - '0');
            if (keys.Count > 0 && !playing) StartCoroutine(Play(keys));
        }

        IEnumerator Play(List<int> keys)
        {
            playing = true;
            if (melody == null) melody = new Melody(Plugin.U5.Ovl);
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.spatialBlend = 1f;
                source.maxDistance = 30f;
                source.rolloffMode = AudioRolloffMode.Linear;
            }
            foreach (int n in keys)
            {
                source.PlayOneShot(Note(n), 0.6f);
                bool done = melody.Play(n);
                yield return new WaitForSeconds(NoteGap);
                if (done) Toggle();
            }
            playing = false;
        }

        /// <summary>The walls it guards open, or shut when open; the floor shakes.</summary>
        void Toggle()
        {
            var walls = new List<TuneWall>();
            foreach (var w in TuneWall.All) if (w != null && Vector3.Distance(w.transform.position, transform.position) < Reach) walls.Add(w);
            if (walls.Count == 0) return;
            bool open = !walls[0].Open;
            foreach (var w in walls) w.Set(open);
            GameCamera.instance?.AddShake(transform.position, 50f, 4f, false);
            var sfx = ZNetScene.instance?.GetPrefab("sfx_stonedoor_sliding_open");
            if (sfx != null) Instantiate(sfx, walls[0].transform.position, Quaternion.identity);
            Plugin.Log.LogInfo($"Harpsichord: the melody {(open ? "opens" : "shuts")} the wall ({walls.Count} bricks)");
        }

        /// <summary>A plucked note at the original's pitch for digit n (its scale rises from 1), harpsichord-like: bright harmonics dying away fast.</summary>
        static AudioClip Note(int n)
        {
            if (notes.TryGetValue(n, out var clip)) return clip;
            const int rate = 44100;
            float freq = Melody.Frequency(Plugin.U5.Ovl, n) / 8f;
            int length = (int)(rate * 0.9f);
            var data = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / rate, v = 0;
                for (int h = 1; h <= 7; h++) v += Mathf.Sin(2 * Mathf.PI * freq * h * t) / h * Mathf.Exp(-t * (3f + h));
                data[i] = v * 0.35f * Mathf.Min(1f, t * 400f);
            }
            clip = AudioClip.Create($"u5_note_{n}", length, 1, rate, false);
            clip.SetData(data, 0);
            return notes[n] = clip;
        }
    }

    /// <summary>A brick of a wall the harpsichord opens (look "tune"): seen and solid while shut; its state is in its ZDO ("u5_tune_open").</summary>
    public class TuneWall : MonoBehaviour
    {
        public const string OpenKey = "u5_tune_open";
        public static readonly List<TuneWall> All = new List<TuneWall>();
        ZNetView nview;
        Renderer[] renderers;
        Collider[] colliders;
        bool? shown;
        float checkAt;

        public bool Open => nview?.GetZDO()?.GetBool(OpenKey) ?? false;

        void Awake()
        {
            nview = GetComponent<ZNetView>();
            renderers = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
            All.Add(this);
        }

        void OnDestroy() => All.Remove(this);

        public void Set(bool open)
        {
            if (nview?.GetZDO() == null) return;
            nview.ClaimOwnership();
            nview.GetZDO().Set(OpenKey, open);
            Show(!open);
        }

        void Update()
        {
            if (Time.time < checkAt) return;
            checkAt = Time.time + 0.5f;
            if (nview?.GetZDO() != null) Show(!Open);
        }

        void Show(bool on)
        {
            if (shown == on) return;
            shown = on;
            foreach (var r in renderers) if (r != null) r.enabled = on;
            foreach (var c in colliders) if (c != null) c.enabled = on;
        }
    }
}
