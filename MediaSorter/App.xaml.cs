using Microsoft.UI.Xaml;

namespace MediaSorter;
public partial class App : Application
{
    private Window? window;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try
            {
                var folder = Path.Combine(Preferences.DataDirectory, "logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "crash.log"), $"{DateTime.Now:O}\n{e.Exception}\n\n");
            }
            catch { }
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
        var smoke = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith("--smoke-test=", StringComparison.Ordinal));
        if (smoke != null && window is MainWindow main)
            main.DispatcherQueue.TryEnqueue(async () => await main.RunSmokeTestAsync(smoke["--smoke-test=".Length..]));
    }
}
