using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace VideoCapture.Services;

internal sealed record CecDevice(int Address, string Name)
{
    public override string ToString() => $"{Name} (CEC {Address:X})";
}

internal enum CecKey : byte
{
    Select = 0x00, Up = 0x01, Down = 0x02, Left = 0x03, Right = 0x04,
    Home = 0x09, Back = 0x0D, VolumeUp = 0x41, VolumeDown = 0x42,
    Mute = 0x43, Play = 0x44, Stop = 0x45, Pause = 0x46
}

// Own one client process per connection. The prompt delimits command responses;
// exit codes alone cannot establish delivery (cec-client discards Transmit's return value).
internal sealed class CecClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<ProcessStartInfo> _startInfo;
    private readonly TimeSpan _timeout;
    private Process? _process;
    private Channel<string>? _output;
    private Task? _stdout, _stderr;
    private bool _disposed;
    private volatile bool _connectionLost;
    private int _address = -1;
    public bool IsConnected => !_connectionLost && _address >= 0 && _process is { HasExited: false };

    public CecClient(string executable, string port) : this(() =>
    {
        if (!Regex.IsMatch(port, @"^COM[1-9][0-9]*$", RegexOptions.IgnoreCase))
            throw new ArgumentException("Select a valid CEC adapter COM port.");
        var info = new ProcessStartInfo(Path.GetFullPath(executable));
        foreach (string value in new[] { "-d", "1", "-t", "r", "-o", "VideoCapture", "-aw", "0", port })
            info.ArgumentList.Add(value);
        return info;
    }, TimeSpan.FromSeconds(45)) { }

    internal CecClient(Func<ProcessStartInfo> startInfo, TimeSpan timeout)
    { _startInfo = startInfo; _timeout = timeout; }

    public static string? FindExecutable()
    {
        foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            string path = Path.Combine(root, "Pulse-Eight", "USB-CEC Adapter", "cec-client.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static async Task<IReadOnlyList<string>> DetectAdaptersAsync(string executable)
    {
        var info = new ProcessStartInfo(Path.GetFullPath(executable))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-l");
        using var process = Process.Start(info) ?? throw new IOException("Could not start cec-client.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw; }
        string text = await output;
        string error = await errors;
        if (process.ExitCode != 0 || text.Contains("Cannot load", StringComparison.OrdinalIgnoreCase))
            throw new IOException("libCEC could not enumerate adapters. " + error + " " + text);
        return Regex.Matches(text, @"(?im)^\s*com port:\s*(COM[1-9][0-9]*)\s*$")
            .Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToArray();
    }

    public Task<IReadOnlyList<CecDevice>> ConnectAsync() => SerializedAsync(async () =>
    {
        if (_process is not null) throw new InvalidOperationException("Disconnect before reconnecting.");
        var info = _startInfo();
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
        _output = Channel.CreateBounded<string>(new BoundedChannelOptions(4096) { FullMode = BoundedChannelFullMode.DropOldest });
        _process = Process.Start(info) ?? throw new IOException("Could not start cec-client.");
        _stdout = ReadOutputAsync(_process.StandardOutput, _output.Writer);
        // Drain stderr separately; only stdout's prompt may complete a command.
        _stderr = _process.StandardError.ReadToEndAsync();
        await ReadResponseAsync();
        string own = await CommandAsync("self");
        var match = Regex.Match(own, @"Addresses controlled by libCEC:\s*(\d+)");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out _address) || _address is < 0 or > 14)
            throw new IOException("libCEC did not allocate a valid logical address.");
        return ParseDevices(await CommandAsync("scan"), _address);
    });

    public Task<IReadOnlyList<CecDevice>> ScanAsync() => SerializedAsync(async () =>
    {
        RequireConnection();
        return ParseDevices(await CommandAsync("scan"), _address);
    });

    public Task PressAsync(int target, CecKey key) => SerializedAsync(async () =>
    {
        ValidateTarget(target);
        if (!Enum.IsDefined(key)) throw new ArgumentOutOfRangeException(nameof(key));
        string header = $"{_address:X}{target:X}";
        // Queue both packets before reading responses, so a negative acknowledgement
        // or a slow response to the press cannot prevent the matching release.
        await _process!.StandardInput.WriteLineAsync($"tx {header}:44:{(byte)key:X2}");
        await _process.StandardInput.WriteLineAsync($"tx {header}:45");
        await _process.StandardInput.FlushAsync();
        string pressed = await ReadResponseAsync();
        string released = await ReadResponseAsync();
        ThrowIfError(pressed);
        ThrowIfError(released);
        return true;
    });

    public Task PowerAsync(int target, bool on) => SerializedAsync(async () =>
    {
        ValidateTarget(target);
        ThrowIfError(await CommandAsync($"{(on ? "on" : "standby")} {target:X}"));
        return true;
    });

    private void ValidateTarget(int target)
    {
        RequireConnection();
        if (target is < 0 or > 14 || target == _address) throw new ArgumentOutOfRangeException(nameof(target));
    }
    private void RequireConnection()
    { if (!IsConnected) throw new IOException("CEC adapter disconnected. Connect again."); }

    internal static IReadOnlyList<CecDevice> ParseDevices(string text, int ownAddress) =>
        Regex.Matches(text, @"(?im)^device #([0-9a-e]):\s*([^\r\n]+)([\s\S]*?)(?=^device #|\z)")
            .Select(m => new { Address = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber),
                Type = m.Groups[2].Value.Trim(), Osd = Regex.Match(m.Groups[3].Value, @"(?im)^osd string:[ \t]*([^\r\n]*)") })
            .Where(d => d.Address != ownAddress)
            .Select(d => new CecDevice(d.Address, d.Osd.Success && !string.IsNullOrWhiteSpace(d.Osd.Groups[1].Value)
                ? d.Osd.Groups[1].Value.Trim() : d.Type)).DistinctBy(d => d.Address).ToArray();

    private async Task<string> CommandAsync(string command)
    {
        await _process!.StandardInput.WriteLineAsync(command);
        await _process.StandardInput.FlushAsync();
        return await ReadResponseAsync();
    }
    private async Task<string> ReadResponseAsync()
    {
        using var timeout = new CancellationTokenSource(_timeout);
        var lines = new List<string>();
        int length = 0;
        try
        {
            while (true)
            {
                string line = await _output!.Reader.ReadAsync(timeout.Token);
                if (line.Trim() == "waiting for input") return string.Join('\n', lines);
                lines.Add(line);
                length += line.Length;
                if (length > 256_000) throw new IOException("Unexpectedly large response from cec-client.");
            }
        }
        catch (OperationCanceledException) { throw new TimeoutException("CEC adapter did not respond in time. Check its connection."); }
        catch (ChannelClosedException) { throw new IOException("cec-client exited before responding. Check the adapter and libCEC installation. " + string.Join('\n', lines)); }
    }
    private async Task ReadOutputAsync(StreamReader reader, ChannelWriter<string> writer)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.Contains("Connection lost", StringComparison.OrdinalIgnoreCase)) _connectionLost = true;
                writer.TryWrite(line);
            }
        }
        catch (IOException) { }
        finally { writer.TryComplete(); }
    }
    private static void ThrowIfError(string response)
    {
        if (response.Contains("ERROR:", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("Connection lost", StringComparison.OrdinalIgnoreCase))
            throw new IOException("CEC command failed. " + response.Trim());
    }
    private async Task<T> SerializedAsync<T>(Func<Task<T>> action)
    {
        await _gate.WaitAsync();
        try { ObjectDisposedException.ThrowIf(_disposed, this); return await action(); }
        catch { await StopProcessAsync(); throw; }
        finally { _gate.Release(); }
    }
    private async Task StopProcessAsync()
    {
        _address = -1;
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync("q");
                await _process.StandardInput.FlushAsync();
                try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (TimeoutException) { _process.Kill(true); await _process.WaitForExitAsync(); }
            }
        }
        catch (IOException) { if (!_process.HasExited) { _process.Kill(true); await _process.WaitForExitAsync(); } }
        finally
        {
            if (_stdout is not null) await _stdout;
            if (_stderr is not null) await _stderr;
            _process.Dispose();
            _process = null;
        }
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { _disposed = true; await StopProcessAsync(); }
        finally { _gate.Release(); }
    }
}
