using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Sorter.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.System;
using Microsoft.VisualBasic.FileIO;
using FileAttributes = System.IO.FileAttributes;

namespace MediaSorter;

public sealed class FileEntry
{
    public string Path { get; init; } = "";
    public string Name => System.IO.Path.GetFileName(Path);
    public bool IsDirectory { get; init; }
    public string Glyph => IsDirectory ? "\uE8B7" : MediaFiles.Kind(Path) == MediaKind.Video ? "\uE714" : "\uEB9F";
    public string Detail { get; init; } = "";
}

public sealed partial class MainWindow : Window
{
    private readonly Preferences preferences = Preferences.Load();
    private readonly Localizer loc = new();
    private Grid root = null!, workspace = null!;
    private Grid leftColumn = null!, rightColumn = null!;
    private ResizeThumb centerGrip = null!;
    private TextBlock status = null!, count = null!, example = null!, info = null!;
    private ProgressBar progress = null!;
    private ListView sourceList = null!, outputList = null!;
    private readonly ObservableCollection<FileEntry> outputItems = [];
    private readonly ObservableCollection<FileEntry> sourceItems = [];
    private TextBlock outputStatistics = null!;
    private TextBox sourceBox = null!, outputBox = null!;
    private ComboBox pattern = null!, filter = null!;
    private CheckBox recursive = null!;
    private Border previewHost = null!;
    private Button cancel = null!;
    private readonly List<Control> operationControls = [];
    private readonly Dictionary<string, Border> panels = [];
    private string sourcePath = "", outputRoot = "", outputCurrent = "";
    private string? selectedFile;
    private bool busy;
    private bool closing;
    private CancellationTokenSource? operation, previewCancellation;
    private int sourceVersion, outputVersion;
    private Microsoft.UI.Xaml.Controls.MediaPlayerElement? player;
    private Windows.Media.Playback.MediaPlayer? videoPlayer;
    private MediaSource? videoSource;
    private Windows.Foundation.TypedEventHandler<Windows.Media.Playback.MediaPlayer, Windows.Media.Playback.MediaPlayerFailedEventArgs>? videoFailed;
    private Image? previewImage;
    private readonly SortEngine engine = new(MetadataService.ReadAsync);
    private readonly SolidColorBrush accent = new(ColorHelper.FromArgb(255, 108, 92, 231));
    private string T(string key) => loc[key];

    public MainWindow()
    {
        Title = "MediaSorter";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 940));
        SystemBackdrop = new MicaBackdrop();
        loc.Load(preferences.Language);
        Build();
        AppWindow.Closing += (_, e) => { if (busy) { e.Cancel = true; status.Text = T("BusyClose"); } else { closing = true; StopPreview(); SaveSettings(); } };
        Closed += (_, _) => closing = true;
    }

    private TextBlock Text(string value, double size = 14, bool muted = false) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Opacity = muted ? .65 : 1 };
    private Button Action(string key, Action action, string? glyph = null, bool lockWhileBusy = true)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        if (glyph != null) content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
        content.Children.Add(Text(T(key), 13));
        var button = new Button { Content = content, Padding = new Thickness(12, 8, 12, 8) };
        button.Click += (_, _) => action();
        ToolTipService.SetToolTip(button, T(key));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, T(key));
        if (lockWhileBusy) operationControls.Add(button);
        return button;
    }
    private void SaveSettings()
    {
        try { preferences.Save(); } catch (Exception ex) { status.Text = ex.Message; }
    }
    private void Build()
    {
        StopPreview(); previewItems.Clear(); navigationList = null; operationControls.Clear(); panels.Clear();
        root = new Grid { Padding = new Thickness(22, 12, 22, 12), RowSpacing = 12, RequestedTheme = preferences.Theme switch { "dark" => ElementTheme.Dark, "light" => ElementTheme.Light, _ => ElementTheme.Default } };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new Grid { Margin = new Thickness(2, 0, 2, 3), Padding = new Thickness(0, 0, 150, 0), MinHeight = 48, Background = new SolidColorBrush(Colors.Transparent) };
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.Children.Add(new Image { Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "app.png"))), Width = 44, Height = 44 });
        var brand = new StackPanel { Margin = new Thickness(12, 0, 0, 0) }; brand.Children.Add(Text("MediaSorter", 23)); brand.Children.Add(Text(T("Tagline"), 12, true)); Grid.SetColumn(brand, 1); heading.Children.Add(brand);
        root.Children.Add(heading);
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SetTitleBar(heading);
        workspace = new Grid();
        workspace.ColumnDefinitions.Add(new() { Width = new GridLength(preferences.SplitX, GridUnitType.Star), MinWidth = 120 });
        workspace.ColumnDefinitions.Add(new() { Width = new GridLength(10) });
        workspace.ColumnDefinitions.Add(new() { Width = new GridLength(1 - preferences.SplitX, GridUnitType.Star), MinWidth = 120 });
        leftColumn = PanelColumn(preferences.SplitY); rightColumn = PanelColumn(preferences.SplitYRight);
        workspace.Children.Add(leftColumn); Grid.SetColumn(rightColumn, 2); workspace.Children.Add(rightColumn);
        Grid.SetRow(workspace, 1); root.Children.Add(workspace);
        panels["source"] = MakePanel("Source", "\uE8B5", BuildSource(), "source");
        panels["settings"] = MakePanel("Settings", "\uE713", BuildSettings(), "settings");
        panels["output"] = MakePanel("Output", "\uE8B7", BuildOutput(), "output");
        panels["preview"] = MakePanel("Preview", "\uE714", BuildPreview(), "preview");
        if (preferences.PanelOrder.Length != 4 || preferences.PanelOrder.Distinct().Count() != 4 || preferences.PanelOrder.Any(x => !panels.ContainsKey(x))) preferences.PanelOrder = ["source", "settings", "output", "preview"];
        PlacePanels();
        AddSplitters();
        var bottom = new Grid { ColumnSpacing = 14 };
        bottom.ColumnDefinitions.Add(new()); bottom.ColumnDefinitions.Add(new() { Width = new GridLength(140) }); bottom.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        status = Text(T("Ready"), 12, true); status.VerticalAlignment = VerticalAlignment.Center; bottom.Children.Add(status);
        progress = new ProgressBar { Height = 3, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(progress, 1); bottom.Children.Add(progress);
        cancel = Action("Cancel", () => operation?.Cancel(), "\uE711", false); cancel.Visibility = Visibility.Collapsed; Grid.SetColumn(cancel, 2); bottom.Children.Add(cancel);
        Grid.SetRow(bottom, 2); root.Children.Add(bottom); Content = root;
        root.ActualThemeChanged += (_, _) => ApplyPalette();
        root.Loaded += (_, _) => ApplyPalette(); ApplyPalette();
        _ = RefreshSource(); _ = RefreshOutput();
    }
    private Border MakePanel(string title, string glyph, UIElement content, string key)
    {
        var grid = new Grid { RowSpacing = 10, Padding = new Thickness(15) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new());
        var header = new Grid { CanDrag = true, AllowDrop = true, Background = new SolidColorBrush(Colors.Transparent) };
        header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var caption = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center };
        caption.Children.Add(new FontIcon { Glyph = glyph, FontSize = 17, Foreground = accent }); caption.Children.Add(Text(T(title), 17)); header.Children.Add(caption);
        var move = new Button { Content = new FontIcon { Glyph = "\uE700", FontSize = 14 }, Padding = new Thickness(6), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) };
        var menu = new MenuFlyout();
        foreach (var other in new[] { "source", "settings", "output", "preview" }.Where(x => x != key))
        {
            var item = new MenuFlyoutItem { Text = T(char.ToUpper(other[0]) + other[1..]) };
            item.Click += (_, _) => Swap(key, other); menu.Items.Add(item);
        }
        move.Flyout = menu; ToolTipService.SetToolTip(move, T("Swap")); operationControls.Add(move); Grid.SetColumn(move, 1); header.Children.Add(move);
        header.DragStarting += (_, e) => { if (busy) { e.Cancel = true; return; } e.Data.Properties["Panel"] = key; e.Data.RequestedOperation = DataPackageOperation.Move; };
        header.DragOver += (_, e) => { if (!busy && e.DataView.Properties.ContainsKey("Panel")) e.AcceptedOperation = DataPackageOperation.Move; };
        header.Drop += (_, e) => { if (!busy && e.DataView.Properties.TryGetValue("Panel", out var from) && from is string source) Swap(source, key); };
        grid.Children.Add(header); Grid.SetRow(content as FrameworkElement, 1); grid.Children.Add(content);
        var card = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
        card.Child = grid;
        return card;
    }
    private void ApplyPalette()
    {
        if (closing) return;
        var dark = root.ActualTheme == ElementTheme.Dark;
        foreach (var card in panels.Values)
        {
            card.Background = new SolidColorBrush(dark ? ColorHelper.FromArgb(245, 35, 36, 43) : ColorHelper.FromArgb(245, 255, 255, 255));
            card.BorderBrush = new SolidColorBrush(dark ? ColorHelper.FromArgb(255, 57, 58, 68) : ColorHelper.FromArgb(255, 225, 226, 234));
        }
        var bar = AppWindow?.TitleBar;
        if (bar == null) return;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = dark ? Colors.White : ColorHelper.FromArgb(255, 30, 30, 38);
        bar.ButtonInactiveForegroundColor = dark ? ColorHelper.FromArgb(255, 145, 145, 155) : ColorHelper.FromArgb(255, 120, 120, 135);
        bar.ButtonHoverBackgroundColor = dark ? ColorHelper.FromArgb(255, 65, 65, 78) : ColorHelper.FromArgb(255, 226, 224, 240);
        bar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonPressedBackgroundColor = dark ? ColorHelper.FromArgb(255, 80, 80, 95) : ColorHelper.FromArgb(255, 205, 200, 225);
    }
    private void Swap(string first, string second)
    {
        var a = Array.IndexOf(preferences.PanelOrder, first); var b = Array.IndexOf(preferences.PanelOrder, second);
        if (a < 0 || b < 0) return;
        (preferences.PanelOrder[a], preferences.PanelOrder[b]) = (preferences.PanelOrder[b], preferences.PanelOrder[a]); PlacePanels(); SaveSettings();
    }
    private void PlacePanels()
    {
        foreach (var panel in panels.Values) { leftColumn.Children.Remove(panel); rightColumn.Children.Remove(panel); }
        for (var i = 0; i < 4; i++) { var panel = panels[preferences.PanelOrder[i]]; Grid.SetRow(panel, i / 2 * 2); (i % 2 == 0 ? leftColumn : rightColumn).Children.Add(panel); }
    }
    private Grid PanelColumn(double ratio)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new() { Height = new GridLength(ratio, GridUnitType.Star), MinHeight = 80 });
        grid.RowDefinitions.Add(new() { Height = new GridLength(10) });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1 - ratio, GridUnitType.Star), MinHeight = 80 });
        return grid;
    }
    private void ResizeVertical(double delta)
    {
        if (workspace.ActualWidth <= 250) return;
        var minimum = 120 / (workspace.ActualWidth - 10);
        preferences.SplitX = Math.Clamp(preferences.SplitX + delta / (workspace.ActualWidth - 10), minimum, 1 - minimum);
        workspace.ColumnDefinitions[0].Width = new(preferences.SplitX, GridUnitType.Star); workspace.ColumnDefinitions[2].Width = new(1 - preferences.SplitX, GridUnitType.Star);
    }
    private void ResizeHorizontal(Grid column, double delta, bool right)
    {
        if (column.ActualHeight <= 170) return;
        var minimum = 80 / (column.ActualHeight - 10);
        var ratio = Math.Clamp((right ? preferences.SplitYRight : preferences.SplitY) + delta / (column.ActualHeight - 10), minimum, 1 - minimum);
        if (right) preferences.SplitYRight = ratio; else preferences.SplitY = ratio;
        column.RowDefinitions[0].Height = new(ratio, GridUnitType.Star); column.RowDefinitions[2].Height = new(1 - ratio, GridUnitType.Star);
        PositionGrip();
    }
    private void PositionGrip()
    {
        if (centerGrip != null) centerGrip.Margin = new Thickness(-3, Math.Max(0, (workspace.ActualHeight - 10) * preferences.SplitY - 3), -3, 0);
    }
    private void AddSplitters()
    {
        var vertical = new ResizeThumb(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
        Grid.SetColumn(vertical, 1); workspace.Children.Add(vertical);
        vertical.DragDelta += (_, e) => ResizeVertical(e.HorizontalChange);
        vertical.DragCompleted += (_, _) => SaveSettings();
        foreach (var right in new[] { false, true })
        {
            var column = right ? rightColumn : leftColumn;
            var horizontal = new ResizeThumb(Microsoft.UI.Input.InputSystemCursorShape.SizeNorthSouth);
            Grid.SetRow(horizontal, 1); column.Children.Add(horizontal);
            horizontal.DragDelta += (_, e) => ResizeHorizontal(column, e.VerticalChange, right);
            horizontal.DragCompleted += (_, _) => SaveSettings();
        }
        centerGrip = new ResizeThumb(Microsoft.UI.Input.InputSystemCursorShape.SizeAll) { Height = 16, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(centerGrip, 1); workspace.Children.Add(centerGrip);
        centerGrip.DragDelta += (_, e) => { ResizeVertical(e.HorizontalChange); ResizeHorizontal(leftColumn, e.VerticalChange, false); ResizeHorizontal(rightColumn, e.VerticalChange, true); };
        centerGrip.DragCompleted += (_, _) => SaveSettings(); workspace.SizeChanged += (_, _) => PositionGrip();
    }
    private Grid SectionGrid(int rows)
    {
        var grid = new Grid { RowSpacing = 10 };
        for (var i = 0; i < rows; i++) grid.RowDefinitions.Add(new() { Height = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        return grid;
    }
    private Grid FolderBar(bool source)
    {
        var bar = new Grid { ColumnSpacing = 8 }; bar.ColumnDefinitions.Add(new()); bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var box = new TextBox { PlaceholderText = T(source ? "ChooseSource" : "ChooseOutput"), IsReadOnly = true, FontSize = 12, Text = source ? sourcePath : outputCurrent };
        if (source) sourceBox = box; else outputBox = box;
        bar.Children.Add(box); var browse = Action("Browse", () => _ = PickFolder(source), "\uE838"); Grid.SetColumn(browse, 1); bar.Children.Add(browse); return bar;
    }
    private ListView FileList(bool output)
    {
        var list = new ListView { SelectionMode = ListViewSelectionMode.Extended, IsItemClickEnabled = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Padding='4,7' ColumnSpacing='10'><Grid.ColumnDefinitions><ColumnDefinition Width='28'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><FontIcon Glyph='{Binding Glyph}' FontSize='21' Opacity='.7'/><StackPanel Grid.Column='1' Spacing='3'><TextBlock Text='{Binding Name}' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding Detail}' FontSize='11' Opacity='.55' TextTrimming='CharacterEllipsis'/></StackPanel></Grid></DataTemplate>");
        list.SelectionChanged += (_, _) =>
        {
            if (syncingPreviewSelection) return;
            if (list.SelectedItem is FileEntry entry)
            {
                SetPreviewItems(list.Items.OfType<FileEntry>(), list);
                _ = ShowEntry(entry);
            }
        };
        list.DoubleTapped += (_, _) => { if (list.SelectedItem is FileEntry entry && entry.IsDirectory && output) _ = EnterOutput(entry.Path); };
        if (output)
        {
            var menu = new MenuFlyout();
            foreach (var (key, handler) in new (string, Action)[] { ("Open", () => OpenSelected()), ("Rename", () => _ = Rename()), ("Cut", CutSelected), ("Paste", () => _ = PasteFiles()), ("ToMisc", () => _ = MoveToMisc()), ("Merge", () => _ = Merge()), ("Delete", () => _ = Delete()), ("Reveal", () => RevealSelected()) })
            { var item = new MenuFlyoutItem { Text = T(key) }; item.Click += (_, _) => { if (!busy) handler(); }; menu.Items.Add(item); }
            list.ContextFlyout = menu;
            list.RightTapped += (_, e) =>
            {
                DependencyObject? node = e.OriginalSource as DependencyObject;
                while (node != null && node is not ListViewItem) node = VisualTreeHelper.GetParent(node);
                if (node is ListViewItem container && list.ItemFromContainer(container) is FileEntry entry && !list.SelectedItems.Contains(entry)) { list.SelectedItems.Clear(); list.SelectedItem = entry; }
            };
            list.KeyDown += (_, e) =>
            {
                if (busy) return;
                if (e.Key == VirtualKey.Delete) { e.Handled = true; _ = Delete(); }
                if (e.Key == VirtualKey.F2) { e.Handled = true; _ = Rename(); }
                if (e.Key == VirtualKey.Enter) { e.Handled = true; OpenSelected(); }
                if (e.Key == VirtualKey.F5) { e.Handled = true; _ = RefreshOutput(); }
                if (e.Key == VirtualKey.Back) { e.Handled = true; Up(); }
            };
            var accelerator = new KeyboardAccelerator { Key = VirtualKey.M, Modifiers = VirtualKeyModifiers.Control };
            accelerator.Invoked += (sender, e) => { if (!busy) _ = Merge(); e.Handled = true; }; list.KeyboardAccelerators.Add(accelerator);
            foreach (var (key, action) in new (VirtualKey, Action)[] { (VirtualKey.X, CutSelected), (VirtualKey.V, () => _ = PasteFiles()) })
            {
                var shortcut = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control, ScopeOwner = list };
                shortcut.Invoked += (sender, e) => { if (!busy) action(); e.Handled = true; }; list.KeyboardAccelerators.Add(shortcut);
            }
        }
        if (!output)
        {
            var menu = new MenuFlyout();
            var delete = new MenuFlyoutItem { Text = T("Delete") };
            delete.Click += (_, _) => { if (!busy) _ = Delete(true); }; menu.Items.Add(delete); list.ContextFlyout = menu;
            list.RightTapped += (_, e) =>
            {
                DependencyObject? node = e.OriginalSource as DependencyObject;
                while (node != null && node is not ListViewItem) node = VisualTreeHelper.GetParent(node);
                if (node is ListViewItem container && list.ItemFromContainer(container) is FileEntry entry && !list.SelectedItems.Contains(entry)) { list.SelectedItems.Clear(); list.SelectedItem = entry; }
            };
            list.KeyDown += (_, e) => { if (!busy && e.Key == VirtualKey.Delete) { e.Handled = true; _ = Delete(true); } };
        }
        return list;
    }
    private UIElement BuildSource()
    {
        var grid = SectionGrid(3); grid.Children.Add(FolderBar(true)); sourceList = FileList(false); sourceList.ItemsSource = sourceItems; Grid.SetRow(sourceList, 1); grid.Children.Add(sourceList);
        count = Text(T("ChooseSource"), 12, true); count.VerticalAlignment = VerticalAlignment.Center;
        var footer = new Grid(); footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.Children.Add(count);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        tools.Children.Add(Action("Delete", () => _ = Delete(true), "\uE74D"));
        tools.Children.Add(Action("Refresh", () => { _ = RefreshSource(); _ = RefreshOutput(); }, "\uE72C"));
        Grid.SetColumn(tools, 1); footer.Children.Add(tools);
        Grid.SetRow(footer, 2); grid.Children.Add(footer); return grid;
    }
    private UIElement BuildOutput()
    {
        var grid = SectionGrid(3); grid.Children.Add(FolderBar(false)); outputList = FileList(true); outputList.ItemsSource = outputItems; Grid.SetRow(outputList, 1); grid.Children.Add(outputList);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        tools.Children.Add(Action("Up", Up, "\uE74A")); tools.Children.Add(Action("Rename", () => _ = Rename(), "\uE8AC")); tools.Children.Add(Action("Merge", () => _ = Merge(), "\uE8B5")); tools.Children.Add(Action("Delete", () => _ = Delete(), "\uE74D"));
        tools.Children.Add(Action("Cut", CutSelected, "\uE8C6")); tools.Children.Add(Action("Paste", () => _ = PasteFiles(), "\uE77F")); tools.Children.Add(Action("ToMisc", () => _ = MoveToMisc(), "\uE8C8"));
        var footer = new Grid { ColumnSpacing = 12 };
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new());
        outputStatistics = Text("", 12, true); outputStatistics.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(outputStatistics); UpdateOutputStatistics();
        tools.HorizontalAlignment = HorizontalAlignment.Right;
        var scroll = new ScrollViewer { Content = tools, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Right, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled };
        ConfigureLibraryToolbar(tools, scroll);
        Grid.SetColumn(scroll, 1); footer.Children.Add(scroll); Grid.SetRow(footer, 2); grid.Children.Add(footer); return grid;
    }
    private UIElement BuildSettings()
    {
        var tabs = new Pivot();
        var organize = new StackPanel { Spacing = 10, Margin = new Thickness(2, 10, 2, 4) };
        organize.Children.Add(Text(T("Pattern"), 13, true));
        var patternBar = new Grid { ColumnSpacing = 7 }; patternBar.ColumnDefinitions.Add(new()); patternBar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        pattern = new ComboBox { IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = preferences.Patterns, Text = preferences.Pattern };
        pattern.SelectionChanged += (_, _) => UpdateExample(); pattern.TextSubmitted += (_, _) => UpdateExample(); pattern.LostFocus += (_, _) => UpdateExample(); operationControls.Add(pattern);
        patternBar.Children.Add(pattern); var save = Action("Save", SavePattern, "\uE74E"); Grid.SetColumn(save, 1); patternBar.Children.Add(save); organize.Children.Add(patternBar);
        example = Text("", 14); organize.Children.Add(example); UpdateExample();
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        foreach (var (token, key) in new[] { ("yyyy / yy", "Year"), ("MM / mm", "Month"), ("dd", "Day") })
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            chip.Children.Add(new TextBlock { Text = token, FontFamily = new FontFamily("Cascadia Mono"), FontSize = 12, Foreground = accent });
            chip.Children.Add(Text(T(key), 12, true));
            legend.Children.Add(new Border { Child = chip, Padding = new Thickness(8, 5, 8, 5), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(ColorHelper.FromArgb(15, 120, 110, 220)) });
        }
        organize.Children.Add(legend);
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        filter = new ComboBox { ItemsSource = new[] { T("AllMedia"), T("Photos"), T("Videos") }, SelectedIndex = 0, MinWidth = 155 }; filter.SelectionChanged += (_, _) => { if (sourceList != null) _ = RefreshSource(); }; operationControls.Add(filter);
        recursive = new CheckBox { Content = T("Recursive"), IsChecked = preferences.Recursive }; recursive.Click += (_, _) => { preferences.Recursive = recursive.IsChecked == true; SaveSettings(); _ = RefreshSource(); }; operationControls.Add(recursive);
        options.Children.Add(filter); options.Children.Add(recursive); organize.Children.Add(options);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var sort = Action("Sort", () => _ = Sort(), "\uE768"); sort.Background = accent; sort.Foreground = new SolidColorBrush(Colors.White); actions.Children.Add(sort); organize.Children.Add(actions);
        tabs.Items.Add(new PivotItem { Header = T("SortTab"), Content = new ScrollViewer { Content = organize } });
        info = Text(T("InfoEmpty"), 13); info.IsTextSelectionEnabled = true; info.Margin = new Thickness(4, 12, 4, 4);
        tabs.Items.Add(new PivotItem { Header = T("InfoTab"), Content = new ScrollViewer { Content = info } });
        var prefs = new StackPanel { Spacing = 10, Margin = new Thickness(4, 12, 4, 4) };
        prefs.Children.Add(Text(T("Theme"), 13, true));
        var themes = new ComboBox { ItemsSource = new[] { T("System"), T("Light"), T("Dark") }, SelectedIndex = preferences.Theme == "light" ? 1 : preferences.Theme == "dark" ? 2 : 0 };
        themes.SelectionChanged += (_, _) => { preferences.Theme = new[] { "system", "light", "dark" }[themes.SelectedIndex]; root.RequestedTheme = themes.SelectedIndex switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default }; SaveSettings(); }; prefs.Children.Add(themes);
        prefs.Children.Add(Text(T("Language"), 13, true));
        var codes = new[] { "system" }.Concat(Localizer.Languages()).ToArray();
        var languages = new ComboBox { ItemsSource = codes.Select(x => x == "system" ? T("System") : x == "ru" ? "Русский" : x == "en" ? "English" : x).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(codes, preferences.Language)) }; operationControls.Add(languages);
        languages.SelectionChanged += (_, _) => { if (languages.SelectedIndex < 0) return; preferences.Language = codes[languages.SelectedIndex]; SaveSettings(); loc.Load(preferences.Language); Build(); }; prefs.Children.Add(languages);
        prefs.Children.Add(Text(T("Portable"), 12, true));
        prefs.Children.Add(Action("Journal", () => { System.IO.Directory.CreateDirectory(Path.Combine(Preferences.DataDirectory, "logs")); Launch(Path.Combine(Preferences.DataDirectory, "logs")); }, "\uE8A5", false));
        tabs.Items.Add(new PivotItem { Header = T("AppTab"), Content = new ScrollViewer { Content = prefs } }); return tabs;
    }
    private UIElement BuildPreview()
    {
        previewHost = new Border { CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(ColorHelper.FromArgb(12, 128, 128, 128)), Child = EmptyPreview() };
        previewHost.SizeChanged += (_, _) => FitPreview();
        previewHost.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(PreviewWheelChanged), true);
        var grid = new Grid { RowSpacing = 8 };
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(previewHost);
        var navigation = BuildPreviewNavigation(); Grid.SetRow(navigation, 1); grid.Children.Add(navigation);
        return grid;
    }
    private void FitPreview()
    {
        var width = Math.Max(1, previewHost.ActualWidth); var height = Math.Max(1, previewHost.ActualHeight);
        if (previewImage != null) { previewImage.Width = width; previewImage.Height = height; }
        if (player != null) { player.Width = width; player.Height = height; }
    }
    private UIElement EmptyPreview()
    {
        var empty = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        empty.Children.Add(new FontIcon { Glyph = "\uEB9F", FontSize = 46, Opacity = .25 }); empty.Children.Add(Text(T("SelectPreview"), 14, true)); return empty;
    }
    private string PatternText => string.IsNullOrWhiteSpace(pattern.Text) ? pattern.SelectedItem as string ?? preferences.Pattern : pattern.Text;
    private void UpdateExample()
    {
        if (example == null) return;
        try { example.Text = T("Example") + ":  " + FolderPattern.Format(PatternText, new DateTime(2026, 4, 28)); preferences.Pattern = PatternText; }
        catch (Exception ex) { example.Text = T(ex.Message); }
    }
    private void SavePattern()
    {
        try { FolderPattern.Format(PatternText, DateTime.Today); preferences.Pattern = PatternText; if (!preferences.Patterns.Contains(PatternText)) preferences.Patterns.Add(PatternText); pattern.ItemsSource = preferences.Patterns.ToArray(); pattern.Text = preferences.Pattern; SaveSettings(); UpdateExample(); }
        catch (Exception ex) { _ = Error(ex); }
    }
    private async Task PickFolder(bool source)
    {
        try
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync(); if (folder == null) return;
            StopPreview(); SetPreviewItems(Array.Empty<FileEntry>(), null);
            if (source) { sourcePath = folder.Path; outputRoot = Path.Combine(sourcePath, "out"); outputCurrent = outputRoot; sourceBox.Text = sourcePath; await RefreshSource(); await RefreshOutput(); }
            else { if (!string.IsNullOrEmpty(sourcePath) && MediaFiles.IsWithin(sourcePath, folder.Path)) throw new ArgumentException("OutputInvalid"); outputRoot = folder.Path; outputCurrent = outputRoot; await RefreshOutput(); await RefreshSource(); }
        }
        catch (Exception ex) { await Error(ex); }
    }
    private MediaKind? Filter => filter.SelectedIndex == 1 ? MediaKind.Photo : filter.SelectedIndex == 2 ? MediaKind.Video : null;
    private static FileEntry Entry(string path, bool directory = false)
    {
        var info = new FileInfo(path);
        return new() { Path = path, IsDirectory = directory, Detail = directory ? "" : $"{info.Length / 1048576d:0.##} MB  ·  {info.LastWriteTime:yyyy-MM-dd HH:mm}" };
    }
    private async Task RefreshSource()
    {
        var version = ++sourceVersion;
        if (string.IsNullOrWhiteSpace(sourcePath)) return;
        var path = sourcePath; var output = outputRoot; var deep = recursive?.IsChecked == true; var kind = filter == null ? null : Filter;
        try
        {
            count.Text = T("Loading");
            var entries = await Task.Run(() => MediaFiles.Enumerate(path, output, deep, kind, CancellationToken.None).Select(x => Entry(x)).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
            if (version != sourceVersion) return;
            sourceItems.Clear(); foreach (var entry in entries) sourceItems.Add(entry); count.Text = $"{entries.Count:N0} {T("Files")}";
            if (navigationList == sourceList) SetPreviewItems(entries, sourceList);
        }
        catch (Exception ex) { if (version == sourceVersion) count.Text = T(ex.Message); }
    }
    private async Task RefreshOutput()
    {
        var version = ++outputVersion; outputBox.Text = outputCurrent;
        if (string.IsNullOrWhiteSpace(outputCurrent) || !System.IO.Directory.Exists(outputCurrent)) { outputItems.Clear(); UpdateOutputStatistics(); return; }
        var path = outputCurrent;
        try
        {
            var entries = await Task.Run(() => System.IO.Directory.EnumerateDirectories(path).Where(x => (File.GetAttributes(x) & FileAttributes.ReparsePoint) == 0).Select(x => Entry(x, true)).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).Concat(System.IO.Directory.EnumerateFiles(path).Where(x => (File.GetAttributes(x) & FileAttributes.ReparsePoint) == 0).Select(x => Entry(x)).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)).ToList());
            if (version == outputVersion) { outputItems.Clear(); foreach (var entry in entries) outputItems.Add(entry); UpdateOutputStatistics(); if (navigationList == outputList) SetPreviewItems(entries, outputList); }
        }
        catch (Exception ex) { if (version == outputVersion) status.Text = T(ex.Message); }
    }
    private void Up() => _ = NavigateUp();
    private static void Launch(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private void RevealSelected() { if (outputList.SelectedItem is FileEntry entry) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.Path}\"") { UseShellExecute = true }); }
    private void OpenSelected() { if (outputList.SelectedItem is FileEntry entry) { if (entry.IsDirectory) _ = EnterOutput(entry.Path); else _ = ShowEntry(entry); } }

    private void StopPreview()
    {
        previewCancellation?.Cancel(); previewCancellation?.Dispose(); previewCancellation = null;
        previewImage = null;
        if (previewHost != null) previewHost.Child = EmptyPreview();
        var oldControl = player; var oldPlayer = videoPlayer; var oldSource = videoSource;
        player = null; videoPlayer = null; videoSource = null;
        if (oldPlayer != null)
        {
            if (videoFailed != null) oldPlayer.MediaFailed -= videoFailed;
            oldPlayer.Pause();
        }
        videoFailed = null;
        oldControl?.SetMediaPlayer(null);
        if (oldPlayer != null) { oldPlayer.Source = null; oldPlayer.Dispose(); }
        oldSource?.Dispose();
        selectedFile = null;
        UpdatePreviewNavigation();
    }
    private async Task ShowEntry(FileEntry entry)
    {
        StopPreview(); previewCancellation = new(); var token = previewCancellation.Token; selectedFile = entry.Path;
        UpdatePreviewNavigation();
        try
        {
            if (entry.IsDirectory) { info.Text = entry.Path; await ShowGallery(entry.Path, token); return; }
            info.Text = T("Loading"); previewHost.Child = Text(T("Loading"));
            var detailsTask = Task.Run(() => MetadataService.ReadAsync(entry.Path, token), token);
            if (MediaFiles.Kind(entry.Path) == MediaKind.Video)
            {
                var file = await StorageFile.GetFileFromPathAsync(entry.Path); token.ThrowIfCancellationRequested();
                videoSource = MediaSource.CreateFromStorageFile(file);
                videoPlayer = new Windows.Media.Playback.MediaPlayer { AutoPlay = false, Source = videoSource };
                player = new MediaPlayerElement { AreTransportControlsEnabled = true, AutoPlay = false, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                player.SetMediaPlayer(videoPlayer);
                var current = player;
                videoFailed = (_, _) => DispatcherQueue.TryEnqueue(() => { if (!token.IsCancellationRequested && player == current) PreviewFailure(entry.Path); });
                videoPlayer.MediaFailed += videoFailed;
                previewHost.Child = player; FitPreview();
            }
            else
            {
                var file = await StorageFile.GetFileFromPathAsync(entry.Path); token.ThrowIfCancellationRequested();
                using var stream = await file.OpenReadAsync();
                var bitmap = new BitmapImage { DecodePixelWidth = 1800 };
                await bitmap.SetSourceAsync(stream); token.ThrowIfCancellationRequested();
                previewImage = new Image { Source = bitmap, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                previewHost.Child = previewImage; FitPreview();
            }
            var metadata = await detailsTask;
            if (!token.IsCancellationRequested) info.Text = MetadataSummary.Format(entry.Path, metadata, loc);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            PreviewFailure(entry.Path);
            try { var md = await Task.Run(() => MetadataService.ReadAsync(entry.Path, token), token); if (!token.IsCancellationRequested) info.Text = MetadataSummary.Format(entry.Path, md, loc); }
            catch { if (!token.IsCancellationRequested) info.Text = ex.Message; }
        }
    }
    private void PreviewFailure(string path)
    {
        var stack = new StackPanel { Spacing = 12, Padding = new Thickness(20), VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Text(T("PreviewUnavailable"), 14)); stack.Children.Add(Action("External", () => { try { Launch(path); } catch (Exception ex) { _ = Error(ex); } }, "\uE8A7", false));
        stack.Children.Add(Action("Codec", () => Launch("ms-windows-store://search/?query=" + (Path.GetExtension(path).Equals(".heic", StringComparison.OrdinalIgnoreCase) ? "HEIF%20Image%20Extensions" : "HEVC%20Video%20Extensions")), "\uE74D", false)); previewHost.Child = stack;
    }
    private async Task ShowGallery(string path, CancellationToken token)
    {
        var files = await Task.Run(() => System.IO.Directory.EnumerateFiles(path).Where(x => MediaFiles.Kind(x) != null && (File.GetAttributes(x) & FileAttributes.ReparsePoint) == 0).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList(), token); token.ThrowIfCancellationRequested();
        SetPreviewItems(files.Select(x => new FileEntry { Path = x }), null);
        var grid = new GridView { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
        grid.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Horizontal'/></ItemsPanelTemplate>");
        grid.ItemClick += (_, e) => { if (e.ClickedItem is StackPanel tile && tile.Tag is string file) _ = ShowEntry(Entry(file)); };
        previewHost.Child = grid;
        if (files.Count == 0) { previewHost.Child = Text(T("Empty")); return; }
        if (files.Count > 300) status.Text = T("GalleryLimit");
        foreach (var file in files.Take(300))
        {
            token.ThrowIfCancellationRequested();
            var tile = new StackPanel { Width = 120, Spacing = 4, Margin = new Thickness(4), Tag = file };
            var image = new Image { Width = 120, Height = 85, Stretch = Stretch.Uniform }; tile.Children.Add(image);
            tile.Children.Add(new TextBlock { Text = Path.GetFileName(file), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis }); grid.Items.Add(tile);
            try { var storage = await StorageFile.GetFileFromPathAsync(file); using var thumbnail = await storage.GetThumbnailAsync(ThumbnailMode.SingleItem, 160); token.ThrowIfCancellationRequested(); if (thumbnail != null) { var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(thumbnail); image.Source = bitmap; } }
            catch (OperationCanceledException) { throw; }
            catch { image.Visibility = Visibility.Collapsed; tile.Children.Insert(0, new FontIcon { Glyph = "\uE714", FontSize = 35, Height = 85 }); }
        }
    }

    private void SetBusy(bool value, string? message = null)
    {
        busy = value; foreach (var control in operationControls) control.IsEnabled = !value;
        sourceList.IsEnabled = !value; outputList.IsEnabled = !value;
        cancel.Visibility = value ? Visibility.Visible : Visibility.Collapsed; progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed; progress.IsIndeterminate = value;
        if (message != null) status.Text = message;
        if (value) { StopPreview(); SetPreviewItems(Array.Empty<FileEntry>(), null); operation = new(); } else { operation?.Dispose(); operation = null; }
        UpdatePreviewNavigation();
    }
    private async Task<ContentDialogResult> Dialog(string title, UIElement content, string? primary = null, bool focusPrimary = false)
    {
        var dialog = new ContentDialog { XamlRoot = root.XamlRoot, RequestedTheme = root.RequestedTheme, Title = title, Content = content, CloseButtonText = T("Close"), PrimaryButtonText = primary ?? "", DefaultButton = ContentDialogButton.Close };
        dialog.Resources["ContentDialogMaxWidth"] = 820d;
        if (focusPrimary) FocusPrimaryButton(dialog);
        return await dialog.ShowAsync();
    }
    private async Task Error(Exception ex) { status.Text = T(ex.Message); try { await Dialog(T("Error"), Text(T(ex.Message))); } catch { } }
    private async Task<bool> ShowPlan(List<PlanItem> plan, bool allowMove, string? primaryText = null, string? notice = null)
    {
        UIElement panel = BuildPlanPreview(plan);
        if (notice != null)
        {
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(Text(notice)); content.Children.Add(panel); panel = content;
        }
        return await Dialog(T("Plan"), panel, allowMove && plan.Any(x => x.Error == null) ? primaryText ?? T("Sort") : null) == ContentDialogResult.Primary;
    }
    private async Task Sort()
    {
        if (busy) return;
        try
        {
            if (!System.IO.Directory.Exists(sourcePath)) throw new IOException("ChooseSource");
            if (string.IsNullOrWhiteSpace(outputRoot)) throw new IOException("ChooseOutput");
            var template = PatternText; FolderPattern.Format(template, DateTime.Today); preferences.Pattern = template; SaveSettings();
            var deep = recursive.IsChecked == true; var kind = Filter;
            SetBusy(true, T("Working")); var token = operation!.Token;
            var reporter = new Progress<int>(n => { if (n % 20 == 0) status.Text = $"{T("Working")}: {n:N0}"; });
            var plan = await Task.Run(() => engine.PlanAsync(sourcePath, outputRoot, template, deep, kind, reporter, token), token);
            token.ThrowIfCancellationRequested();
            if (plan.Count == 0) { status.Text = T("NoItems"); return; }
            if (!await ShowPlan(plan, true)) { status.Text = $"{T("Plan")}: {plan.Count:N0} {T("Files")}"; return; }
            token.ThrowIfCancellationRequested();
            await Execute(plan, token);
        }
        catch (OperationCanceledException) { status.Text = T("Cancelled"); }
        catch (Exception ex) { await Error(ex); }
        finally { SetBusy(false); await RefreshSource(); await RefreshOutput(); }
    }
    private async Task<List<MoveResult>> Execute(List<PlanItem> plan, CancellationToken token)
    {
        progress.IsIndeterminate = false; progress.Maximum = plan.Count; progress.Value = 0;
        var reporter = new Progress<int>(n => { progress.Value = n; status.Text = $"{T("Moving")}: {n:N0} / {plan.Count:N0}"; });
        var journal = Path.Combine(Preferences.DataDirectory, "logs", $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
        var results = await Task.Run(() => SortEngine.ExecuteAsync(plan, journal, reporter, token));
        status.Text = $"{T(token.IsCancellationRequested ? "Cancelled" : "Done")}: {results.Count(x => x.Success):N0} {T("Files")} · {results.Count(x => !x.Success)} {T("Errors")} · {plan.Count - results.Count} {T("Skipped")}";
        if (results.Any(x => !x.Success)) await Dialog(T("Errors"), new ScrollViewer { MaxHeight = 400, Content = Text(string.Join("\n\n", results.Where(x => !x.Success).Take(100).Select(x => x.Item.Source + "\n" + T(x.Error ?? "Error"))), 12) });
        return results;
    }
    private async Task Rename()
    {
        if (busy) return;
        try
        {
            if (outputList.SelectedItems.Count != 1 || outputList.SelectedItem is not FileEntry entry) throw new InvalidOperationException("InvalidSelection");
            var name = new TextBox { Text = entry.Name, Header = T("Name"), MinWidth = 360 }; name.SelectAll();
            if (await Dialog(T("Rename"), name, T("Confirm"), focusPrimary: true) != ContentDialogResult.Primary) return;
            FolderPattern.ValidateName(name.Text); if (name.Text == entry.Name) return;
            var destination = Path.Combine(Path.GetDirectoryName(entry.Path)!, name.Text);
            if (File.Exists(destination) || System.IO.Directory.Exists(destination)) throw new IOException(T("Name") + ": " + destination);
            StopPreview();
            ++outputVersion; ++sourceVersion;
            var offset = FindScrollViewer(outputList)?.VerticalOffset ?? 0;
            await Task.Run(() => { SortEngine.RejectReparseAncestors(Path.GetDirectoryName(entry.Path)!); if (entry.IsDirectory) System.IO.Directory.Move(entry.Path, destination); else File.Move(entry.Path, destination, false); });
            ReplaceRenamedEntry(entry, destination, offset);
        }
        catch (Exception ex) { await Error(ex); }
    }
    private async Task Delete(bool fromSource = false)
    {
        var list = fromSource ? sourceList : outputList;
        var allowedRoot = fromSource ? sourcePath : outputRoot;
        if (busy || list.SelectedItems.Count == 0) return;
        var selected = list.SelectedItems.Cast<FileEntry>().ToArray();
        if (await Dialog(T("ConfirmDelete"), Text($"{selected.Length} {T("Selected")}\n\n{T("ConfirmDeleteBody")}"), T("Delete")) != ContentDialogResult.Primary) return;
        var savedOffset = FindScrollViewer(list)?.VerticalOffset ?? 0;
        var removed = new List<FileEntry>();
        if (!fromSource) ++outputVersion; else ++sourceVersion;
        SetBusy(true, T("Delete"));
        try
        {
            var errors = new List<string>();
            foreach (var entry in selected)
            {
                if (operation!.IsCancellationRequested) break;
                try
                {
                    await Task.Run(() =>
                    {
                        if (!MediaFiles.IsWithin(entry.Path, allowedRoot) || entry.Path.Equals(allowedRoot, StringComparison.OrdinalIgnoreCase) || (fromSource && entry.IsDirectory)) throw new IOException("OutputInvalid");
                        SortEngine.RejectReparseAncestors(entry.IsDirectory ? entry.Path : Path.GetDirectoryName(entry.Path)!);
                        if (entry.IsDirectory) FileSystem.DeleteDirectory(entry.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
                        else FileSystem.DeleteFile(entry.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
                    });
                    removed.Add(entry);
                }
                catch (Exception ex) { errors.Add(entry.Name + ": " + T(ex.Message)); }
            }
            status.Text = T("Done"); if (errors.Count > 0) await Dialog(T("Errors"), Text(string.Join("\n", errors)));
        }
        finally
        {
            if (!fromSource) RemoveOutputEntries(removed, savedOffset);
            else RemoveSourceEntries(removed, savedOffset);
            SetBusy(false);
        }
    }
    private async Task Merge()
    {
        if (busy) return;
        try
        {
            var entries = outputList.SelectedItems.Cast<FileEntry>().ToArray();
            if (entries.Length < 2 || entries.Any(x => !x.IsDirectory)) throw new InvalidOperationException("InvalidSelection");
            SetBusy(true, T("Working")); var token = operation!.Token;
            var files = await Task.Run(() => entries.SelectMany(x => EnumerateAll(x.Path, token)).ToArray(), token);
            if (files.Length == 0) throw new IOException("NoItems");
            var dated = new List<(string File, MediaDate Date)>();
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var md = MediaFiles.Kind(file) == null ? new MediaDate(File.GetLastWriteTime(file), "Modified", new Dictionary<string, string>()) : await Task.Run(() => MetadataService.ReadAsync(file, token), token);
                dated.Add((file, md));
            }
            var first = FolderPattern.Format(PatternText, dated.Min(x => x.Date.Date)); var last = FolderPattern.Format(PatternText, dated.Max(x => x.Date.Date));
            var range = first == last ? Path.GetFileName(first) : Path.GetDirectoryName(first) == Path.GetDirectoryName(last) ? Path.GetFileName(first) + " - " + Path.GetFileName(last) : first.Replace('\\', '-') + " - " + last.Replace('\\', '-');
            var text = new TextBox { Text = range, Header = T("Name"), MinWidth = 380 };
            var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(Text(T("MergeHelp"), 13)); panel.Children.Add(text); panel.Children.Add(Text(T("MergePattern"), 12, true));
            if (await Dialog(T("MergeTitle"), panel, T("Confirm")) != ContentDialogResult.Primary) return;
            FolderPattern.ValidateName(text.Text); var target = Path.Combine(outputCurrent, text.Text);
            if (System.IO.Directory.Exists(target) || File.Exists(target)) throw new IOException(T("Name") + ": " + target);
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var plan = dated.Select(x => { var f = new FileInfo(x.File); return new PlanItem(x.File, SortEngine.UniquePath(Path.Combine(target, f.Name), reserved), f.Length, f.LastWriteTimeUtc, x.Date.Date, x.Date.Source); }).ToList();
            if (!await ShowPlan(plan, true)) return;
            var results = await Execute(plan, token);
            if (results.Count == plan.Count && results.All(x => x.Success)) await Task.Run(() => { foreach (var entry in entries) RemoveEmpty(entry.Path); });
        }
        catch (OperationCanceledException) { status.Text = T("Cancelled"); }
        catch (Exception ex) { await Error(ex); }
        finally { SetBusy(false); await RefreshOutput(); await RefreshSource(); }
    }
    private static IEnumerable<string> EnumerateAll(string path, CancellationToken token)
    {
        SortEngine.RejectReparseAncestors(path);
        foreach (var file in System.IO.Directory.EnumerateFiles(path)) { token.ThrowIfCancellationRequested(); if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) yield return file; }
        foreach (var folder in System.IO.Directory.EnumerateDirectories(path)) { token.ThrowIfCancellationRequested(); if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0) foreach (var file in EnumerateAll(folder, token)) yield return file; }
    }
    private static void RemoveEmpty(string path)
    {
        foreach (var folder in System.IO.Directory.EnumerateDirectories(path)) if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0) RemoveEmpty(folder);
        if (!System.IO.Directory.EnumerateFileSystemEntries(path).Any()) System.IO.Directory.Delete(path);
    }
}
