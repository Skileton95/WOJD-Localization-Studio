using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio;

internal static class TokenPreviewEnhancement
{
    private static readonly ConditionalWeakTable<MainWindow, object> Installed = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || Installed.TryGetValue(window, out _))
            return;

        if (window.FindName("TranslationBox") is not TextBox box)
            return;

        Installed.Add(window, new object());

        window.Dispatcher.BeginInvoke(() =>
        {
            var layer = AdornerLayer.GetAdornerLayer(box);
            if (layer is null)
                return;

            var adorner = new TokenRibbonAdorner(box);
            layer.Add(adorner);
            box.TextChanged += (_, _) => adorner.InvalidateVisual();
            box.SizeChanged += (_, _) => adorner.InvalidateVisual();
            window.Closed += (_, _) => layer.Remove(adorner);
        });
    }

    private sealed class TokenRibbonAdorner : Adorner
    {
        private readonly TextBox _box;

        public TokenRibbonAdorner(TextBox adornedElement)
            : base(adornedElement)
        {
            _box = adornedElement;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var tokens = TokenHighlightService.Parse(_box.Text)
                .Where(x => x.Kind != HighlightTokenKind.Text)
                .Take(8)
                .ToList();

            if (tokens.Count == 0 || ActualWidth < 220 || ActualHeight < 60)
                return;

            var dpi = VisualTreeHelper.GetDpi(_box).PixelsPerDip;
            var typeface = new Typeface(
                new FontFamily("Consolas"),
                FontStyles.Normal,
                FontWeights.SemiBold,
                FontStretches.Normal);

            var rendered = new List<(FormattedText Text, Brush Foreground, Brush Background)>();
            var totalWidth = 8.0;

            foreach (var token in tokens)
            {
                var display = token.Text.Length > 24
                    ? token.Text[..21] + "…"
                    : token.Text;

                var text = new FormattedText(
                    display,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    10.5,
                    Brushes.Black,
                    dpi);

                var (foreground, background) = token.Kind switch
                {
                    HighlightTokenKind.Tag =>
                        (new SolidColorBrush(Color.FromRgb(109, 40, 217)),
                         new SolidColorBrush(Color.FromArgb(226, 243, 232, 255))),
                    HighlightTokenKind.Placeholder =>
                        (new SolidColorBrush(Color.FromRgb(180, 83, 9)),
                         new SolidColorBrush(Color.FromArgb(226, 255, 247, 237))),
                    _ =>
                        (new SolidColorBrush(Color.FromRgb(8, 145, 178)),
                         new SolidColorBrush(Color.FromArgb(226, 236, 254, 255)))
                };

                rendered.Add((text, foreground, background));
                totalWidth += text.Width + 14;
            }

            totalWidth = Math.Min(totalWidth, ActualWidth * 0.65);
            var height = 23.0;
            var x = Math.Max(4, ActualWidth - totalWidth - 6);
            var y = 5.0;

            drawingContext.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                new Pen(new SolidColorBrush(Color.FromArgb(90, 148, 163, 184)), 1),
                new Rect(x, y, totalWidth, height),
                6,
                6);

            var cursor = x + 5;
            var maxX = x + totalWidth - 5;

            foreach (var item in rendered)
            {
                var width = item.Text.Width + 10;
                if (cursor + width > maxX)
                    break;

                drawingContext.DrawRoundedRectangle(
                    item.Background,
                    null,
                    new Rect(cursor, y + 3, width, height - 6),
                    4,
                    4);

                item.Text.SetForegroundBrush(item.Foreground);
                drawingContext.DrawText(
                    item.Text,
                    new Point(cursor + 5, y + 4));

                cursor += width + 3;
            }
        }
    }
}
