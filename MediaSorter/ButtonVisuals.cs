using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MediaSorter;

public sealed partial class MainWindow
{
    private StackPanel libraryToolbar = null!;

    private static IconElement ActionIcon(string key, string glyph)
    {
        // Two branches converge into one arrow; a downward arrow enters a collection tray.
        var data = key switch
        {
            "Merge" => "M1,1 L3,1 L3,5 L8,9 L13,5 L13,1 L15,1 L15,6 L9,11 L9,12 L12,12 L8,16 L4,12 L7,12 L7,11 L1,6 Z",
            "ToMisc" => "M7,0 L9,0 L9,6 L12,6 L8,10 L4,6 L7,6 Z M0,10 L2,10 L2,14 L14,14 L14,10 L16,10 L16,16 L0,16 Z",
            _ => null
        };
        if (data == null) return new FontIcon { Glyph = glyph, FontSize = 16 };
        return (PathIcon)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='16' Height='16' Data='{data}'/>");
    }
    private static void ColorAction(Button button, string key)
    {
        void Apply()
        {
            var dark = button.ActualTheme == ElementTheme.Dark;
            var color = key switch
            {
                "Delete" => dark ? ColorHelper.FromArgb(255, 255, 153, 157) : ColorHelper.FromArgb(255, 169, 38, 52),
                "Merge" or "Paste" or "Save" => dark ? ColorHelper.FromArgb(255, 104, 220, 194) : ColorHelper.FromArgb(255, 0, 110, 91),
                "ToMisc" => dark ? ColorHelper.FromArgb(255, 244, 198, 111) : ColorHelper.FromArgb(255, 137, 87, 10),
                _ => dark ? ColorHelper.FromArgb(255, 174, 185, 255) : ColorHelper.FromArgb(255, 77, 82, 174)
            };
            var primary = key == "Sort";
            var fill = primary ? ColorHelper.FromArgb(255, 89, 79, 190) : ColorHelper.FromArgb(dark ? (byte)28 : (byte)18, color.R, color.G, color.B);
            button.Background = new SolidColorBrush(fill);
            button.Foreground = new SolidColorBrush(primary ? Colors.White : color);
            button.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(55, color.R, color.G, color.B));
            button.CornerRadius = new CornerRadius(8);
            button.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(primary ? ColorHelper.FromArgb(255, 108, 96, 211) : ColorHelper.FromArgb(45, color.R, color.G, color.B));
            button.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(primary ? ColorHelper.FromArgb(255, 73, 64, 161) : ColorHelper.FromArgb(65, color.R, color.G, color.B));
            button.Resources["ButtonForegroundPointerOver"] = button.Foreground;
            button.Resources["ButtonForegroundPressed"] = button.Foreground;
        }
        button.Loaded += (_, _) => Apply(); button.ActualThemeChanged += (_, _) => Apply(); Apply();
    }
    private static void ColorExample(TextBlock text)
    {
        void Apply() => text.Foreground = new SolidColorBrush(text.ActualTheme == ElementTheme.Dark
            ? ColorHelper.FromArgb(255, 104, 220, 194) : ColorHelper.FromArgb(255, 0, 110, 91));
        text.Loaded += (_, _) => Apply(); text.ActualThemeChanged += (_, _) => Apply(); Apply();
    }
}
