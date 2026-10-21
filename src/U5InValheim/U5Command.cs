using System;
using System.Linq;
using HarmonyLib;
using U5InValheim.Data;

namespace U5InValheim
{
    /// <summary>
    /// F5 console: "u5 dump &lt;table&gt; [args]" prints any Ultima V table (see Dump.Usage).
    /// Registered the vanilla way (postfix on Terminal.InitTerminal, as in AdminHelpDesk): Jötunn
    /// 2.29's CommandManager cannot construct the current Terminal.ConsoleCommand.
    /// </summary>
    public static class U5Command
    {
        public static void Install(Harmony harmony)
        {
            var init = AccessTools.Method(typeof(Terminal), "InitTerminal");
            if (init == null)
            {
                Plugin.Log.LogError("Terminal.InitTerminal not found: the u5 console command is unavailable");
                return;
            }
            harmony.Patch(init, postfix: new HarmonyMethod(typeof(U5Command), nameof(InitTerminal_Postfix)));
        }

        static void InitTerminal_Postfix()
        {
            new Terminal.ConsoleCommand("u5", "Ultima V data: u5 dump <locations|map|npcs|talk|items|monsters|spells|...> [args]; u5 encounter (devcommands) brings a random encounter; u5 play <notes> plays the harpsichord you stand at",
                delegate (Terminal.ConsoleEventArgs args) { Run(args.Context, args.Args.Skip(1).ToArray()); });
        }

        static void Run(Terminal terminal, string[] args)
        {
            void Print(string text)
            {
                foreach (var line in text.TrimEnd('\n').Split('\n')) terminal.AddString(line);
            }

            if (args.Length > 0 && args[0] == "encounter")
            {
                var me = Player.m_localPlayer;
                if (!Terminal.m_cheat) Print("u5 encounter needs devcommands");
                else if (me == null || WorldGen.Britannia.Active == null) Print("u5 encounter: only in Britannia");
                else Print(WorldGen.UltimaSpawner.Encounter(me.transform.position, me.GetPlayerName(), new System.Random()) ?? "nothing comes here (no monster for this land, a town, or 8 already near)");
                return;
            }
            if (args.Length > 0 && args[0] == "play")
            {
                var me = Player.m_localPlayer;
                var harp = me == null ? null : UnityEngine.Object.FindObjectsByType<WorldGen.Harpsichord>(UnityEngine.FindObjectsSortMode.None)
                    .OrderBy(h => UnityEngine.Vector3.Distance(h.transform.position, me.transform.position)).FirstOrDefault();
                if (harp == null || UnityEngine.Vector3.Distance(harp.transform.position, me.transform.position) > 5f) Print("u5 play: no harpsichord here");
                else harp.SetText(string.Join("", args.Skip(1)));
                return;
            }
            if (args.Length == 0 || args[0] != "dump")
            {
                Print(Dump.Usage);
                return;
            }
            if (Plugin.U5 == null)
            {
                Print("Ultima V data is not loaded: set UltimaVDirectory in BepInEx/config/tarbaby.u5invalheim.cfg and restart.");
                return;
            }
            try
            {
                Print(Dump.Run(Plugin.U5, args.Skip(1).ToArray()));
            }
            catch (Exception e)
            {
                Print($"error: {e.Message}");
            }
        }
    }
}
