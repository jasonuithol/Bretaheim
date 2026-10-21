# Bretaheim

*Ultima V: Warriors of Destiny* rebuilt inside Valheim as a BepInEx/Jötunn mod. The mod reads the
player's own copy of Ultima V at runtime; no Ultima data is distributed.

- Plan: [MASTER_PLAN.md](MASTER_PLAN.md) · decisions: [docs/MAPPING_DECISIONS.md](docs/MAPPING_DECISIONS.md)
- Status: **Phase 0 (foundation) done** — data readers, `u5 dump` console command, mapping tables, tests.

```sh
dotnet build Bretaheim.sln
dotnet test tests/Bretaheim.Tests              # U5_DIR=/path/to/Ultima5 for the data tests
dotnet run --project tools/U5Dump -- talk Britain 4
```
