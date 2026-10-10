using System.Collections.Generic;

namespace HexRevival
{
    /// <summary>
    /// Textes du mod dans les langues du jeu. La langue suit l'option "Language" du jeu
    /// (PlayerConfig.Language, ex. "fr-FR" -> "fr") ; anglais par defaut.
    /// Ordre des colonnes : en, fr, de, es, it, pt, pl, ru, tr, ja, zh.
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
        };

        /// <summary>Code de langue du jeu sur deux lettres ("en" par defaut).</summary>
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

        /// <summary>Langue sans police latine (titres et texte avec une police adaptee).</summary>
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
