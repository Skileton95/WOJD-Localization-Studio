using System.Windows.Media;

namespace WOJD.LocalizationStudio.Views;

public partial class TranslationPage
{
    private bool _legacyRelatedPanelDetached;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (_legacyRelatedPanelDetached || !_flowUiInstalled || _flowRelatedPanel is null || _relatedEntriesPanel is null)
            return;

        _relatedEntriesPanel.Children.Clear();
        _relatedEntriesPanel = null;
        _legacyRelatedPanelDetached = true;
    }
}
