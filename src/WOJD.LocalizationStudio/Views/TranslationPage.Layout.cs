using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class TranslationPage
{
    private Grid? _workspaceGrid;
    private FrameworkElement? _contextElement;
    private GridSplitter? _listSplitter;
    private GridSplitter? _contextSplitter;
    private bool _layoutHooked;

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

        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(source))
        {
            parts.Add("Original отсутствует — структурный QA недоступен.");
        }
        else
        {
            parts.Add($"Строк с тем же Original: {sameSource.Count:N0}.");
            if (variants.Count > 1)
                parts.Add($"Вариантов перевода: {variants.Count} — требуется проверка согласованности.");
        }

        if (entry.HasValidationIssues && !string.IsNullOrWhiteSpace(entry.ValidationSummary))
            parts.Add(entry.ValidationSummary);
        else if (!entry.HasValidationIssues)
            parts.Add("QA: ошибок не найдено.");

        ContextQaText.Text = string.Join("\n", parts);
    }
}
