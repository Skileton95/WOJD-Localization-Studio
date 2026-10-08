using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public partial class TranslationPage
{
    private bool _flowUiInstalled;
    private StackPanel? _flowRelatedPanel;
    private readonly Dictionary<string, bool> _flowGroupExpansion =
        new(StringComparer.OrdinalIgnoreCase);
    private DispatcherOperation? _flowRefreshOperation;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += TranslationPage_FlowLoaded;
    }

    private void TranslationPage_FlowLoaded(object sender, RoutedEventArgs e)
    {
        if (_flowUiInstalled)
            return;

        ApplyWorkspaceLayoutSettings();
        InstallGroupedEntryList();
        InstallTranslationFlowControls();
        InstallFlowRelatedPanel();

        EntriesGrid.SelectionChanged += FlowSelectionChanged;
        _flowUiInstalled = true;
        ScheduleFlowRefresh();
    }

    private void InstallGroupedEntryList()
    {
        EntriesGrid.LoadingRow += EntriesGrid_LoadingRowForGrouping;
        if (EntriesGrid.Columns.Count == 0 || EntriesGrid.Columns[0] is not DataGridTemplateColumn column)
            return;

        const string template = """
<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
  <StackPanel Margin="6,5">
    <Border Background="#EAF2FF" BorderBrush="#C9DCF9" BorderThickness="0,0,0,1" CornerRadius="5" Padding="7,4" Margin="0,0,0,5">
      <Border.Style>
        <Style TargetType="Border">
          <Setter Property="Visibility" Value="Collapsed"/>
          <Style.Triggers>
            <DataTrigger Binding="{Binding StartsVisualGroup}" Value="True">
              <Setter Property="Visibility" Value="Visible"/>
            </DataTrigger>
          </Style.Triggers>
        </Style>
      </Border.Style>
      <TextBlock Text="{Binding VisualGroupLabel}" FontWeight="SemiBold" Foreground="#2F70D8" FontSize="12"/>
    </Border>
    <Grid>
      <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
      <StackPanel>
        <TextBlock Text="{Binding SemanticRole}" FontSize="11" FontWeight="SemiBold" Foreground="#4C6B9C" TextTrimming="CharacterEllipsis"/>
        <TextBlock Text="{Binding OriginalDisplay}" FontSize="14" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" Margin="0,2,0,0"/>
        <TextBlock Text="{Binding Key}" FontSize="10" Foreground="#8290A7" Margin="0,2,0,0" TextTrimming="CharacterEllipsis"/>
      </StackPanel>
      <TextBlock Grid.Column="1" Text="{Binding QaIndicator}" Margin="10,0,3,0" VerticalAlignment="Center" FontSize="16">
        <TextBlock.Style>
          <Style TargetType="TextBlock">
            <Setter Property="Foreground" Value="#2CBF76"/>
            <Style.Triggers>
              <DataTrigger Binding="{Binding HasStructuralValidationIssues}" Value="True"><Setter Property="Foreground" Value="#E34C55"/></DataTrigger>
              <DataTrigger Binding="{Binding HasValidationIssues}" Value="True"><Setter Property="Foreground" Value="#E29A24"/></DataTrigger>
            </Style.Triggers>
          </Style>
        </TextBlock.Style>
      </TextBlock>
    </Grid>
  </StackPanel>
</DataTemplate>
""";

        column.CellTemplate = (DataTemplate)XamlReader.Parse(template);
    }

    private void EntriesGrid_LoadingRowForGrouping(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is not LocalizationEntry entry || GetRelationIndex() is not { } index)
            return;

        var family = index.GetFamily(entry);
        entry.SetVisualGroupMetadata(
            family,
            EntryRelationIndex.GetSemanticRole(entry.Key, family),
            index.StartsVisualGroup(entry));
    }

    private void InstallTranslationFlowControls()
    {
        TranslationBox.ToolTip =
            "Ctrl+Enter — применить и перейти к следующей строке\n" +
            "Ctrl+Shift+Enter — применить и перейти к следующей непереведённой";
        TranslationBox.PreviewKeyDown += TranslationBox_FlowPreviewKeyDown;

        if (AutoCorrectButton.Parent is not StackPanel actions)
            return;

        var applyNext = CreateRelatedButton("Применить →");
        applyNext.ToolTip = "Ctrl+Enter";
        applyNext.Click += (_, _) => CommitAndNavigate(nextUntranslated: false);
        actions.Children.Add(applyNext);

        var nextUntranslated = CreateRelatedButton("След. без перевода");
        nextUntranslated.ToolTip = "Ctrl+Shift+Enter";
        nextUntranslated.Click += (_, _) => CommitAndNavigate(nextUntranslated: true);
        actions.Children.Add(nextUntranslated);
    }

    private void TranslationBox_FlowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;

        CommitAndNavigate(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        e.Handled = true;
    }

    private void CommitAndNavigate(bool nextUntranslated)
    {
        CommitTranslation();
        CaptureManualHistory();

        if (_viewModel is null)
            return;

        if (nextUntranslated)
        {
            if (_viewModel.NextUntranslatedCommand.CanExecute(null))
                _viewModel.NextUntranslatedCommand.Execute(null);
        }
        else if (_viewModel.ApplyCommand.CanExecute(null))
        {
            _viewModel.ApplyCommand.Execute(null);
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                TranslationBox.Focus();
                Keyboard.Focus(TranslationBox);
                TranslationBox.CaretIndex = TranslationBox.Text.Length;
            }));
    }

    private void InstallFlowRelatedPanel()
    {
        if (_relatedEntriesPanel?.Parent is not Panel parent)
            return;

        _relatedEntriesPanel.Visibility = Visibility.Collapsed;
        _relatedEntriesHeading!.Text = "Связанные строки и одинаковый Original";

        _flowRelatedPanel = new StackPanel();
        var insertIndex = parent.Children.IndexOf(_relatedEntriesPanel) + 1;
        parent.Children.Insert(insertIndex, _flowRelatedPanel);
    }

    private void FlowSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => ScheduleFlowRefresh();

    private void ScheduleFlowRefresh()
    {
        if (_flowRefreshOperation is { Status: DispatcherOperationStatus.Pending })
            _flowRefreshOperation.Abort();

        _flowRefreshOperation = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(RenderFlowContext));
    }

    private void RenderFlowContext()
    {
        if (_flowRelatedPanel is null)
            return;

        _flowRelatedPanel.Children.Clear();
        if (_viewModel?.SelectedEntry is not LocalizationEntry current || GetRelationIndex() is not { } index)
            return;

        var sameOriginal = index.GetSameOriginal(current);
        if (sameOriginal.Count > 1)
            _flowRelatedPanel.Children.Add(CreateSameOriginalCard(current, index, sameOriginal));

        var candidates = index.GetRelated(current, 64);
        var groups = index.GetVisualGroups(current, 32);
        foreach (var group in groups)
            _flowRelatedPanel.Children.Add(CreateFlowGroup(current, group, index, candidates));
    }

    private UIElement CreateSameOriginalCard(
        LocalizationEntry current,
        EntryRelationIndex index,
        IReadOnlyList<LocalizationEntry> sameOriginal)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 250, 238)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(235, 205, 139)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = $"Одинаковый Original · {sameOriginal.Count:N0}",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(104, 79, 28))
        });

        var variants = sameOriginal
            .Select(x => x.Translation)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(6)
            .ToList();
        stack.Children.Add(new TextBlock
        {
            Text = variants.Count <= 1
                ? "Переводы согласованы."
                : "Есть разные варианты: " + string.Join(" | ", variants),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(113, 92, 48)),
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0)
        });

        var plan = TranslationPropagationService.BuildSameOriginalPlan(index, current);
        var apply = CreateRelatedButton($"Применить текущий ко всем · {plan.Changes.Count:N0}");
        apply.IsEnabled = plan.Changes.Count > 0 && !string.IsNullOrWhiteSpace(current.Translation);
        apply.Margin = new Thickness(0, 8, 0, 0);
        apply.ToolTip = "Перед массовой правкой будет создан снимок, а действие попадёт в bulk undo.";
        apply.Click += (_, _) => ApplyTranslationToSameOriginal(current, index);
        stack.Children.Add(apply);

        border.Child = stack;
        return border;
    }

    private void ApplyTranslationToSameOriginal(LocalizationEntry current, EntryRelationIndex index)
    {
        CommitTranslation();
        CaptureManualHistory();
        var document = _viewModel?.ActiveDocument;
        if (document is null || string.IsNullOrWhiteSpace(current.Translation))
            return;

        var plan = TranslationPropagationService.BuildSameOriginalPlan(index, current);
        if (plan.Changes.Count == 0)
            return;

        var answer = AppDialog.Show(
            $"Применить перевод текущей строки к {plan.Changes.Count:N0} строкам с тем же Original?\n\nПеред изменением будет создан снимок файла.",
            "Одинаковый Original",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            Window.GetWindow(this));
        if (answer != MessageBoxResult.Yes)
            return;

        var snapshot = SnapshotService.CreateSnapshot(document, "before-same-original-apply");
        using (SuppressViewModelHistory())
            TranslationPropagationService.Apply(plan);

        BulkUndoService.Push(document.FilePath, "Одинаковый Original", plan.Changes);
        ProjectHistoryService.Record(
            document.FilePath,
            "Применить перевод к одинаковому Original",
            plan.Changes.Count,
            current.Original,
            snapshot.Path);
        TranslationMemoryService.Invalidate(document);
        ScheduleFlowRefresh();
    }

    private UIElement CreateFlowGroup(
        LocalizationEntry current,
        RelatedEntryGroup group,
        EntryRelationIndex index,
        IReadOnlyList<LocalizationEntry> compareCandidates)
    {
        var expander = new Expander
        {
            Margin = new Thickness(0, 0, 0, 8)
        };

        var defaultExpanded = group.Items.Any(x => x.IsCurrent);
        expander.IsExpanded = _flowGroupExpansion.TryGetValue(group.Id, out var saved)
            ? saved
            : defaultExpanded;
        expander.Expanded += (_, _) => _flowGroupExpansion[group.Id] = true;
        expander.Collapsed += (_, _) => _flowGroupExpansion[group.Id] = false;

        var header = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = group.Label,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 35, 60))
        });
        var count = new TextBlock
        {
            Text = group.Items.Count.ToString("N0"),
            Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 154)),
            FontSize = 11,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(count, 1);
        header.Children.Add(count);
        expander.Header = header;

        var body = new StackPanel();
        foreach (var item in group.Items)
            body.Children.Add(CreateFlowRelatedRow(current, item, index, compareCandidates));
        expander.Content = body;

        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(224, 230, 239)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = expander
        };
        return card;
    }

    private UIElement CreateFlowRelatedRow(
        LocalizationEntry current,
        RelatedEntryItem item,
        EntryRelationIndex index,
        IReadOnlyList<LocalizationEntry> compareCandidates)
    {
        var border = new Border
        {
            Background = item.IsCurrent
                ? new SolidColorBrush(Color.FromRgb(239, 245, 255))
                : new SolidColorBrush(Color.FromRgb(248, 250, 253)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(232, 237, 244)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 5, 0, 0)
        };

        var stack = new StackPanel();
        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(new TextBlock
        {
            Text = item.Role,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = item.Entry.Key
        });

        var status = new TextBlock
        {
            Text = item.IsCurrent ? "A · " + GetEntryStatusGlyph(item.Entry) : GetEntryStatusGlyph(item.Entry),
            Foreground = GetEntryStatusBrush(item.Entry),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = item.Entry.QaStateText
        };
        Grid.SetColumn(status, 1);
        title.Children.Add(status);
        stack.Children.Add(title);

        stack.Children.Add(new TextBlock
        {
            Text = item.Relation,
            Foreground = new SolidColorBrush(Color.FromRgb(47, 112, 245)),
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0)
        });
        stack.Children.Add(new TextBlock
        {
            Text = item.Entry.OriginalDisplay,
            Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 154)),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 0),
            ToolTip = item.Entry.OriginalDisplay
        });

        var box = new TextBox
        {
            Text = item.Entry.Translation ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MinHeight = 42,
            MaxHeight = 86,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 225, 236)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Padding = new Thickness(6),
            Margin = new Thickness(0, 6, 0, 0),
            IsReadOnly = item.IsCurrent
        };

        if (!item.IsCurrent)
        {
            box.Tag = new FlowRelatedEditState(item.Entry, item.Entry.Translation ?? string.Empty);
            box.PreviewKeyDown += FlowRelatedBox_PreviewKeyDown;
            box.LostKeyboardFocus += FlowRelatedBox_LostKeyboardFocus;
        }
        stack.Children.Add(box);

        if (!item.IsCurrent)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var compare = CreateRelatedButton("Сравнить A / B");
            compare.Click += (_, _) =>
            {
                CommitFlowRelated(box);
                new EntryComparisonWindow(current, item.Entry, compareCandidates, index)
                {
                    Owner = Window.GetWindow(this)
                }.ShowDialog();
            };
            buttons.Children.Add(compare);

            var open = CreateRelatedButton("Открыть");
            open.Click += (_, _) =>
            {
                CommitFlowRelated(box);
                OpenRelatedEntry(item.Entry);
            };
            buttons.Children.Add(open);
            stack.Children.Add(buttons);
        }

        border.Child = stack;
        return border;
    }

    private void FlowRelatedBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
            CommitFlowRelated(box);
    }

    private void FlowRelatedBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not FlowRelatedEditState state)
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
            CommitFlowRelated(box);
            e.Handled = true;
        }
    }

    private void CommitFlowRelated(TextBox box)
    {
        if (box.Tag is not FlowRelatedEditState state)
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
            ProjectHistoryService.Record(document.FilePath, "Правка связанной строки", 1, state.Entry.Key);
            TranslationMemoryService.Invalidate(document);
        }
    }

    private static string GetEntryStatusGlyph(LocalizationEntry entry)
        => string.IsNullOrWhiteSpace(entry.Translation)
            ? "○"
            : entry.HasStructuralValidationIssues
                ? "✕"
                : entry.HasValidationIssues
                    ? "⚠"
                    : "✓";

    private static Brush GetEntryStatusBrush(LocalizationEntry entry)
        => string.IsNullOrWhiteSpace(entry.Translation)
            ? new SolidColorBrush(Color.FromRgb(139, 151, 171))
            : entry.HasStructuralValidationIssues
                ? new SolidColorBrush(Color.FromRgb(224, 76, 85))
                : entry.HasValidationIssues
                    ? new SolidColorBrush(Color.FromRgb(226, 154, 36))
                    : new SolidColorBrush(Color.FromRgb(45, 157, 91));

    private sealed class FlowRelatedEditState(LocalizationEntry entry, string baseline)
    {
        public LocalizationEntry Entry { get; } = entry;
        public string Baseline { get; set; } = baseline;
    }
}
