# Mapping decisions

One line per change to the U5 → Valheim mapping (MASTER_PLAN §8): date, U5 thing, Valheim thing, why.
The default is `Ultima5 to Valheim asset mappings.txt`; the tables live in `assets/mappings/*.json`.
Prefab names are checked against `tools/data/valheim_prefabs.txt` by the tests.

## Decided (Phase 0 seeding, 2026-10-16)

| Date | U5 | Valheim | Why |
|---|---|---|---|
| 2026-10-16 | Ring of Regeneration | `TrinketFlametalEitr` | "Jormundling" in the doc is the Jörmundling trinket. |
| 2026-10-16 | Gem | `Ruby`, tinted purple | Doc: "reshade a ruby to be purple". |
| 2026-10-16 | Spyglass | `TankardAnniversary` | "Horn of Celebration" is the anniversary tankard prefab. |
| 2026-10-16 | Lord British's Crown | `HelmetDverger`, gold | "Dvergr circlet" = Dverger Circlet. |
| 2026-10-16 | Black pearl | `Blueberries` | Doc: "Bukeberries ?"; no such item, blueberries are the closest. |
| 2026-10-16 | Chain coif, Iron helm | `HelmetPadded`, `HelmetIron` | Not in the doc; follows its chain → padded rule and the plain name. |
| 2026-10-16 | Club | `Club` | Not in the doc; Valheim has one. |
| 2026-10-16 | Armour sets | chest + legs | Each U5 armour gives the matching Valheim chest and legs pieces. |
| 2026-10-16 | Troll | `GoblinBrute` | Doc says "Brute"; Fuling Berserker chosen over Greydwarf Brute (`Greydwarf_Elite`) for size. |
| 2026-10-16 | Daemon | `Skeleton_Hildir` (Brenna), alt `Surtling` | Doc: "Brenna / Surtling". |
| 2026-10-16 | Sea Horse / Shark | `Fish12` (pufferfish) ×4 / `Fish9` ×6 | Doc: giant pufferfish / "a big fish". Both need an attack added. |
| 2026-10-16 | Pirate ship crew | per ship: human pirates (leather + bow) or Fulings throwing `GoblinSpear` | User: "sometimes fulings and sometimes humans; the fuling ones would throw spears". |
| 2026-10-16 | Gazer | `Gjall`, reshaded pink and purple | User decision ("Skugg" is not a Valheim creature). |
| 2026-10-16 | Wisp | `Wraith`, very glowy, translucent, bright cyan | User decision (Valheim's Wisp is a light, not a creature). |
| 2026-10-16 | Shadow Lord | `Wraith` | User decision. |
| 2026-10-16 | Squid | `BonemawSerpent` | User: the Ashlands serpent, unused elsewhere. |
| 2026-10-16 | Main Gauche | `KnifeBlackMetal` | User: any unused knife. |
| 2026-10-16 | Crawler | `Seeker` (alt `Tick`) | User suggestion. |
| 2026-10-16 | Jewel Shield / Magic Shield | `ShieldSerpentscale` / `ShieldSilver` | User: the jewel shield is definitely the serpent scale one, so the magic shield moves to silver. |
| 2026-10-16 | Blackthorn, Lord British | mage robe + iron legs + sword (Blackthorn: Ashlands robe, black metal sword; Lord British: silver sword, gold circlet) | User: both are SpellSwords, part mage and part fighter. |
| 2026-10-16 | Wanderer (type 13) | none | No settlement NPC uses the type. |
| 2026-10-16 | Horse | `Lox` now; hornless rideable `Eikthyr` later | User decision. |
| 2026-10-16 | Mage / Fighter NPCs | mage robe, legs and hood / iron set and sword | Proposal approved by the user. |
| 2026-10-16 | Spells (mead bases) | one `MeadBase*` per circle | Doc: "bowls of raw mead". 18 bases exist for 48 spells, so the circles are told apart, not the spells (proposal). |
| 2026-10-16 | Terrain: moongate sites | tile 0xa4 (and 0xdc) | BRIT.DAT marks the 8 moongate sites with tile 0xa4. |
| 2026-10-16 | Terrain: shoals | −1.2 m | Walkable shallows ("Water (walkable) → Shallows"); the SCUM value (−5 m) is too deep to wade. |
| 2026-10-16 | Terrain: extra tiles | bridge 0x1d, ruined shrine 0x1a, waterfalls 0xd4–0xd7, desert 0xdd/0xe0–0xe2, water 0xe4–0xe7, Codex flames 0x89/0xde, darkness 0xff | Present in BRIT.DAT/UNDER.DAT but missing from `generate_terrain.py`'s CLASS_OF. |
| 2026-10-16 | Ararat | Underworld location | Its keep tile is in UNDER.DAT (49, 58); the surface tile there is brush. |
| 2026-10-17 | Town scale | 2 m × 2 m × 4 m per tile; 4 m wall zones, wood inter-level floors, roofs on the last wall zone | `Constructing world assets.txt`, Solution A; the user raised the wall zone from 3 m to 4 m. Doors stay 3 m (2 m + viewport); openings get a lintel course. |
| 2026-10-17 | Walls (0x4f, 0x4e, 0x8c, 0xf8, 0xfe) | solid blocks: four courses of `stone_wall_2x1`, each 1 m out of phase, `stone_wall_1x1` at run ends (Blackthorn: black marble) | Doc "deluxe" basic stone wall. 0x4e (nick) will be the secret door; 0x8c (loose brick) and 0xfe are plain walls; 0xf8 carries its SIGNS.DAT sign. |
| 2026-10-17 | Cobblestone wall (0x4d) | `wood_pole2` frame (two 2 m posts) on the tile corners, two rows of `stone_fence` four high, `wood_beam` on top of open faces | Doc. Buildings walled mostly with it are cottages (thatch, plain beds). |
| 2026-10-17 | Archway (0x87) | walk-through, a course of bricks on top | Doc calls 135 "a wall above a gate"; the tiles flank or form town entrances (Paws, Jhelom), so they stay passable. |
| 2026-10-17 | Window (0x4b) / arrow slit (0x4a) | `stone_floor_2x2` sill + two `stone_arch` at the top / brick courses with a cross-shaped gap in the middle one | Doc. |
| 2026-10-17 | Doors (0xb8–0xbb, 0x97, 0x98) | `wood_door` 3 m tall (scaled 1.5), or 2 m with an open 1 m viewport above; locks (key / magic) stored on the door | Doc. Jimmy / skull keys and the blue glow come with the action work (Phase 5). |
| 2026-10-17 | Parapets (0x50–0x57) | 1 m `stone_wall_2x1` course flush with the outer face, `stone_wall_1x1` merlon in the middle (none on 0x56/0x57) | Doc. The outer side is the open side facing away from the settlement centre. |
| 2026-10-17 | Table with candelabra (0xbe) | a table tile (joins the table run) with three `Candle_resin` on it, burning for ever | User (was a standing torch). |
| 2026-10-17 | Randotron: hallway ends | the dead end of a one-tile hallway (empty, or its stairs/ladder): 60 % get a banner (`piece_banner01`–`11`) or an animal head (deer, boar, wolf, lox, bear, neck, hare, fenring, serpent trophy) on an `itemstand`, fixed to the stand | User: the first random-decoration rule. Picked by a hash of settlement, floor and tile. |
| 2026-10-17 | Overworld paths (0x20–0x26, bridges) | a 4 m dirt track through the tile centres, smoothed into curves, joining the places the roads reach; the tiles stay Plains | User: \"4 m wide\", not the whole 32 m tile. Plains per the doc (\"for now\"). |
| 2026-10-17 | Flat roofs | a building with parapets on top gets no roof, except over the tiles the floor above draws as roof (0x27/0x28) | User (both rules). Lord British's castle: its wall-walks are open, its halls roofed. |
| 2026-10-17 | Roofs | cottages `wood_roof` (thatch), other town buildings `darkwood_roof` (shingle), castles/keeps/lighthouses `piece_grausten_roof_45` | Doc. Grausten hips are its two triangle pieces turned upside down (y scale −1); crests are half-size slopes. |
| 2026-10-17 | Shop signposts (0xe0 bottom, 0xe1/0xe2 top) | a 4 m post on 0xe0; the guild icon tile keeps its banner | Doc 224–226 (the tile names say "parched desert"; the maps show them as posts). Banner with the guild icon on the post: later. |
| 2026-10-17 | Furniture (first pass) | anvil `forge_ext2`, forge tool rack (0xfc) `forge_ext6` hung on the south wall 2.2 m up, harpsichord `blackforge`, guillotine `piece_MeadCauldron`, torture rack `piece_table`, metal grate `iron_floor_2x2`, wall torches `piece_walltorch`, brazier and lighthouse light `piece_brazierfloor01`, street lamp `piece_dvergr_lantern_pole` (turned 90° anticlockwise), fireplace `hearth` squeezed to 2×2, stove `fire_pit` + `piece_cauldron`, spit `fire_pit` + `piece_cookingstation`, Flame `blackmarble_column_1` + `MountainKit_brazier_blue`, carpets a random rug, beds `piece_bed02` (cottages `bed`), dead tree `SwampTree1` (the swamp's ancient tree) at ¼ size; water fountains (0xd8–0xdb) `piece_bathtub` at 0.62 (2 m) on a `blackmarble_column_2`, both white, the stove pressed flat into the rim, no fuel, interact to drink; shelves, bookshelves, well, hitching post, stocks, grave cross and headstone, mirror and vanity built from pieces | Doc. Still to come: telescope, cannonball pyramid, manacles' chains, food on tables, candles, the playable harpsichord, the coin well, drawbridges (0x48/0x49 are plank floors for now), real stairs and beam ladders. |
| 2026-10-17 | Overworld biomes | meadows (6) open, scrub (8) Meadows thick forest, forest (9) light / dense forest (10) heavy Black Forest, foothills (11) treeless Black Forest, border foothills (14, 15) open Meadows, paths Plains, shoreline (48–55) Plains at sea level with 52–55 split diagonally into land and sea, big castle walls Plains (Lord British) / treeless Black Forest (Blackthorn) | `Constructing world assets.txt`. Tree density is Valheim's forest factor per tile. Boulders for the foothills: to come. |
| 2026-10-17 | Elevation | mountains 90 m, peaks 140 m, foothills 28 m, border foothills 18 m, heights smoothed over ~50 m (median slope: mountains 16°, was 54°) | User: "calm that down even more" (were 140 / 230, before that 220 / 420). `U5Dump slopes` measures it. Cliffs at the waterfalls (212–215): to come. |
| 2026-10-17 | Rivers | gentle banks everywhere (the ground eases down over ~40 m, the bed shelves to a 2.8 m channel), wider near the sea; water lilies in the shallows, shrubs and berry bushes on the banks | User (replaces steep upstream banks). Lilies are Valheim's lily clutter limited to rivers. |
| 2026-10-17 | Biome borders | biome lookup warped by noise (~2 tile meanders, ~1 tile wobble); ground colour from the biome map every 4 m blurred over ~8 m instead of Valheim's 64 m chunk corners | User: no squares or straight borders. |
| 2026-10-17 | Overland signpost (0xa4) | a rune stone of the surrounding biome (`RuneStone_*`) in a 14 m clearing | Doc. The same tile marks the moongate sites; the stones stay and the gates appear there in Phase 7. |

## Open (need a decision)

| U5 | Problem | Proposal |
|---|---|---|
| — | Nothing open. | |

## Data quirks found while reading the files

- **Upper floors repeat the ground below them** (so an upper window had something to show). Only the
  tiles walkable from the ladders and stairs arriving from the floor below (through doors, secret doors
  and archways, standing on something built or floored below) and the walls bounding that walk are on
  an upper floor; roofs go only where nothing of the floor above stands.

- **Tile numbers in `Constructing world assets.txt`** are decimal TILES.16 indexes and match the data, except around
  140: the data has 139 torture rack, 140 loose brick, 141 harpsichord, 142 guillotine, 143 lava (the doc is one lower).
  The tables follow the data's names.

- **TOWNE.TLK 23 (Mario, Yew)** has a stray NUL inside his "mino/crim" answer, so every later keyword is
  shifted by one section in the original too. We reproduce the original's sectioning; his last
  keywords never match.
- **Scripted NPCs** (Blackthorn, Stillwelt, Sutek, …) have an empty job: their description jumps
  straight to label 1.
- **The Shrine of Spirituality** has coordinates (0,0): it is not on the map and is reached only
  through a moongate (Phase 7 story work).
- **Destard, Shame and Doom** have no ladder on level 0; you enter them in a room.
