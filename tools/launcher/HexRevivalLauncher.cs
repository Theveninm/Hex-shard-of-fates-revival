// HEX Revival - launcher / installer (Windows, .NET Framework 4.x).
//
// Double-click:
//   1. looks for HEX: Shards of Fate (launcher folder, Windows registry, Steam libraries,
//      folder chosen last time);
//   2. missing: offers to install it through Steam (if it is in the player's library) or to
//      choose the game folder;
//   3. present: installs / updates the mod (BepInEx + HexRevival) in the game folder,
//      then starts the game.
// Options: --uninstall (removes the mod), --install "<folder>" (internal use, after elevation).
//
// Built a second time with -define:UNINSTALLER as HexRevival-Uninstall.exe (tools/launcher/build.sh): double-click
// removes every version of the mod (HEX Revival, and its former name HEX Offline) and puts the game folder back as
// the game ships it; the player's saves are kept unless they choose to delete them too.
//
// Transparency: the launcher does not use the network, reads the registry only to find Steam and
// the game (and remembers the chosen folder there, HKCU\Software\HexRevival), copies the files of the "mod" folder shipped next to it into the game folder, and
// asks for administrator rights only if that folder is protected, after the player agrees.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("HEX Revival")]
[assembly: AssemblyProduct("HEX Revival")]
[assembly: AssemblyVersion("1.2.27.0")]
[assembly: AssemblyFileVersion("1.2.27.0")]

namespace HexRevivalLauncher
{
    internal static class Program
    {
        private const string AppId = "410380";
        private const string ExeName = "Hex.exe";
        private const string DefaultInstallDir = "HEX SHARDS OF FATE";
        private const string Version = "1.2.27";
        private const string Title = "HEX Revival";

        // ------------------------------------------------------------------
        // User-facing text, in the game's languages (Windows display language; English by default).
        // Column order: en, fr, de, es, it, pt, pl, ru, tr, ja, zh.
        // ------------------------------------------------------------------

        private static readonly string[] Langs = { "en", "fr", "de", "es", "it", "pt", "pl", "ru", "tr", "ja", "zh" };

        private static readonly Dictionary<string, string[]> Text = new Dictionary<string, string[]>
        {
            { "unexpected", new[] {
                "Unexpected error:\n\n",
                "Erreur inattendue :\n\n",
                "Unerwarteter Fehler:\n\n",
                "Error inesperado:\n\n",
                "Errore imprevisto:\n\n",
                "Erro inesperado:\n\n",
                "Nieoczekiwany błąd:\n\n",
                "Непредвиденная ошибка:\n\n",
                "Beklenmeyen hata:\n\n",
                "予期しないエラー:\n\n",
                "意外错误：\n\n" } },
            { "missing_steam", new[] {
                "HEX: Shards of Fate was not found on this computer.\n\nYes: ask Steam to install it. This only works if the game is already in your Steam library (it was removed from the store at the end of 2020).\nNo: choose the game folder yourself (if you have the game files from another source).",
                "HEX: Shards of Fate n'a pas été trouvé sur cet ordinateur.\n\nOui : demander à Steam de l'installer. Cela ne fonctionne que si le jeu est déjà dans votre bibliothèque Steam (il a été retiré de la boutique fin 2020).\nNon : choisir vous-même le dossier du jeu (si vous avez les fichiers du jeu par un autre moyen).",
                "HEX: Shards of Fate wurde auf diesem Computer nicht gefunden.\n\nJa: Steam um die Installation bitten. Das funktioniert nur, wenn das Spiel bereits in deiner Steam-Bibliothek ist (es wurde Ende 2020 aus dem Shop entfernt).\nNein: den Spielordner selbst auswählen (wenn du die Spieldateien aus einer anderen Quelle hast).",
                "No se encontró HEX: Shards of Fate en este equipo.\n\nSí: pedir a Steam que lo instale. Solo funciona si el juego ya está en tu biblioteca de Steam (se retiró de la tienda a finales de 2020).\nNo: elegir tú mismo la carpeta del juego (si tienes los archivos del juego de otra fuente).",
                "HEX: Shards of Fate non è stato trovato su questo computer.\n\nSì: chiedi a Steam di installarlo. Funziona solo se il gioco è già nella tua libreria di Steam (è stato rimosso dallo store alla fine del 2020).\nNo: scegli tu la cartella del gioco (se hai i file del gioco da un'altra fonte).",
                "HEX: Shards of Fate não foi encontrado neste computador.\n\nSim: pedir ao Steam que o instale. Só funciona se o jogo já estiver na sua biblioteca Steam (foi retirado da loja no fim de 2020).\nNão: escolher você mesmo a pasta do jogo (se tiver os ficheiros do jogo de outra fonte).",
                "Nie znaleziono HEX: Shards of Fate na tym komputerze.\n\nTak: poproś Steam o instalację. Działa to tylko, jeśli gra jest już w twojej bibliotece Steam (została usunięta ze sklepu pod koniec 2020 r.).\nNie: wskaż samodzielnie folder gry (jeśli masz pliki gry z innego źródła).",
                "HEX: Shards of Fate не найдена на этом компьютере.\n\nДа: попросить Steam установить игру. Это работает, только если игра уже есть в вашей библиотеке Steam (в конце 2020 года её убрали из магазина).\nНет: указать папку игры самостоятельно (если у вас есть файлы игры из другого источника).",
                "HEX: Shards of Fate bu bilgisayarda bulunamadı.\n\nEvet: Steam'den yüklemesini iste. Bu yalnızca oyun zaten Steam kütüphanendeyse çalışır (2020 sonunda mağazadan kaldırıldı).\nHayır: oyun klasörünü kendin seç (oyun dosyalarını başka bir kaynaktan edindiysen).",
                "このコンピューターに HEX: Shards of Fate が見つかりません。\n\nはい: Steam にインストールを依頼します。ゲームがすでに Steam ライブラリにある場合にのみ有効です（2020年末にストアから削除されました）。\nいいえ: ゲームのフォルダーを自分で選択します（別の入手元からゲームファイルを持っている場合）。",
                "在此电脑上找不到 HEX: Shards of Fate。\n\n是：请 Steam 安装。仅当游戏已在你的 Steam 库中时有效（该游戏已于 2020 年底从商店下架）。\n否：自行选择游戏文件夹（如果你从其他来源获得了游戏文件）。" } },
            { "missing", new[] {
                "HEX: Shards of Fate was not found on this computer.\n\nChoose the game folder (the one containing Hex.exe)?",
                "HEX: Shards of Fate n'a pas été trouvé sur cet ordinateur.\n\nChoisir le dossier du jeu (celui qui contient Hex.exe) ?",
                "HEX: Shards of Fate wurde auf diesem Computer nicht gefunden.\n\nSpielordner auswählen (der Ordner mit Hex.exe)?",
                "No se encontró HEX: Shards of Fate en este equipo.\n\n¿Elegir la carpeta del juego (la que contiene Hex.exe)?",
                "HEX: Shards of Fate non è stato trovato su questo computer.\n\nScegliere la cartella del gioco (quella che contiene Hex.exe)?",
                "HEX: Shards of Fate não foi encontrado neste computador.\n\nEscolher a pasta do jogo (a que contém Hex.exe)?",
                "Nie znaleziono HEX: Shards of Fate na tym komputerze.\n\nWskazać folder gry (ten, który zawiera Hex.exe)?",
                "HEX: Shards of Fate не найдена на этом компьютере.\n\nУказать папку игры (ту, где находится Hex.exe)?",
                "HEX: Shards of Fate bu bilgisayarda bulunamadı.\n\nOyun klasörünü (Hex.exe dosyasını içeren) seçmek ister misin?",
                "このコンピューターに HEX: Shards of Fate が見つかりません。\n\nゲームのフォルダー（Hex.exe があるフォルダー）を選択しますか？",
                "在此电脑上找不到 HEX: Shards of Fate。\n\n要选择游戏文件夹（包含 Hex.exe 的文件夹）吗？" } },
            { "after_steam", new[] {
                "When the installation is finished, run HEX Revival again.\nIf Steam answers \"No license\", the game is not in your library: run HEX Revival again and choose No to pick the game folder.",
                "Une fois l'installation terminée, relancez HEX Revival.\nSi Steam répond « Pas de licence », le jeu n'est pas dans votre bibliothèque : relancez HEX Revival et choisissez Non pour indiquer le dossier du jeu.",
                "Starte HEX Revival nach Abschluss der Installation erneut.\nWenn Steam „Keine Lizenz“ meldet, ist das Spiel nicht in deiner Bibliothek: Starte HEX Revival erneut und wähle Nein, um den Spielordner anzugeben.",
                "Cuando termine la instalación, vuelve a ejecutar HEX Revival.\nSi Steam responde «Sin licencia», el juego no está en tu biblioteca: vuelve a ejecutar HEX Revival y elige No para indicar la carpeta del juego.",
                "Al termine dell'installazione, avvia di nuovo HEX Revival.\nSe Steam risponde \"Nessuna licenza\", il gioco non è nella tua libreria: avvia di nuovo HEX Revival e scegli No per indicare la cartella del gioco.",
                "Quando a instalação terminar, execute novamente o HEX Revival.\nSe o Steam responder \"Sem licença\", o jogo não está na sua biblioteca: execute novamente o HEX Revival e escolha Não para indicar a pasta do jogo.",
                "Po zakończeniu instalacji uruchom ponownie HEX Revival.\nJeśli Steam odpowie „Brak licencji”, gry nie ma w twojej bibliotece: uruchom ponownie HEX Revival i wybierz Nie, aby wskazać folder gry.",
                "Когда установка завершится, снова запустите HEX Revival.\nЕсли Steam отвечает «Нет лицензии», игры нет в вашей библиотеке: снова запустите HEX Revival и выберите «Нет», чтобы указать папку игры.",
                "Kurulum bittiğinde HEX Revival'ı yeniden çalıştır.\nSteam \"Lisans yok\" derse oyun kütüphanende değildir: HEX Revival'ı yeniden çalıştır ve oyun klasörünü seçmek için Hayır'ı seç.",
                "インストールが完了したら、HEX Revival をもう一度実行してください。\nSteam が「ライセンスがありません」と表示した場合、ゲームはライブラリにありません。HEX Revival をもう一度実行し、「いいえ」を選んでゲームのフォルダーを指定してください。",
                "安装完成后，请再次运行 HEX Revival。\n如果 Steam 提示“无许可证”，说明游戏不在你的库中：请再次运行 HEX Revival，并选择“否”来指定游戏文件夹。" } },
            { "pick_folder", new[] {
                "Select the HEX: Shards of Fate folder (the one containing Hex.exe).",
                "Choisissez le dossier de HEX: Shards of Fate (celui qui contient Hex.exe).",
                "Wähle den Ordner von HEX: Shards of Fate (der Ordner mit Hex.exe).",
                "Selecciona la carpeta de HEX: Shards of Fate (la que contiene Hex.exe).",
                "Seleziona la cartella di HEX: Shards of Fate (quella che contiene Hex.exe).",
                "Selecione a pasta do HEX: Shards of Fate (a que contém Hex.exe).",
                "Wybierz folder HEX: Shards of Fate (ten, który zawiera Hex.exe).",
                "Выберите папку HEX: Shards of Fate (ту, где находится Hex.exe).",
                "HEX: Shards of Fate klasörünü seç (Hex.exe dosyasını içeren).",
                "HEX: Shards of Fate のフォルダー（Hex.exe があるフォルダー）を選択してください。",
                "请选择 HEX: Shards of Fate 文件夹（包含 Hex.exe 的文件夹）。" } },
            { "no_exe", new[] {
                "Hex.exe is not in this folder.",
                "Hex.exe ne se trouve pas dans ce dossier.",
                "Hex.exe befindet sich nicht in diesem Ordner.",
                "Hex.exe no está en esta carpeta.",
                "Hex.exe non si trova in questa cartella.",
                "Hex.exe não está nesta pasta.",
                "W tym folderze nie ma pliku Hex.exe.",
                "В этой папке нет Hex.exe.",
                "Hex.exe bu klasörde değil.",
                "このフォルダーに Hex.exe がありません。",
                "此文件夹中没有 Hex.exe。" } },
            { "need_admin", new[] {
                "Windows does not allow writing to the game folder:\n{0}\n\nHEX Revival only needs to copy its files there (BepInEx and the HexRevival plugin).\nRetry with administrator rights?",
                "Windows n'autorise pas l'écriture dans le dossier du jeu :\n{0}\n\nHEX Revival doit seulement y copier ses fichiers (BepInEx et le module HexRevival).\nRéessayer avec les droits administrateur ?",
                "Windows erlaubt keinen Schreibzugriff auf den Spielordner:\n{0}\n\nHEX Revival muss dort nur seine Dateien ablegen (BepInEx und das HexRevival-Plugin).\nMit Administratorrechten erneut versuchen?",
                "Windows no permite escribir en la carpeta del juego:\n{0}\n\nHEX Revival solo necesita copiar allí sus archivos (BepInEx y el complemento HexRevival).\n¿Reintentar con permisos de administrador?",
                "Windows non consente di scrivere nella cartella del gioco:\n{0}\n\nHEX Revival deve solo copiarvi i suoi file (BepInEx e il plugin HexRevival).\nRiprovare con i diritti di amministratore?",
                "O Windows não permite escrever na pasta do jogo:\n{0}\n\nO HEX Revival só precisa de copiar para lá os seus ficheiros (BepInEx e o plugin HexRevival).\nTentar novamente com direitos de administrador?",
                "Windows nie pozwala na zapis w folderze gry:\n{0}\n\nHEX Revival musi tylko skopiować tam swoje pliki (BepInEx i wtyczkę HexRevival).\nSpróbować ponownie z uprawnieniami administratora?",
                "Windows не разрешает запись в папку игры:\n{0}\n\nHEX Revival нужно лишь скопировать туда свои файлы (BepInEx и модуль HexRevival).\nПовторить с правами администратора?",
                "Windows oyun klasörüne yazmaya izin vermiyor:\n{0}\n\nHEX Revival'ın yalnızca dosyalarını oraya kopyalaması gerekiyor (BepInEx ve HexRevival eklentisi).\nYönetici haklarıyla yeniden denensin mi?",
                "Windows がゲームのフォルダーへの書き込みを許可していません:\n{0}\n\nHEX Revival はそこに自分のファイル（BepInEx と HexRevival プラグイン）をコピーするだけです。\n管理者権限で再試行しますか？",
                "Windows 不允许写入游戏文件夹：\n{0}\n\nHEX Revival 只需要将其文件（BepInEx 和 HexRevival 插件）复制到该处。\n要以管理员权限重试吗？" } },
            { "need_admin_remove", new[] {
                "Windows does not allow changes to the game folder:\n{0}\n\nRetry with administrator rights?",
                "Windows n'autorise pas la modification du dossier du jeu :\n{0}\n\nRéessayer avec les droits administrateur ?",
                "Windows erlaubt keine Änderungen am Spielordner:\n{0}\n\nMit Administratorrechten erneut versuchen?",
                "Windows no permite modificar la carpeta del juego:\n{0}\n\n¿Reintentar con permisos de administrador?",
                "Windows non consente di modificare la cartella del gioco:\n{0}\n\nRiprovare con i diritti di amministratore?",
                "O Windows não permite alterar a pasta do jogo:\n{0}\n\nTentar novamente com direitos de administrador?",
                "Windows nie pozwala na zmiany w folderze gry:\n{0}\n\nSpróbować ponownie z uprawnieniami administratora?",
                "Windows не разрешает изменять папку игры:\n{0}\n\nПовторить с правами администратора?",
                "Windows oyun klasöründe değişiklik yapılmasına izin vermiyor:\n{0}\n\nYönetici haklarıyla yeniden denensin mi?",
                "Windows がゲームのフォルダーの変更を許可していません:\n{0}\n\n管理者権限で再試行しますか？",
                "Windows 不允许修改游戏文件夹：\n{0}\n\n要以管理员权限重试吗？" } },
            { "no_mod_folder", new[] {
                "The \"mod\" folder is missing next to HexRevival.exe. Extract the whole archive, then try again.",
                "Le dossier \"mod\" est absent à côté de HexRevival.exe. Extrayez toute l'archive, puis réessayez.",
                "Der Ordner \"mod\" fehlt neben HexRevival.exe. Entpacke das ganze Archiv und versuche es erneut.",
                "Falta la carpeta \"mod\" junto a HexRevival.exe. Extrae el archivo completo y vuelve a intentarlo.",
                "Manca la cartella \"mod\" accanto a HexRevival.exe. Estrai l'intero archivio, poi riprova.",
                "Falta a pasta \"mod\" ao lado do HexRevival.exe. Extraia o arquivo completo e tente novamente.",
                "Brak folderu \"mod\" obok HexRevival.exe. Rozpakuj całe archiwum i spróbuj ponownie.",
                "Рядом с HexRevival.exe нет папки \"mod\". Распакуйте архив целиком и повторите попытку.",
                "HexRevival.exe dosyasının yanında \"mod\" klasörü yok. Arşivin tamamını çıkar ve yeniden dene.",
                "HexRevival.exe の隣に \"mod\" フォルダーがありません。アーカイブ全体を展開してから、もう一度お試しください。",
                "HexRevival.exe 旁边缺少 \"mod\" 文件夹。请解压整个压缩包后重试。" } },
            { "install_busy", new[] {
                "Could not install the mod (is the game already running?):\n\n",
                "Impossible d'installer le mod (le jeu est-il déjà lancé ?) :\n\n",
                "Der Mod konnte nicht installiert werden (läuft das Spiel bereits?):\n\n",
                "No se pudo instalar el mod (¿el juego ya está en ejecución?):\n\n",
                "Impossibile installare la mod (il gioco è già in esecuzione?):\n\n",
                "Não foi possível instalar o mod (o jogo já está em execução?):\n\n",
                "Nie udało się zainstalować moda (czy gra jest już uruchomiona?):\n\n",
                "Не удалось установить мод (игра уже запущена?):\n\n",
                "Mod yüklenemedi (oyun zaten çalışıyor mu?):\n\n",
                "MOD をインストールできませんでした（ゲームがすでに起動していませんか？）:\n\n",
                "无法安装模组（游戏是否已在运行？）：\n\n" } },
            { "install_failed", new[] {
                "Could not install the mod:\n\n",
                "Impossible d'installer le mod :\n\n",
                "Der Mod konnte nicht installiert werden:\n\n",
                "No se pudo instalar el mod:\n\n",
                "Impossibile installare la mod:\n\n",
                "Não foi possível instalar o mod:\n\n",
                "Nie udało się zainstalować moda:\n\n",
                "Не удалось установить мод:\n\n",
                "Mod yüklenemedi:\n\n",
                "MOD をインストールできませんでした:\n\n",
                "无法安装模组：\n\n" } },
            { "uninstall_confirm", new[] {
                "Remove HEX Revival from:\n{0}\n\nYour decks and champions are kept.",
                "Retirer HEX Revival de :\n{0}\n\nVos decks et champions sont conservés.",
                "HEX Revival entfernen aus:\n{0}\n\nDeine Decks und Champions bleiben erhalten.",
                "¿Quitar HEX Revival de:\n{0}?\n\nTus mazos y campeones se conservan.",
                "Rimuovere HEX Revival da:\n{0}\n\nI tuoi mazzi e campioni vengono conservati.",
                "Remover o HEX Revival de:\n{0}\n\nOs seus baralhos e campeões são mantidos.",
                "Usunąć HEX Revival z:\n{0}\n\nTwoje talie i czempioni zostaną zachowani.",
                "Удалить HEX Revival из:\n{0}\n\nВаши колоды и чемпионы сохранятся.",
                "HEX Revival şuradan kaldırılsın mı:\n{0}\n\nDestelerin ve şampiyonların korunur.",
                "次の場所から HEX Revival を削除しますか:\n{0}\n\nデッキとチャンピオンは保持されます。",
                "从以下位置移除 HEX Revival：\n{0}\n\n你的卡组和英雄会被保留。" } },
            { "close_game", new[] {
                "HEX is running. Close the game, then click OK.",
                "HEX est lancé. Fermez le jeu, puis cliquez sur OK.",
                "HEX läuft. Schließe das Spiel und klicke dann auf OK.",
                "HEX está abierto. Cierra el juego y pulsa Aceptar.",
                "HEX è in esecuzione. Chiudi il gioco, poi fai clic su OK.",
                "O HEX está aberto. Feche o jogo e clique em OK.",
                "HEX jest uruchomiony. Zamknij grę, a potem kliknij OK.",
                "HEX запущен. Закройте игру и нажмите OK.",
                "HEX açık. Oyunu kapat, sonra Tamam'a tıkla.",
                "HEX が起動しています。ゲームを閉じてから OK をクリックしてください。",
                "HEX 正在运行。请关闭游戏，然后点击确定。" } },
            { "uninstall_all", new[] {
                "Remove HEX Revival (any version) from:\n{0}\n\nThe game folder goes back to the original game.",
                "Retirer HEX Revival (toutes versions) de :\n{0}\n\nLe dossier du jeu redevient celui du jeu d'origine.",
                "HEX Revival (jede Version) entfernen aus:\n{0}\n\nDer Spielordner wird wieder wie im Originalspiel.",
                "¿Quitar HEX Revival (cualquier versión) de:\n{0}?\n\nLa carpeta del juego vuelve a ser la del juego original.",
                "Rimuovere HEX Revival (qualsiasi versione) da:\n{0}\n\nLa cartella del gioco torna come quella del gioco originale.",
                "Remover o HEX Revival (qualquer versão) de:\n{0}\n\nA pasta do jogo volta a ser a do jogo original.",
                "Usunąć HEX Revival (każdą wersję) z:\n{0}\n\nFolder gry wróci do stanu oryginalnej gry.",
                "Удалить HEX Revival (любую версию) из:\n{0}\n\nПапка игры вернётся к исходной игре.",
                "HEX Revival (tüm sürümler) şuradan kaldırılsın mı:\n{0}\n\nOyun klasörü orijinal oyuna geri döner.",
                "次の場所から HEX Revival（全バージョン）を削除しますか:\n{0}\n\nゲームフォルダーは元のゲームの状態に戻ります。",
                "从以下位置移除 HEX Revival（所有版本）：\n{0}\n\n游戏文件夹将恢复为原版游戏。" } },
            { "delete_saves", new[] {
                "Also delete your HEX Revival saves (accounts, decks, champions, collection)?\n\nNo: they are kept in the game folder, and come back if you reinstall the mod.",
                "Supprimer aussi vos sauvegardes HEX Revival (comptes, decks, champions, collection) ?\n\nNon : elles restent dans le dossier du jeu et reviennent si vous réinstallez le mod.",
                "Auch deine HEX-Revival-Spielstände löschen (Konten, Decks, Champions, Sammlung)?\n\nNein: Sie bleiben im Spielordner und sind nach einer Neuinstallation wieder da.",
                "¿Borrar también tus partidas de HEX Revival (cuentas, mazos, campeones, colección)?\n\nNo: se conservan en la carpeta del juego y vuelven si reinstalas el mod.",
                "Eliminare anche i salvataggi di HEX Revival (account, mazzi, campioni, collezione)?\n\nNo: restano nella cartella del gioco e tornano se reinstalli la mod.",
                "Apagar também os seus saves do HEX Revival (contas, baralhos, campeões, coleção)?\n\nNão: eles ficam na pasta do jogo e voltam se reinstalar o mod.",
                "Usunąć też zapisy HEX Revival (konta, talie, czempioni, kolekcja)?\n\nNie: zostaną w folderze gry i wrócą po ponownej instalacji moda.",
                "Удалить также сохранения HEX Revival (аккаунты, колоды, чемпионы, коллекция)?\n\nНет: они останутся в папке игры и вернутся при повторной установке мода.",
                "HEX Revival kayıtların da silinsin mi (hesaplar, desteler, şampiyonlar, koleksiyon)?\n\nHayır: oyun klasöründe kalırlar ve modu yeniden kurarsan geri gelirler.",
                "HEX Revival のセーブデータ（アカウント、デッキ、チャンピオン、コレクション）も削除しますか？\n\nいいえ：ゲームフォルダーに残り、MOD を再インストールすると戻ります。",
                "同时删除你的 HEX Revival 存档（账户、卡组、英雄、收藏）吗？\n\n否：存档保留在游戏文件夹中，重新安装模组后会恢复。" } },
            { "removed", new[] {
                "HEX Revival has been removed.",
                "HEX Revival a été retiré.",
                "HEX Revival wurde entfernt.",
                "HEX Revival se ha quitado.",
                "HEX Revival è stato rimosso.",
                "O HEX Revival foi removido.",
                "HEX Revival został usunięty.",
                "HEX Revival удалён.",
                "HEX Revival kaldırıldı.",
                "HEX Revival を削除しました。",
                "HEX Revival 已移除。" } },
            { "remove_failed", new[] {
                "Could not remove the mod:\n\n",
                "Impossible de retirer le mod :\n\n",
                "Der Mod konnte nicht entfernt werden:\n\n",
                "No se pudo quitar el mod:\n\n",
                "Impossibile rimuovere la mod:\n\n",
                "Não foi possível remover o mod:\n\n",
                "Nie udało się usunąć moda:\n\n",
                "Не удалось удалить мод:\n\n",
                "Mod kaldırılamadı:\n\n",
                "MOD を削除できませんでした:\n\n",
                "无法移除模组：\n\n" } },
        };

        private static string T(string key)
        {
            string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            int i = Array.IndexOf(Langs, lang);
            string[] v;
            if (!Text.TryGetValue(key, out v)) return key;
            return i > 0 && i < v.Length ? v[i] : v[0];
        }

        private static string T(string key, object arg) { return string.Format(T(key), arg); }

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            try
            {
                if (args.Length >= 2 && args[0] == "--install")
                    return Install(args[1]) ? 0 : 1;

#if UNINSTALLER
                if (args.Length >= 2 && args[0] == "--remove") return RemoveAll(args[1], args.Length >= 3 && args[2] == "--saves") ? 0 : 1;
                return UninstallerMain();
#else
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
#endif
            }
            catch (Exception e)
            {
                MessageBox.Show(T("unexpected") + e.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        // ------------------------------------------------------------------
        // Finding the game and Steam
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
            // Launcher placed directly in the game folder.
            string here = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(here, ExeName))) return here.TrimEnd('\\');

            // "Programs and Features" entry created by Steam when the game was installed.
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

            // Folder chosen by hand on a previous launch.
            try
            {
                string saved = Registry.GetValue("HKEY_CURRENT_USER\\Software\\HexRevival", "GameFolder", null) as string
                    ?? Registry.GetValue("HKEY_CURRENT_USER\\Software\\HexOffline", "GameFolder", null) as string; // former name of the mod
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
            // HEX was removed from Steam at the end of 2020: Steam only installs it for accounts that already
            // have it in their library (otherwise "No license").
            bool steam = SteamPath() != null;
            string text = steam
                ? T("missing_steam")
                : T("missing");
            DialogResult r = MessageBox.Show(text, Title, steam ? MessageBoxButtons.YesNoCancel : MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (steam && r == DialogResult.Yes)
            {
                Process.Start("steam://install/" + AppId);
                MessageBox.Show(T("after_steam"),
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            if ((steam && r == DialogResult.No) || (!steam && r == DialogResult.OK))
            {
                string dir = AskFolder();
                if (dir != null)
                {
                    try { Registry.SetValue("HKEY_CURRENT_USER\\Software\\HexRevival", "GameFolder", dir); } catch { }
                }
                return dir;
            }
            return null;
        }

        private static string AskFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = T("pick_folder");
                while (d.ShowDialog() == DialogResult.OK)
                {
                    if (File.Exists(Path.Combine(d.SelectedPath, ExeName))) return d.SelectedPath;
                    MessageBox.Show(T("no_exe"), Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Installing the mod
        // ------------------------------------------------------------------

        private static string VersionFile(string game) { return Path.Combine(game, "BepInEx\\plugins\\HexRevival\\version.txt"); }

        private static bool IsModUpToDate(string game)
        {
            try
            {
                return File.Exists(Path.Combine(game, "winhttp.dll"))
                    && File.Exists(Path.Combine(game, "BepInEx\\plugins\\HexRevival\\HexRevival.dll"))
                    && File.Exists(VersionFile(game))
                    && File.ReadAllText(VersionFile(game)).Trim() == Version;
            }
            catch { return false; }
        }

        private static bool InstallWithElevationIfNeeded(string game)
        {
            if (CanWrite(game)) return Install(game);
            // Write-protected folder: rights are elevated only with the player's explicit consent.
            if (MessageBox.Show(T("need_admin", game),
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
                string test = Path.Combine(dir, ".hexrevival_write_test");
                File.WriteAllText(test, "ok");
                File.Delete(test);
                return true;
            }
            catch { return false; }
        }

        /// <summary>"mod" folder shipped next to the launcher: files copied as they are into the game folder.</summary>
        private static string PayloadDir { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mod"); } }

        private static bool Install(string game)
        {
            try
            {
                string src = PayloadDir;
                if (!Directory.Exists(src))
                {
                    MessageBox.Show(T("no_mod_folder"), Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
                // Former name of the mod (HEX Offline 1.0): its plugin must not be loaded on top of this one.
                // Decks and champions (HexOffline.*.txt) are taken over by the plugin at startup.
                string old = Path.Combine(game, "BepInEx\\plugins\\HexOffline");
                if (Directory.Exists(old)) Directory.Delete(old, true);
                foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    string rel = file.Substring(src.Length).TrimStart('\\', '/');
                    string dest = Path.Combine(game, rel);
                    // Player settings: never overwritten if they already exist.
                    if (Path.GetFileName(rel) == "HexRevival.autostart" && File.Exists(dest)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(file, dest, true);
                }
                File.WriteAllText(VersionFile(game), Version);
                return true;
            }
            catch (IOException e)
            {
                MessageBox.Show(T("install_busy") + e.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            catch (Exception e)
            {
                MessageBox.Show(T("install_failed") + e.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static void Uninstall(string game)
        {
            if (MessageBox.Show(T("uninstall_confirm", game), Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            if (RemoveAll(game, false)) MessageBox.Show(T("removed"), Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------------
        // The standalone uninstaller (HexRevival-Uninstall.exe, built with -define:UNINSTALLER)
        // ------------------------------------------------------------------

        private static int UninstallerMain()
        {
            string game = FindGame() ?? AskFolder();
            if (game == null) return 0;
            if (MessageBox.Show(T("uninstall_all", game), Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return 0;
            while (GameRunning(game))
                if (MessageBox.Show(T("close_game"), Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return 0;
            bool saves = MessageBox.Show(T("delete_saves"), Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            bool ok;
            if (CanWrite(game)) ok = RemoveAll(game, saves);
            else
            {
                // Write-protected folder (Program Files): the same removal, with administrator rights the player agrees to.
                if (MessageBox.Show(T("need_admin_remove", game), Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
                try
                {
                    Process p = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--remove \"" + game + "\"" + (saves ? " --saves" : "")) { Verb = "runas", UseShellExecute = true });
                    p.WaitForExit();
                    ok = p.ExitCode == 0;
                }
                catch { ok = false; }
            }
            if (ok)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree("Software\\HexRevival", false); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree("Software\\HexOffline", false); } catch { }
                MessageBox.Show(T("removed"), Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return ok ? 0 : 1;
        }

        private static bool GameRunning(string game)
        {
            foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            {
                try { if (string.Equals(Path.GetDirectoryName(p.MainModule.FileName), game.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true; }
                catch { return true; }   // not readable (other user, rights): assumed to be this game
            }
            return false;
        }

        /// <summary>
        /// Everything the mod puts in the game folder, in any version: its plugin (HexRevival, HexOffline), BepInEx and
        /// its loader (only when no other mod uses BepInEx), and the files and folders the mod writes next to Hex.exe
        /// (HexRevival*, HexOffline*). The saves (HexRevivalSaves, HexRevivalOnline and the HEX Offline save files) are
        /// kept unless deleteSaves. The game's own files are never touched.
        /// </summary>
        private static bool RemoveAll(string game, bool deleteSaves)
        {
            try
            {
                string bep = Path.Combine(game, "BepInEx");
                foreach (string name in new[] { "HexRevival", "HexOffline" })
                {
                    string plugin = Path.Combine(Path.Combine(bep, "plugins"), name);
                    if (Directory.Exists(plugin)) Directory.Delete(plugin, true);
                }
                string plugins = Path.Combine(bep, "plugins");
                bool otherMods = Directory.Exists(plugins) && Directory.GetFileSystemEntries(plugins).Length > 0;
                if (!otherMods)
                {
                    foreach (string f in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" })
                        if (File.Exists(Path.Combine(game, f))) File.Delete(Path.Combine(game, f));
                    if (Directory.Exists(bep)) Directory.Delete(bep, true);
                }
                foreach (string f in Directory.GetFiles(game))
                {
                    string n = Path.GetFileName(f);
                    if (!IsModEntry(n)) continue;
                    if (!deleteSaves && IsSave(n)) continue;
                    File.Delete(f);
                }
                foreach (string d in Directory.GetDirectories(game))
                {
                    string n = Path.GetFileName(d);
                    if (!IsModEntry(n)) continue;
                    if (!deleteSaves && IsSave(n)) continue;
                    Directory.Delete(d, true);
                }
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(T("remove_failed") + e.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static bool IsModEntry(string name)
        {
            return name.StartsWith("HexRevival", StringComparison.OrdinalIgnoreCase) || name.StartsWith("HexOffline", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The player's saves: HEX Revival's accounts and online profile, HEX Offline's decks and champions.</summary>
        private static bool IsSave(string name)
        {
            return name.StartsWith("HexRevivalSaves", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("HexRevivalOnline", StringComparison.OrdinalIgnoreCase)
                || (name.StartsWith("HexOffline.", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        }
    }
}
