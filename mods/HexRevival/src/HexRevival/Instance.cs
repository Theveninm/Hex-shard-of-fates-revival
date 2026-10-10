using System;

namespace HexRevival
{
    /// <summary>
    /// Identity of the game instance (test tool). Several HEX can run in parallel:
    ///   Hex.exe -batchmode -nographics -hexinstance 2 -hexinstances 4
    /// Instance 0 (default) keeps the original file names; instance k uses the
    /// suffix ".k" (HexRevival.cmd.k, HexRevival.state.log.k, HexRevival.gauntlet.csv.k ...).
    /// </summary>
    public static class Instance
    {
        public static readonly int Id;
        public static readonly int Count = 1;
        /// <summary>Started by the supervisor (HexRevival_soak.bat): to restart, just exit.</summary>
        public static readonly bool Supervised;
        public static readonly bool Headless;

        static Instance()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLower();
                int v;
                if (a == "-hexinstance" && i + 1 < args.Length && int.TryParse(args[i + 1], out v)) { Id = v; Supervised = true; }
                if (a == "-hexinstances" && i + 1 < args.Length && int.TryParse(args[i + 1], out v)) Count = Math.Max(1, v);
                if (a == "-batchmode" || a == "-nographics") Headless = true;
            }
        }

        public static string Suffix { get { return Id == 0 ? "" : "." + Id; } }

        public static string Name(string baseName) { return baseName + Suffix; }
    }
}
