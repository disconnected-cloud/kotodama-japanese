namespace Matsubakira.Views;

/// <summary>
/// Инструкции для языковой модели. Маленьким моделям (1–3 млрд параметров) нужны явные роли
/// и готовые примеры ответа — иначе они путают, кто клиент, а кто продавец, и пропускают ошибки.
/// </summary>
public static class ChatPrompts
{
    public sealed record Scene(string Key, string Title, string Emoji, string AiRole, string UserRole, string Topic, string Example);

    public static readonly Scene[] Scenes =
    {
        new("free", "Свободная тема", "speech_balloon", "日本人の友だち", "日本語を勉強している友だち", "趣味、一日のこと、好きなもの", "こんにちは！今日は何をしましたか？"),
        new("intro", "Знакомство", "name_badge", "初めて会った日本人", "初めて会った外国人", "名前、出身、仕事や勉強", "はじめまして、たなかです。お名前は何ですか？"),
        new("cafe", "Кафе", "hot_beverage", "カフェの店員", "カフェの客", "注文（飲み物、食べ物、サイズ、店内か持ち帰りか）", "いらっしゃいませ。ご注文は何になさいますか？"),
        new("shop", "Магазин", "tshirt", "服屋の店員", "買い物に来た客", "サイズ、色、値段、試着", "いらっしゃいませ。何をお探しですか？"),
        new("way", "Дорога", "world_map", "親切な通行人", "道に迷った旅行者", "駅、ホテル、コンビニへの行き方", "どうしましたか？どこへ行きたいですか？"),
        new("hotel", "Отель", "hotel", "ホテルのフロント係", "ホテルの客", "チェックイン、名前、部屋、朝食の時間", "いらっしゃいませ。チェックインですか？"),
        new("hobby", "Хобби", "artist_palette", "趣味の話が好きな日本人", "日本語を勉強している人", "趣味、アニメ、音楽、食べ物、週末", "週末はたいてい何をしますか？"),
    };

    static string Words(string level) => level switch
    {
        "N5" => "とてもやさしい日本語（JLPT N5）。です・ます形。短い文。",
        "N4" => "やさしい日本語（JLPT N4）。です・ます形。",
        _ => "自然でやさしい日本語（JLPT N3）。",
    };

    public static string System(Scene s, string level) =>
        $"あなたは「{s.AiRole}」です。ユーザーは「{s.UserRole}」です。\n" +
        $"あなたは必ず「{s.AiRole}」として話してください。ユーザーの役（{s.UserRole}）のセリフを言ってはいけません。\n" +
        $"話題：{s.Topic}\n" +
        $"ユーザーは日本語の初心者です。{Words(level)}\n" +
        "ルール：\n" +
        "・日本語だけで答える。英語、ロシア語、ローマ字、翻訳、ふりがなは書かない。\n" +
        "・一回の返事は1〜2文。\n" +
        "・最後にユーザーへの簡単な質問をして、会話を続ける。\n" +
        "・話題から離れない。言語の勉強の話はしない。\n" +
        $"返事の例：{s.Example}";

    /// <summary>Первая реплика: просим начать сцену.</summary>
    public static string Opener(Scene s) => $"（会話を始めてください。「{s.AiRole}」として、最初のあいさつと質問をひとつ。）";

    /// <summary>Проверка фразы ученика: несколько примеров «ошибка → исправление» и «верно → OK».</summary>
    public static (string role, string text)[] Correct(string sentence) => new[]
    {
        ("system", "あなたは日本語の先生です。学習者の文に文法や言葉の間違いがあれば、正しい文だけを書きます。間違いがなければ「OK」とだけ書きます。説明はしません。"),
        ("user", "きのう学校に行きます。"), ("assistant", "きのう学校に行きました。"),
        ("user", "コーヒーをください。"), ("assistant", "OK"),
        ("user", "わたしはりんごが好きです。"), ("assistant", "OK"),
        ("user", "あした友だちと映画を見ました。"), ("assistant", "あした友だちと映画を見ます。"),
        ("user", "これは私のかばんじゃありますか。"), ("assistant", "これは私のかばんですか。"),
        ("user", sentence),
    };

    /// <summary>Перевод на русский с примерами — без них маленькая модель путает, кто что говорит.</summary>
    public static (string role, string text)[] Translate(string text) => new[]
    {
        ("system", "Ты переводчик с японского на русский. Пиши только точный перевод, без пояснений и без японского текста."),
        ("user", "いらっしゃいませ。ご注文は何になさいますか？"), ("assistant", "Добро пожаловать! Что будете заказывать?"),
        ("user", "はい、コーヒーをお願いします。"), ("assistant", "Да, кофе, пожалуйста."),
        ("user", text),
    };

    /// <summary>Пример короткого ответа ученика на реплику собеседника.</summary>
    public static (string role, string text)[] Hint(Scene s, string level, string question) => new[]
    {
        ("system", $"あなたは日本語の先生です。「{s.UserRole}」として言える、とても短くてやさしい返事の例を日本語で一つだけ書きます。{Words(level)}説明はしません。"),
        ("user", "いらっしゃいませ。ご注文は何になさいますか？"), ("assistant", "コーヒーをひとつください。"),
        ("user", "お名前は何ですか？"), ("assistant", "アンナです。よろしくお願いします。"),
        ("user", question),
    };
}
