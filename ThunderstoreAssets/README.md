# Bretaheim (alpha)

*Ultima V: Warriors of Destiny* rebuilt inside Valheim: Britannia as the world, its towns, castles and
dungeons to walk into, its people on their daily schedules with their original conversations.

> **Alpha.** Most of the game is in and playable from Iolo's hut to the Underworld, but expect rough
> edges, missing pieces and changes between versions. Play it in a new world. Bug reports are welcome
> on [GitHub](https://github.com/jasonuithol/Bretaheim/issues).

## You need your own copy of Ultima V

No Ultima V data ships with this mod. It reads the game files (DATA.OVL, BRIT.DAT, TOWNE.DAT, ...)
from your own copy, for example the GOG release, at start-up.

1. Install the mod (r2modman or manually, together with BepInExPack and Jötunn).
2. Start Valheim once, then quit. This creates `BepInEx/config/tarbaby.bretaheim.cfg`.
3. Set `UltimaVDirectory` in that file to the folder that holds `DATA.OVL`, for example
   `C:\GOG Games\Ultima 5` or `~/Ultima 5`.
4. Start Valheim and create a new world. New worlds are Britannia (their seed starts with
   "Britannia"); worlds with other seeds stay vanilla Valheim. If the folder is wrong, the BepInEx log
   lists the missing files and the mod stays disabled.

## What is in it

- **Britannia** from BRIT.DAT at 32 m a tile, with the Underworld beneath it as a second continent.
- **Towns, castles, keeps and dwellings** built from the original maps, floor by floor, with their
  furniture, signs, locked doors, secret walls and the harpsichord.
- **Townsfolk** on their original schedules, with their conversations, shops, inns, healers,
  shipwrights, stables and guards (fire a town's ballista and see what happens).
- **The Avatar**: Hildir's gypsy questions, Ultima stats on the Tab screen, karma, food, experience
  and levels. Dying costs experience, not items: Lord British raises you in his throne room.
- **Magic**: reagents, mixing, the 48 spells, scrolls and potions (cast with Z).
- **Monsters** with their Ultima names and colours, random encounters by the original's rates.
- **Dungeons**, the moongates and moons, the Shadowlords, ships, skiffs, horses and the magic carpet,
  the shrines and the story's scenes up to Lord British's rescue.

## Console

Press F5 and type `u5` for the commands: `u5 quest`, `u5 karma`, `u5 time`, `u5 moons`, `u5 intro`,
`u5 dump <table>`, and more with `devcommands` on.

## Multiplayer

Every player and the server need the mod (the same minor version) and their own Ultima V files.
