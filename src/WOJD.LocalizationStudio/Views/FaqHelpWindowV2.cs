using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WOJD.LocalizationStudio.Views;

public sealed class FaqHelpWindowV2 : Window
{
    private readonly TextBox _search = new();
    private readonly ListBox _categories = new();
    private readonly ListBox _topics = new();
    private readonly StackPanel _details = new();
    private readonly TextBlock _count = new();
    private readonly IReadOnlyList<FaqTopic> _allTopics = BuildTopics();
    private string _category = "Все разделы";

    public FaqHelpWindowV2()
    {
        Title = "FAQ и справка — WOJD Localization Studio";
        Width = 1160;
        Height = 760;
        MinWidth = 900;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(247, 249, 252));

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "FAQ и справка",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Все актуальные механики редактора 0.1.60. F1 — открыть справку, Ctrl+F — поиск внутри FAQ.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(header);

        var searchCard = Card();
        searchCard.Padding = new Thickness(10, 6, 10, 6);
        searchCard.Margin = new Thickness(0, 0, 0, 12);
        _search.BorderThickness = new Thickness(0);
        _search.Background = Brushes.Transparent;
        _search.FontSize = 14;
        _search.ToolTip = "Поиск по названию, описанию и ключевым словам";
        _search.TextChanged += (_, _) => RefreshTopics();
        searchCard.Child = _search;
        Grid.SetRow(searchCard, 1);
        root.Children.Add(searchCard);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var categoryCard = Card();
        _categories.BorderThickness = new Thickness(0);
        _categories.Background = Brushes.Transparent;
        _categories.Padding = new Thickness(6);
        _categories.ItemsSource = new[] { "Все разделы" }
            .Concat(_allTopics.Select(x => x.Category).Distinct())
            .ToList();
        _categories.SelectedIndex = 0;
        _categories.SelectionChanged += (_, _) =>
        {
            _category = _categories.SelectedItem as string ?? "Все разделы";
            RefreshTopics();
        };
        categoryCard.Child = _categories;
        body.Children.Add(categoryCard);

        var topicCard = Card();
        _topics.BorderThickness = new Thickness(0);
        _topics.Background = Brushes.Transparent;
        _topics.DisplayMemberPath = nameof(FaqTopic.Title);
        _topics.Padding = new Thickness(6);
        _topics.SelectionChanged += (_, _) => RenderTopic();
        topicCard.Child = _topics;
        Grid.SetColumn(topicCard, 2);
        body.Children.Add(topicCard);

        var detailsCard = Card();
        _details.Margin = new Thickness(18, 14, 20, 18);
        detailsCard.Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _details
        };
        Grid.SetColumn(detailsCard, 4);
        body.Children.Add(detailsCard);

        Grid.SetRow(body, 2);
        root.Children.Add(body);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _count.Foreground = Brushes.DimGray;
        _count.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(_count);
        var close = new Button
        {
            Content = "Закрыть",
            Padding = new Thickness(18, 7, 18, 7),
            IsCancel = true
        };
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        Content = root;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _search.Focus();
                _search.SelectAll();
                e.Handled = true;
            }
        };
        Loaded += (_, _) => _search.Focus();
        RefreshTopics();
    }

    private static Border Card()
        => new()
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(224, 229, 238)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            ClipToBounds = true
        };

    private void RefreshTopics()
    {
        var query = _search.Text.Trim();
        IEnumerable<FaqTopic> result = _allTopics;
        if (_category != "Все разделы")
            result = result.Where(x => x.Category == _category);

        if (query.Length > 0)
        {
            result = result.Where(x =>
                x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.Body.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.Category.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var list = result.ToList();
        _topics.ItemsSource = list;
        _count.Text = $"Найдено тем: {list.Count}";
        if (list.Count > 0)
            _topics.SelectedIndex = 0;
        else
            ShowEmpty();
    }

    private void ShowEmpty()
    {
        _details.Children.Clear();
        _details.Children.Add(new TextBlock
        {
            Text = "Ничего не найдено. Попробуйте другой запрос.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private void RenderTopic()
    {
        if (_topics.SelectedItem is not FaqTopic topic)
        {
            ShowEmpty();
            return;
        }

        _details.Children.Clear();
        _details.Children.Add(new TextBlock
        {
            Text = topic.Category,
            Foreground = new SolidColorBrush(Color.FromRgb(54, 111, 214)),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        });
        _details.Children.Add(new TextBlock
        {
            Text = topic.Title,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        foreach (var paragraph in topic.Body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            _details.Children.Add(new TextBlock
            {
                Text = paragraph.Trim(),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                LineHeight = 21,
                Foreground = new SolidColorBrush(Color.FromRgb(48, 55, 68)),
                Margin = new Thickness(0, 0, 0, 11)
            });
        }
    }

    private static IReadOnlyList<FaqTopic> BuildTopics()
        =>
        [
            new("Быстрый старт", "Основной рабочий цикл",
                "Откройте файл или папку, найдите нужную строку, измените поле «Перевод», проверьте строку QA-индикаторами и сохраните Ctrl+S.\n\nДля технических проблем используйте «↺ Структура» и «Автоисправить». Для большого количества безопасных правок текущего файла используйте «Массово…».",
                "начать открыть перевод сохранить workflow"),

            new("Главное окно", "Панель файлов",
                "Слева находятся открытые файлы и папки. Можно держать несколько файлов открытыми одновременно и переключаться между ними.\n\n«Массово…» всегда работает только с текущим активным файлом, а не со всеми открытыми файлами.",
                "файлы папки active document текущий файл"),
            new("Главное окно", "Таблица строк",
                "В таблице отображаются номер, Namespace, ключ, Original, Перевод, QA и обычный статус перевода. Выбор строки открывает её в нижнем редакторе.\n\nКолонки можно частично скрывать через верхнее меню «Настройки».",
                "таблица namespace key original перевод qa статус"),
            new("Главное окно", "Поиск и фильтры",
                "Поиск работает по активному файлу: Namespace, ключу, Original и переводу. Ctrl+F переводит фокус в поиск. Ctrl+Shift+F открывает поиск по проекту.\n\nФильтры «Все / Переведено / Без перевода / Изменено / Ошибки» и быстрые QA-фильтры помогают сузить список.",
                "поиск фильтры ctrl f project search"),

            new("Редактор строки", "Оригинал и Перевод",
                "«Оригинал» показывает исходный текст и не редактируется. «Перевод» — рабочее поле русского текста.\n\nЕсли Original отсутствует, программа явно пишет, что структуру проверить невозможно.",
                "original перевод исходный текст"),
            new("Редактор строки", "История",
                "Кнопка «История» показывает изменения выбранной строки в текущей рабочей истории и позволяет восстановить предыдущий вариант, если он сохранён в истории.",
                "история строки restore"),
            new("Редактор строки", "Consistency",
                "Consistency ищет один и тот же Original, который переведён несколькими разными способами. Это помогает находить непоследовательность терминологии и повторяющихся фраз.",
                "consistency согласованность одинаковый original разные переводы"),
            new("Редактор строки", "Стрелка ⌃ / ⌄",
                "Стрелка в правой части заголовка сворачивает или разворачивает редактор. ⌃ — свернуть, ⌄ — развернуть. Состояние запоминается.",
                "свернуть развернуть стрелка"),
            new("Редактор строки", "Цветной предпросмотр токенов",
                "Под полем перевода может отображаться технический предпросмотр тегов и плейсхолдеров. Он нужен только для визуального контроля и может быть скрыт через «Настройки».",
                "токены preview теги placeholders"),

            new("QA и структура", "Теги / Плейсхолдеры / Переносы",
                "Строка QA сравнивает структуру Original и Перевода. Например «Теги 2/3 ✕» означает, что структура тегов не совпадает. «Плейсхолдеры 2/2 ✓» означает совпадение.\n\nТакже проверяются конкретные отсутствующие или лишние токены и порядок тегов.",
                "теги плейсхолдеры переносы 0/0 2/3"),
            new("QA и структура", "Автоматическая защита структуры",
                "Отдельной кнопки «Защита» больше нет. Защита работает автоматически: редактор блокирует Backspace/Delete/Cut, если действие явно ломает обязательный тег или плейсхолдер, и предупреждает при опасной вставке.",
                "защита автоматически backspace delete paste"),
            new("QA и структура", "↺ Структура",
                "Пытается восстановить только однозначно повреждённые теги и плейсхолдеры по Original. Русский текст не должен переписываться.\n\nЕсли восстановление неоднозначно, программа не угадывает и оставляет строку для ручной правки.",
                "структура восстановить тег placeholder"),
            new("QA и структура", "Автоисправить",
                "Применяет безопасные правила только к текущей строке: пробелы, часть пунктуации и доказуемые структурные исправления. Перед сложными случаями программа не должна угадывать смысл перевода.",
                "автоисправить текущая строка safe rules"),
            new("QA и структура", "Массово…",
                "Анализирует и исправляет только текущий активный файл. Другие открытые файлы не затрагиваются.\n\nСначала строится план и предпросмотр. Перед применением автоматически создаётся защитный снимок, а операция поддерживает пакетный Undo/Redo.",
                "массово текущий файл active document preview undo snapshot"),
            new("QA и структура", "QA-профили",
                "Инструменты → QA-профили… позволяет задавать дополнительные правила по Namespace/Key, например ограничения длины или переносов для определённых типов строк.",
                "qa profiles профиль namespace key длина"),

            new("Инструменты", "Глоссарий…",
                "Глоссарий перенесён в Инструменты → Глоссарий…. В нём задаются соответствия Source → Target и обязательность терминов.\n\nГлоссарий продолжает использоваться программой для QA даже без отдельной вкладки под редактором.",
                "глоссарий инструменты glossary terms"),
            new("Инструменты", "История проекта…",
                "Показывает журнал ручных и массовых операций проекта: что менялось, сколько строк затронуто и какие защитные снимки были созданы.",
                "история проекта операции журнал"),
            new("Инструменты", "Сравнить со старой версией…",
                "Сравнивает активный файл со старой версией по Namespace + Key. Помогает переносить переводы в обновлённый файл, не перезаписывая строки без необходимости.",
                "compare старая версия перенос переводов"),

            new("Настройки", "Верхнее меню Настройки",
                "Настройки вынесены в отдельный пункт верхнего меню рядом с «Файл / Правка / Инструменты / Справка».\n\nТам настраиваются высота редактора, размер шрифта, перенос длинных строк, цветной предпросмотр токенов, подробный QA и видимость основных колонок.",
                "настройки верхнее меню font columns editor"),

            new("Файлы и безопасность", "Сохранение",
                "Ctrl+S сохраняет активный файл, Ctrl+Shift+S — все открытые файлы. Сохранение проходит через уникальный временный файл с проверкой NDJSON перед заменой оригинала.",
                "сохранить ctrl s save ndjson temp"),
            new("Файлы и безопасность", "Бэкапы и автоматические снимки",
                "Перед сохранением и опасными массовыми операциями используются резервные копии или защитные снимки. Отдельная кнопка «Снимки» с главного экрана удалена, чтобы не перегружать интерфейс.\n\nСнимки продолжают автоматически создаваться там, где они нужны для безопасного отката.",
                "снимки backups snapshot автоматические"),
            new("Файлы и безопасность", "Файл изменён другой программой",
                "Если открытый файл изменил другой процесс, появляется жёлтая панель: «Сравнить / Перезагрузить / Сохранить поверх / Игнорировать». Это защищает от случайного затирания изменений wojd-trans или другого редактора.",
                "external changed compare reload overwrite ignore"),
            new("Файлы и безопасность", "Recovery",
                "Несохранённые изменения могут сохраняться во внутренний recovery-черновик. Он не заменяет исходный игровой файл и нужен для восстановления после сбоя или перезапуска.",
                "recovery crash черновик"),

            new("Экспорт и навигация", "Экспорт",
                "Файл → Экспорт позволяет выгрузить выбранные строки, непереведённые строки или QA-ошибки в отдельный NDJSON для анализа или обмена.",
                "export выбранные untranslated qa"),
            new("Экспорт и навигация", "Переход к строке или ключу",
                "Ctrl+G открывает переход по номеру строки, ключу или Namespace:Key. F6 / Shift+F6 переходят по непереведённым строкам, F7 / Shift+F7 — по структурным QA-проблемам.",
                "ctrl g f6 f7 key line navigation"),

            new("Что скрыто сейчас", "ИИ-проверка",
                "Кнопки «ИИ: строка» и «ИИ: пакет» временно удалены из интерфейса. ИИ-проверка сейчас не является частью основного рабочего процесса 0.1.60.",
                "ии ai hidden отключено"),
            new("Что скрыто сейчас", "Ручная механика Проверка",
                "Выпадающий статус «Проверка», кнопки «✓ и дальше» и «⚠ и дальше» удалены. Ручной workflow проверки строк сейчас не используется.",
                "проверка reviewed status удалено"),
            new("Что скрыто сейчас", "Translation Memory помощник",
                "Нижняя панель «Помощник: Translation Memory и глоссарий» удалена полностью вместе с кнопкой «Применить». Глоссарий остался доступен через меню «Инструменты» и продолжает использоваться QA.",
                "translation memory помощник применить удалено"),

            new("Горячие клавиши", "Основные сочетания",
                "Ctrl+O — открыть файл. Ctrl+Shift+O — открыть папку. Ctrl+S — сохранить. Ctrl+Shift+S — сохранить всё. Ctrl+F — поиск. Ctrl+Shift+F — поиск по проекту. Ctrl+H — заменить. Ctrl+G — перейти. Ctrl+Z / Ctrl+Y — Undo / Redo. F6 / Shift+F6 — непереведённые. F7 / Shift+F7 — структурные ошибки. F1 — FAQ.",
                "keyboard hotkeys ctrl f s z y f1 f6 f7"),

            new("FAQ", "Автоисправить или Массово?",
                "«Автоисправить» работает только с выбранной строкой. «Массово…» строит план для всех строк только текущего активного файла и показывает предпросмотр перед применением.",
                "автоисправить массово разница"),
            new("FAQ", "Что делать со сломанными тегами?",
                "Сначала посмотрите строку QA. Если ошибка однозначная — нажмите «↺ Структура». Если программа не может безопасно восстановить структуру, исправьте строку вручную, сверяясь с Original.",
                "сломанные теги restore structure"),
            new("FAQ", "Что безопасно делать перед массовой операцией?",
                "Проверьте, что открыт нужный файл и сохранены важные ручные правки. Массовая операция работает только с текущим файлом, показывает предпросмотр и автоматически создаёт защитный снимок перед применением.",
                "массовая операция snapshot current file safe")
        ];

    private sealed record FaqTopic(
        string Category,
        string Title,
        string Body,
        string Keywords);
}
