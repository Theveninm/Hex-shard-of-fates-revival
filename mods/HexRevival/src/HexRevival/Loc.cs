using System.Collections.Generic;

namespace HexRevival
{
    /// <summary>
    /// Mod texts in the game's languages. The language follows the game's "Language" option
    /// (PlayerConfig.Language, e.g. "fr-FR" -> "fr"); English by default.
    /// Column order: en, fr, de, es, it, pt, pl, ru, tr, ja, zh.
    /// </summary>
    public static class Loc
    {
        private static readonly string[] Langs = { "en", "fr", "de", "es", "it", "pt", "pl", "ru", "tr", "ja", "zh" };

        private static readonly Dictionary<string, string[]> T = new Dictionary<string, string[]>
        {
            { "quick_match", new[] { "Quick Match", "Partie rapide", "Schnelles Spiel", "Partida rápida", "Partita rapida", "Partida rápida", "Szybka gra", "Быстрая игра", "Hızlı Oyun", "クイックマッチ", "快速对战" } },
            { "your_deck", new[] { "1.  Your deck", "1.  Ton deck", "1.  Dein Deck", "1.  Tu mazo", "1.  Il tuo mazzo", "1.  O seu baralho", "1.  Twoja talia", "1.  Ваша колода", "1.  Desten", "1.  あなたのデッキ", "1.  你的卡组" } },
            { "your_opponent", new[] { "2.  Your opponent", "2.  Ton adversaire", "2.  Dein Gegner", "2.  Tu rival", "2.  Il tuo avversario", "2.  O seu adversário", "2.  Twój przeciwnik", "2.  Ваш противник", "2.  Rakibin", "2.  対戦相手", "2.  你的对手" } },
            { "new_deck", new[] { "Create a new deck", "Créer un nouveau deck", "Neues Deck erstellen", "Crear un mazo nuevo", "Crea un nuovo mazzo", "Criar um novo baralho", "Utwórz nową talię", "Создать новую колоду", "Yeni deste oluştur", "新しいデッキを作成", "创建新卡组" } },
            { "tab_enc", new[] { "Encounters", "Rencontres", "Begegnungen", "Encuentros", "Incontri", "Encontros", "Spotkania", "Встречи", "Karşılaşmalar", "エンカウンター", "遭遇战" } },
            { "tab_game", new[] { "Game decks", "Decks du jeu", "Spieldecks", "Mazos del juego", "Mazzi del gioco", "Baralhos do jogo", "Talie z gry", "Колоды игры", "Oyun desteleri", "ゲームのデッキ", "游戏卡组" } },
            { "tab_mine", new[] { "My decks", "Mes decks", "Meine Decks", "Mis mazos", "I miei mazzi", "Os meus baralhos", "Moje talie", "Мои колоды", "Destelerim", "マイデッキ", "我的卡组" } },
            { "search", new[] { "Search a name, a champion…", "Rechercher un nom, un champion…", "Name oder Champion suchen…", "Buscar un nombre, un campeón…", "Cerca un nome, un campione…", "Procurar um nome, um campeão…", "Szukaj nazwy, czempiona…", "Поиск по имени или чемпиону…", "Bir ad, bir şampiyon ara…", "名前・チャンピオンで検索…", "搜索名称或英雄…" } },
            { "hint_enc", new[] { "The AI plays the encounter's deck, with its special rules.", "L'IA joue le deck de la rencontre, avec ses règles spéciales.", "Die KI spielt das Deck der Begegnung mit ihren Sonderregeln.", "La IA juega el mazo del encuentro, con sus reglas especiales.", "L'IA gioca il mazzo dell'incontro, con le sue regole speciali.", "A IA joga o baralho do encontro, com as suas regras especiais.", "SI gra talią spotkania, z jej specjalnymi zasadami.", "ИИ играет колодой встречи с её особыми правилами.", "Yapay zekâ, karşılaşmanın destesini özel kurallarıyla oynar.", "AIはエンカウンターのデッキを特別ルール付きで使います。", "AI 使用该遭遇战的卡组及其特殊规则。" } },
            { "hint_game", new[] { "Prebuilt game decks, played by the AI.", "Decks préconstruits du jeu, joués par l'IA.", "Vorgefertigte Spieldecks, von der KI gespielt.", "Mazos prediseñados del juego, jugados por la IA.", "Mazzi precostruiti del gioco, giocati dall'IA.", "Baralhos pré-construídos do jogo, jogados pela IA.", "Gotowe talie z gry, rozgrywane przez SI.", "Готовые колоды игры под управлением ИИ.", "Yapay zekânın oynadığı hazır oyun desteleri.", "AIが使うゲームの構築済みデッキ。", "由 AI 使用的游戏预组卡组。" } },
            { "hint_mine", new[] { "The AI plays one of your decks.", "L'IA joue un de tes decks.", "Die KI spielt eines deiner Decks.", "La IA juega uno de tus mazos.", "L'IA gioca uno dei tuoi mazzi.", "A IA joga um dos seus baralhos.", "SI gra jedną z twoich talii.", "ИИ играет одной из ваших колод.", "Yapay zekâ destelerinden birini oynar.", "AIがあなたのデッキの1つを使います。", "AI 使用你的一个卡组。" } },
            { "no_match", new[] { "Nothing matches.", "Rien ne correspond.", "Keine Treffer.", "No hay coincidencias.", "Nessun risultato.", "Nenhum resultado.", "Brak wyników.", "Ничего не найдено.", "Eşleşme yok.", "該当なし。", "没有匹配项。" } },
            { "versus", new[] { "   vs   ", "   contre   ", "   gegen   ", "   contra   ", "   contro   ", "   contra   ", "   przeciw   ", "   против   ", "   vs   ", "   対   ", "   对阵   " } },
            { "close", new[] { "Close", "Fermer", "Schließen", "Cerrar", "Chiudi", "Fechar", "Zamknij", "Закрыть", "Kapat", "閉じる", "关闭" } },
            { "fight", new[] { "Fight", "Combattre", "Kämpfen", "Combatir", "Combatti", "Combater", "Walcz", "В бой", "Savaş", "対戦する", "开战" } },
            { "msg_pick_deck", new[] { "Choose your deck first.", "Choisis d'abord ton deck.", "Wähle zuerst dein Deck.", "Elige primero tu mazo.", "Scegli prima il tuo mazzo.", "Escolha primeiro o seu baralho.", "Najpierw wybierz talię.", "Сначала выберите колоду.", "Önce desteni seç.", "まずデッキを選んでください。", "请先选择你的卡组。" } },
            { "msg_pick_opp", new[] { "Choose an opponent.", "Choisis un adversaire.", "Wähle einen Gegner.", "Elige un rival.", "Scegli un avversario.", "Escolha um adversário.", "Wybierz przeciwnika.", "Выберите противника.", "Bir rakip seç.", "対戦相手を選んでください。", "请选择对手。" } },
            { "msg_no_opp_deck", new[] { "Opponent deck not found.", "Deck adversaire introuvable.", "Gegnerdeck nicht gefunden.", "No se encontró el mazo rival.", "Mazzo avversario non trovato.", "Baralho do adversário não encontrado.", "Nie znaleziono talii przeciwnika.", "Колода противника не найдена.", "Rakip destesi bulunamadı.", "相手のデッキが見つかりません。", "找不到对手卡组。" } },
            { "cards", new[] { "{0} cards", "{0} cartes", "{0} Karten", "{0} cartas", "{0} carte", "{0} cartas", "{0} kart", "{0} карт", "{0} kart", "{0}枚", "{0} 张卡" } },
            { "vs_champ", new[] { "Against {0}", "Contre {0}", "Gegen {0}", "Contra {0}", "Contro {0}", "Contra {0}", "Przeciw: {0}", "Против: {0}", "Rakip: {0}", "対戦相手：{0}", "对手：{0}" } },
            { "enc_opp", new[] { "Encounter opponent", "Adversaire de la rencontre", "Gegner der Begegnung", "Rival del encuentro", "Avversario dell'incontro", "Adversário do encontro", "Przeciwnik spotkania", "Противник встречи", "Karşılaşma rakibi", "エンカウンターの相手", "遭遇战对手" } },
            { "boss", new[] { "boss", "boss", "Boss", "jefe", "boss", "chefe", "boss", "босс", "boss", "ボス", "首领" } },
            { "special_rules", new[] { "special rules", "règles spéciales", "Sonderregeln", "reglas especiales", "regole speciali", "regras especiais", "zasady specjalne", "особые правила", "özel kurallar", "特別ルール", "特殊规则" } },
            { "del_title", new[] { "Delete deck?", "Supprimer le deck ?", "Deck löschen?", "¿Eliminar el mazo?", "Eliminare il mazzo?", "Eliminar o baralho?", "Usunąć talię?", "Удалить колоду?", "Deste silinsin mi?", "デッキを削除しますか？", "删除卡组？" } },
            { "del_text", new[] { "“{0}” will be permanently deleted.", "« {0} » sera supprimé définitivement.", "„{0}“ wird endgültig gelöscht.", "«{0}» se eliminará definitivamente.", "«{0}» verrà eliminato definitivamente.", "«{0}» será eliminado definitivamente.", "„{0}” zostanie trwale usunięta.", "«{0}» будет удалена навсегда.", "“{0}” kalıcı olarak silinecek.", "「{0}」は完全に削除されます。", "“{0}”将被永久删除。" } },
            { "cancel", new[] { "Cancel", "Annuler", "Abbrechen", "Cancelar", "Annulla", "Cancelar", "Anuluj", "Отмена", "İptal", "キャンセル", "取消" } },
            { "delete", new[] { "Delete", "Supprimer", "Löschen", "Eliminar", "Elimina", "Eliminar", "Usuń", "Удалить", "Sil", "削除", "删除" } },
            { "accounts", new[] { "Choose an account", "Choisir un compte", "Konto wählen", "Elige una cuenta", "Scegli un account", "Escolha uma conta", "Wybierz konto", "Выберите аккаунт", "Bir hesap seç", "アカウントを選択", "选择账户" } },
            { "new_account", new[] { "Create a new account", "Créer un nouveau compte", "Neues Konto erstellen", "Crear una cuenta nueva", "Crea un nuovo account", "Criar uma nova conta", "Utwórz nowe konto", "Создать новый аккаунт", "Yeni hesap oluştur", "新しいアカウントを作成", "创建新账户" } },
            { "new_account_title", new[] { "New account", "Nouveau compte", "Neues Konto", "Cuenta nueva", "Nuovo account", "Nova conta", "Nowe konto", "Новый аккаунт", "Yeni hesap", "新規アカウント", "新账户" } },
            { "account_name", new[] { "Account name", "Nom du compte", "Kontoname", "Nombre de la cuenta", "Nome dell'account", "Nome da conta", "Nazwa konta", "Имя аккаунта", "Hesap adı", "アカウント名", "账户名称" } },
            { "opt_unlock", new[] { "Unlock everything", "Tout débloquer", "Alles freischalten", "Desbloquear todo", "Sblocca tutto", "Desbloquear tudo", "Odblokuj wszystko", "Открыть всё", "Her şeyin kilidini aç", "すべてアンロック", "全部解锁" } },
            { "opt_unlock_desc", new[] { "Every card and every equipment from the start. Otherwise, cards are earned by playing.", "Toutes les cartes et tous les équipements dès le départ. Sinon, les cartes se gagnent en jouant.", "Alle Karten und Ausrüstungen von Anfang an. Andernfalls verdient man Karten durch Spielen.", "Todas las cartas y equipos desde el principio. Si no, las cartas se ganan jugando.", "Tutte le carte e gli equipaggiamenti fin dall'inizio. Altrimenti le carte si guadagnano giocando.", "Todas as cartas e equipamentos desde o início. Caso contrário, as cartas ganham-se a jogar.", "Wszystkie karty i ekwipunek od początku. W przeciwnym razie karty zdobywa się, grając.", "Все карты и снаряжение с самого начала. Иначе карты зарабатываются в игре.", "Tüm kartlar ve ekipmanlar baştan açık. Aksi hâlde kartlar oynayarak kazanılır.", "最初からすべてのカードと装備を所持。オフにするとカードはプレイで獲得します。", "从一开始就拥有所有卡牌和装备。否则需通过游戏获得卡牌。" } },
            { "create", new[] { "Create", "Créer", "Erstellen", "Crear", "Crea", "Criar", "Utwórz", "Создать", "Oluştur", "作成", "创建" } },
            { "no_account", new[] { "No account yet.", "Aucun compte pour l'instant.", "Noch kein Konto.", "Aún no hay cuentas.", "Nessun account per ora.", "Ainda não há contas.", "Brak kont.", "Аккаунтов пока нет.", "Henüz hesap yok.", "アカウントはまだありません。", "还没有账户。" } },
            { "account_name_empty", new[] { "Enter a name.", "Saisis un nom.", "Gib einen Namen ein.", "Escribe un nombre.", "Inserisci un nome.", "Introduza um nome.", "Wpisz nazwę.", "Введите имя.", "Bir ad gir.", "名前を入力してください。", "请输入名称。" } },
            { "account_name_invalid", new[] { "This name cannot be used (32 characters max, no special characters).", "Ce nom ne peut pas être utilisé (32 caractères max, sans caractères spéciaux).", "Dieser Name ist nicht zulässig (max. 32 Zeichen, keine Sonderzeichen).", "Este nombre no se puede usar (32 caracteres máx., sin caracteres especiales).", "Questo nome non può essere usato (max 32 caratteri, niente caratteri speciali).", "Este nome não pode ser usado (máx. 32 caracteres, sem caracteres especiais).", "Tej nazwy nie można użyć (maks. 32 znaki, bez znaków specjalnych).", "Это имя нельзя использовать (не более 32 символов, без спецсимволов).", "Bu ad kullanılamaz (en fazla 32 karakter, özel karakter yok).", "この名前は使用できません（32文字以内、特殊文字不可）。", "此名称不可用（最多 32 个字符，不含特殊字符）。" } },
            { "account_name_taken", new[] { "An account with this name already exists.", "Un compte porte déjà ce nom.", "Ein Konto mit diesem Namen existiert bereits.", "Ya existe una cuenta con este nombre.", "Esiste già un account con questo nome.", "Já existe uma conta com este nome.", "Konto o tej nazwie już istnieje.", "Аккаунт с таким именем уже существует.", "Bu adda bir hesap zaten var.", "この名前のアカウントは既に存在します。", "已存在同名账户。" } },
            { "change_account", new[] { "Change account", "Changer de compte", "Konto wechseln", "Cambiar de cuenta", "Cambia account", "Mudar de conta", "Zmień konto", "Сменить аккаунт", "Hesap değiştir", "アカウント切替", "切换账户" } },
            { "cheat_menu", new[] { "Cheats", "Triches", "Cheats", "Trucos", "Trucchi", "Truques", "Kody", "Читы", "Hileler", "チート", "作弊" } },
            { "cheat_win", new[] { "Win the battle", "Gagner le combat", "Kampf gewinnen", "Ganar el combate", "Vinci la battaglia", "Vencer a batalha", "Wygraj walkę", "Выиграть бой", "Savaşı kazan", "対戦に勝利", "赢得对战" } },
            { "cheat_resources", new[] { "+5 resources", "+5 ressources", "+5 Ressourcen", "+5 recursos", "+5 risorse", "+5 recursos", "+5 zasobów", "+5 ресурсов", "+5 kaynak", "リソース+5", "+5 资源" } },
            { "cheat_gold", new[] { "+1000 gold", "+1000 pièces d'or", "+1000 Gold", "+1000 de oro", "+1000 oro", "+1000 de ouro", "+1000 złota", "+1000 золота", "+1000 altın", "ゴールド+1000", "+1000 金币" } },
            { "cheat_reveal", new[] { "Reveal the whole map", "Révéler toute la carte", "Ganze Karte aufdecken", "Revelar todo el mapa", "Rivela tutta la mappa", "Revelar o mapa inteiro", "Odkryj całą mapę", "Открыть всю карту", "Tüm haritayı aç", "マップ全体を表示", "显示整张地图" } },
            { "cheat_done", new[] { "Done.", "C'est fait.", "Erledigt.", "Hecho.", "Fatto.", "Feito.", "Gotowe.", "Готово.", "Tamam.", "完了。", "已完成。" } },
            { "show_cheats", new[] { "Show the cheat menu", "Afficher le menu de triche", "Cheat-Menü anzeigen", "Mostrar el menú de trucos", "Mostra il menu dei trucchi", "Mostrar o menu de truques", "Pokaż menu kodów", "Показывать меню читов", "Hile menüsünü göster", "チートメニューを表示", "显示作弊菜单" } },
            { "last_played", new[] { "Last played: {0}", "Dernière partie : {0}", "Zuletzt gespielt: {0}", "Última partida: {0}", "Ultima partita: {0}", "Última partida: {0}", "Ostatnia gra: {0}", "Последняя игра: {0}", "Son oynama: {0}", "最終プレイ：{0}", "上次游戏：{0}" } },
            { "quick_match_tip", new[] { "Play against the AI with one of your decks.", "Jouer contre l'IA avec un de tes decks.", "Mit einem deiner Decks gegen die KI spielen.", "Juega contra la IA con uno de tus mazos.", "Gioca contro l'IA con uno dei tuoi mazzi.", "Jogue contra a IA com um dos seus baralhos.", "Zagraj przeciw SI jedną ze swoich talii.", "Сыграть против ИИ одной из своих колод.", "Destelerinden biriyle yapay zekâya karşı oyna.", "自分のデッキでAIと対戦します。", "用你的卡组与 AI 对战。" } },
            { "change_account_tip", new[] { "Log out and choose another account.", "Se déconnecter et choisir un autre compte.", "Abmelden und ein anderes Konto wählen.", "Cerrar sesión y elegir otra cuenta.", "Disconnettiti e scegli un altro account.", "Terminar sessão e escolher outra conta.", "Wyloguj się i wybierz inne konto.", "Выйти и выбрать другой аккаунт.", "Çıkış yap ve başka bir hesap seç.", "ログアウトして別のアカウントを選びます。", "登出并选择其他账户。" } },
        };

        /// <summary>Two-letter game language code ("en" by default).</summary>
        public static string Lang
        {
            get
            {
                string l = PlayerConfig.Language;
                if (string.IsNullOrEmpty(l)) return "en";
                l = (l.Length > 2 ? l.Substring(0, 2) : l).ToLowerInvariant();
                return System.Array.IndexOf(Langs, l) >= 0 ? l : "en";
            }
        }

        /// <summary>Language without a Latin font (titles and text use a suitable font).</summary>
        public static bool NonLatin { get { string l = Lang; return l == "ru" || l == "ja" || l == "zh"; } }

        public static string Get(string key)
        {
            string[] v;
            if (!T.TryGetValue(key, out v)) return key;
            int i = System.Array.IndexOf(Langs, Lang);
            return i >= 0 && i < v.Length && !string.IsNullOrEmpty(v[i]) ? v[i] : v[0];
        }

        public static string Get(string key, object arg) { return string.Format(Get(key), arg); }
    }
}
