using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.Controls;
public sealed class TokenTextBox : TextBox
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(string), typeof(TokenTextBox),
        new PropertyMetadata("", (d, _) => ((TokenTextBox)d)._adorner?.InvalidateVisual()));
    public string Source { get => (string)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    private TokenAdorner? _adorner;
    private AdornerLayer? _layer;
    public TokenTextBox()
    {
        Loaded += (_, _) => { _layer = AdornerLayer.GetAdornerLayer(this); if (_layer is not null && _adorner is null) { _adorner = new(this); _layer.Add(_adorner); } };
        Unloaded += (_, _) => { if (_adorner is not null) _layer?.Remove(_adorner); _adorner = null; _layer = null; };
        TextChanged += (_, _) => _adorner?.InvalidateVisual(); SizeChanged += (_, _) => _adorner?.InvalidateVisual();
        AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => _adorner?.InvalidateVisual()));
    }
    private sealed class TokenAdorner(TokenTextBox editor) : Adorner(editor)
    {
        protected override void OnRender(DrawingContext context)
        {
            IsHitTestVisible = false;
            context.PushClip(new RectangleGeometry(new Rect(editor.RenderSize)));
            foreach (var token in TokenSyntaxService.Analyze(editor.Source, editor.Text))
                for (var i = token.Start; i < token.Start + token.Length; i++)
                {
                    var start = editor.GetRectFromCharacterIndex(i); var end = editor.GetRectFromCharacterIndex(i, true);
                    if (start.IsEmpty || end.IsEmpty) continue;
                    var rectangle = new Rect(start.Left, start.Top, Math.Max(3, end.Right - start.Left), start.Height);
                    var color = token.Mismatch ? Color.FromArgb(65, 220, 70, 70) : Color.FromArgb(45, 47, 125, 244);
                    context.DrawRectangle(new SolidColorBrush(color), null, rectangle);
                }
            context.Pop();
        }
    }
}