using System;
using System.IO;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Entry point injected into Main.FinializeInit() by the patcher (HexRevival.Patcher).
    /// Serves as a smoke test: confirms that the plugin can be loaded by the game's Mono.
    /// </summary>
    public static class Bootstrap
    {
        public static void Init()
        {
            try
            {
                string root = LocalServer.Root;
                string logPath = Path.Combine(root, "HexRevival.log");
                File.AppendAllText(logPath, DateTime.Now.ToString("s") + "  Bootstrap.Init() called" + Environment.NewLine);
                Debug.Log("[HexRevival] Bootstrap.Init OK - plugin loaded");

                // Offline mode: local session client + standalone platform.
                GameConfig.Standalone = true;
                GameConfig.CurrentAuthPlatform = GameConfig.AuthPlatform.STANDALONE;
                // ConnDevMode: lets the local engine build a default deck
                // for the human player (AuthoritativeSessionBase.LoadPlayerDeck).
                GameConfig.ConnDevMode = true;
                Debug.Log("[HexRevival] Standalone=" + GameConfig.Standalone + " AuthPlatform=" + GameConfig.CurrentAuthPlatform + " ConnDevMode=" + GameConfig.ConnDevMode);

                // Black box / runtime debugging (always on; self-test if marker present).
                TestBehaviour.Enable();
            }
            catch (Exception e)
            {
                Debug.LogError("[HexRevival] Bootstrap.Init failed: " + e);
            }
        }
    }

    /// <summary>
    /// Developer mode: detailed logs (state every second, event trace, network
    /// probe, rejected transactions). Active only if the HexRevival.dev file exists next
    /// to Hex.exe; disabled in the published version.
    /// </summary>
    public static class Dev
    {
        public static readonly bool Enabled = System.IO.File.Exists(System.IO.Path.Combine(LocalServer.Root, "HexRevival.dev"));
    }
}
