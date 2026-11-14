using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>
/// Тренажёр частиц: шпаргалка (смысл + пример со звуком) и квизы «вставь частицу».
/// Неверные варианты подбираются из частиц, которые путают с правильной (は/が, に/で, へ/に…),
/// после ответа — правило, по которому выбирается частица.
/// </summary>
public sealed class ParticlesPage : UserControl, IPage
{
    public string Title => "Частицы";

    public sealed record Rule(string Key, string P, string Name, string Explain, string ExJp, string ExRu);
    public sealed record Item(string Q, string A, string Ru, string Rule)
    {
        public string Filled => Q.Replace("＿", A);
    }

    static readonly Lazy<(Rule[] rules, Item[] items)> _data = new(() =>
    {
        using var s = typeof(Db).Assembly.GetManifestResourceStream("particles.json")!;
        using var doc = JsonDocument.Parse(s);
        string S(JsonElement e, int i) => e[i].GetString() ?? "";
        var rules = doc.RootElement.GetProperty("rules").EnumerateArray().Select(r => new Rule(S(r, 0), S(r, 1), S(r, 2), S(r, 3), S(r, 4), S(r, 5))).ToArray();
        var items = doc.RootElement.GetProperty("items").EnumerateArray().Select(r => new Item(S(r, 0), S(r, 1), S(r, 2), S(r, 3))).ToArray();
        return (rules, items);
    });
    public static Rule[] Rules => _data.Value.rules;
    public static Item[] Items => _data.Value.items;

    // с чем путают каждую частицу — из этого и берутся неверные варианты
    static readonly Dictionary<string, string[]> Confused = new()
    {
        ["は"] = new[] { "が", "も", "を" }, ["が"] = new[] { "は", "を", "も" }, ["を"] = new[] { "が", "に", "で" },
        ["に"] = new[] { "で", "へ", "を" }, ["で"] = new[] { "に", "を", "へ" }, ["へ"] = new[] { "に", "で", "を" },
        ["と"] = new[] { "や", "に", "の" }, ["も"] = new[] { "は", "が", "を" }, ["の"] = new[] { "が", "は", "に" },
        ["から"] = new[] { "まで", "で", "に" }, ["まで"] = new[] { "から", "に", "へ" }, ["や"] = new[] { "と", "の", "も" },
    };

    // взаимозаменяемые в этих предложениях частицы никогда не идут неверным вариантом (東京へ/に行く, パンと/や牛乳)
    static readonly Dictionary<string, string[]> Never = new()
    {
        ["へ"] = new[] { "に" }, ["に"] = new[] { "へ" }, ["と"] = new[] { "や" }, ["や"] = new[] { "と" },
    };

    static readonly (string title, string sub, string[] keys)[] Modes =
    {
        ("Все частицы", "15 предложений вперемешку", Array.Empty<string>()),
        ("は или が", "Тема или «именно это»", new[] { "wa", "ga" }),
        ("に, で или へ", "Где есть, где делаем, куда идём", new[] { "ni", "de", "e" }),
        ("を и が", "Объект действия и подлежащее", new[] { "o", "ga" }),
        ("と, も, の, や", "И, тоже, чей, и прочее", new[] { "to", "mo", "no", "ya" }),
        ("から и まで", "Откуда и докуда", new[] { "kara", "made" }),
    };

    public void OnShown()
    {
        var root = new StackPanel { MaxWidth = 1080 };
        root.Children.Add(U.Header("助", "Частицы", "Маленькие слова, на которых держится японское предложение. Шпаргалка со звуком и квизы: «вставь частицу» с разбором каждого ответа."));

        var grid = new UniformGrid { Columns = U.Columns(3), Margin = new Thickness(-6, 0, -6, 0) };
        foreach (var m in Modes)
        {
            var mode = m;
            var sp = U.Stack(U.T(mode.title, "H3", 16), U.T(mode.sub, "Muted", 13).M(0, 4, 0, 0));
            var card = new Border { Classes = { "Card" }, Child = sp, Margin = new Thickness(6), Cursor = Cursors.Hand };
            HomePage.Hover(card);
            card.PointerReleased += (_, _) => { Shell.Main.PendingExpand = card; Shell.Open(Quiz(mode.title, mode.keys)); };
            grid.Children.Add(card);
        }
        root.Children.Add(grid);

        root.Children.Add(U.T("Шпаргалка", "H2").M(0, 24, 0, 10));
        foreach (var r in Rules) root.Children.Add(RuleRow(r));
        Anim.Stagger(grid, 35, 40);
        U.DeferTail(root, 4);
        Content = U.Page(root);
    }

    static Control RuleRow(Rule r)
    {
        var g = U.Cols("104", "*", "auto");
        g.Children.Add(U.Stack(U.Jp(r.P, 30, U.B("Accent"), true).Center(), U.T(r.Name, "Muted", 12).Also(t => { t.TextAlignment = TextAlignment.Center; t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 100; }).Center()).CenterV());
        g.Children.Add(U.Stack(
            U.T(r.Explain, "Body", 14).Also(t => t.TextWrapping = TextWrapping.Wrap),
            U.Jp(r.ExJp, 17).M(0, 6, 0, 0),
            U.T(r.ExRu, "Muted", 12.5)).M(14, 0, 10, 0).Col(1));
        g.Children.Add(U.SpeakBtn(() => r.ExJp).CenterV().Col(2));
        return U.Card(g, new Thickness(14, 12, 10, 12)).M(0, 0, 0, 8);
    }

    public static QuizSession Quiz(string title, string[] keys, int count = 15) => new("Частицы: " + title, Questions(keys, count));

    public static List<QuizQ> Questions(string[] keys, int count = 15)
    {
        var rules = Rules.ToDictionary(r => r.Key);
        var pool = Items.Where(i => keys.Length == 0 || keys.Contains(i.Rule)).Shuffled().Take(count);
        var all = Rules.Select(r => r.P).ToArray();
        return pool.Select(it =>
        {
            var r = rules[it.Rule];
            var wrong = (Confused.TryGetValue(it.A, out var c) ? c : all).Where(x => x != it.A);
            // в узком режиме (は/が) варианты — только частицы режима: так тренируется именно это различие
            if (keys.Length > 0) wrong = keys.Select(k => rules[k].P).Where(x => x != it.A).Concat(wrong).Distinct();
            var never = Never.GetValueOrDefault(it.A) ?? Array.Empty<string>();
            wrong = wrong.Where(x => !never.Contains(x)).Concat(all.Where(x => x != it.A && !never.Contains(x)).Shuffled()).Distinct();
            return new QuizQ
            {
                Prompt = it.Q, PromptSize = 34, Sub = it.Ru, Answer = it.A, OptionsJp = true,
                Options = wrong.Take(3).Append(it.A).Shuffled().ToArray(),
                Speak = it.Filled,
                Explain = $"{it.Filled}\n{r.P} — {r.Name}: {r.Explain}",
            };
        }).ToList();
    }
}
