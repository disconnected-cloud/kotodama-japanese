using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>
/// «Похожие кандзи»: слово с пропуском (＿来 — みらい, «будущее») и выбор среди знаков, которые легко спутать (未 / 末).
/// </summary>
public static class SimilarKanji
{
    static readonly string[] Groups =
    {
        "未末", "土士", "人入八", "日目白田", "大犬太天夫", "待持特時侍", "休体", "千干午牛", "右石", "力刀万方", "王玉主", "今令",
        "間問聞門開閉", "鳥島", "貝見具", "買員貴", "晴清請情精静", "読続", "話語", "校交効", "使便", "住注柱", "運連", "遠園",
        "計針", "勉免", "送近", "識職織", "微徴徹", "険検験", "側測則", "輸輪論", "績積責", "復複腹", "賃貸資", "折祈析",
        "末未本木", "雪雷電", "思恩", "考老者", "銀根限眼", "飯飲館", "暑著", "牛半", "已己", "失矢", "外夕名", "東車", "姉妹", "持特",
        "治台始", "官管", "博薄", "義議儀", "績債", "壊懐", "掘堀", "裁栽載", "暮墓募幕", "援暖緩", "徒従", "既概",
    };

    static Dictionary<char, KanjiInfo>? _byChar;

    /// <summary>Группы, где хотя бы два знака есть в курсе (у них есть значение и примеры).</summary>
    static List<string> Usable()
    {
        _byChar ??= Db.Kanji.GroupBy(k => k.Char[0]).ToDictionary(g => g.Key, g => g.First());
        return Groups.Select(g => new string(g.Where(_byChar.ContainsKey).Distinct().ToArray())).Where(g => g.Length >= 2).ToList();
    }

    public static int Count => Usable().Sum(g => g.Length);

    public static QuizSession Quiz(int n = 15)
    {
        var groups = Usable();
        var all = groups.SelectMany(g => g).Distinct().ToList();
        // сначала знаки, которые учишь (или любые, если ещё ничего не начато)
        var learning = all.Where(c => ProgressStore.Card(_byChar![c].Id) != null).ToList();
        var pool = (learning.Count >= 8 ? learning : all).Shuffled();
        var qs = new List<QuizQ>();
        foreach (var c in pool)
        {
            if (qs.Count >= n) break;
            var k = _byChar![c];
            var ex = k.Examples.FirstOrDefault(e => e.Jp.Contains(c) && e.Jp.Length >= 2);
            var group = groups.First(g => g.Contains(c));
            var opts = group.Select(x => x.ToString()).ToList();
            // меньше четырёх вариантов — добавляем похожих «соседей» из других групп
            foreach (var other in groups.Where(g => g != group).Shuffled().SelectMany(g => g)) { if (opts.Count >= 4) break; if (!opts.Contains(other.ToString())) opts.Add(other.ToString()); }
            var options = opts.Take(4).Contains(c.ToString()) ? opts.Take(4) : opts.Take(3).Append(c.ToString());
            string prompt, sub;
            if (ex != null)
            {
                prompt = ex.Jp.Replace(c, '＿');
                sub = $"{ex.Kana} — {ex.Ru}";
            }
            else { prompt = "？"; sub = $"Знак со значением «{k.Ru}»"; }
            var differ = string.Join("  ·  ", group.Where(x => x != c).Select(x => $"{x} — {_byChar[x].Ru.Split(';')[0]}"));
            qs.Add(new QuizQ
            {
                Prompt = prompt, PromptSize = prompt.Length > 4 ? 56 : 72, Sub = sub, Answer = c.ToString(), OptionsJp = true,
                Options = options.Shuffled().ToArray(), Speak = ex?.Kana ?? k.On, Key = k.Id,
                Explain = $"{c} — {k.Ru.Split(';')[0]}" + (differ.Length > 0 ? $"   ·   не путать: {differ}" : ""),
            });
        }
        return new QuizSession("Похожие кандзи", qs);
    }
}
