using System.Windows;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

internal static class AiUiWorkflow
{
    public static void ApplyStoredModel()
    {
        var settings = EditorSettingsService.Current;
        AiCorrectionService.Model = settings.AiModel;
    }

    public static bool EnsureConfigured(Window? owner)
    {
        ApplyStoredModel();
        if (AiCorrectionService.IsConfigured)
            return true;

        var dialog = new AiSettingsWindow
        {
            Owner = owner
        };

        if (dialog.ShowDialog() != true)
            return false;

        AiCorrectionService.ConfigureSession(dialog.ApiKey, dialog.Model);
        var settings = EditorSettingsService.Current;
        settings.AiModel = AiCorrectionService.Model;
        EditorSettingsService.Save(settings);
        return true;
    }
}
