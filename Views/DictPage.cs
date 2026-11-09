using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Matsubakira.Services;

namespace Matsubakira.Views;

public sealed record DictRow(DictEntry E)
{
    public string Jp => E.Jp;
    public string Kana => E.Kanji.Length > 0 ? E.Kana : "";
    public string Romaji => E.Romaji;
    // Убираем служебную разметку JMdict: «: {～な} ясный» → «(～な) ясный»
    public string Ru { get; } = System.Text.RegularExpressions.Regex.Replace(E.Ru.TrimStart(':', ' '), @"(\d\)):\s*", "$1 ").Replace('{', '(').Replace('}', ')');
    public Visibility CommonVis => E.Common ? Visibility.Visible : Visibility.Collapsed;
    public string AddGlyph => JDict.InStudy(E) ? "" : "";
    FrameworkElement? _accent;
    public FrameworkElement? AccentEl => _accent ??= Pitch.Pattern(E.Jp, E.Kana) is { } p ? new Controls.AccentView(p, compact: true) { HorizontalAlignment = HorizontalAlignment.Left } : null;
}

/// <summary>Большой словарь: ~69 тыс. статей, поиск по-русски, латиницей, каной и иероглифами.</summary>
public sealed class DictPage : UserControl, IPage
{
    public string Title => "Словарь";
    readonly ListBox _list;
    readonly TextBox _box = new() { FontSize = 18, Padding = new Thickness(44, 10, 16, 10), VerticalContentAlignment = VerticalAlignment.Center };
    readonly TextBlock _info = U.T("", "Muted");
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(160) };
    int _searchId;

    public DictPage()
    {
        _list = new ListBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = U.B("Text"),
            ItemTemplate = U.R<DataTemplate>("DictRow"), ItemContainerStyle = U.St("PlainListItem"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(_list, ScrollUnit.Pixel);
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnRowButton));

        _debounce.Tick += (_, _) => { _debounce.Stop(); Search(); };
        _box.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };

        var root = new Grid { Margin = new Thickness(40, 12, 40, 16), MaxWidth = 1240 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(U.Header("辞", "Словарь", "JMdict: десятки тысяч слов с русским переводом. Ищи по-русски, латиницей (taberu), каной или кандзи."));

        var search = new Grid();
        search.Children.Add(_box);
        var ph = U.T("Например: кошка · neko · ねこ · 猫", "Muted", 16);
        ph.IsHitTestVisible = false; ph.Margin = new Thickness(46, 0, 0, 0); ph.VerticalAlignment = VerticalAlignment.Center;
        _box.TextChanged += (_, _) => ph.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        search.Children.Add(ph);
        search.Children.Add(new TextBlock { Text = "", FontFamily = U.R<FontFamily>("Icons"), FontSize = 16, Foreground = U.B("Muted"), Margin = new Thickness(16, 0, 0, 0), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(search.RowAt(1));
        root.Children.Add(_info.M(2, 10, 0, 10).RowAt(2));
        root.Children.Add(_list.RowAt(3));
        Content = root;
    }

    /// <summary>Запрос, переданный из палитры Ctrl+K.</summary>
    public static string? PendingQuery;

    public async void OnShown()
    {
        if (PendingQuery != null) { _box.Text = PendingQuery; PendingQuery = null; }
        _ = _box.Dispatcher.BeginInvoke(() => _box.Focus(), DispatcherPriority.Input);
        if (!JDict.Loaded)
        {
            _info.Text = "Загружаю словарь…";
            await JDict.LoadAsync();
        }
        Search();
    }

    async void Search()
    {
        int id = ++_searchId;
        var q = _box.Text;
        if (q.Trim().Length == 0)
        {
            _list.ItemsSource = null;
            _info.Text = $"{JDict.Count:N0} статей. Введи слово — по-русски, латиницей, каной или кандзи. Кнопка «+» добавит слово в твои карточки.";
            return;
        }
        var res = await Task.Run(() => JDict.Search(q));
        if (id != _searchId) return; // пришёл более новый запрос
        _list.ItemsSource = res.Select(e => new DictRow(e)).ToList();
        _info.Text = q.Trim().Length == 0
            ? $"{JDict.Count:N0} статей · показаны самые частые слова"
            : res.Count == 0 ? "Ничего не найдено. Попробуй другое написание или более короткий запрос."
            : $"Найдено: {res.Count}{(res.Count >= 120 ? "+" : "")}";
    }

    void OnRowButton(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button b || b.DataContext is not DictRow row) return;
        if ((b.Tag as string) == "speak") Speech.Say(row.E.Kana);
        else if ((b.Tag as string) == "add")
        {
            if (JDict.AddToStudy(row.E))
            {
                b.Content = "";
                Anim.Pop(b, 0.6);
                Shell.Toast($"«{row.E.Jp}» добавлено в «Мои слова»", "追加");
            }
            else Shell.Toast("Это слово уже в твоих карточках", "✓");
        }
    }
}
