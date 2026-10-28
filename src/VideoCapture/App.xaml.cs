using System.Configuration;
using System.Data;
using System.Windows;

namespace VideoCapture;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        // Publish-time maintenance only; never show a window or scan devices.
        if (e.Args is ["--prepare-runtime"])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                await Task.Run(() => { using var session = new VideoCapture.Services.PlaybackSession(false, "--reset-plugins-cache"); });
                Shutdown(0);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Console.Error.WriteLine("Playback runtime cache preparation failed: " + ex.Message);
                Shutdown(1);
            }
            return;
        }
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
