using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Jotunn.Entities;
using Jotunn.Managers;
using U5InValheim.Data;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Every Ultima item that can be carried (items.json, <see cref="ItemMap"/>) made as a Valheim
    /// item of its own: a copy of its Valheim item with the Ultima name and numbers (weapons hit
    /// for their attack, armour shows its defence, both × <see cref="Ultima.Scale"/>, as fights
    /// count them), never wearing out (nothing in Britannia repairs), worth nothing to Haldor, not
    /// craftable. Recoloured items (the gold regalia, the purple gem, the potions) get their own
    /// materials and inventory icon, on machines that draw.
    /// </summary>
    public static class UltimaItems
    {
        static ItemMap map;

        /// <summary>The item table (spells and scrolls named from DATA.OVL).</summary>
        public static ItemMap Map
        {
            get
            {
                if (map != null) return map;
                try { map = ItemMap.Parse(File.ReadAllText(Path.Combine(Plugin.PluginDirectory, "mappings", "items.json"))).Named(Plugin.U5.Ovl); }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"items.json: {e.Message}");
                    map = ItemMap.Parse("{\"equipment\":[],\"reagents\":[],\"special\":[],\"spells\":{\"prefab_by_circle\":[]},\"scrolls\":{\"spells\":[]},\"potions\":{\"colours\":[]}}");
                }
                return map;
            }
        }

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var watch = Stopwatch.StartNew();
            int made = 0, recoloured = 0;
            foreach (var def in Map.All)
            {
                if (!def.IsOurs) continue;
                var custom = new CustomItem(def.Prefab, def.Base);
                if (custom.ItemPrefab == null || custom.ItemDrop == null)
                {
                    Plugin.Log.LogWarning($"Items: {def.Name}: no Valheim item {def.Base}");
                    continue;
                }
                Make(def, custom.ItemDrop.m_itemData.m_shared);
                if (def.Tint != null && Recolour.Draws && ColorUtility.TryParseHtmlString(def.Tint, out var tint))
                {
                    Recolour.Model(custom.ItemPrefab, tint);
                    var shared = custom.ItemDrop.m_itemData.m_shared;
                    for (int i = 0; i < shared.m_icons.Length; i++) shared.m_icons[i] = Recolour.Icon(shared.m_icons[i], tint);
                    if (shared.m_armorMaterial != null) shared.m_armorMaterial = Recolour.Material(shared.m_armorMaterial, tint);
                    recoloured++;
                }
                ItemManager.Instance.AddItem(custom);
                made++;
            }
            Plugin.Log.LogInfo($"Items: {made} Ultima items made ({recoloured} recoloured) in {watch.ElapsedMilliseconds} ms");
        }

        static void Make(ItemDef def, ItemDrop.ItemData.SharedData s)
        {
            var ovl = Plugin.U5.Ovl;
            s.m_name = def.Name;
            s.m_useDurability = false;
            s.m_canBeReparied = false;
            s.m_maxQuality = 1;
            s.m_value = 0;
            s.m_damagesPerLevel = new HitData.DamageTypes();
            s.m_armorPerLevel = 0;
            switch (def.Kind)
            {
                case ItemKind.Equipment:
                    int attack = ovl.ItemAttack(def.Id), defence = ovl.ItemDefense(def.Id), range = ovl.ItemRange(def.Id);
                    if (attack > 0 && Hits(s.m_itemType)) Scale(ref s.m_damages, attack * Ultima.Scale);
                    if (Worn(s.m_itemType)) s.m_armor = defence * Ultima.Scale;
                    var line = new List<string>();
                    if (attack > 0) line.Add($"Attack {attack}");
                    if (defence > 0) line.Add($"Defence {defence}");
                    if (range > 0) line.Add($"Range {range}");
                    s.m_description = line.Count > 0 ? string.Join(", ", line) + "." : "";
                    break;
                case ItemKind.ArmourPart:
                    s.m_armor = 0;
                    s.m_description = $"Worn with the {def.Name.Replace(" Leggings", "")}, whose defence it is part of.";
                    break;
                case ItemKind.Reagent:
                    Carried(s, 99);
                    s.m_description = "A reagent, for mixing spells.";
                    break;
                case ItemKind.Spell:
                    Carried(s, 99);
                    s.m_description = $"A mixed spell of the {Ordinal(def.Spell / 6 + 1)} circle.";
                    break;
                case ItemKind.Scroll:
                    Carried(s, 99);
                    s.m_description = $"A scroll of the spell {ovl.SpellWords(def.Spell)}.";
                    break;
                case ItemKind.Potion:
                    // (drinking them comes with their effects)
                    Carried(s, 99);
                    s.m_description = "A potion.";
                    break;
                case ItemKind.Moonstone:
                    Carried(s, 1);
                    s.m_description = "One of the eight moonstones.";
                    break;
                case ItemKind.Special:
                    if (s.m_itemType == ItemDrop.ItemData.ItemType.Material || s.m_itemType == ItemDrop.ItemData.ItemType.Consumable) Carried(s, 99);
                    s.m_description = "";
                    break;
            }
        }

        static bool Hits(ItemDrop.ItemData.ItemType t)
            => t == ItemDrop.ItemData.ItemType.OneHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
               || t == ItemDrop.ItemData.ItemType.Bow || t == ItemDrop.ItemData.ItemType.Ammo || t == ItemDrop.ItemData.ItemType.Shield;

        static bool Worn(ItemDrop.ItemData.ItemType t)
            => t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs
               || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Trinket
               || t == ItemDrop.ItemData.ItemType.Utility;

        /// <summary>Something carried, not eaten or drunk (a material), stacking to <paramref name="stack"/>.</summary>
        static void Carried(ItemDrop.ItemData.SharedData s, int stack)
        {
            s.m_itemType = ItemDrop.ItemData.ItemType.Material;
            s.m_food = s.m_foodStamina = s.m_foodEitr = s.m_foodBurnTime = s.m_foodRegen = 0;
            s.m_consumeStatusEffect = null;
            s.m_maxStackSize = stack;
        }

        /// <summary>The fighting damage (not chopping or mining) scaled to add up to <paramref name="total"/>, in its own mix of kinds.</summary>
        static void Scale(ref HitData.DamageTypes d, float total)
        {
            float sum = d.m_damage + d.m_blunt + d.m_slash + d.m_pierce + d.m_fire + d.m_frost + d.m_lightning + d.m_poison + d.m_spirit;
            if (sum <= 0) { d.m_blunt = total; return; }
            float k = total / sum;
            d.m_damage *= k; d.m_blunt *= k; d.m_slash *= k; d.m_pierce *= k; d.m_fire *= k;
            d.m_frost *= k; d.m_lightning *= k; d.m_poison *= k; d.m_spirit *= k;
        }

        static string Ordinal(int n) => n + (n == 1 ? "st" : n == 2 ? "nd" : n == 3 ? "rd" : "th");
    }

    /// <summary>
    /// Recolouring an item: each texture coloured to the tint (its light and shade kept, its
    /// colour replaced), so a coal lump can turn ash white and a ruby purple.
    /// </summary>
    static class Recolour
    {
        static readonly Dictionary<(Texture, Color), Texture2D> textures = new Dictionary<(Texture, Color), Texture2D>();
        static readonly Dictionary<(Material, Color), Material> materials = new Dictionary<(Material, Color), Material>();

        public static bool Draws => SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

        public static void Model(GameObject prefab, Color tint)
        {
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetType().Name == "ParticleSystemRenderer") continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null) mats[i] = Material(mats[i], tint);
                r.sharedMaterials = mats;
            }
        }

        public static Material Material(Material m, Color tint)
        {
            if (materials.TryGetValue((m, tint), out var done)) return done;
            var copy = new Material(m) { name = m.name + " (u5)" };
            if (m.HasProperty("_MainTex") && m.mainTexture != null)
            {
                var tex = Coloured(m.mainTexture, new Rect(0, 0, m.mainTexture.width, m.mainTexture.height), tint);
                if (tex != null) copy.mainTexture = tex;
                if (copy.HasProperty("_Color")) copy.SetColor("_Color", new Color(1, 1, 1, copy.GetColor("_Color").a));
            }
            else if (copy.HasProperty("_Color")) copy.SetColor("_Color", new Color(tint.r, tint.g, tint.b, copy.GetColor("_Color").a));
            return materials[(m, tint)] = copy;
        }

        public static Sprite Icon(Sprite icon, Color tint)
        {
            if (icon == null) return null;
            var rect = icon.textureRect;
            var tex = Coloured(icon.texture, rect, tint);
            if (tex == null) return icon;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), icon.pixelsPerUnit);
        }

        /// <summary>The part <paramref name="rect"/> of a texture coloured to the tint (read back through the GPU: game textures are not readable).</summary>
        static Texture2D Coloured(Texture source, Rect rect, Color tint)
        {
            bool whole = rect.x == 0 && rect.y == 0 && rect.width == source.width && rect.height == source.height;
            if (whole && textures.TryGetValue((source, tint), out var done)) return done;
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var was = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                int w = (int)rect.width, h = (int)rect.height;
                var copy = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = source.name + " (u5)" };
                copy.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0);
                var px = copy.GetPixels32();
                // its mean light (where it shows) becomes the tint's; lighter and darker stay so
                double sum = 0;
                int n = 0;
                foreach (var p in px) if (p.a > 16) { sum += Light(p); n++; }
                float mean = n > 0 ? Mathf.Max(0.02f, (float)(sum / n)) : 0.5f;
                for (int i = 0; i < px.Length; i++)
                {
                    float k = Mathf.Clamp(Light(px[i]) / mean, 0.25f, 1.75f);
                    px[i] = new Color32(Byte(tint.r * k), Byte(tint.g * k), Byte(tint.b * k), px[i].a);
                }
                copy.SetPixels32(px);
                copy.Apply(true);
                if (whole) textures[(source, tint)] = copy;
                return copy;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Items: could not recolour {source.name}: {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = was;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static float Light(Color32 p) => (0.299f * p.r + 0.587f * p.g + 0.114f * p.b) / 255f;
        static byte Byte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255), 0, 255);
    }
}
