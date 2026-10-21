using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The whirlpools of Britannia's deep sea (MAINOUT choose_spawn_monster, monster_turn,
    /// monster_contact; MASTER_PLAN §5.1 "Whirlpools (decided)"). One comes as a sea encounter on
    /// deep water (1 in 8 of them); it drifts a tile every other turn, half the time at random,
    /// else towards the nearest player. A vessel that meets it ("WHIRLPOOL!") is dragged down,
    /// spinning, and springs back up in the Underworld at (0x22, 0x12) (<see cref="Underworld"/>).
    /// Swimmers are not taken (the original spares a party on foot).
    /// </summary>
    public class Whirlpool : MonoBehaviour
    {
        public const string PrefabName = "u5_whirlpool";
        /// <summary>How near a ship's middle must come to be dragged in.</summary>
        public const float Reach = 9f;
        const float TurnEvery = 8f, Range = 250f, After = 30f;

        static readonly List<Whirlpool> all = new List<Whirlpool>();
        public static IEnumerable<Whirlpool> All => all;

        ZNetView nview;
        float nextTurn, alone, nextFx, spin;
        bool odd;
        Vector3 from, to;
        float moveStart;

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var holder = new GameObject("U5InValheim_Whirlpools");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            var prefab = new GameObject(PrefabName);
            prefab.transform.SetParent(holder.transform, false);
            var nview = prefab.AddComponent<ZNetView>();
            nview.m_persistent = false;
            prefab.AddComponent<ZSyncTransform>();
            prefab.AddComponent<Whirlpool>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
        }

        /// <summary>A whirlpool on the sea at a point (the owner's: it drifts from there).</summary>
        public static GameObject Spawn(Vector3 at)
        {
            var prefab = ZNetScene.instance?.GetPrefab(PrefabName);
            if (prefab == null) return null;
            at.y = Britannia.SeaLevel;
            var go = Object.Instantiate(prefab, at, Quaternion.identity);
            Plugin.Log.LogInfo($"A whirlpool at {at}");
            return go;
        }

        void Awake()
        {
            nview = GetComponent<ZNetView>();
            from = to = transform.position;
            all.Add(this);
        }

        void OnDestroy() => all.Remove(this);

        public bool Valid => nview != null && nview.IsValid();

        /// <summary>The whirlpool is gone (it took a ship): removed by whoever owns it.</summary>
        public void Vanish()
        {
            if (!Valid) return;
            nview.ClaimOwnership();
            ZNetScene.instance.Destroy(gameObject);
        }

        void Update()
        {
            if (!Valid) return;
            Draw();
            if (!nview.IsOwner()) return;
            alone = Player.IsPlayerInRange(transform.position, Range) ? 0 : alone + Time.deltaTime;
            if (alone > After) { ZNetScene.instance.Destroy(gameObject); return; }
            // a tile every other turn: slides there over the turn
            if (Time.time >= nextTurn)
            {
                nextTurn = Time.time + TurnEvery;
                odd = !odd;
                from = transform.position;
                to = from;
                if (odd) to = from + Step();
                moveStart = Time.time;
            }
            var p = Vector3.Lerp(from, to, Mathf.Clamp01((Time.time - moveStart) / TurnEvery));
            p.y = Britannia.SeaLevel;
            transform.position = p;
        }

        /// <summary>monster_turn for a whirlpool: half the time a random step, else towards the nearest player; only onto deep enough water.</summary>
        Vector3 Step()
        {
            var shape = Britannia.Active;
            float tile = shape?.TileMetres ?? 32f;
            Vector3 dir;
            Player near = null;
            float best = float.MaxValue;
            foreach (var pl in Player.GetAllPlayers())
            {
                float d = Vector3.Distance(pl.transform.position, transform.position);
                if (d < best) { best = d; near = pl; }
            }
            if (Random.value < 0.5f || near == null)
            {
                int k = Random.Range(0, 4);
                dir = new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right }[k];
            }
            else
            {
                var d = near.transform.position - transform.position;
                dir = Mathf.Abs(d.x) > Mathf.Abs(d.z) ? new Vector3(Mathf.Sign(d.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(d.z));
            }
            var next = transform.position + dir * tile;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(next) > Britannia.SeaLevel - 4f) return Vector3.zero;
            return dir * tile;
        }

        /// <summary>The look, drawn by each machine: foam turning round a dark middle.</summary>
        void Draw()
        {
            if (!Recolour.Draws || Time.time < nextFx) return;
            nextFx = Time.time + 0.35f;
            spin += 50f;
            var c = transform.position;
            for (int k = 0; k < 3; k++)
            {
                float a = (spin + k * 120f) * Mathf.Deg2Rad, r = 3f + 2.5f * ((spin / 50f + k) % 3);
                SpellStatus.LocalFx("vfx_WaterImpact_Karve", new Vector3(c.x + Mathf.Cos(a) * r, Britannia.SeaLevel, c.z + Mathf.Sin(a) * r), 2f);
            }
            if (Random.value < 0.1f) SpellStatus.LocalFx("vfx_serpent_watersurface", c, 4f);
        }
    }
}
