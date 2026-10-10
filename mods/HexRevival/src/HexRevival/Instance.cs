using System;

namespace HexRevival
{
    /// <summary>
    /// Identite de l'instance de jeu (outil de test). Plusieurs HEX peuvent tourner en parallele :
    ///   Hex.exe -batchmode -nographics -hexinstance 2 -hexinstances 4
    /// L'instance 0 (par defaut) garde les noms de fichiers d'origine ; l'instance k utilise le
    /// suffixe ".k" (HexRevival.cmd.k, HexRevival.state.log.k, HexRevival.gauntlet.csv.k ...).
    /// </summary>
    public static class Instance
    {
        public static readonly int Id;
        public static readonly int Count = 1;
        /// <summary>Lancee par le superviseur (HexRevival_soak.bat) : pour relancer, il suffit de quitter.</summary>
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
