// HEX Offline - lanceur / installeur (Windows, .NET Framework 4.x).
//
// Double-clic :
//   1. cherche HEX: Shards of Fate (dossier du lanceur, registre Windows, bibliotheques Steam,
//      dossier choisi la fois precedente) ;
//   2. absent : propose de l'installer via Steam (s'il est dans la bibliotheque du joueur) ou de
//      choisir le dossier du jeu ;
//   3. present : installe / met a jour le mod (BepInEx + HexOffline) dans le dossier du jeu,
//      puis lance le jeu.
// Options : --uninstall (retire le mod), --install "<dossier>" (usage interne, apres elevation).
//
// Transparence : le lanceur n'utilise pas le reseau, lit le registre seulement pour trouver Steam et
// le jeu (et y retient le dossier choisi, HKCU\Software\HexOffline), copie les fichiers du dossier "mod" livre a cote de lui dans le dossier du jeu, et ne
// demande les droits administrateur que si ce dossier est protege, apres accord du joueur.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("HEX Offline")]
[assembly: AssemblyProduct("HEX Offline")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace HexOfflineLauncher
{
    internal static class Program
    {
        private const string AppId = "410380";
        private const string ExeName = "Hex.exe";
        private const string DefaultInstallDir = "HEX SHARDS OF FATE";
        private const string Version = "1.0.0";
        private const string Title = "HEX Offline";

        private static bool Fr { get { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr"; } }

        private static string L(string en, string fr) { return Fr ? fr : en; }

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            try
            {
                if (args.Length >= 2 && args[0] == "--install")
                    return Install(args[1]) ? 0 : 1;

                string game = FindGame();
                if (args.Length >= 1 && args[0] == "--uninstall")
                {
                    if (game == null) game = AskFolder();
                    if (game != null) Uninstall(game);
                    return 0;
                }

                if (game == null)
                {
                    game = HandleMissingGame();
                    if (game == null) return 0;
                }

                if (!IsModUpToDate(game) && !InstallWithElevationIfNeeded(game))
                    return 1;

                Process.Start(new ProcessStartInfo(Path.Combine(game, ExeName)) { WorkingDirectory = game, UseShellExecute = true });
                return 0;
            }
            catch (Exception e)
            {
                MessageBox.Show(L("Unexpected error:\n\n", "Erreur inattendue :\n\n") + e.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        // ------------------------------------------------------------------
        // Recherche du jeu et de Steam
        // ------------------------------------------------------------------

        private static string SteamPath()
        {
            string[][] keys =
            {
                new[] { "HKEY_CURRENT_USER\\Software\\Valve\\Steam", "SteamPath" },
                new[] { "HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Valve\\Steam", "InstallPath" },
                new[] { "HKEY_LOCAL_MACHINE\\SOFTWARE\\Valve\\Steam", "InstallPath" },
            };
            foreach (string[] k in keys)
            {
                try
                {
                    string p = Registry.GetValue(k[0], k[1], null) as string;
                    if (!string.IsNullOrEmpty(p))
                    {
                        p = p.Replace('/', '\\');
                        if (Directory.Exists(p)) return p;
                    }
                }
                catch { }
            }
            string def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            return Directory.Exists(def) ? def : null;
        }

        private static List<string> SteamLibraries(string steam)
        {
            List<string> libs = new List<string>();
            if (steam == null) return libs;
            libs.Add(steam);
            string vdf = Path.Combine(steam, "steamapps\\libraryfolders.vdf");
            try
            {
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"(?:path|\\d+)\"\\s+\"([^\"]+)\""))
                    {
                        string p = m.Groups[1].Value.Replace("\\\\", "\\");
                        if (Directory.Exists(p) && !libs.Exists(x => string.Equals(x.TrimEnd('\\'), p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                            libs.Add(p);
                    }
            }
            catch { }
            return libs;
        }

        private static string FindGame()
        {
            // Lanceur depose directement dans le dossier du jeu.
            string here = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(here, ExeName))) return here.TrimEnd('\\');

            // Entree "Programmes et fonctionnalites" creee par Steam a l'installation du jeu.
            foreach (string root in new[] { "HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App " + AppId,
                                            "HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App " + AppId })
            {
                try
                {
                    string loc = Registry.GetValue(root, "InstallLocation", null) as string;
                    if (!string.IsNullOrEmpty(loc) && File.Exists(Path.Combine(loc, ExeName))) return loc.TrimEnd('\\');
                }
                catch { }
            }

            // Dossier choisi a la main lors d'un lancement precedent.
            try
            {
                string saved = Registry.GetValue("HKEY_CURRENT_USER\\Software\\HexOffline", "GameFolder", null) as string;
                if (!string.IsNullOrEmpty(saved) && File.Exists(Path.Combine(saved, ExeName))) return saved;
            }
            catch { }

            foreach (string lib in SteamLibraries(SteamPath()))
            {
                string installDir = DefaultInstallDir;
                string manifest = Path.Combine(lib, "steamapps\\appmanifest_" + AppId + ".acf");
                try
                {
                    if (File.Exists(manifest))
                    {
                        Match m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                        if (m.Success) installDir = m.Groups[1].Value;
                    }
                }
                catch { }
                string dir = Path.Combine(lib, "steamapps\\common\\" + installDir);
                if (File.Exists(Path.Combine(dir, ExeName))) return dir;
            }
            return null;
        }

        private static string HandleMissingGame()
        {
            // HEX a ete retire de Steam fin 2020 : Steam ne l'installe que pour les comptes qui l'ont deja
            // dans leur bibliotheque (sinon "Pas de licence").
            bool steam = SteamPath() != null;
            string text = steam
                ? L("HEX: Shards of Fate was not found on this computer.\n\n" +
                    "Yes: ask Steam to install it. This only works if the game is already in your Steam library (it was removed from the store at the end of 2020).\n" +
                    "No: choose the game folder yourself (if you have the game files from another source).",
                    "HEX: Shards of Fate n'a pas été trouvé sur cet ordinateur.\n\n" +
                    "Oui : demander à Steam de l'installer. Cela ne fonctionne que si le jeu est déjà dans votre bibliothèque Steam (il a été retiré de la boutique fin 2020).\n" +
                    "Non : choisir vous-même le dossier du jeu (si vous avez les fichiers du jeu par un autre moyen).")
                : L("HEX: Shards of Fate was not found on this computer.\n\nChoose the game folder (the one containing Hex.exe)?",
                    "HEX: Shards of Fate n'a pas été trouvé sur cet ordinateur.\n\nChoisir le dossier du jeu (celui qui contient Hex.exe) ?");
            DialogResult r = MessageBox.Show(text, Title, steam ? MessageBoxButtons.YesNoCancel : MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (steam && r == DialogResult.Yes)
            {
                Process.Start("steam://install/" + AppId);
                MessageBox.Show(L("When the installation is finished, run HEX Offline again.\nIf Steam answers \"No license\", the game is not in your library: run HEX Offline again and choose No to pick the game folder.",
                    "Une fois l'installation terminée, relancez HEX Offline.\nSi Steam répond « Pas de licence », le jeu n'est pas dans votre bibliothèque : relancez HEX Offline et choisissez Non pour indiquer le dossier du jeu."),
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            if ((steam && r == DialogResult.No) || (!steam && r == DialogResult.OK))
            {
                string dir = AskFolder();
                if (dir != null)
                {
                    try { Registry.SetValue("HKEY_CURRENT_USER\\Software\\HexOffline", "GameFolder", dir); } catch { }
                }
                return dir;
            }
            return null;
        }

        private static string AskFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = L("Select the HEX: Shards of Fate folder (the one containing Hex.exe).", "Choisissez le dossier de HEX: Shards of Fate (celui qui contient Hex.exe).");
                while (d.ShowDialog() == DialogResult.OK)
                {
                    if (File.Exists(Path.Combine(d.SelectedPath, ExeName))) return d.SelectedPath;
                    MessageBox.Show(L("Hex.exe is not in this folder.", "Hex.exe ne se trouve pas dans ce dossier."), Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Installation du mod
        // ------------------------------------------------------------------

        private static string VersionFile(string game) { return Path.Combine(game, "BepInEx\\plugins\\HexOffline\\version.txt"); }

        private static bool IsModUpToDate(string game)
        {
            try
            {
                return File.Exists(Path.Combine(game, "winhttp.dll"))
                    && File.Exists(Path.Combine(game, "BepInEx\\plugins\\HexOffline\\HexOffline.dll"))
                    && File.Exists(VersionFile(game))
                    && File.ReadAllText(VersionFile(game)).Trim() == Version;
            }
            catch { return false; }
        }

        private static bool InstallWithElevationIfNeeded(string game)
        {
            if (CanWrite(game)) return Install(game);
            // Dossier protege en ecriture : on n'eleve les droits qu'avec l'accord explicite du joueur.
            if (MessageBox.Show(L(
                    "Windows does not allow writing to the game folder:\n" + game + "\n\n" +
                    "HEX Offline only needs to copy its files there (BepInEx and the HexOffline plugin).\n" +
                    "Retry with administrator rights?",
                    "Windows n'autorise pas l'écriture dans le dossier du jeu :\n" + game + "\n\n" +
                    "HEX Offline doit seulement y copier ses fichiers (BepInEx et le module HexOffline).\n" +
                    "Réessayer avec les droits administrateur ?"),
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return false;
            try
            {
                Process p = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--install \"" + game + "\"") { Verb = "runas", UseShellExecute = true });
                p.WaitForExit();
                return p.ExitCode == 0 && IsModUpToDate(game);
            }
            catch
            {
                return false;
            }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                string test = Path.Combine(dir, ".hexoffline_write_test");
                File.WriteAllText(test, "ok");
                File.Delete(test);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Dossier "mod" livre a cote du lanceur : fichiers copies tels quels dans le dossier du jeu.</summary>
        private static string PayloadDir { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mod"); } }

        private static bool Install(string game)
        {
            try
            {
                string src = PayloadDir;
                if (!Directory.Exists(src))
                {
                    MessageBox.Show(L("The \"mod\" folder is missing next to HexOffline.exe. Extract the whole archive, then try again.",
                        "Le dossier \"mod\" est absent à côté de HexOffline.exe. Extrayez toute l'archive, puis réessayez."), Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
                foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    string rel = file.Substring(src.Length).TrimStart('\\', '/');
                    string dest = Path.Combine(game, rel);
                    // Reglages du joueur : jamais ecrases s'ils existent deja.
                    if (Path.GetFileName(rel) == "HexOffline.autostart" && File.Exists(dest)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(file, dest, true);
                }
                File.WriteAllText(VersionFile(game), Version);
                return true;
            }
            catch (IOException e)
            {
                MessageBox.Show(L("Could not install the mod (is the game already running?):\n\n", "Impossible d'installer le mod (le jeu est-il déjà lancé ?) :\n\n") + e.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            catch (Exception e)
            {
                MessageBox.Show(L("Could not install the mod:\n\n", "Impossible d'installer le mod :\n\n") + e.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static void Uninstall(string game)
        {
            if (MessageBox.Show(L("Remove HEX Offline from:\n", "Retirer HEX Offline de :\n") + game + L("\n\nYour decks and champions are kept.", "\n\nVos decks et champions sont conservés."), Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            try
            {
                string bep = Path.Combine(game, "BepInEx");
                string plugin = Path.Combine(bep, "plugins\\HexOffline");
                if (Directory.Exists(plugin)) Directory.Delete(plugin, true);
                if (File.Exists(Path.Combine(game, "HexOffline.autostart"))) File.Delete(Path.Combine(game, "HexOffline.autostart"));
                // BepInEx n'est retire que s'il ne sert a aucun autre mod.
                string plugins = Path.Combine(bep, "plugins");
                bool otherMods = Directory.Exists(plugins) && Directory.GetFileSystemEntries(plugins).Length > 0;
                if (!otherMods)
                {
                    foreach (string f in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" })
                        if (File.Exists(Path.Combine(game, f))) File.Delete(Path.Combine(game, f));
                    if (Directory.Exists(bep)) Directory.Delete(bep, true);
                }
                MessageBox.Show(L("HEX Offline has been removed.", "HEX Offline a été retiré."), Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e)
            {
                MessageBox.Show(L("Could not remove the mod:\n\n", "Impossible de retirer le mod :\n\n") + e.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
