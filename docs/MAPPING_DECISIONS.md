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

## Open (need a decision)

| U5 | Problem | Proposal |
|---|---|---|
| — | Nothing open. | |

## Data quirks found while reading the files

- **TOWNE.TLK 23 (Mario, Yew)** has a stray NUL inside his "mino/crim" answer, so every later keyword is
  shifted by one section in the original too. We reproduce the original's sectioning; his last
  keywords never match.
- **Scripted NPCs** (Blackthorn, Stillwelt, Sutek, …) have an empty job: their description jumps
  straight to label 1.
- **Destard, Shame and Doom** have no ladder on level 0; you enter them in a room.
