using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public partial class QaPage
{
    private bool _qaAiUiInstalled;
    private Button? _manualFixButton;
    private Button? _autoFixButton;
    private Button? _aiFixButton;
    private Button? _aiBatchButton;
    private Button? _aiCancelButton;
    private TextBlock? _aiStatusText;
    private CancellationTokenSource? _qaAiCts;
    private bool _qaAiBusy;

    [ModuleInitializer]
    internal static void RegisterQaAiLoadedHandler()
        => EventManager.RegisterClassHandler(
            typeof(QaPage),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((QaPage)sender).InstallQaAiActions()));

    private void InstallQaAiActions()
    {
        if (_qaAiUiInstalled || CategoryList.Parent is not Grid leftGrid)
            return;

        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var separator = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 242)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(2, 8, 2, 0),
            Padding = new Thickness(2, 10, 2, 0)
        };
        Grid.SetRow(separator, 2);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Исправление",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(45, 58, 82)),
            Margin = new Thickness(2, 0, 0, 7)
        });

        _manualFixButton = CreateQaActionButton("Исправить вручную", false);
        _manualFixButton.Click += ManualFix_Click;
        panel.Children.Add(_manualFixButton);

        _autoFixButton = CreateQaActionButton("Автоисправить", false);
        _autoFixButton.Click += AutoFix_Click;
        panel.Children.Add(_autoFixButton);

        _aiFixButton = CreateQaActionButton("✦ ИИ-исправление", true);
        _aiFixButton.Click += AiFix_Click;
        panel.Children.Add(_aiFixButton);

        _aiBatchButton = CreateQaActionButton("✦ ИИ для категории", false);
        _aiBatchButton.Click += AiBatchFix_Click;
        panel.Children.Add(_aiBatchButton);

        _aiCancelButton = CreateQaActionButton("Отменить ИИ", false);
        _aiCancelButton.Visibility = Visibility.Collapsed;
        _aiCancelButton.Click += (_, _) => _qaAiCts?.Cancel();
        panel.Children.Add(_aiCancelButton);

        _aiStatusText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 154)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 5, 2, 0)
        };
        panel.Children.Add(_aiStatusText);

        separator.Child = panel;
        leftGrid.Children.Add(separator);

        IssueGrid.SelectionChanged += (_, _) => UpdateQaAiActionState();
        CategoryList.SelectionChanged += (_, _) => UpdateQaAiActionState();
        AiUiWorkflow.ApplyStoredModel();
        _qaAiUiInstalled = true;
        UpdateQaAiActionState();
    }

    private Button CreateQaActionButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(9, 6, 9, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            Style = TryFindResource(primary ? "ActionButton" : "SecondaryButton") as Style
        };
        return button;
    }

    private void UpdateQaAiActionState()
    {
        if (!_qaAiUiInstalled)
            return;

        var entry = IssueGrid.SelectedItem as LocalizationEntry ?? _selectedIssue;
        var hasEntry = entry is not null;
        _manualFixButton!.IsEnabled = hasEntry && !_qaAiBusy;

        if (entry is not null && !_qaAiBusy)
        {
            var result = TranslationAutoCorrectionService.RuleBased.Correct(entry);
            _autoFixButton!.IsEnabled = result.HasChanges;
            _autoFixButton.Content = result.HasChanges
                ? $"Автоисправить · {result.FixCount}"
                : "Автоисправить";
            _autoFixButton.ToolTip = result.HasChanges
                ? $"Безопасных исправлений: {result.FixCount}"
                : result.RequiresReview
                    ? result.ReviewReason
                    : "Безопасных исправлений не найдено";
        }
        else
        {
            _autoFixButton!.IsEnabled = false;
            _autoFixButton.Content = "Автоисправить";
        }

        _aiFixButton!.IsEnabled = hasEntry &&
                                  !string.IsNullOrWhiteSpace(entry?.Original) &&
                                  !_qaAiBusy;

        var categoryId = CurrentCategoryId();
        var visibleCount = GetVisibleIssues(categoryId).Count;
        _aiBatchButton!.IsEnabled = visibleCount > 0 &&
                                    categoryId != "sourceMissing" &&
                                    !_qaAiBusy;

        _aiCancelButton!.Visibility = _qaAiBusy ? Visibility.Visible : Visibility.Collapsed;
        if (!_qaAiBusy && _aiStatusText is not null)
        {
            _aiStatusText.Text = AiCorrectionService.IsConfigured
                ? $"ИИ: {AiCorrectionService.Model}"
                : "ИИ не настроен — ключ будет запрошен при первом запуске.";
        }
    }

    private void ManualFix_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIssue is null)
            return;

        DetailTranslationBox.Focus();
        System.Windows.Input.Keyboard.Focus(DetailTranslationBox);
        DetailTranslationBox.CaretIndex = DetailTranslationBox.Text.Length;
    }

    private void AutoFix_Click(object sender, RoutedEventArgs e)
    {
        var entry = _selectedIssue;
        if (entry is null)
            return;

        CommitQaTranslation();
        var before = entry.Translation;
        var result = TranslationAutoCorrectionService.RuleBased.Correct(entry);
        if (!result.HasChanges)
        {
            AppDialog.Show(
                result.RequiresReview ? result.ReviewReason : "Безопасных автоисправлений для этой строки нет.",
                "Автоисправление",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                Window.GetWindow(this));
            return;
        }

        ApplyQaProgrammaticChange(entry, before, result.CorrectedText, "QA: автоисправление");
    }

    private async void AiFix_Click(object sender, RoutedEventArgs e)
    {
        var entry = _selectedIssue;
        if (entry is null || string.IsNullOrWhiteSpace(entry.Original))
            return;

        CommitQaTranslation();
        if (!AiUiWorkflow.EnsureConfigured(Window.GetWindow(this)))
        {
            UpdateQaAiActionState();
            return;
        }

        BeginQaAiOperation($"ИИ проверяет {entry.Key}…");
        try
        {
            var result = await AiCorrectionService.ReviewAsync(entry, _qaAiCts!.Token);
            var preview = new AiReviewPreviewWindow(entry, result)
            {
                Owner = Window.GetWindow(this)
            };
            preview.ShowDialog();

            if (result.Changed && preview.ApplySuggestion)
                ApplyQaProgrammaticChange(entry, entry.Translation, result.Translation, "QA: ИИ-исправление");
            else if (_aiStatusText is not null)
                _aiStatusText.Text = result.Changed ? "Предложение ИИ не применено." : "ИИ считает текущий перевод корректным.";
        }
        catch (OperationCanceledException)
        {
            if (_aiStatusText is not null)
                _aiStatusText.Text = "ИИ-операция отменена.";
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                ex.Message,
                "ИИ-исправление",
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                Window.GetWindow(this));
        }
        finally
        {
            EndQaAiOperation();
        }
    }

    private async void AiBatchFix_Click(object sender, RoutedEventArgs e)
    {
        CommitQaTranslation();
        var document = _viewModel?.ActiveDocument;
        if (document is null)
            return;

        if (!AiUiWorkflow.EnsureConfigured(Window.GetWindow(this)))
        {
            UpdateQaAiActionState();
            return;
        }

        var categoryId = CurrentCategoryId();
        var visible = GetVisibleIssues(categoryId);
        var settings = EditorSettingsService.Current;
        AiCorrectionService.Model = settings.AiModel;

        var targets = visible
            .Where(x => !string.IsNullOrWhiteSpace(x.Original))
            .Distinct()
            .Take(settings.AiBatchLimit)
            .ToList();

        if (targets.Count == 0)
        {
            AppDialog.Show(
                "В текущей категории нет строк, которые можно отправить на ИИ-проверку.",
                "ИИ для категории",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                Window.GetWindow(this));
            return;
        }

        var estimate = AiBatchReviewService.Estimate(targets, settings);
        var truncated = visible.Count > targets.Count
            ? $"\nБудет обработано первые {targets.Count:N0} из {visible.Count:N0} строк по установленному лимиту."
            : string.Empty;
        var answer = AppDialog.Show(
            $"Категория: {(CategoryList.SelectedItem as QaCategory)?.Name ?? categoryId}\n" +
            $"Строк к ИИ-проверке: {targets.Count:N0}{truncated}\n" +
            $"Вход: ≈ {estimate.ApproxInputTokens:N0} токенов\n" +
            $"Выход: ≈ {estimate.ApproxOutputTokens:N0} токенов\n" +
            $"Оценка: {estimate.CostText}\n\n" +
            "Запустить? Предложения не будут применены без предпросмотра.",
            "Пакетная ИИ-проверка",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            Window.GetWindow(this));
        if (answer != MessageBoxResult.Yes)
            return;

        BeginQaAiOperation($"ИИ: 0 / {targets.Count:N0}");
        try
        {
            var progress = new Progress<BackgroundOperationProgress>(p =>
            {
                if (_aiStatusText is not null)
                    _aiStatusText.Text = $"ИИ: {p.Current:N0} / {p.Total:N0}";
            });

            var results = await AiBatchReviewService.ReviewAsync(
                targets,
                settings,
                progress,
                _qaAiCts!.Token);

            var suggestions = results
                .Where(x => x.Success && x.Review is { Changed: true })
                .Select(x => new AiBatchSuggestion
                {
                    Entry = x.Entry,
                    Before = x.Entry.Translation,
                    After = x.Review!.Translation,
                    Reason = x.Review.Reason
                })
                .ToList();
            var errors = results.Count(x => !x.Success);

            if (suggestions.Count == 0)
            {
                AppDialog.Show(
                    $"ИИ не предложил изменений. Ошибок запросов: {errors:N0}.",
                    "ИИ для категории",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    Window.GetWindow(this));
                return;
            }

            var preview = new AiBatchPreviewWindow(suggestions)
            {
                Owner = Window.GetWindow(this)
            };
            if (preview.ShowDialog() != true)
                return;

            var selected = preview.SelectedSuggestions.ToList();
            if (selected.Count == 0)
                return;

            var snapshot = SnapshotService.CreateSnapshot(document, "before-ai-qa-batch");
            var changes = selected
                .Select(x => new BulkTextChange(x.Entry, x.Before, x.After))
                .ToList();

            using (SuppressQaViewModelHistory())
            {
                foreach (var suggestion in selected)
                    suggestion.Entry.Translation = suggestion.After;
            }

            BulkUndoService.Push(document.FilePath, "QA: пакетное ИИ-исправление", changes);
            ProjectHistoryService.Record(
                document.FilePath,
                "QA: пакетное ИИ-исправление",
                selected.Count,
                $"Категория: {(CategoryList.SelectedItem as QaCategory)?.Name ?? categoryId}; ошибок API: {errors:N0}",
                snapshot.Path);
            TranslationMemoryService.Invalidate(document);

            if (_qaIndex is not null)
            {
                var relations = EntryRelationIndex.For(document);
                foreach (var suggestion in selected)
                    _qaIndex.UpdateAfterTranslation(suggestion.Entry, relations);
            }

            UpdateCategories(categoryId);
            RefreshIssues();
            if (_aiStatusText is not null)
                _aiStatusText.Text = $"Применено ИИ-исправлений: {selected.Count:N0}.";
        }
        catch (OperationCanceledException)
        {
            if (_aiStatusText is not null)
                _aiStatusText.Text = "Пакетная ИИ-проверка отменена.";
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                ex.Message,
                "Пакетная ИИ-проверка",
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                Window.GetWindow(this));
        }
        finally
        {
            EndQaAiOperation();
        }
    }

    private void ApplyQaProgrammaticChange(
        LocalizationEntry entry,
        string before,
        string after,
        string reason)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
            return;

        var preferredIndex = Math.Max(0, IssueGrid.SelectedIndex);
        entry.Translation = after;
        EntryHistoryService.Record(entry, before, after, reason);

        var document = _viewModel?.ActiveDocument;
        if (document is not null)
        {
            ProjectHistoryService.Record(document.FilePath, reason, 1, entry.Key);
            TranslationMemoryService.Invalidate(document);
            if (_qaIndex is not null)
                _qaIndex.UpdateAfterTranslation(entry, EntryRelationIndex.For(document));
        }

        var categoryId = CurrentCategoryId();
        UpdateCategories(categoryId);
        RefreshIssues(preferredIndex);
        UpdateQaAiActionState();
    }

    private void BeginQaAiOperation(string text)
    {
        _qaAiCts?.Cancel();
        _qaAiCts?.Dispose();
        _qaAiCts = new CancellationTokenSource();
        _qaAiBusy = true;
        if (_aiStatusText is not null)
            _aiStatusText.Text = text;
        UpdateQaAiActionState();
    }

    private void EndQaAiOperation()
    {
        _qaAiBusy = false;
        _qaAiCts?.Dispose();
        _qaAiCts = null;
        UpdateQaAiActionState();
    }

    private IDisposable SuppressQaViewModelHistory()
    {
        if (_viewModel is null)
            return QaEmptyScope.Instance;

        var field = typeof(ViewModels.MainViewModel).GetField(
            "_activeSession",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var session = field?.GetValue(_viewModel) as DocumentSession;
        if (session is null)
            return QaEmptyScope.Instance;

        session.HistoryChangeInProgress = true;
        return new QaActionScope(() => session.HistoryChangeInProgress = false);
    }

    private sealed class QaActionScope(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;
        public void Dispose() => Interlocked.Exchange(ref _onDispose, null)?.Invoke();
    }

    private sealed class QaEmptyScope : IDisposable
    {
        public static readonly QaEmptyScope Instance = new();
        public void Dispose() { }
    }
}
