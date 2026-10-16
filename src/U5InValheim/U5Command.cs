using System.Linq;
using Jotunn.Entities;
using U5InValheim.Data;

namespace U5InValheim
{
    /// <summary>F5 console: "u5 dump &lt;table&gt; [args]" prints any Ultima V table (see Dump.Usage).</summary>
    public class U5Command : ConsoleCommand
    {
        public override string Name => "u5";
        public override string Help => "Ultima V data: u5 dump <locations|map|npcs|talk|items|monsters|spells|...> [args]";

        public override void Run(string[] args)
        {
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
            Print(Dump.Run(Plugin.U5, args.Skip(1).ToArray()));
        }

        static void Print(string text)
        {
            foreach (var line in text.TrimEnd('\n').Split('\n')) Console.instance.Print(line);
        }
    }
}
