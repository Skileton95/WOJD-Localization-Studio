using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WOJD.LocalizationStudio.Views;

public sealed class FaqHelpWindow : Window
{
    private readonly TextBox _searchBox = new();
    private readonly ListBox _categories = new();
    private readonly ListBox _topicsList = new();
    private readonly StackPanel _details = new();
    private readonly TextBlock _countText = new();
    private readonly IReadOnlyList<FaqTopic> _topics = BuildTopics();
    private string _selectedCategory = "Все разделы";

    public FaqHelpWindow()
    {
        Title = "FAQ и справка — WOJD Localization Studio";
        Width = 1180;
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
            Text = "Все основные кнопки, окна, режимы и механики редактора. F1 — открыть справку, Ctrl+F — поиск внутри FAQ.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(header);

        var searchBorder = Card();
        searchBorder.Padding = new Thickness(10, 6, 10, 6);
        searchBorder.Margin = new Thickness(0, 0, 0, 12);
        _searchBox.BorderThickness = new Thickness(0);
        _searchBox.Background = Brushes.Transparent;
        _searchBox.FontSize = 14;
        _searchBox.ToolTip = "Поиск по названию, описанию и ключевым словам";
        _searchBox.TextChanged += (_, _) => RefreshTopics();
        searchBorder.Child = _searchBox;
        Grid.SetRow(searchBorder, 1);
        root.Children.Add(searchBorder);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var categoryCard = Card();
        _categories.BorderThickness = new Thickness(0);
        _categories.Background = Brushes.Transparent;
        _categories.Padding = new Thickness(6);
        _categories.ItemsSource = new[] { "Все разделы" }
            .Concat(_topics.Select(x => x.Category).Distinct())
            .ToList();
        _categories.SelectedIndex = 0;
        _categories.SelectionChanged += (_, _) =>
        {
            _selectedCategory = _categories.SelectedItem as string ?? "Все разделы";
            RefreshTopics();
        };
        categoryCard.Child = _categories;
        body.Children.Add(categoryCard);

        var topicCard = Card();
        _topicsList.BorderThickness = new Thickness(0);
        _topicsList.Background = Brushes.Transparent;
        _topicsList.DisplayMemberPath = nameof(FaqTopic.Title);
        _topicsList.Padding = new Thickness(6);
        _topicsList.SelectionChanged += (_, _) => RenderTopic();
        topicCard.Child = _topicsList;
        Grid.SetColumn(topicCard, 2);
        body.Children.Add(topicCard);

        var detailsCard = Card();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _details
        };
        _details.Margin = new Thickness(18, 14, 20, 18);
        detailsCard.Child = scroll;
        Grid.SetColumn(detailsCard, 4);
        body.Children.Add(detailsCard);

        Grid.SetRow(body, 2);
        root.Children.Add(body);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _countText.Foreground = Brushes.DimGray;
        _countText.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(_countText);
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
                _searchBox.Focus();
                _searchBox.SelectAll();
                e.Handled = true;
            }
        };

        Loaded += (_, _) => _searchBox.Focus();
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
        var query = _searchBox.Text.Trim();
        IEnumerable<FaqTopic> result = _topics;

        if (_selectedCategory != "Все разделы")
            result = result.Where(x => x.Category == _selectedCategory);

        if (query.Length > 0)
        {
            result = result.Where(x =>
                x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.Body.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.Category.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var list = result.ToList();
        _topicsList.ItemsSource = list;
        _countText.Text = $"Найдено тем: {list.Count}";

        if (list.Count > 0)
            _topicsList.SelectedIndex = 0;
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
        if (_topicsList.SelectedItem is not FaqTopic topic)
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
            new("Быстрый старт", "Как работать с редактором",
                "Откройте файл или папку, выберите строку, введите перевод, проверьте QA, при необходимости используйте TM/глоссарий/автоисправление/ИИ, отметьте строку как проверенную и сохраните Ctrl+S.\n\nДля последовательной вычитки больших файлов используйте «✓ и дальше» и «⚠ и дальше».", "начать открыть перевести сохранить workflow"),

            new("Главное окно", "Таблица и колонки",
                "Таблица содержит №, Namespace, Ключ, Original, Перевод, QA и Статус. Namespace и Key помогают понять назначение строки, QA показывает проблемы, Статус — состояние перевода.\n\nМожно выделять несколько строк для массовых действий.", "таблица namespace key original перевод qa статус"),
            new("Главное окно", "Фильтры",
                "«Все» — все строки. «Переведено» — только с переводом. «Без перевода» — пустые. «Изменено» — изменённые после открытия. «Ошибки» — строки с QA-проблемами.\n\nДополнительные QA-фильтры позволяют отдельно показать Теги, Плейсхолдеры и Переносы. Фильтр Namespace ограничивает список выбранным Namespace.", "фильтры все переведено без перевода изменено ошибки теги плейсхолдеры переносы namespace"),
            new("Главное окно", "Поиск",
                "Обычный поиск фильтрует активный файл по Namespace, Key, Original и Переводу. Ctrl+F переводит фокус в поиск.\n\nCtrl+Shift+F открывает поиск по всем открытым файлам проекта.", "поиск ctrl f ctrl shift f проект"),
            new("Главное окно", "Панель файлов",
                "Слева отображаются открытые файлы и папки. Можно держать несколько файлов открытыми и переключаться между ними. Кнопка × закрывает файл. Несохранённые изменения должны быть защищены предупреждением/recovery.", "файлы папки multi file закрыть"),

            new("Редактор строки", "Original и Перевод",
                "Original — исходный текст и не редактируется. Перевод — русская строка, которую вы меняете.\n\nЕсли Original отсутствует, редактор пишет, что структура не проверяется. Это не означает, что перевод правильный или неправильный: просто нет эталона для сравнения тегов/плейсхолдеров.", "original перевод исходный текст отсутствует"),
            new("Редактор строки", "Цветной предпросмотр токенов",
                "Под полем перевода отображается технический предпросмотр. Теги и плейсхолдеры визуально отделены от обычного текста, а проблемные токены выделяются.\n\nЭто только подсказка; реальный текст редактируется в поле «Перевод».", "токены предпросмотр теги плейсхолдеры"),
            new("Редактор строки", "Свернуть / Развернуть и высота панели",
                "«Свернуть» освобождает место таблице. «Развернуть» возвращает редактор. Высоту нижней панели можно менять разделителем, а настройки запоминаются.", "свернуть развернуть высота grid splitter"),

            new("QA и структура", "Теги X/Y, Плейсхолдеры X/Y, Переносы X/Y",
                "Первое число — сколько элементов найдено в Переводе, второе — сколько ожидается по Original. Например «Теги 2/3 ✕» означает потерю или замену одного тега. «Плейсхолдеры 2/2 ✓» означает совпадение количества.\n\nКроме количества проверяются конкретные отсутствующие/лишние токены и порядок тегов.", "теги 0/0 2/3 плейсхолдеры переносы структура"),
            new("QA и структура", "↺ Структура",
                "Восстанавливает только однозначно повреждённые теги/плейсхолдеры по Original. Русский текст не должен переписываться.\n\nЕсли безопасно определить позицию токена нельзя, программа оставит случай для ручной проверки.", "структура restore восстановить тег placeholder"),
            new("QA и структура", "Защита",
                "Когда включена «Защита», Backspace/Delete/Cut не должны случайно ухудшить структуру. При опасной вставке появляется предупреждение.\n\nВыключайте защиту только когда действительно нужно вручную менять технические токены.", "защита backspace delete cut paste вставка"),
            new("QA и структура", "Значки QA",
                "✓ — проверка пройдена. ✕ — структурная ошибка. ⚠ — предупреждение или профильное правило. — — проверка невозможна, например нет Original.\n\nПодробность обычно доступна в подсказке строки/колонки QA и в нижнем структурном блоке.", "qa значки галочка крест предупреждение"),
            new("QA и структура", "QA-профили",
                "QA-профили задают правила по маскам Namespace/Key: максимальную длину, разрешение переносов и другие ограничения. Например *-SkillName можно ограничить 32 символами и запретить переносы.\n\nЭто полезно для UI-кнопок, названий навыков и других строк с жёсткими лимитами.", "qa profiles длина namespace key"),

            new("Автоисправление", "Автоисправить",
                "Работает только с текущей строкой и применяет локальные безопасные правила: пробелы, типографика и однозначное восстановление структуры. Это не ИИ и не должно менять смысл перевода.", "автоисправить текущая строка безопасно правила"),
            new("Автоисправление", "Массово…",
                "Анализирует весь текущий файл и собирает безопасные исправления. Перед применением показывается предпросмотр. Для крупной операции создаётся защитный снимок, а прогресс можно отменить.\n\nМассовая операция может поддерживать пакетный Undo/Redo.", "массово весь файл preview snapshot undo"),
            new("Автоисправление", "Прогресс и Отмена",
                "При долгой операции снизу появляется прогресс, например «ИИ-проверка: 20 / 100». «Отмена» просит остановить текущую операцию. Уже выполняющийся сетевой запрос или блок может завершиться прежде, чем операция полностью остановится.", "прогресс отмена 20 100 операция"),

            new("Проверка перевода", "Статус проверки",
                "«Не проверено» — человек ещё не подтверждал строку. «Проверено» — перевод просмотрен. «Требует правки» — к строке надо вернуться. «Пропустить» — сознательно исключить из текущей вычитки.\n\nЭти статусы хранятся отдельно и не попадают в игровой NDJSON. После изменения уже проверенного перевода статус «Проверено» сбрасывается.", "проверка reviewed не проверено требует правки пропустить"),
            new("Проверка перевода", "✓ и дальше / ⚠ и дальше",
                "«✓ и дальше» отмечает текущую строку Проверенной и переходит к следующей непроверенной. «⚠ и дальше» ставит Требует правки и тоже идёт дальше.\n\nЭто основной режим последовательной ручной вычитки больших файлов.", "и дальше следующая непроверенная"),

            new("Translation Memory", "Translation Memory",
                "TM ищет уже переведённые строки с таким же или похожим Original в текущем файле. Процент — степень сходства. 100% означает точное совпадение нормализованного исходника, но не гарантирует одинаковый игровой контекст.\n\nВыберите предложение и нажмите «Применить» или используйте двойной клик. TM ничего не меняет автоматически.", "translation memory tm 100 применить"),
            new("Translation Memory", "Помощник: Translation Memory и глоссарий",
                "Раскрывающийся блок «Помощник» содержит вкладки TM и Глоссарий. TM показывает похожие существующие переводы. Глоссарий показывает термины из Original и ожидаемые русские варианты.\n\nПолный глоссарий редактируется отдельной кнопкой «Глоссарий» сверху.", "помощник tm glossary вкладки"),

            new("Глоссарий", "Глоссарий",
                "Редактор терминов Source → Target для единообразного перевода игровых терминов. Термины могут быть обязательными или рекомендованными.\n\nЕсли обязательный Source найден в Original, QA может проверить наличие ожидаемого Target в переводе. Глоссарий сам не переписывает весь файл.", "глоссарий source target термин"),

            new("Consistency", "Consistency",
                "Ищет один и тот же Original, который уже переведён несколькими разными способами. Нужен для поиска терминологического разнобоя.\n\nПеред унификацией обязательно проверьте контекст: одинаковый Original иногда используется в разных игровых ситуациях.", "consistency одинаковый original разные переводы"),

            new("ИИ", "ИИ: строка",
                "Проверяет текущую строку через настроенную модель OpenAI. ИИ сначала показывает предложение и причину, а не должен молча переписывать перевод. Предложение со сломанными тегами/плейсхолдерами отклоняется.\n\nНужен OPENAI_API_KEY или ключ, введённый в настройках сеанса.", "ии ai строка openai preview api key"),
            new("ИИ", "ИИ: пакет…",
                "Пакетная проверка обрабатывает набор строк: выделенные, QA-проблемы, «Требует правки» и другие доступные режимы. Перед запуском показывается объём и приблизительная оценка токенов.\n\nConcurrency — число параллельных запросов, retries — число повторов после временной ошибки. Перед массовым применением создаётся снимок.", "ии пакет batch concurrency retries tokens"),
            new("ИИ", "Стоимость ИИ",
                "Оценка стоимости приблизительная. Она корректна только если вы сами указали актуальные цены выбранной модели. Реальная цена API может изменяться независимо от программы.\n\nСначала тестируйте ИИ на небольшом пакете.", "ии стоимость цена api tokens"),

            new("История и безопасность", "История",
                "«История» показывает изменения выбранной строки: старый текст, новый текст, тип операции и время. Можно вернуть предыдущий вариант.\n\nИстория строки отличается от Ctrl+Z: она предназначена для просмотра конкретной записи, а Undo работает с текущим стеком операций.", "история строки undo old new"),
            new("История и безопасность", "Снимки",
                "Снимок сохраняет состояние переводов всего файла. Его стоит создавать перед большой или рискованной операцией. При восстановлении проверяется совместимость со структурой текущего файла.\n\nСнимок надёжнее обычного Undo для длинной цепочки массовых изменений.", "снимки snapshot восстановить файл"),
            new("История и безопасность", "Undo / Redo",
                "Ctrl+Z отменяет последнее совместимое изменение, Ctrl+Y возвращает его. Некоторые массовые операции сохраняются как единый пакет.\n\nПеред очень большой операцией всё равно лучше дополнительно создать снимок.", "undo redo ctrl z ctrl y"),
            new("История и безопасность", "Recovery и резервные копии",
                "Recovery-черновик хранит несохранённые изменения отдельно от игрового файла и помогает восстановиться после сбоя. Перед сохранением и некоторыми массовыми операциями используются дополнительные резервные копии/снимки.\n\nRecovery — страховка, а не замена Ctrl+S.", "recovery backup резерв сбой"),

            new("Файлы и сохранение", "Безопасное сохранение",
                "При Ctrl+S редактор пишет уникальный временный файл, закрывает его, проверяет NDJSON и количество строк и только затем заменяет оригинал. Параллельные Save одного пути блокируются.\n\nЕсли файл занят внешней программой, исходник не должен повреждаться.", "save ctrl s temp ndjson файл занят"),
            new("Файлы и сохранение", "Файл изменён другой программой",
                "Жёлтая панель появляется, если открытый файл изменился на диске.\n\n«Сравнить» — посмотреть различия. «Перезагрузить» — принять версию с диска. «Сохранить поверх» — перезаписать внешние изменения текущей версией редактора. «Игнорировать» — скрыть предупреждение для текущего события.\n\nЕсли параллельно работает wojd-trans, чаще всего сначала выбирайте «Сравнить».", "external change сравнить перезагрузить сохранить поверх игнорировать wojd-trans"),
            new("Файлы и сохранение", "Сравнить со старой версией",
                "Сравнивает текущий и старый файл по Namespace + Key и показывает новые, удалённые и изменённые строки. Безопасный перенос перевода не должен перезаписывать существующий перевод и требует ручной проверки, если Original изменился.", "compare old старая версия перенос"),
            new("Файлы и сохранение", "Экспорт",
                "Файл → Экспорт умеет выгружать выбранные строки, непереведённые строки и QA-ошибки в отдельный NDJSON. Это удобно для внешнего ревью или обработки ограниченного набора строк.", "экспорт selected untranslated qa"),

            new("Поиск и навигация", "Найти и заменить",
                "Ctrl+H открывает замену. «Заменить» меняет текущее совпадение, «Заменить всё» — все подходящие строки выбранной области. После массовой замены обязательно проверьте QA, особенно если запрос мог затронуть теги/плейсхолдеры.", "ctrl h replace заменить все"),
            new("Поиск и навигация", "Перейти к строке или ключу",
                "Ctrl+G принимает номер строки, Key или Namespace:Key и прокручивает таблицу к найденной записи.", "ctrl g goto key namespace"),
            new("Поиск и навигация", "F6 / F7",
                "F6 / Shift+F6 — следующая / предыдущая непереведённая строка. F7 / Shift+F7 — следующая / предыдущая структурная QA-ошибка.", "f6 f7 непереведенная qa"),

            new("Настройки", "Настройки",
                "Окно настроек управляет параметрами интерфейса: размером шрифта, переносом текста, высотой/сворачиванием редактора, видимостью технического предпросмотра и QA-блока, а также параметрами ИИ, доступными в текущей версии.\n\nНастройки сохраняются между запусками.", "настройки font wrap editor ai"),

            new("Горячие клавиши", "Все основные горячие клавиши",
                "Ctrl+O — открыть файл.\nCtrl+Shift+O — открыть папку.\nCtrl+S — сохранить.\nCtrl+Shift+S — сохранить всё.\nCtrl+F — поиск.\nCtrl+Shift+F — поиск по проекту.\nCtrl+H — найти и заменить.\nCtrl+G — перейти к строке/ключу.\nCtrl+Z / Ctrl+Y — Undo / Redo.\nF6 / Shift+F6 — непереведённые строки.\nF7 / Shift+F7 — структурные QA-ошибки.\nF1 — FAQ и справка.\nCtrl+F внутри FAQ — поиск по справке.", "горячие клавиши shortcuts f1 ctrl"),

            new("Типичные вопросы", "Автоисправить или ИИ?",
                "«Автоисправить» — локальные предсказуемые правила без отправки текста наружу. «ИИ» — внешняя модель, способная предложить смысловые/стилистические изменения и потребляющая API.\n\nДля сломанных тегов сначала используйте «↺ Структура» или обычное автоисправление.", "разница автоисправление ии"),
            new("Типичные вопросы", "Что делать со сломанными тегами",
                "Посмотрите структурный diff → нажмите «↺ Структура» → если случай неоднозначный, исправьте вручную по Original → оставьте «Защита» включённой → перед массовой обработкой создайте снимок.", "сломаны теги восстановить структура"),
            new("Типичные вопросы", "TM, Consistency и Глоссарий — в чём разница",
                "TM переиспользует уже существующий перевод похожего Original. Consistency ищет одинаковый Original с разными переводами. Глоссарий задаёт заранее утверждённый перевод конкретного термина.\n\nЭти механики дополняют друг друга и не являются взаимозаменяемыми.", "tm consistency glossary отличие"),
            new("Типичные вопросы", "Что делать перед большой массовой операцией",
                "Безопасный порядок: Ctrl+S → создать снимок → выполнить операцию → проверить выборку и QA → сохранить. Undo удобен для быстрых откатов, но снимок лучше защищает состояние всего файла.", "массовая операция безопасность snapshot save")
        ];

    private sealed record FaqTopic(string Category, string Title, string Body, string Keywords);
}
