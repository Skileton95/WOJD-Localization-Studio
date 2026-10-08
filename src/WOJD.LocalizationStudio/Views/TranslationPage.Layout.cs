using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public partial class TranslationPage
{
    private Grid? _workspaceGrid;
    private FrameworkElement? _contextElement;
    private GridSplitter? _listSplitter;
    private GridSplitter? _contextSplitter;
    private bool _layoutHooked;
    private bool _quickLayoutMenuInstalled;

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
        UpdateContextSummary();
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
            EntriesGrid.SelectionChanged += LayoutSelectionChanged;
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
            heading.Text = "Контекст строки";
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
            Background = System.Windows.Media.Brushes.Transparent,
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
        => UpdateContextSummary();

    private void UpdateContextSummary()
    {
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry ||
            _viewModel.ActiveDocument is not LocalizationDocument document)
        {
            ContextQaText.Text = "Выберите строку, чтобы увидеть контекст.";
            return;
        }

        var source = entry.Original ?? string.Empty;
        var sameSource = string.IsNullOrEmpty(source)
            ? []
            : document.Entries
                .Where(x => string.Equals(x.Original, source, StringComparison.Ordinal))
                .ToList();
        var variants = sameSource
            .Select(x => x.Translation)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(6)
            .ToList();
        var relatedKeys = sameSource
            .Where(x => !ReferenceEquals(x, entry))
            .Select(x => string.IsNullOrWhiteSpace(x.Namespace) ? x.Key : $"{x.Namespace} / {x.Key}")
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToList();

        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(source))
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
            if (relatedKeys.Count > 0)
                parts.Add("Связанные ключи:\n• " + string.Join("\n• ", relatedKeys));
        }

        if (entry.HasValidationIssues && !string.IsNullOrWhiteSpace(entry.ValidationSummary))
            parts.Add("QA: " + entry.ValidationSummary);
        else if (!entry.HasValidationIssues)
            parts.Add("QA: ошибок не найдено.");

        ContextQaText.Text = string.Join("\n\n", parts);
    }
}
