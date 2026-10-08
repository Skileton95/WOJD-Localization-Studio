using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public partial class TranslationPage
{
    private bool _translationAiInstalled;
    private Button? _translationAiButton;
    private CancellationTokenSource? _translationAiCts;

    [ModuleInitializer]
    internal static void RegisterTranslationAiLoadedHandler()
        => EventManager.RegisterClassHandler(
            typeof(TranslationPage),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((TranslationPage)sender).InstallTranslationAiUi()));

    private void InstallTranslationAiUi()
    {
        if (_translationAiInstalled || AutoCorrectButton.Parent is not Panel actions)
            return;

        _translationAiButton = new Button
        {
            Content = "✦ ИИ-исправление",
            Style = TryFindResource("MiniButton") as Style,
            ToolTip = "Проверить текущий перевод ИИ. Предложение сначала показывается в предпросмотре."
        };
        _translationAiButton.Click += TranslationAi_Click;
        actions.Children.Add(_translationAiButton);

        if (MoreButton.ContextMenu is ContextMenu menu)
        {
            var item = new MenuItem { Header = "✦ ИИ-исправление…" };
            item.Click += TranslationAi_Click;
            menu.Items.Insert(0, item);
            menu.Items.Insert(1, new Separator());
        }

        AiUiWorkflow.ApplyStoredModel();
        _translationAiInstalled = true;
        RefreshTranslationAiState();
        EntriesGrid.SelectionChanged += (_, _) => RefreshTranslationAiState();
    }

    private void RefreshTranslationAiState()
    {
        if (_translationAiButton is null)
            return;

        var entry = _viewModel?.SelectedEntry;
        _translationAiButton.IsEnabled = entry is not null &&
                                         !string.IsNullOrWhiteSpace(entry.Original) &&
                                         _translationAiCts is null;
        _translationAiButton.ToolTip = entry is null
            ? "Выберите строку."
            : string.IsNullOrWhiteSpace(entry.Original)
                ? "ИИ-исправление недоступно без Original."
                : AiCorrectionService.IsConfigured
                    ? $"ИИ-проверка через {AiCorrectionService.Model}. Предложение сначала показывается в предпросмотре."
                    : "ИИ будет настроен при первом запуске. API key не записывается в проект.";
    }

    private async void TranslationAi_Click(object sender, RoutedEventArgs e)
    {
        CommitTranslation();
        CaptureManualHistory();
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry ||
            string.IsNullOrWhiteSpace(entry.Original))
        {
            return;
        }

        if (!AiUiWorkflow.EnsureConfigured(Window.GetWindow(this)))
        {
            RefreshTranslationAiState();
            return;
        }

        _translationAiCts?.Cancel();
        _translationAiCts?.Dispose();
        _translationAiCts = new CancellationTokenSource();
        if (_translationAiButton is not null)
        {
            _translationAiButton.Content = "✦ ИИ проверяет…";
            _translationAiButton.IsEnabled = false;
        }

        try
        {
            var result = await AiCorrectionService.ReviewAsync(entry, _translationAiCts.Token);
            var preview = new AiReviewPreviewWindow(entry, result)
            {
                Owner = Window.GetWindow(this)
            };
            preview.ShowDialog();

            if (!result.Changed || !preview.ApplySuggestion)
                return;

            var before = entry.Translation;
            entry.Translation = result.Translation;
            TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            RecordProgrammaticChange(entry, before, result.Translation, "ИИ-исправление");
            RefreshDiagnostics();
        }
        catch (OperationCanceledException)
        {
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
            _translationAiCts?.Dispose();
            _translationAiCts = null;
            if (_translationAiButton is not null)
                _translationAiButton.Content = "✦ ИИ-исправление";
            RefreshTranslationAiState();
        }
    }
}
