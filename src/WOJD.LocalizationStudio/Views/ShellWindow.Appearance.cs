namespace WOJD.LocalizationStudio.Views;

public partial class ShellWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        ApplyAppearanceSettings();
    }

    public void ApplyAppearanceSettings()
    {
        TranslationPage.ApplySettings();
        TranslationPage.ApplyWorkspaceLayoutSettings();
    }
}
