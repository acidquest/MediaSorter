using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MediaSorter;

public sealed partial class MainWindow
{
    private StackPanel libraryToolbar = null!;
    private double libraryToolbarFullWidth;

    private void ConfigureLibraryToolbar(StackPanel toolbar, ScrollViewer viewport)
    {
        libraryToolbar = toolbar;
        libraryToolbarFullWidth = 0;
        toolbar.Loaded += (_, _) =>
        {
            if (libraryToolbarFullWidth > 0) { UpdateLibraryToolbar(viewport.ActualWidth); return; }
            // Measure the complete labels once; hiding them must not change the breakpoint.
            toolbar.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            libraryToolbarFullWidth = toolbar.DesiredSize.Width;
            UpdateLibraryToolbar(viewport.ActualWidth);
        };
        viewport.SizeChanged += (_, _) => UpdateLibraryToolbar(viewport.ActualWidth);
    }
    private void UpdateLibraryToolbar(double availableWidth)
    {
        if (libraryToolbarFullWidth <= 0) return;
        var compact = availableWidth + .5 < libraryToolbarFullWidth;
        foreach (var button in libraryToolbar.Children.OfType<Button>())
        {
            if (button.Content is not StackPanel content) continue;
            foreach (var label in content.Children.OfType<TextBlock>())
                label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            button.Padding = compact ? new Thickness(9, 8, 9, 8) : new Thickness(12, 8, 12, 8);
        }
    }
}
