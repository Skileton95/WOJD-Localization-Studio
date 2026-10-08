using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public partial class TranslationPage
{
    private Grid? _workspaceGrid;
    private FrameworkElement? _contextElement;
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
        columns[1].Width = new GridLength(10);

        if (settings.ShowContextPane)
        {
            columns[3].Width = new GridLength(10);
            columns[4].Width = new GridLength(settings.ContextPaneWidth);
            if (_contextElement is not null)
                _contextElement.Visibility = Visibility.Visible;
        }
        else
        {
            columns[3].Width = new GridLength(0);
            columns[4].Width = new GridLength(0);
            if (_contextElement is not null)
                _contextElement.Visibility = Visibility.Collapsed;
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

        if (!_layoutHooked)
        {
            // Ordinary selection must stay cheap on 600k+ virtualized rows.
            EntriesGrid.SelectionChanged -= EntriesGrid_SelectionChanged;
            EntriesGrid.SelectionChanged += LayoutSelectionChanged;
            EntriesGrid.EnableColumnVirtualization = true;
            EntriesGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
            EntriesGrid.RowHeaderWidth = 0;
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

    private void UpdateContextSummary()
    {
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry ||
            _viewModel.ActiveDocument is not LocalizationDocument ||
            GetRelationIndex() is not { } index)
        {
            ContextQaText.Text = "Выберите строку, чтобы увидеть контекст.";
            UpdateRelatedEntries(null, null, [], []);
            return;
        }

        var sameSource = index.GetSameOriginal(entry);
        var variants = sameSource
            .Take(256)
            .Select(x => x.Translation)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(6)
            .ToList();
        var family = index.GetFamily(entry);
        var compareCandidates = index.GetRelated(entry, 64);
        var groups = index.GetVisualGroups(entry, 32);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(family))
            parts.Add($"Группа ключа: {family} · связанных строк: {compareCandidates.Count:N0}.");

        if (string.IsNullOrWhiteSpace(entry.Original))
        {
            parts.Add("Original отсутствует — структурный QA недоступен.");
        }
        else
        {
            parts.Add($"Строк с тем же Original: {sameSource.Count:N0}.");
            if (variants.Count > 1)
            {
                parts.Add($"Вариантов перевода в быстрой выборке: {variants.Count}. Требуется проверка согласованности.");
                parts.Add("Переводы: " + string.Join(" | ", variants));
            }
        }

        if (entry.HasValidationIssues && !string.IsNullOrWhiteSpace(entry.ValidationSummary))
            parts.Add("QA: " + entry.ValidationSummary);
        else if (!entry.HasValidationIssues)
            parts.Add("QA: ошибок не найдено.");

        ContextQaText.Text = string.Join("\n\n", parts);
        UpdateRelatedEntries(entry, index, groups, compareCandidates);
    }

    private void UpdateRelatedEntries(
        LocalizationEntry? current,
        EntryRelationIndex? index,
        IReadOnlyList<RelatedEntryGroup> groups,
        IReadOnlyList<LocalizationEntry> compareCandidates)
    {
        if (_relatedEntriesPanel is null || _relatedEntriesHeading is null)
            return;

        _relatedEntriesPanel.Children.Clear();
        var relatedCount = groups.Sum(x => x.Items.Count(y => !y.IsCurrent));
        _relatedEntriesHeading.Text = relatedCount == 0
            ? "Связанные строки"
            : $"Связанные строки · {relatedCount:N0}";

        if (current is null || index is null)
            return;

        if (relatedCount == 0)
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

        foreach (var group in groups)
            _relatedEntriesPanel.Children.Add(CreateRelatedGroupCard(current, group, index, compareCandidates));
    }

    private UIElement CreateRelatedGroupCard(
        LocalizationEntry current,
        RelatedEntryGroup group,
        EntryRelationIndex index,
        IReadOnlyList<LocalizationEntry> compareCandidates)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(224, 230, 239)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var root = new StackPanel();
        var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = group.Label,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 35, 60)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = group.Label
        });
        var count = new TextBlock
        {
            Text = group.Items.Count.ToString("N0"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 154)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(count, 1);
        header.Children.Add(count);
        root.Children.Add(header);

        for (var i = 0; i < group.Items.Count; i++)
        {
            var item = group.Items[i];
            root.Children.Add(CreateRelatedEntryRow(
                current,
                item,
                i == group.Items.Count - 1,
                index,
                compareCandidates));
        }

        card.Child = root;
        return card;
    }

    private UIElement CreateRelatedEntryRow(
        LocalizationEntry current,
        RelatedEntryItem item,
        bool isLast,
        EntryRelationIndex index,
        IReadOnlyList<LocalizationEntry> compareCandidates)
    {
        var row = new Border
        {
            Background = item.IsCurrent
                ? new SolidColorBrush(Color.FromRgb(239, 245, 255))
                : new SolidColorBrush(Color.FromRgb(248, 250, 253)),
            BorderBrush = item.IsCurrent
                ? new SolidColorBrush(Color.FromRgb(181, 207, 252))
                : new SolidColorBrush(Color.FromRgb(235, 239, 245)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, isLast ? 0 : 6)
        };

        var stack = new StackPanel();
        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        title.Children.Add(new TextBlock
        {
            Text = isLast ? "└" : "├",
            Foreground = new SolidColorBrush(Color.FromRgb(139, 151, 171)),
            Margin = new Thickness(0, 0, 5, 0)
        });
        var role = new TextBlock
        {
            Text = item.Role,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = item.Entry.Key
        };
        Grid.SetColumn(role, 1);
        title.Children.Add(role);
        if (item.IsCurrent)
        {
            var currentBadge = new TextBlock
            {
                Text = "A",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(47, 112, 245)),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(currentBadge, 2);
            title.Children.Add(currentBadge);
        }
        stack.Children.Add(title);

        stack.Children.Add(new TextBlock
        {
            Text = item.Entry.OriginalDisplay,
            Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 154)),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(18, 4, 0, 0),
            ToolTip = item.Entry.OriginalDisplay
        });

        var translationBox = new TextBox
        {
            Text = item.Entry.Translation ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MinHeight = 44,
            MaxHeight = 90,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 225, 236)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Padding = new Thickness(6),
            Margin = new Thickness(18, 6, 0, 0),
            IsReadOnly = item.IsCurrent,
            ToolTip = item.IsCurrent
                ? "Текущая строка редактируется в основном поле перевода."
                : "Редактирование связанной строки без перехода. Ctrl+Enter — применить, Esc — отменить."
        };

        if (!item.IsCurrent)
        {
            translationBox.Tag = new RelatedEditState(item.Entry, item.Entry.Translation ?? string.Empty);
            translationBox.LostKeyboardFocus += RelatedTranslation_LostKeyboardFocus;
            translationBox.PreviewKeyDown += RelatedTranslation_PreviewKeyDown;
        }
        stack.Children.Add(translationBox);

        if (!item.IsCurrent)
        {
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(18, 7, 0, 0)
            };
            var compareButton = CreateRelatedButton("Сравнить A / B");
            compareButton.Click += (_, _) =>
            {
                CommitRelatedTranslation(translationBox);
                var dialog = new EntryComparisonWindow(current, item.Entry, compareCandidates, index)
                {
                    Owner = Window.GetWindow(this)
                };
                dialog.ShowDialog();
            };
            buttons.Children.Add(compareButton);

            var openButton = CreateRelatedButton("Открыть");
            openButton.Click += (_, _) =>
            {
                CommitRelatedTranslation(translationBox);
                OpenRelatedEntry(item.Entry);
            };
            buttons.Children.Add(openButton);
            stack.Children.Add(buttons);
        }

        row.Child = stack;
        return row;
    }

    private void RelatedTranslation_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
            CommitRelatedTranslation(box);
    }

    private void RelatedTranslation_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not RelatedEditState state)
            return;

        if (e.Key == Key.Escape)
        {
            box.Text = state.Baseline;
            box.CaretIndex = box.Text.Length;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CommitRelatedTranslation(box);
            e.Handled = true;
        }
    }

    private void CommitRelatedTranslation(TextBox box)
    {
        if (box.Tag is not RelatedEditState state)
            return;

        var after = box.Text ?? string.Empty;
        if (string.Equals(state.Baseline, after, StringComparison.Ordinal))
            return;

        var before = state.Baseline;
        state.Entry.Translation = after;
        state.Baseline = after;
        EntryHistoryService.Record(state.Entry, before, after, "Правка связанной строки");

        if (_viewModel?.ActiveDocument is LocalizationDocument document)
        {
            ProjectHistoryService.Record(
                document.FilePath,
                "Правка связанной строки",
                1,
                state.Entry.Key);
            TranslationMemoryService.Invalidate(document);
        }
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
            Cursor = Cursors.Hand
        };

    private void OpenRelatedEntry(LocalizationEntry entry)
    {
        if (_viewModel is null)
            return;

        if (_viewModel.FilterAllCommand.CanExecute(null))
            _viewModel.FilterAllCommand.Execute(null);
        _viewModel.SearchText = string.Empty;
        _viewModel.SelectedEntry = entry;
        _viewModel.EntriesView.MoveCurrentTo(entry);
        ScrollToSelected();
    }

    private sealed class RelatedEditState(LocalizationEntry entry, string baseline)
    {
        public LocalizationEntry Entry { get; } = entry;
        public string Baseline { get; set; } = baseline;
    }
}
