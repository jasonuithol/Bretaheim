# U5InValheim — Master Plan

Rebuild *Ultima V: Warriors of Destiny* inside Valheim as a BepInEx/Jötunn mod: Britannia as the
world, its towns, castles, keeps and dungeons as places you can walk into, its people on their daily
schedules with their original conversations, its monsters, items and magic mapped onto Valheim's
systems, and the main story (Shadowlords, shards, Blackthorn, Lord British) playable to the end.

This document is the backbone: what we are building, what we already have, the architecture, the
phases, and the decisions that must not drift. Everything here builds on six bodies of prior work:

| Source | What it gives us |
|---|---|
| `~/Projects/Ultima5Revolutions` (this week's decompilation of the PC game to Rust) | The *exact* game logic and every file format: `analysis/ARCHITECTURE.md`, `analysis/*.md` per module (world, towns, NPC schedules, TLK dialogue bytecode, combat, spells, shops, dungeons), `analysis/symbols.json` + `globals_seed.jsonl` (every DATA.OVL table with offsets), `rust/src` (reference implementation of every routine). |
| `/media/jason/FABA03B0BA036889/Users/jason/Desktop/Projects/SCUM-Mods/Mods/Britannia/tools/` | `generate_terrain.py`: BRIT.DAT + DATA.OVL → heightmap, per-class weightmaps, POI list, preview; the tile-class → elevation/layer tables are reused as-is. (SCUM itself is no longer a target; `heightfield_patch.py` and the SCUM world size are irrelevant here.) |
| `/media/jason/FABA03B0BA036889/Users/jason/Desktop/Projects/BritanniaUE/` | The UE build of Britain: `scripts/ue_town_layout.py`, `ue_britain_v5.py`, `ue_britain_roofs.py`, `ue_ground*.py`, `ue_path_taxi*.py`, `ue_pois.py`, `ue_scatter.py`, `ue_orchard_*.py`, `ue_inn*.py`: tile → building/wall/roof/path/vegetation placement rules that worked, plus `britannia_build/` (heightmap.png, layers, pois.json, meta.json) and `scum_assets/` (asset inventory). |
| `~/Projects/U5InValheim/Ultima5 to Valheim asset mappings.txt` | The preferred U5 → Valheim *asset* mapping: humans, monsters, static assets, biomes, armour, shields, weapons, rings/amulets (Valheim trinkets), reagents, mixed spells, potions, scrolls, quest items, transport, character and party status effects. Treated as the default; deviations must be recorded in `docs/MAPPING_DECISIONS.md`. |
| `~/Projects/U5InValheim/Restrictions and Etc.txt` | Food (barley rations, daily eating, karma for stealing crops, karma as a "skill"), the Valheim tools allowed only around Iolo's hut, the banned Valheim actions, the new-world intro and character generation in Iolo's hut. Summarised in §5.12–5.13; the file is the authority. |
| `~/Projects/U5InValheim/Constructing world assets.txt` | The scale problem and its resolution (Solution A: 32 m×32 m×3 m overworld tiles, 2 m×2 m×3 m indoor tiles), the elevation guidance, and the authoritative tile-by-tile construction tables for overworld tiles (biomes, structures, paths, rivers, signposts, guardians) and indoor tiles (height zones, roofs, walls, doors, furniture, fixtures). Summarised in §5.1–5.2; the file is the authority. |
| `~/Projects/U5InValheim/Action mappings.txt` | How each U5 *command* (Attack … Z-stats) is realised in Valheim: which existing mechanic is reused, which key/interaction triggers it, and the custom rules (camping, mixing, climbing, spyglass sky, yelling words of power). Summarised in §5.11; the file is the authority. |
| `~/Projects/UltimatePyve/docs/` | Older notes: `SPAWN_RULES.md`, `ultima5_npc_porting_notes.md`, NPC/armour spreadsheets. |
| `~/Projects/Jotunn`, `~/Projects/ValheimKillFeed`, `~/Projects/ClaudeHeim` | Our Valheim modding toolchain and precedents (BepInEx 5 + Harmony, Jötunn lib source, csproj reference layout, Thunderstore publishing rules in `~/.claude/CLAUDE.md`). |

---

## 1. Guiding decisions

1. **No Ultima data ships with the mod.** Like the Rust port and `generate_terrain.py`, the mod reads
   the player's own Ultima V directory (GOG) at runtime, configured in `BepInEx/config`. World, towns,
   NPCs, dialogue, item names and stats are all built from those files on load. The Thunderstore
   package contains code, mapping tables, and only assets we made ourselves (re-shades of Valheim
   items — potions, rubies, gold regalia, dragons — scroll reels, the night-sky skybox, shaders). If the configured directory is missing, the mod prints a
   clear message and disables itself.
2. **Valheim systems first, custom systems only where U5 has no analogue.** Combat, movement, boats,
   day/night, status effects, inventory, building pieces are Valheim's. We add: a world generator,
   place builders, NPC schedules + conversation, shops, the magic system, moongates, the quest state.
   Valheim's *progression* is bypassed: no building or crafting outside Iolo's hut (§5.12), no
   workbench/kettle chain, no boss gating; Valheim skills are replaced by the U5 stats
   (STR/DEX/INT/MP/level, karma).
6. **One character per player.** There is no AI party: a party is other people on your server.
   Party-wide effects (Crown/Amulet of Lord British) apply to everyone within one tile (32 m).
3. **Faithful data, adapted feel.** Every number comes from DATA.OVL/`.NPC`/`.TLK` via the documented
   offsets; where real-time Valheim and turn-based U5 disagree (combat, time), we map rather than
   emulate (see §5.6, §5.8).
4. **One mapping table per domain, data-driven.** Tile → prefab, monster → prefab + stat overrides,
   item → prefab + stat overrides, status → status effect. Tables are JSON in `assets/mappings/`,
   seeded from the mappings document, so balancing never needs a recompile.
5. **Build incrementally on a single town.** Britain first (we already solved its layout in UE), then
   generalise. Every phase ends with something you can walk around in.

## 2. What the decompilation gives us (and where)

| Need | Where the answer already is |
|---|---|
| Overworld map (256×256 tiles, 16×16 chunks) and Underworld | `BRIT.DAT`/`UNDER.DAT` format: `analysis/ovl_OUTSUBS.md`, `ovl_MAINOUT.md`; tile classes in `generate_terrain.py` `CLASS_OF`; tile names in `LOOK2.DAT`. |
| Town/castle/keep/dwelling maps (32×32, floors), entry positions, names | `analysis/ovl_TOWN.md`; DATA.OVL index tables (`globals_seed.jsonl`: location x/y 0x1e8a/0x1eb2, name pointers, map-file index tables 0x1e1a..). |
| NPC schedules, positions, dialogue index per NPC | `.NPC` format: `analysis/ovl_NPC.md` (32 NPCs × 4 slots, activity codes, the eight-state movement machine, pathing BFS). |
| Conversations | `.TLK` format + full bytecode semantics: `analysis/ovl_TALK.md`, `rust/src/ovl/talk.rs`; compressed-word table (DATA.OVL 0x104c-ish, see symbols). Keyword match = first 4 letters, case-insensitive (`str_find_ci` quirk documented). |
| Shops (prices, inventories, dialogue) | `analysis/ovl_SHOPPES*.md`, `SHOPPE.DAT` strings, DATA.OVL price tables; Shadowlord of Falsehood surcharge; inn/healer/tavern/guild/shipwright/reagent rules. |
| Monsters: stats, spawn terrain, groups, AI | `analysis/root_part3.md` (monster table 0x5c5a), `ovl_MAINOUT.md` (spawn_rate_for_terrain, choose_spawn_monster, random_spawn_position), `ovl_COMBAT.md`/`ovl_COMSUBS.md` (attack resolution, specials, morale), DATA.OVL monster stats at 0x13bd, names 0x1c3/0x31b, `SPAWN_RULES.md`. |
| Items: names, attack/defence/range, equip rules | DATA.OVL tables (armour/weapon strings 0x42/0xe8, attack 0x15fc, defence 0x1634, range 0x1664), `ovl_ZSTATS.md` (equipment slots, ready rules), `ovl_SJOG.md` (chest loot tables). |
| Magic: spell list, syllables, reagents, MP cost, effects | `analysis/ovl_CAST.md`, `ovl_CAST2.md`; DATA.OVL spell names 0x6f9, runes/words 0x931/0x995, reagent requirements. |
| Dungeons: 8 dungeons × 8 levels × 8×8 cells, cell codes, rooms | `analysis/ovl_DUNGEON.md`, `ovl_DNGLOOK.md` (cell semantics, DUNGEON.CBT room layouts/monsters/triggers, the 7-room-set index quirk, persistent rooms). |
| Calendar, moons, moongates, Shadowlords' daily moves, light levels | `analysis/root_part3.md` (pass_time, update_moongates, moongate_travel, moon phase table), `ovl_TOWN.md` (spawn_shadowlord). |
| Save game layout (party, inventory, flags, karma, quest items) | `analysis/globals_seed.jsonl` (every SAVED.GAM field with offsets), `reference/wiki_SAVED_GAM.txt` (local copy; not in git). |
| Story scripts (intro, Blackthorn scenes, endgame) | `ovl_INTRO.md`, `ovl_BLCKTHRN.md` (cut-scene byte-code), `ovl_ENDGAME.md`. |

A C# port of the readers is the first engineering task (§6, Phase 0). The Rust modules
`lzw.rs`, `data/images.rs`, `engine/files.rs` and the loaders in the overlays are the reference.

## 3. Target experience (scope for v1.0)

- **World:** the whole Britannian surface at a playable scale (§5.1), with correct biomes, rivers,
  roads, coastlines, the 26 settlements + 5 castles/keeps + 8 dungeon entrances + shrines +
  lighthouses + moongate sites placed where the map says.
- **Places:** every town, castle, keep and dwelling built from its map, with doors, furniture, signs
  (SIGNS.DAT text), floors/levels, and the look of the mapped materials.
- **People:** all NPCs with schedules, movement, sleep/work/wander behaviour, guards, and the original
  conversations through a keyword dialogue panel; shops, inns, healers, taverns functioning.
- **Player:** one character per player (the Avatar, or a companion-styled character); karma, gold,
  food, reagents, spells, quest items and the U5 stats tracked in a mod save block; parties are
  multiplayer groups.
- **Monsters & combat:** mapped Valheim creatures with U5-derived stats, spawning by terrain and by
  the U5 spawn tables; dungeon rooms as prefab encounter rooms.
- **Magic & items:** all armour/weapons/shields/rings/amulets/potions/scrolls per the mapping; the
  reagent/mixing system; spells as usable items with Valheim-side effects.
- **Story:** Shadowlords roaming towns by the calendar, the three shards, Blackthorn's castle events,
  the Underworld (reduced), Lord British's rescue and the ending.

Out of scope for v1: the original's combat grid. The Underworld is in scope (Phase 7) as a second
continent (§5.1); the original introduction is in scope as the new-world intro (§5.13).

## 4. Architecture

```
U5InValheim/
  src/U5InValheim/            BepInEx plugin (C#, .NET 4.7.2 / Valheim assemblies)
    Plugin.cs                 entry: config (U5 path, scale), Jötunn registrations
    Data/                     readers: Lzw, DataOvl, MapFiles, NpcFile, TlkFile, DungeonDat, Cbt,
                              SaveGame (INIT.GAM defaults), tables (offsets from symbols.json)
    World/                    WorldGenerator patches: heightmap/biome from BRIT.DAT; rivers; roads
    Places/                   procedural builders: Town, Castle, Keep, Dwelling, Lighthouse, Shrine,
                              Moongate, DungeonEntrance; tile→prefab rules; signs; furniture
    Npc/                      TownNpc component (schedule state machine, pathing), guards, sleep,
                              ConversationUI, TlkInterpreter, Shops (all shop types)
    Creatures/                monster mapping + stat overrides, spawner (terrain/time/U5 tables)
    Items/                    item mapping + stat overrides, rings/amulets/potions/scrolls/reagents,
                              mixing, spell items and effects
    Story/                    calendar, moons, moongates, Shadowlords, quest flags, cut-scenes
    Save/                     mod state (ZDO/custom data in the world save), import of INIT.GAM
    Actions/                  per-command glue (camp H, mix M, cast via equipped spell, yell, use-item
                              handlers, grapple climbing, spyglass sky, sextant, pocket watch)
    UI/                       dialogue panel, shop panel, mix menu, sky map, U5 stats in the Tab screen
  assets/                     our own asset bundle (reshades, scroll reel, night-sky skybox, shaders)
  assets/mappings/*.json      tile/monster/item/status/biome tables (from the mappings doc)
  tools/                      offline helpers: previews, validation against the Rust port, docs
  docs/                       this plan, MAPPING_DECISIONS.md, per-phase notes
  ThunderstoreAssets/         manifest.json, README, CHANGELOG, icon (see ~/.claude/CLAUDE.md rules)
```

Runtime flow: Plugin loads → validates U5 path → `Data` parses everything into immutable
tables → `World` installs Harmony patches on `WorldGenerator` → `Places`/`Creatures`/`Items` register
Jötunn prefabs, locations and vendors → on world load `Story` restores quest state → `Npc` spawns
town populations when their zone loads.

## 5. Design per subsystem

### 5.1 World generation
- Source: 256×256 overworld classified exactly as `generate_terrain.py` does (`CLASS_OF`, elevation
  per class, noise), ported to C#. Biome per class from the mappings doc (Grasslands→Plains,
  Forest→Black Forest, Swamp→Swamp, Grassy hills→Meadows, Foothills/Mountains→Mountain,
  Peaks→Mistlands, Desert→Ashlands, shoals→shallows, sea→ocean).
- Scale: **32 m per tile (decided)** → Britannia is an 8.2 km square. With the Underworld (below)
  and the wrap margins, the world radius is expanded to ~11 km so both continents fit.
- **World wrap (decided).** Both U5 maps are tori (x and y wrap at 256 tiles). Each continent gets:
  (a) a replicated margin of ~16 tiles beyond every edge, filled with the opposite edge's tiles, so
  the view across the edge is the correct continuation; (b) a per-frame wrap patch that shifts the
  player, their vehicle (boat/horse/carpet, passengers included), pirate ships and flagged creatures
  by exactly ±8192 m on the crossed axis once they are inside the margin — the destination looks
  identical, only a zone-reload hitch is visible. Unflagged wandering monsters despawn at the edge.
  Coordinates stay the original 256×256 on both continents, so links between them are 1:1. The
  "over/under" of the two maps is purely a label; they are simply two wrapped squares side by side.
  Normal wrapping must be invisible: the destination zones are pre-loaded while crossing the
  margin and the ship moves only when they are ready (first in-game test, 2026-10-16: moving into
  unloaded zones dropped the ship into a void, flipped and damaged it).
- **Whirlpools (decided, user idea from that test).** U5's whirlpools drag a ship down into the
  Underworld. We reuse the dramatic version of the transition on purpose: the ship sinks/falls
  into the whirlpool, is moved to the Underworld continent, and springs back up there.
- Implementation: Harmony patches on `WorldGenerator.GetBiome`, `GetBaseHeight`/`GetHeight`,
  river/lake generation, and `ZoneSystem` location placement, sampling our heightmap/biome maps
  (the same approach the BetterContinents mod uses — study it for the exact hook set). Height field
  is generated once per world seed from the U5 data (fast: 256² → bilinear + noise), cached in memory.
- Roads: U5 road tiles → Valheim "dirt empathen"/"rock empathen" paint (TerrainModifier at zone
  load). Rivers: U5 river tiles → carved channels (height) + water, wide/flat near the coast and
  narrow/steep near their mountain sources, taking the surrounding biome; bridges (29, 106, 107)
  are built from wood pieces (Valheim has no bridge asset).
- Elevation (from `Constructing world assets.txt`): do not use the full height range — mountains
  and peaks rise with a managed ascent rather than straight up; where cliffs (height discontinuities)
  are needed, place them where the map has waterfalls (tiles 212–215).
- Overworld tile table (authority: `Constructing world assets.txt`, section "Tile Mappings –
  Overworld"): biome tiles map as listed there (e.g. Scrub = Meadows with heavy tree cover,
  Foothills = treeless Black Forest with boulders, border foothills = open meadow with boulders,
  shoreline tiles 48–55 = Plains at sea level with the diagonal land/sea splits); structure-entry
  tiles (hut, tower, village, town, castle, cave/mine/dungeon entrances, shrines, lighthouse, the
  two castle entrances) take the biome of their neighbours (dungeon entrances: mountain); the
  non-enterable castle-wall tiles are fixed (Lord British's castle = Plains, Blackthorn's = treeless
  Black Forest with boulders); the overland signpost (164) is a rune-lit standing stone in a
  clearing; the Codex guardians (94/95) are two enormous stone Valkyries, one good, one fallen.
- **Underworld = a full second continent (decided).** Built from UNDER.DAT with the same pipeline,
  placed beside Britannia on an expanded world disc (the radius override is a known-working
  technique, cf. the "Expand World Size" mod: generator, water edge and minimap constants). It is
  real terrain, so rivers, boats and lava work; a permanent-night/cave environment is applied by
  position via the environment manager. Dungeon bottoms, the falls and the Hythloth route teleport
  between corresponding coordinates, preserving the "beneath Britannia" fiction. Fallback if the
  radius override proves fragile: an elevated built layer of runtime chunk meshes with a ceiling
  (Valheim's own crypt-interior technique), at the cost of cosmetic-only water.

### 5.2 Places (towns, castles, keeps, dwellings)
- **Scale (decided — Solution A of `Constructing world assets.txt`).** U5 draws everything at one
  tile size and lets the player rescale in their head; Valheim is 1:1. We therefore use two
  scales: an overworld tile is **32 m × 32 m × 3 m**, and inside a settlement each map tile is
  **2 m × 2 m × 3 m** (1 m proved awful). A 31×31 town therefore spills over its neighbouring
  overworld tiles; places are shuffled to fit. This keeps a direct, near-effortless mapping from the
  `.DAT` files to the world at the cost of a boxy look. Solution B (describe each town and hand-model
  Valheim-style buildings) and C (lay foundations, build by hand in-game) were considered and
  rejected for v1: B burns enormous effort on bespoke assets and drifts from the originals; C takes
  ages and spoils discovery for the builder.
- **Height zones (decided).** Ground zone: tops of all floor tiles in one plane at ground level.
  Wall zone: 3 m above the floor; all walls, furniture and loose objects live here. Multi-storey:
  an inter-level zone whose floor tops align with the wall-zone top below it (wood floors — stone
  is impractical here), then another wall zone, repeated per storey. Roof zone starts at the top
  wall zone: cobblestone cottages get thatch, other town buildings shingles, castles/keeps/towers/
  lighthouses grausten.
- **Indoor tile table (authority: `Constructing world assets.txt`, "Tile Mappings – Town/City/
  Castle" and "Furniture").** Each location is a Jötunn `CustomLocation` built at runtime from its
  32×32 map(s) through that table: floors (stone, black marble, jetty decks, drawbridges where
  water flanks the planks), walls (the basic stone wall in deluxe/medium/easy variants with the
  out-of-phase brick look, arrow-slit and window walls oriented by neighbours, cobblestone walls as
  wood frame + stone fence, secret doors revealed by hitting a rune-marked wall, parapets with and
  without merlons), doors (2 m with a gap above when they have a viewport, 3 m otherwise; magically
  locked doors glow blue; iron gates are guard-operated, shut 22:00–06:00, never jimmied), and the
  furniture/fixture list (anvils, the interactable telescope = spyglass, shelves, torture tables
  and stocks, grates, cannonball pyramids, graves with eulogies and a dig spot to the south,
  trapdoors, the playable harpsichord, guillotine, chairs/tables consolidated into runs, standing
  mirrors incl. the end-game one, indoor signposts, the coin-taking wishing well, hitching posts,
  firewood, searchable desks and barrels, beds in two-tile runs, dressers, braziers and wall
  torches, camp/hearth cooking setups, ballistas for cannons, traversable hearths in walls (smoke
  off), lamp poles, kettle stoves, stairs, ladders, climbable fences, marble drinking fountains,
  flames of virtue, shop-front banners with the guild icon, clocks, bellows). The UE-era layout
  rules (`ue_town_layout.py`, roofs, path routing) remain guidelines for consolidation and roofing.
- Multi-floor maps (castles, keeps, towers) follow the height zones; basements (jails) below
  ground level.
- Signs: SIGNS.DAT text on Valheim sign pieces; runic font replaced by text.
- Entry: the overworld tile for the settlement is the location centre; the U5 entry position
  (15,30 etc.) becomes the gate.
- Build cost: a town is ~1–3 k pieces. Use plain piece prefabs first; if performance demands,
  merge static runs into combined meshes (Phase 2 risk item; combined building shells are already
  the direction taken in the current builder).

### 5.3 NPCs and conversation
- Prefab per NPC type from the mapping (child → Hildir; guard → iron armour + bronze spear; etc.),
  spawned per settlement from the `.NPC` file; names/jobs from the `.TLK` records.
- `TownNpc` MonoBehaviour implements the original eight-state movement machine
  (`analysis/ovl_NPC.md`): schedule slot by hour (Valheim day mapped to 24 U5 hours), target
  (x,y,floor), BFS path over the location's walkability grid (derived from the same tile table),
  door handling, sleep (bed), "work" animation by tile (anvil/stove/stage), wander, guard patrol,
  arrest/attack on crimes. Networked via ZNetView/ZSyncTransform; only the zone owner simulates.
- Conversation (decided): press **E** on an NPC → it turns to you, its greeting shows as a Valheim
  speech bubble (`Chat.SetNpcText`, the trader/Hildir mechanism) and a conversation panel opens:
  scrolling log + one input line (the chat box captured in a "talking" mode, so T/Enter works, but
  text is routed to the NPC, not broadcast) with Name/Job/Bye buttons. Keyword matching is the
  original's first-four-letters rule; replies go to the log and the bubble (others nearby see the
  NPC speak); yes/no questions, gold/item transfers and party joining run through the same panel;
  shop branches open the shop panel. Bye/Escape/walking away ends it and the schedule resumes.
  `TlkInterpreter` ported from `talk.rs` (name/job/bye, keyword branches, karma gates, ask yes/no,
  give/pay, join, labels, guard challenge, Shadowlord theft); text decoded from the `.TLK` with the
  DATA.OVL word table.
- Shops: port the shop state machines (arms, reagents, healer, guild, horse, tavern, shipwright,
  inn) as dialogue-driven panels; items given are the mapped Valheim items; gold is our own
  currency item (Valheim coins mapped 1:1). Reagent sellers also sell staffs if we add them (§5.5).
- Yell: words of power are typed in the normal in-game chat; the mod listens and opens the dungeon
  if the speaker is within one tile (32 m) of its entrance. Sails are Valheim's rudder, no yelling.

### 5.4 Creatures and spawning
- `assets/mappings/monsters.json`: U5 creature → Valheim prefab (+ optional recolour/scale) + stat
  override derived from DATA.OVL monster stats (HP, attack, armour, speed, specials: poison, sleep,
  ranged, fly, swim). Per the asset mapping: Gazer → Skugg, Headless → Shaman (headless if we can),
  Ettin → Troll with two heads side by side if achievable, Seahorse → giant pufferfish, Mongbat →
  brown-shaded Drake, Mimic → the wild (non-craftable) chests, Reaper → immobilised Elder,
  Dragon → Moder re-shaded by attack type (red fire, green poison, blue frost, black tar, white
  lightning), Sand trap → yellow Root cluster around a yellow Lox head, Pirate ship → longboat crew.
- Overworld spawner replaces Valheim's for the Britannia surface: U5 rules by terrain, time of
  day and level (`spawn_rate_for_terrain`, `choose_spawn_monster`, `SPAWN_RULES.md`), groups of
  1–9. Pirate ships → longboat with a Fuling crew (mapping).
- Dungeon rooms: encounter rooms with the room's monster set from DUNGEON.CBT.

### 5.5 Items, equipment, magic
- `items.json` from the asset mapping. Jötunn `ItemManager` clones with U5-derived damage/armour
  (attack/defence tables scaled onto Valheim's numbers; weapon range → melee vs ranged), names
  from DATA.OVL. **Rings and amulets are Valheim trinkets** (already equippable; no custom worn
  asset): invisibility→Nimble Anklet, protection→Wolf Sight, regeneration→Jormundling; ankh→Crystal
  Heart, spiked→Resounding Shackle, turning→Bronze Pendant. Potions: one mead re-shaded in the 8
  colours; scrolls, Magic Sheet Music and the HMS Cape plans: linen reels (crawling-rune shader as
  a stretch goal); reagents: the mapped Valheim consumables.
- Quest/utility items: Gem → purple ruby (right-click opens the Valheim map = "view gem");
  Spyglass → horn of celebration (equip like Megingjord; at night on the surface it reveals the
  8 town-stars and 3 Shadowlord-comets in a **modified skybox** — the world tree goes, a U5 night
  sky comes); Sextant → Heart of the Forest trinket (night, surface: exact lat/long, mapped
  faithfully from the original's formula because quests depend on it); Pocket watch → Iron Brooch
  trinket (time via the message system); Grapple → iron pickaxe (equipped: climb slopes beyond
  ~45°, see §5.11 Klimb); Black Badge → black-metal Iron Brooch (equipped: Blackthorn's guards stand
  down); Sandalwood Box → Pulled Bear; Shards → black-metal rubies; Crown/Sceptre/Amulet of Lord
  British → gold-reshaded Dvergr circlet / bronze mace / silver necklace. Keys → swamp key
  re-shaded bronze; Skull keys → the swamp key as is (locks: see §5.11 Jimmy).
- Magic (decided): premixed spells are bowls of raw mead and are **equipped like a stack of arrows**
  (right-click in the inventory → the active spell); casting fires the active spell as a bow fires
  an arrow. Staffs are optional "bows" for this — add them only if it reads cleaner, sold with
  reagents. Casting consumes MP (a U5 stat) and applies the effect: healing/cure/light/protection/
  sleep/charm/blink/tremor (we have the earthquake routine)/wind/… as status effects, teleports or
  custom actions; combat-grid-only spells become area effects.
- Mixing (decided): press **M** anywhere within range of a campfire (no mead kettle, no progression
  chain) → recipe menu driven by the DATA.OVL reagent table; mixing consumes reagents and yields the
  spell mead. (M is free because maps are opened with the gem, not a map key.)
- Status effects per mapping (sleep→Resting, charmed→Wisplight, dead→no skill drain; rings/amulets
  → Cold/Sheltered/red Magic barrier, Corpse run/white-gold Rested). Party effects from equipped
  regalia reach everyone within 32 m: Crown → blue Magic barrier that blocks **magic attacks only**;
  Amulet → silver Rested plus **+50 armour** while equipped.
- Search (decided): no global search verb. Hidden objects from the original's tables are discreet
  harvestables (a glint/mound; E picks up), moonstone sites are mounds, reagent patches are plants
  with the original time-of-day rules; chests open normally and **Shift+E (secondary interact)
  searches for traps**, applying the original's trap/loot tables before opening.

### 5.6 Combat
Real-time Valheim combat replaces the grid. What we keep from U5: who is hostile, stats, specials,
morale/flee (monster_morale), experience/levelling from DATA.OVL tables, loot/chest tables
(`roll_random_chest_loot`), karma effects of attacking non-hostiles, guards reacting to crimes.
Attack targets characters and the environment, as Valheim already allows (which also replaces
Push/Pull: barrels and the few puzzle walls are destroyable instead of movable). Cannons: boats
have none; castles get fixed ballistas you can fire.

### 5.7 Dungeons
- Each of the 8 dungeons: entrance location (crypt/cave door per biome) → an interior built from
  DUNGEON.DAT: 8 levels of 8×8 cells, each cell a 16 m module (hallway, ladder up/down, chest,
  fountain of each kind, pit/trap, open chest, energy field, wall, door, secret door, room).
  Build as a "dungeon layer" stacked under the map (y < 0) at the entrance, like Valheim's
  interiors (which live at high altitude; we can do the same at y = 5000+ with a per-dungeon
  offset) — teleport in/out with fade.
- Rooms from DUNGEON.CBT: 11×11 tile rooms with monsters/items/triggers (tile-changing
  triggers when stepped on) as encounter modules; persistent-room table honoured.
- Light: dungeons dark; torches/light spell = Valheim light sources with duration.

### 5.8 Time, calendar, moons, Shadowlords
- Mod calendar (year/month/day/hour/minute) advanced from Valheim's day cycle (configurable:
  1 Valheim day = 1 U5 day). Drives NPC schedules, shop hours, moon phases (Trammel/Felucca table
  from DATA.OVL), moongate activation and destinations (`update_moongates`), Shadowlord daily
  relocation (`pass_time`), harvest/skull-key days.
- Moongates (decided): custom Moongate prefab (glowing arch, portal particles; not buildable). At
  dusk the site selected by the Trammel phase animates *up out of the ground* over a few seconds
  (the original's moongate-height animation), sinks at dawn; while fully risen its trigger teleports
  the player (`Player.TeleportTo`, normal portal transition) to the site chosen by the Felucca
  phase. No Valheim portal tags; destinations are set nightly by the calendar. Moonstones (refined
  eitr or a recolour) are buried at sites: harvest when no gate is up (site goes dormant), "use" on
  the ground to bury and create a site; the 8 site entries live in the mod save like the original.

### 5.9 Player, party, camping, stats
- **One character per player (decided).** No AI companions and no New Order: a party is the other
  players on the server. Companion NPCs (Shamino, Iolo, Jaana, Dupre …) keep their dialogue; their
  "join" branches are answered in character but nobody follows you. INIT.GAM supplies the starting
  character, items and world flags.
- Stats: the Tab screen shows U5 stats (STR/DEX/INT, HP/MP, level/experience, gold, food)
  **replacing Valheim's skill list**, with **karma listed as a skill** (Z-stats = Tab). Ready/equip =
  Valheim inventory. Save = the Valheim menu save (U5's Quit was only ever Save).
- Food (decided): rations are **barley**. Every crop field in Britannia grows barley; harvesting from
  someone else's garden costs karma. Each character eats one barley from their inventory once a
  day; with none, they lose 30 % of their health. Valheim food/cooking is not used.
- No building in the world. **H** places a campfire; interacting with it offers "sleep": the player
  lies down by the fire and sleep succeeds once everyone present has tried (no watch is kept);
  a random attack wakes everyone. Day sleeping is allowed, as in the original. Resting/healing and
  the U5 "hole up" camp encounters hang off this.
- Karma, gold, MP, level, quest flags live in a mod save component on the player/world save.
- Starting stats come from the virtue questionnaire in Iolo's hut (§5.13), not from a class pick.

### 5.10 Multiplayer
Design for it from the start: builders and spawners run on the zone owner; NPC state via ZDO;
quest flags via world-level custom data; dialogue is per-player. Test in a dedicated server early
(Phase 3), not at the end.

### 5.11 Action mappings (summary of `Action mappings.txt`)

| U5 command | In Valheim |
|---|---|
| Attack | Valheim combat (characters and environment). |
| Board | Interaction points on boats (ladder/rudder/seats) and on the carpet; horse/carpet riding borrows the Lox-riding logic. |
| Cast | Equip a spell mead like ammunition; cast fires it (staff optional). |
| Enter | Valheim's existing enter/door mechanics; settlements are just places you walk into. |
| Fire | Fixed ballistas in castles via the default interaction; no ship cannons. |
| Get | Default pick-up interaction. |
| Hole up & camp | H places a campfire; sleep at it (rules in §5.9); day sleep allowed. |
| Ignite torch | Valheim torches. |
| Jimmy | A locked door offers "Jimmy" when you carry a key, else says "Locked – no keys!". Magically locked doors offer "Use Skull Key" when you carry one, else "Magically sealed." (a deliberate change from the original's Use-then-direction). Guard gates cannot be unlocked. |
| Klimb | Stairs/ladders/jumping; equipped grapple (pickaxe) allows climbing slopes beyond ~45°. |
| Look | Interaction points (as on Vegvisir stones); wells etc. open a small panel; wells built from rock pieces. |
| Mix | M near a campfire → recipe menu (no kettle). |
| New order | Not relevant (one character per player). |
| Open | Valheim doors and chests. |
| Push/Pull | Not a mechanic: pushed objects are destroyable instead (barrels, puzzle walls). |
| Quit & save | Valheim's Save. |
| Ready | Tab inventory. |
| Search | Harvesting (§5.5); case by case whether the container is destroyed. |
| Talk | §5.3 conversation flow. |
| Use item | Right-click in the inventory; per-item handlers (§5.5): spyglass sky, carpet placement via the blueprint-style placement, sextant, pocket watch, torch, gem, grapple. |
| View gem | Right-click the gem → the Valheim map. |
| Yell | Words of power typed in chat within 32 m of the dungeon entrance; sails via the rudder. |
| Z-stats | Tab, showing U5 stats instead of Valheim skills. |

### 5.12 Restrictions (summary of `Restrictions and Etc.txt`)

- **Valheim tools only around Iolo's hut.** Hammer, hoe and cultivator can be picked up, but the
  player cannot leave the hut's area until they are back in the tool chest inside the hut. One
  exposed crafting table by the hut lets you maintain/repair the hut and cultivate its small plot
  (planted with barley). Enforcement: a radius check around the hut that blocks leaving while a
  tool is carried, with a message; tools cannot be dropped elsewhere.
- **Banned everywhere, hut included:** chopping trees; raising, digging or otherwise reshaping
  ground; breaking rocks; making potions or food; building any crafting table; planting food;
  crafting axes, clubs, torches or hammers; crafting anything at any table. Potions, torches and
  weapons come from shops, loot and the original's item placement. Exception: **garlic and
  ginseng** may be planted and grown (the two cultivable reagents).
- Implementation: Harmony guards on the piece/plant/terrain-op and crafting entry points keyed by
  position and item, plus removing the recipes from the crafting UI so the options never appear.

### 5.13 New world: intro and character generation (summary of `Restrictions and Etc.txt`)

- **Intro (decided).** Where Valheim shows its text blurb before the valkyrie flight, a new world
  instead plays a scrolling assembly of the original introduction screens — the STORY pictures and
  their text, rendered at runtime from the player's own files with the port's readers (`STORYn.16`,
  `TEXT.16`, `STORY.DAT`, the proportional font), skippable. When it ends or is skipped the player is
  standing in **Iolo's hut** (the dwelling built from its map; also the only place with Valheim tools,
  §5.12).
- **Character generation (decided).** Phase one is Valheim's own character customiser (cosmetic
  only). In the hut, **Hildir** stands beside the player and the door is locked; talking to her runs
  the original virtue questionnaire (the Create-a-Character gypsy questions from `QUESTION.DAT`,
  logic in `ovl_FONT.md` / `rust/src/ovl/font.rs`), whose answers set the starting STR/DEX/INT/MP and
  class exactly as the original computes them. Until it is completed all of the player's stats are
  zero. When every player present in the hut has finished, the door unlocks and Hildir vanishes; if a
  new player spawns in without having answered, the door locks and Hildir reappears until they have.
  Completion is stored per player in the mod save, so returning players are never asked again.

## 6. Phases and milestones

**Phase 0 — Foundation (1–2 weeks)**
- Repo + csproj (pattern: ValheimKillFeed; add Jötunn), BepInEx config for U5 path/scale,
  Thunderstore skeleton.
- C# data readers with tests that compare against the Rust port's loaders on the same files
  (LZW, DATA.OVL tables, map files, .NPC, .TLK decode, DUNGEON.DAT, .CBT, INIT.GAM).
- `assets/mappings/*.json` seeded from the mappings document; `docs/MAPPING_DECISIONS.md`.
- Exit: `u5 dump` console command prints any town/NPC/dialogue/item table in-game.

**Phase 1 — Britannia under your feet (2–3 weeks)**
- WorldGenerator patches: heightmap + biomes from BRIT.DAT; rivers; roads; sea.
- POIs as placeholder locations (a sign with the name); minimap pins; walk Britain→Cove→Yew.
- World wrap prototype (margins + edge teleport, on foot and by boat) and the radius expansion.
- Exit: a new world is Britannia; biomes/coasts match the U5 map; sailing off any edge arrives on
  the opposite side.

**Phase 2 — Britain and Iolo's hut (2–3 weeks)**
- Town builder for Britain from TOWNE.DAT: walls/floors/doors/roofs/paths/furniture/signs/crops;
  Castle Britannia (multi-floor) next to it; Iolo's hut from DWELLING.DAT as the spawn point with
  its tool chest, crafting table and barley plot.
- Exit: Britain, the castle and the hut are walkable and look like the mapping says; new players
  spawn in the hut.
- Status (2026-10-18): built and walked in-game: Britain, Lord British's castle, Iolo's hut and West/North/East Britanny,
  plus the overworld pass (beaches, river banks, paths, edge dressing, biome borders). Every
  decision is in `docs/MAPPING_DECISIONS.md`; the changes are in `ThunderstoreAssets/CHANGELOG.md`
  (Unreleased). Basements are built (Lord British's castle, Blackthorn's palace, Yew, Serpent's Hold),
  a full storey under the stone ground floor; ladders are rails and rungs you walk through and
  climb from any part. Still open: real stairs (stairs are ladders for now), overworld bridges (town drawbridges are done), cliffs at waterfalls, the telescope and other one-off props,
  merged meshes for FPS, and gardens/ponds for the other towns' small water features.

**Phase 3 — People (3–4 weeks)**
- NPC prefabs, schedules, pathing, sleep/work, guards; conversation panel + TLK interpreter;
  shops (arms/reagents/healer/inn/tavern) in Britain; words-of-power chat listener; Hildir's
  virtue questionnaire and the hut door lock (§5.13); dedicated-server test with two players.
- Exit: talk to everyone in Britain, buy and sell, watch the town live through a day.
- Status (2026-10-18): townsfolk of the built settlements spawn dressed (npcs.json) and walk their
  schedules across storeys (Core/TownWalker, TownPath; simulated over a day in TownsfolkTests).
  They sit and sleep, keep one to a tile, and talk (E: panel + Data/TalkSession, TALK.OVL ported).
  Shops sell (arms, reagents, guild, tavern rations; healers heal/cure) through Valheim's trade
  window; guards fight monsters in town, raised by a villager's alarm (World/TownWatch: messenger,
  hiding behind locked doors, guards fetching guards, 3000-hp guards, reinforcements). Next: selling, inn/stables/shipwright, tavern meals and
  rumours, the guards' challenge and alarm, doors, work poses, gifts (with the item set).

**Phase 4 — All settlements (2 weeks)**
- Generalise builders to all 26 towns/dwellings/keeps/castles (type-specific materials), all NPCs.

**Phase 5 — Items, magic, player stats, actions (3 weeks)**
- Item set (trinket rings/amulets, reshades, reels), reagents + M mixing menu, spell meads as
  ammunition + effects, U5 stats replacing skills in Tab, karma/gold/MP/level, INIT.GAM new-game
  import, mod save; H campfire + sleep rules; grapple climbing; use-item handlers (gem map,
  sextant, pocket watch, carpet placement); the spyglass night-sky skybox; barley rations and
  daily eating; Iolo's-hut tool rules and the banned-action guards (§5.12); the new-world intro
  slideshow (§5.13).

**Phase 6 — Monsters, spawning, dungeons (3–4 weeks)**
- Monster mapping + stats, overworld spawner, pirate ships; dungeon interiors + rooms; chests and
  loot; dungeon light.

**Phase 7 — Story (3–4 weeks)**
- Calendar/moons/moongates, whirlpools to the Underworld (§5.1), Shadowlords, shards and shrines, Blackthorn's castle scenes, the
  Underworld continent (world-radius expansion, UNDER.DAT terrain, cave environment, the
  connections from dungeon bottoms/falls/Hythloth), Lord British's rescue, ending.

**Phase 8 — Release**
- Balance pass, performance (piece merging/culling), docs, Thunderstore publish (namespace
  `tarbaby`, categories `ai-generated` + `client-side`/`server-side` as applicable, versions in
  manifest/Plugin/CHANGELOG together — see `~/.claude/CLAUDE.md`).

## 7. Risks and how we handle them

| Risk | Mitigation |
|---|---|
| Patching `WorldGenerator` breaks with Valheim updates | Isolate hooks in `World/`; follow BetterContinents' hook set; pin the Valheim version in the manifest. |
| World-radius expansion (for the Underworld) conflicts with other patches/mods | Prototype it in Phase 1 (empty expanded world); fall back to the elevated-layer Underworld (§5.1) if unstable. |
| Edge wrap: teleporting ships with passengers and nearby creatures; zone reload hitch | Prototype in Phase 1 on the empty world: wrap the player on foot, then a Karve with two players; measure the hitch; keep margins wide enough that nothing important sits on the seam. |
| Thousands of pieces per town hurt FPS/zone load | Build on zone load only; merge static walls into combined meshes; aggressive LOD; measure in Phase 2 before scaling to 26 towns. |
| NPC pathing in Valheim's physics world | Use our own grid BFS (as the original) on the generated layout, move with `ZSyncTransform`; collisions only with the player. |
| Real-time combat changes balance drastically | Keep U5 stat ratios, tune multipliers in `monsters.json`; group sizes from U5 tables. |
| Legal: redistributing game content | Runtime reading of the player's own files; our package has no U5 text/art/maps (same posture as the Rust port and the SCUM tools). |
| Text volume (TLK/SIGNS/SHOPPE) in UI | Decoded at runtime, rendered in Valheim's font; proportional-font story screens replaced by plain panels. |
| Multiplayer desync of schedules/quest flags | Zone-owner simulation; quest flags in world custom data; test on a dedicated server from Phase 3. |
| Removing the hammer/progression and replacing skills conflicts with Valheim's core UI | Patch at the narrowest points (hide build hotkey, replace the skills tab content); keep vanilla data intact so unloading the mod leaves worlds valid. |
| Skybox change for the spyglass stars/comets | Prototype early in Phase 5 (swap the night sky material, keep the day sky); fall back to a 2-D sky-map panel if the skybox proves awkward. |

## 8. Working agreements

- One mapping change = one line in `docs/MAPPING_DECISIONS.md` (date, U5 thing, Valheim thing, why).
- Every table has a dump command and a unit test against the Rust port's output.
- Each phase ends with a short playable demo and a CHANGELOG entry; tag it.
- Keep `~/Projects/Ultima5Revolutions/analysis/*.md` as the authority on original behaviour; when
  in doubt, read the Rust function (`rust/src/...`) rather than guessing.
