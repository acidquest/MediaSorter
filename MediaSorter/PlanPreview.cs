using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Sorter.Core;

namespace MediaSorter;

public sealed record PlanFileRow(string Name, string Source, string Date, string Notice, string Glyph)
{
    public Visibility NoticeVisibility => string.IsNullOrEmpty(Notice) ? Visibility.Collapsed : Visibility.Visible;
}
public sealed record PlanFolderRow(string Name, string Summary, string Glyph, List<PlanFileRow> Files);

public sealed partial class MainWindow
{
    private List<PlanFolderRow> PlanGroups(IReadOnlyList<PlanItem> plan)
    {
        PlanFileRow Row(PlanItem item)
        {
            var original = Path.GetFileName(item.Source);
            var renamed = item.Error == null && original != Path.GetFileName(item.Destination);
            var notice = item.Error != null ? T(item.Error) : renamed ? T("PlanRenamed") : item.DateSource is "Modified" or "Created" ? T("PlanFallback") : "";
            return new(renamed ? original + " → " + Path.GetFileName(item.Destination) : original,
                T("PlanFrom") + ": " + item.Source,
                item.Error != null ? "" : $"{item.Date:yyyy-MM-dd HH:mm} · {T(item.DateSource)}", notice,
                item.Error != null ? "\uE7BA" : MediaFiles.Kind(item.Source) == MediaKind.Video ? "\uE714" : "\uEB9F");
        }
        var groups = plan.Where(x => x.Error == null).GroupBy(x => Path.GetDirectoryName(x.Destination)!, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new PlanFolderRow(
                (string.IsNullOrWhiteSpace(outputRoot) ? group.Key : Path.GetRelativePath(outputRoot, group.Key)).Replace("\\", "  ›  "),
                $"{group.Count():N0} {T("Files")} · {FormatBytes(group.Sum(x => x.Length))}", "\uE8B7", group.OrderBy(x => Path.GetFileName(x.Destination), StringComparer.CurrentCultureIgnoreCase).Select(Row).ToList())).ToList();
        var errors = plan.Where(x => x.Error != null).Select(Row).ToList();
        if (errors.Count > 0) groups.Add(new(T("PlanBlocked"), $"{errors.Count:N0} {T("Errors")}", "\uE7BA", errors));
        return groups;
    }
    private static string FormatBytes(long bytes) => bytes >= 1073741824 ? $"{bytes / 1073741824d:0.##} GB" : bytes >= 1048576 ? $"{bytes / 1048576d:0.##} MB" : $"{bytes / 1024d:0.##} KB";
    private Grid BuildPlanPreview(List<PlanItem> plan)
    {
        var groups = PlanGroups(plan);
        var panel = new Grid { Width = Math.Max(300, Math.Min(740, root.ActualWidth - 100)), Height = Math.Max(240, Math.Min(520, root.ActualHeight - 200)), RowSpacing = 14 };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new());
        var metrics = new Grid { ColumnSpacing = 10 };
        var valid = plan.Where(x => x.Error == null).ToList();
        var values = new[] { (T("PlanFiles"), valid.Count.ToString("N0")), (T("PlanFolders"), valid.Select(x => Path.GetDirectoryName(x.Destination)).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0")), (T("PlanSize"), FormatBytes(valid.Sum(x => x.Length))) };
        for (var i = 0; i < values.Length; i++)
        {
            metrics.ColumnDefinitions.Add(new());
            var stack = new StackPanel { Spacing = 4 }; stack.Children.Add(Text(values[i].Item2, 23)); stack.Children.Add(Text(values[i].Item1, 12, true));
            var card = new Border { Child = stack, Padding = new Thickness(12), CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(18, 120, 110, 220)) };
            Grid.SetColumn(card, i); metrics.Children.Add(card);
        }
        panel.Children.Add(metrics);
        var destination = Text(T("PlanDestination") + "\n" + outputRoot, 12, true); destination.IsTextSelectionEnabled = true; Grid.SetRow(destination, 1); panel.Children.Add(destination);
        var browser = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        browser.ColumnDefinitions.Add(new() { Width = new GridLength(.38, GridUnitType.Star) }); browser.ColumnDefinitions.Add(new() { Width = new GridLength(.62, GridUnitType.Star) });
        browser.RowDefinitions.Add(new() { Height = GridLength.Auto }); browser.RowDefinitions.Add(new());
        browser.Children.Add(Text(T("PlanFolderList"), 12, true)); var heading = Text(T("PlanFileList"), 12, true); Grid.SetColumn(heading, 1); browser.Children.Add(heading);
        var folders = new ListView { ItemsSource = groups, SelectionMode = ListViewSelectionMode.Single, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        folders.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Padding='2,8' ColumnSpacing='8'><Grid.ColumnDefinitions><ColumnDefinition Width='22'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><FontIcon Glyph='{Binding Glyph}' Foreground='#8B75FF' FontSize='18'/><StackPanel Grid.Column='1' Spacing='5'><TextBlock Text='{Binding Name}' TextWrapping='Wrap' FontSize='14'/><TextBlock Text='{Binding Summary}' FontSize='11' Opacity='.65'/></StackPanel></Grid></DataTemplate>");
        var files = new ListView { SelectionMode = ListViewSelectionMode.None, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        files.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Padding='2,9' ColumnSpacing='8'><Grid.ColumnDefinitions><ColumnDefinition Width='20'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><FontIcon Glyph='{Binding Glyph}' FontSize='16' Opacity='.6' VerticalAlignment='Top' Margin='0,3,0,0'/><StackPanel Grid.Column='1' Spacing='5'><TextBlock Text='{Binding Name}' FontSize='14' TextWrapping='Wrap' IsTextSelectionEnabled='True'/><TextBlock Text='{Binding Date}' FontSize='11' Opacity='.7' TextWrapping='Wrap'/><TextBlock Text='{Binding Source}' FontSize='11' Opacity='.6' TextTrimming='CharacterEllipsis' ToolTipService.ToolTip='{Binding Source}'/><TextBlock Text='{Binding Notice}' Visibility='{Binding NoticeVisibility}' Foreground='#C58A22' FontSize='12' TextWrapping='Wrap'/></StackPanel></Grid></DataTemplate>");
        folders.SelectionChanged += (_, _) => { if (folders.SelectedItem is PlanFolderRow folder) files.ItemsSource = folder.Files; };
        Grid.SetRow(folders, 1); browser.Children.Add(folders); Grid.SetRow(files, 1); Grid.SetColumn(files, 1); browser.Children.Add(files);
        if (groups.Count > 0) folders.SelectedIndex = 0;
        Grid.SetRow(browser, 2); panel.Children.Add(browser);
        return panel;
    }
}
