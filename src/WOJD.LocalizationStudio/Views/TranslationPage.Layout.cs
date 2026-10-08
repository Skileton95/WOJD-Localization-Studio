using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public partial class TranslationPage
{
    private Grid? _workspaceGrid;
    private FrameworkElement? _contextElement;
    private GridSplitter? _listSplitter;
    private GridSplitter? _contextSplitter;
    private StackPanel? _relatedEntriesPanel;
    private TextBlock? _relatedEntriesHeading;
    private bool _layoutHooked;
    private bool _quickLayoutMenuInstalled;
    private bool _relatedUiInstalled;
    private DispatcherOperation? _contextRefreshOperation;
    private LocalizationDocument? _relationIndexDocument;
    private EntryRelationIndex? _relationIndex;

    public void ApplyWorkspaceLayoutSettings()
    {
        EnsureWorkspaceLayout();
        if (_workspaceGrid is null)
            return;

        var settings = EditorSettingsService.Current;
        var columns = _workspaceGrid.ColumnDefinitions;
        if (columns.Count < 5)
            return;

        columns[0].Width = new GridLength(settings.TranslationListWidth);
        columns[1].Width = new GridLength(8);

        if (settings.ShowContextPane)
        {
            columns[3].Width = new GridLength(8);
            columns[4].Width = new GridLength(settings.ContextPaneWidth);
            if (_contextElement is not null)
                _contextElement.Visibility = Visibility.Visible;
            if (_contextSplitter is not null)
                _contextSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            columns[3].Width = new GridLength(0);
            columns[4].Width = new GridLength(0);
            if (_contextElement is not null)
                _contextElement.Visibility = Visibility.Collapsed;
            if (_contextSplitter is not null)
                _contextSplitter.Visibility = Visibility.Collapsed;
        }

        EntriesGrid.RowHeight = settings.CompactEntryList ? 40 : double.NaN;
        EntriesGrid.FontSize = settings.CompactEntryList ? 12 : 13;

        if (QaDetailsText.Parent is FrameworkElement qaContainer)
            qaContainer.Visibility = settings.ShowQaDetails ? Visibility.Visible : Visibility.Collapsed;

        UpdateQuickLayoutMenuChecks();
        ScheduleContextRefresh();
    }

    public void ToggleContextPane()
    {
        var settings = EditorSettingsService.Current;
        settings.ShowContextPane = !settings.ShowContextPane;
        EditorSettingsService.Save(settings);
        ApplyWorkspaceLayoutSettings();
    }

    private void EnsureWorkspaceLayout()
    {
        if (_workspaceGrid is null && Content is Grid root)
        {
            _workspaceGrid = root.Children
                .OfType<Grid>()
                .FirstOrDefault(x => Grid.GetRow(x) == 1 && x.ColumnDefinitions.Count >= 5);
        }

        if (_workspaceGrid is null)
            return;

        _contextElement ??= _workspaceGrid.Children
            .OfType<FrameworkElement>()
            .FirstOrDefault(x => Grid.GetColumn(x) == 4);

        SimplifyContextPane();

        if (_listSplitter is null)
        {
            _listSplitter = CreateSplitter(1, "Изменить ширину списка строк");
            _listSplitter.DragCompleted += Splitter_DragCompleted;
            _workspaceGrid.Children.Add(_listSplitter);
        }

        if (_contextSplitter is null)
        {
            _contextSplitter = CreateSplitter(3, "Изменить ширину панели контекста");
            _contextSplitter.DragCompleted += Splitter_DragCompleted;
            _workspaceGrid.Children.Add(_contextSplitter);
        }

        if (!_layoutHooked)
        {
            // The XAML selection handler called ScrollIntoView for every mouse click.
            // That is useful for programmatic navigation but needlessly expensive for
            // ordinary selection in very large virtualized lists.
            EntriesGrid.SelectionChanged -= EntriesGrid_SelectionChanged;
            EntriesGrid.SelectionChanged += LayoutSelectionChanged;
            EntriesGrid.LoadingRow += EntriesGrid_LoadingRow;
            EntriesGrid.EnableColumnVirtualization = true;
            EntriesGrid.HeadersVisibility = DataGridHeadersVisibility.All;
            EntriesGrid.RowHeaderWidth = 72;
            ScrollViewer.SetCanContentScroll(EntriesGrid, true);
            VirtualizingPanel.SetIsVirtualizing(EntriesGrid, true);
            VirtualizingPanel.SetVirtualizationMode(EntriesGrid, VirtualizationMode.Recycling);
            _layoutHooked = true;
        }

        InstallQuickLayoutMenu();
    }

    private void SimplifyContextPane()
    {
        if (_contextElement is not Border contextBorder || contextBorder.Child is not Grid contextGrid)
            return;

        if (contextGrid.RowDefinitions.Count >= 4)
        {
            contextGrid.RowDefinitions[1].Height = new GridLength(0);
            var preview = contextGrid.Children
                .OfType<FrameworkElement>()
                .FirstOrDefault(x => Grid.GetRow(x) == 1);
            if (preview is not null)
                preview.Visibility = Visibility.Collapsed;
        }

        var heading = contextGrid.Children
            .OfType<TextBlock>()
            .FirstOrDefault(x => Grid.GetRow(x) == 2);
        if (heading is not null)
        {
            heading.Text = "Контекст и связанные строки";
            heading.Margin = new Thickness(0, 12, 0, 8);
        }

        if (_relatedUiInstalled)
            return;

        var contentStack = contextGrid.Children
            .OfType<StackPanel>()
            .FirstOrDefault(x => Grid.GetRow(x) == 3);
        if (contentStack is null)
            return;

        contextGrid.Children.Remove(contentStack);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = contentStack
        };
        Grid.SetRow(scroll, 3);
        contextGrid.Children.Add(scroll);

        _relatedEntriesHeading = new TextBlock
        {
            Text = "Связанные строки",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 8),
            Foreground = new SolidColorBrush(Color.FromRgb(23, 35, 60))
        };
        contentStack.Children.Add(_relatedEntriesHeading);

        _relatedEntriesPanel = new StackPanel();
        contentStack.Children.Add(_relatedEntriesPanel);
        _relatedUiInstalled = true;
    }

    private void InstallQuickLayoutMenu()
    {
        if (_quickLayoutMenuInstalled || MoreButton.ContextMenu is null)
            return;

        MoreButton.ContextMenu.Items.Add(new Separator());
        var contextItem = new MenuItem
        {
            Header = "Показывать правую панель",
            IsCheckable = true,
            Tag = "ContextToggle"
        };
        contextItem.Click += (_, _) => ToggleContextPane();
        MoreButton.ContextMenu.Items.Add(contextItem);

        var compactItem = new MenuItem
        {
            Header = "Компактный список строк",
            IsCheckable = true,
            Tag = "CompactToggle"
        };
        compactItem.Click += (_, _) =>
        {
            var settings = EditorSettingsService.Current;
            settings.CompactEntryList = !settings.CompactEntryList;
            EditorSettingsService.Save(settings);
            ApplyWorkspaceLayoutSettings();
        };
        MoreButton.ContextMenu.Items.Add(compactItem);
        _quickLayoutMenuInstalled = true;
        UpdateQuickLayoutMenuChecks();
    }

    private void UpdateQuickLayoutMenuChecks()
    {
        if (MoreButton.ContextMenu is null)
            return;

        var settings = EditorSettingsService.Current;
        foreach (var item in MoreButton.ContextMenu.Items.OfType<MenuItem>())
        {
            if (Equals(item.Tag, "ContextToggle"))
                item.IsChecked = settings.ShowContextPane;
            else if (Equals(item.Tag, "CompactToggle"))
                item.IsChecked = settings.CompactEntryList;
        }
    }

    private static GridSplitter CreateSplitter(int column, string toolTip)
    {
        var splitter = new GridSplitter
        {
            Width = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            Background = Brushes.Transparent,
            ShowsPreview = true,
            ToolTip = toolTip
        };
        Grid.SetColumn(splitter, column);
        Grid.SetRow(splitter, 0);
        Panel.SetZIndex(splitter, 20);
        return splitter;
    }

    private void Splitter_DragCompleted(object? sender, DragCompletedEventArgs e)
    {
        if (_workspaceGrid is null || _workspaceGrid.ColumnDefinitions.Count < 5)
            return;

        var settings = EditorSettingsService.Current;
        settings.TranslationListWidth = _workspaceGrid.ColumnDefinitions[0].ActualWidth;
        if (settings.ShowContextPane)
            settings.ContextPaneWidth = _workspaceGrid.ColumnDefinitions[4].ActualWidth;
        EditorSettingsService.Save(settings);
    }

    private void LayoutSelectionChanged(object sender, SelectionChangedEventArgs e)
        => ScheduleContextRefresh();

    private void ScheduleContextRefresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ScheduleContextRefresh);
            return;
        }

        if (_contextRefreshOperation is { Status: DispatcherOperationStatus.Pending })
            _contextRefreshOperation.Abort();

        _contextRefreshOperation = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(UpdateContextSummary));
    }

    private EntryRelationIndex? GetRelationIndex()
    {
        var document = _viewModel?.ActiveDocument;
        if (document is null)
        {
            _relationIndexDocument = null;
            _relationIndex = null;
            return null;
        }

        if (!ReferenceEquals(document, _relationIndexDocument))
        {
            _relationIndexDocument = document;
            _relationIndex = EntryRelationIndex.For(document);
        }

        return _relationIndex;
    }

    private void EntriesGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Header = null;
        e.Row.BorderBrush = new SolidColorBrush(Color.FromRgb(237, 241, 246));
        e.Row.BorderThickness = new Thickness(0, 0, 0, 1);

        if (e.Row.Item is not LocalizationEntry entry || GetRelationIndex() is not { } index)
            return;

        var family = index.GetFamily(entry);
        if (string.IsNullOrWhiteSpace(family))
            return;

        var visualIndex = e.Row.GetIndex();
        var previous = visualIndex > 0
            ? EntriesGrid.Items[visualIndex - 1] as LocalizationEntry
            : null;
        var previousFamily = previous is null ? string.Empty : index.GetFamily(previous);
        var startsGroup = !string.Equals(family, previousFamily, StringComparison.OrdinalIgnoreCase);

        if (startsGroup)
        {
            e.Row.Header = family;
            e.Row.BorderBrush = new SolidColorBrush(Color.FromRgb(117, 166, 247));
            e.Row.BorderThickness = new Thickness(0, 2, 0, 1);
            e.Row.ToolTip = $"Группа связанных ключей: {family}";
        }
        else
        {
            e.Row.ToolTip = $"Группа {family}";
        }
    }

    private void UpdateContextSummary()
    {
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry ||
            _viewModel.ActiveDocument is not LocalizationDocument document ||
            GetRelationIndex() is not { } index)
        {
            ContextQaText.Text = "Выберите строку, чтобы увидеть контекст.";
            UpdateRelatedEntries(null, null, []);
            return;
        }

        var sameSource = index.GetSameOriginal(entry);
        var variants = sameSource
            .Select(x => x.Translation)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(6)
            .ToList();
        var family = index.GetFamily(entry);
        var related = index.GetRelated(entry, 24);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(family))
            parts.Add($"Группа ключа: {family} · связанных строк: {related.Count:N0}.");

        if (string.IsNullOrWhiteSpace(entry.Original))
        {
            parts.Add("Original отсутствует — структурный QA недоступен.");
        }
        else
        {
            parts.Add($"Строк с тем же Original: {sameSource.Count:N0}.");
            if (variants.Count > 1)
            {
                parts.Add($"Вариантов перевода: {variants.Count}. Требуется проверка согласованности.");
                parts.Add("Переводы: " + string.Join(" | ", variants));
            }
        }

        if (entry.HasValidationIssues && !string.IsNullOrWhiteSpace(entry.ValidationSummary))
            parts.Add("QA: " + entry.ValidationSummary);
        else if (!entry.HasValidationIssues)
            parts.Add("QA: ошибок не найдено.");

        ContextQaText.Text = string.Join("\n\n", parts);
        UpdateRelatedEntries(entry, index, related);
    }

    private void UpdateRelatedEntries(
        LocalizationEntry? current,
        EntryRelationIndex? index,
        IReadOnlyList<LocalizationEntry> related)
    {
        if (_relatedEntriesPanel is null || _relatedEntriesHeading is null)
            return;

        _relatedEntriesPanel.Children.Clear();
        _relatedEntriesHeading.Text = related.Count == 0
            ? "Связанные строки"
            : $"Связанные строки · {related.Count:N0}";

        if (current is null || index is null)
            return;

        if (related.Count == 0)
        {
            _relatedEntriesPanel.Children.Add(new TextBlock
            {
                Text = "Для текущего ключа связанные строки не найдены.",
                Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 154)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            return;
        }

        foreach (var candidate in related)
            _relatedEntriesPanel.Children.Add(CreateRelatedEntryCard(current, candidate, index));
    }

    private UIElement CreateRelatedEntryCard(
        LocalizationEntry current,
        LocalizationEntry candidate,
        EntryRelationIndex index)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 253)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 233, 241)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = candidate.Key,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = candidate.Key
        });
        root.Children.Add(new TextBlock
        {
            Text = index.DescribeRelation(current, candidate),
            Foreground = new SolidColorBrush(Color.FromRgb(47, 112, 245)),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0)
        });
        root.Children.Add(new TextBlock
        {
            Text = candidate.OriginalDisplay,
            Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 154)),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 5, 0, 0),
            ToolTip = candidate.OriginalDisplay
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var openButton = CreateRelatedButton("Открыть");
        openButton.Click += (_, _) => OpenRelatedEntry(candidate);
        buttons.Children.Add(openButton);

        var compareButton = CreateRelatedButton("Сравнить");
        compareButton.Click += (_, _) =>
        {
            var dialog = new EntryComparisonWindow(current, candidate)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        };
        buttons.Children.Add(compareButton);
        root.Children.Add(buttons);

        card.Child = root;
        return card;
    }

    private static Button CreateRelatedButton(string text)
        => new()
        {
            Content = text,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(224, 230, 239)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };

    private void OpenRelatedEntry(LocalizationEntry entry)
    {
        if (_viewModel is null)
            return;

        _viewModel.SelectedEntry = entry;
        _viewModel.EntriesView.MoveCurrentTo(entry);
        ScrollToSelected();
    }
}
