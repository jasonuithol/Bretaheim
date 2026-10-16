using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using U5InValheim.Data;

namespace U5InValheim.Tests
{
    public static class Env
    {
        public static readonly string U5Dir = Environment.GetEnvironmentVariable("U5_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects", "Ultima 5");

        public static bool HaveU5 => U5Data.Validate(U5Dir).Count == 0;

        static readonly Lazy<U5Data> data = new Lazy<U5Data>(() => U5Data.Load(U5Dir));
        public static U5Data Data => data.Value;

        public static string RepoRoot
        {
            get
            {
                var d = new DirectoryInfo(AppContext.BaseDirectory);
                while (d != null && !File.Exists(Path.Combine(d.FullName, "MASTER_PLAN.md"))) d = d.Parent;
                return d?.FullName ?? throw new InvalidOperationException("repo root not found");
            }
        }

        /// <summary>Tab-separated golden file rows (tests/.../golden/name).</summary>
        public static List<string[]> Golden(string name)
            => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "golden", name))
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .Select(l => l.Split('\t')).ToList();

        public static ulong Fnv1a64(byte[] data)
        {
            ulong h = 0xcbf29ce484222325;
            foreach (byte b in data) { h ^= b; h *= 0x100000001b3; }
            return h;
        }
    }

    /// <summary>A fact that needs the player's Ultima V files; skipped when they are not installed.</summary>
    public sealed class U5FactAttribute : FactAttribute
    {
        public U5FactAttribute()
        {
            if (!Env.HaveU5) Skip = $"Ultima V files not found in '{Env.U5Dir}' (set U5_DIR)";
        }
    }
}
