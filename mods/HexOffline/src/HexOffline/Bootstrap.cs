using System;
using System.IO;
using UnityEngine;

namespace HexOffline
{
    /// <summary>
    /// Point d'entrée injecté dans Main.FinializeInit() par le patcher (HexOffline.Patcher).
    /// Sert de test de fumée : confirme que le plugin est chargeable par le Mono du jeu.
    /// </summary>
    public static class Bootstrap
    {
        public static void Init()
        {
            try
            {
                string root = Path.GetFullPath(Application.dataPath + "/../");
                string logPath = Path.Combine(root, "HexOffline.log");
                File.AppendAllText(logPath, DateTime.Now.ToString("s") + "  Bootstrap.Init() called" + Environment.NewLine);
                Debug.Log("[HexOffline] Bootstrap.Init OK - plugin loaded");

                // Mode hors-ligne : client de session local + plateforme standalone.
                GameConfig.Standalone = true;
                GameConfig.CurrentAuthPlatform = GameConfig.AuthPlatform.STANDALONE;
                // ConnDevMode : permet au moteur local de fabriquer un deck par defaut
                // pour le joueur humain (AuthoritativeSessionBase.LoadPlayerDeck).
                GameConfig.ConnDevMode = true;
                Debug.Log("[HexOffline] Standalone=" + GameConfig.Standalone + " AuthPlatform=" + GameConfig.CurrentAuthPlatform + " ConnDevMode=" + GameConfig.ConnDevMode);

                // Boite noire / debogage runtime (toujours actif ; auto-test si marqueur).
                TestBehaviour.Enable();
            }
            catch (Exception e)
            {
                Debug.LogError("[HexOffline] Bootstrap.Init failed: " + e);
            }
        }
    }

    /// <summary>
    /// Mode developpeur : journaux detailles (etat chaque seconde, trace des evenements, sonde
    /// reseau, transactions refusees). Actif seulement si le fichier HexOffline.dev existe a cote
    /// de Hex.exe ; desactive dans la version publiee.
    /// </summary>
    public static class Dev
    {
        public static readonly bool Enabled = System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.GetFullPath(UnityEngine.Application.dataPath + "/../"), "HexOffline.dev"));
    }
}
