using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Matsubakira.Services;

/// <summary>Иллюстрации — Microsoft Fluent Emoji (MIT, 3D). Встроены в exe, кэшируются и замораживаются.</summary>
public static class Emoji
{
    static readonly Dictionary<string, ImageSource?> Cache = new();

    public static ImageSource? Get(string key)
    {
        if (Cache.TryGetValue(key, out var s)) return s;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri($"pack://application:,,,/Assets/emoji/{key}.png");
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            s = bmp;
        }
        catch { s = null; }
        Cache[key] = s;
        return s;
    }

    public static FrameworkElement Img(string key, double size)
    {
        var src = Get(key);
        if (src == null) return new Border { Width = size, Height = size };
        var img = new Image { Source = src, Width = size, Height = size };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        return img;
    }

    static readonly Dictionary<string, string> Topics = new()
    {
        ["Люди"] = "speech_balloon", ["Семья"] = "house_with_garden", ["Числа"] = "input_numbers", ["Время"] = "alarm_clock",
        ["Еда и напитки"] = "bento_box", ["Места и город"] = "cityscape", ["Транспорт"] = "bullet_train", ["Вещи"] = "backpack",
        ["Природа и погода"] = "cherry_blossom", ["Глаголы"] = "rocket", ["Прилагательные"] = "artist_palette", ["Вопросы и указатели"] = "red_question_mark",
        ["Работа и учёба"] = "briefcase", ["Быт и дом"] = "couch_and_lamp", ["Город и поездки"] = "world_map", ["Здоровье и тело"] = "pill",
        ["Чувства и характер"] = "smiling_face_with_hearts", ["Глаголы N4"] = "gear", ["Наречия и связки"] = "link", ["Мои слова"] = "bookmark_tabs",
    };

    public static string ForTopic(string topic) =>
        Topics.TryGetValue(topic, out var k) ? k
        : topic.StartsWith("Сущ.") ? "open_book" : topic.StartsWith("Глаголы") ? "rocket" : topic.StartsWith("Прилаг.") ? "artist_palette"
        : topic.StartsWith("Наречия") ? "sparkles" : topic.StartsWith("N3") ? "graduation_cap" : "memo";

    static readonly Dictionary<string, string> Titles = new()
    {
        // диалоги
        ["Знакомство"] = "speech_balloon", ["В кафе"] = "hot_beverage", ["В магазине одежды"] = "tshirt", ["Как пройти?"] = "round_pushpin",
        ["В гостинице"] = "hotel", ["У врача"] = "stethoscope", ["Приглашение"] = "clapper_board", ["По телефону"] = "mobile_phone",
        // говорение
        ["Первые звуки"] = "musical_note", ["Приветствия"] = "sunrise", ["Вежливость"] = "wrapped_gift", ["В кафе и магазине"] = "shortcake",
        ["Дорога и транспорт"] = "station", ["Выручалочки"] = "light_bulb", ["Повседневные фразы"] = "calendar",
        // чтение
        ["Мой день"] = "sun_with_face", ["Моя семья"] = "house", ["Весна в Японии"] = "cherry_blossom", ["Первый день в Токио"] = "tokyo_tower",
        ["Японский язык"] = "books",
    };

    public static string For(string title, string fallback = "sparkles") => Titles.TryGetValue(title, out var k) ? k : fallback;
}
