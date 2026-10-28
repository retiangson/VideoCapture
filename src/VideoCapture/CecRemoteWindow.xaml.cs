using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoCapture.Services;

namespace VideoCapture;

public partial class CecRemoteWindow : Window
{
    private string? _executable;
    private CecClient? _client;
    private bool _busy, _closing, _closed;
    private Task _operation = Task.CompletedTask;
    private Task? _shutdown;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _wasConnected;

    public CecRemoteWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.ApplyDarkCaption(this);
        _executable = CecClient.FindExecutable();
        ClientPath.Text = _executable ?? "libCEC tools not found. Install them, then locate cec-client.exe.";
        Closing += OnClosing;
        _statusTimer.Tick += (_, _) =>
        {
            bool connected = _client?.IsConnected == true;
            if (_wasConnected && !connected && !_busy && !_closing)
                Feedback.Text = "CEC adapter disconnected. Disconnect, then detect and connect again.";
            _wasConnected = connected;
            UpdateControls();
        };
        _statusTimer.Start();
    }

    private void Locate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select libCEC client", Filter = "libCEC client|cec-client.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        _executable = dialog.FileName;
        ClientPath.Text = _executable;
        Adapters.ItemsSource = null;
        UpdateControls();
    }
    private async void Detect_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_executable is null) throw new InvalidOperationException("Install libCEC Windows tools and locate cec-client.exe first.");
        var ports = await CecClient.DetectAdaptersAsync(_executable);
        Adapters.ItemsSource = ports;
        Adapters.SelectedIndex = ports.Count > 0 ? 0 : -1;
        Feedback.Text = ports.Count == 0 ? "No compatible USB-CEC adapter found. Connect a separate adapter and detect again." : "Adapter found. Connect to scan HDMI devices.";
    });
    private async void Connect_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
            Devices.ItemsSource = null;
            Feedback.Text = "Disconnected.";
            return;
        }
        if (_executable is null || Adapters.SelectedItem is not string port) return;
        Feedback.Text = "Connecting and scanning HDMI devices…";
        _client = new CecClient(_executable, port);
        SetDevices(await _client.ConnectAsync());
    });
    private async void Scan_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    { if (_client is not null) SetDevices(await _client.ScanAsync()); });
    private void SetDevices(IReadOnlyList<CecDevice> devices)
    {
        int? previous = (Devices.SelectedItem as CecDevice)?.Address;
        Devices.ItemsSource = devices;
        // Do not guess which playback device is the Roku or send to a broadcast address.
        Devices.SelectedItem = devices.FirstOrDefault(d => d.Address == previous);
        Feedback.Text = devices.Count == 0 ? "Adapter connected; no other CEC devices detected. Check HDMI wiring and enable CEC on the source/TV."
            : "Adapter connected. Select the device you want to control.";
    }
    private async void Key_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_client is null || Devices.SelectedItem is not CecDevice device) return;
        var key = Enum.Parse<CecKey>((string)((Button)sender).Tag);
        await _client.PressAsync(device.Address, key);
        Feedback.Text = $"{key} submitted to {device.Name}. Device action is not confirmed.";
    });
    private async void Power_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_client is null || Devices.SelectedItem is not CecDevice device) return;
        bool on = (string)((Button)sender).Tag == "on";
        await _client.PowerAsync(device.Address, on);
        Feedback.Text = $"{(on ? "Power on" : "Standby")} submitted to {device.Name}. Device action is not confirmed.";
    });
    private void Device_Changed(object sender, SelectionChangedEventArgs e) => UpdateControls();
    private void UpdateControls()
    {
        if (RemoteButtons is null) return;
        bool connected = _client?.IsConnected == true;
        LocateButton.IsEnabled = DetectButton.IsEnabled = Adapters.IsEnabled = !_busy && !_closing && !connected;
        ConnectButton.IsEnabled = !_busy && !_closing && (_client is not null || Adapters.SelectedItem is string);
        ConnectButton.Content = _client is not null ? "Disconnect" : "Connect";
        ScanButton.IsEnabled = Devices.IsEnabled = !_busy && !_closing && connected;
        RemoteButtons.IsEnabled = !_busy && !_closing && connected && Devices.SelectedItem is CecDevice;
    }
    private Task RunAsync(Func<Task> action)
    {
        if (_busy || _closing) return Task.CompletedTask;
        return _operation = ExecuteAsync(action);
    }
    private async Task ExecuteAsync(Func<Task> action)
    {
        _busy = true;
        UpdateControls();
        try { await action(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Feedback.Text = ex.Message;
            if (_client is not null) { await _client.DisposeAsync(); _client = null; }
            Devices.ItemsSource = null;
        }
        finally { _busy = false; UpdateControls(); }
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true;
        await ShutdownAsync();
        Close();
    }
    internal Task ShutdownAsync() => _shutdown ??= ShutdownCoreAsync();
    private async Task ShutdownCoreAsync()
    {
        _closing = true;
        _statusTimer.Stop();
        UpdateControls();
        Hide();
        await _operation;
        if (_client is not null) { await _client.DisposeAsync(); _client = null; }
        _closed = true;
    }
}
