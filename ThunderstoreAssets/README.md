# Bretaheim

*Ultima V: Warriors of Destiny* rebuilt inside Valheim: Britannia as the world, its towns, castles and
dungeons to walk into, its people on their daily schedules with their original conversations.

**Work in progress.** Version 0.1 is the foundation only: it reads your Ultima V files and lets you
inspect them in-game. It does not change the world yet.

## You need your own copy of Ultima V

No Ultima V data ships with this mod. It reads the game files (DATA.OVL, BRIT.DAT, TOWNE.DAT, ...)
from your own copy, for example the GOG release, at start-up.

1. Install the mod (r2modman or manually, together with BepInExPack and Jötunn).
2. Start Valheim once, then quit. This creates `BepInEx/config/tarbaby.bretaheim.cfg`.
3. Set `UltimaVDirectory` in that file to the folder that holds `DATA.OVL`, for example
   `C:\GOG Games\Ultima 5` or `~/Projects/Ultima 5`.
4. Start Valheim. The BepInEx log says how many locations were read. If the folder is wrong, the
   log lists the missing files and the mod stays disabled.

## Console

Press F5 and type `u5 dump` to see the tables: `u5 dump locations`, `u5 dump npcs Britain`,
`u5 dump talk Britain 4`, `u5 dump items`, `u5 dump monsters`, `u5 dump init`, ...

## Multiplayer

Every player and the server need the mod and their own Ultima V files.
