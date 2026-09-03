using Microsoft.UI.Xaml;

namespace Diaphane.App;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diaphane-app.log"),
                $"{DateTime.Now:o} UNHANDLED: {e.Message}\n{e.Exception}\n\n");
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            MainWindow = new MainWindow();
            MainWindow.Activate();
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diaphane-app.log"),
                $"{DateTime.Now:o} LAUNCH FAILED:\n{ex}\n\n");
            throw;
        }
    }
}
