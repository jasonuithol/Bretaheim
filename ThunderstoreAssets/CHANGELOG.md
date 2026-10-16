# Changelog

## 0.1.0
- Phase 0 (foundation): BepInEx/Jötunn plugin skeleton with the `UltimaVDirectory` and `TileMetres` settings.
- Readers for the player's own Ultima V files: LZW, DATA.OVL tables, BRIT.DAT/UNDER.DAT, settlement
  maps with floors, .NPC schedules, .TLK conversations, DUNGEON.DAT, .CBT combat maps, LOOK2.DAT,
  SIGNS.DAT and INIT.GAM.
- `u5 dump <table>` console command (F5) prints any of those tables in-game.
- Mapping tables (terrain, monsters, items, NPC looks, status effects) in `mappings/`.
- No gameplay changes yet: the world is still Valheim's.
