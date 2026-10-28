using System.Diagnostics;
using System.IO;
using VideoCapture.Services;

internal static class CecTests
{
    internal static int RunFake(string scenario, string log)
    {
        if (scenario == "exit") { Console.WriteLine("unable to open the device"); return 1; }
        if (scenario == "timeout") { Thread.Sleep(15_000); return 0; }
        Console.WriteLine("waiting for input");
        while (Console.ReadLine() is { } command)
        {
            File.AppendAllText(log, command + Environment.NewLine);
            if (command == "q") return 0;
            if (command == "self") Console.WriteLine("Addresses controlled by libCEC: 1");
            if (command == "scan") Console.WriteLine("device #0: TV\nosd string:    Living room\n\ndevice #1: Recorder\nosd string:    VideoCapture\n\ndevice #4: Playback\nosd string:    Roku\n\ndevice #A: Tuner\nosd string:    \npower status: on");
            if (scenario == "reject" && command.EndsWith(":44:01")) Console.WriteLine("ERROR: transmit failed");
            Console.WriteLine("waiting for input");
        }
        return 0;
    }

    internal static async Task RunAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "VideoCapture-Cec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        CecClient Create(string scenario, string log) => new(() =>
        {
            var info = new ProcessStartInfo("dotnet");
            foreach (string arg in new[] { typeof(CecTests).Assembly.Location, "--fake-cec", scenario, log }) info.ArgumentList.Add(arg);
            return info;
        }, TimeSpan.FromSeconds(2));
        try
        {
            string log = Path.Combine(folder, "normal.txt");
            await using (var client = Create("normal", log))
            {
                var devices = await client.ConnectAsync();
                if (!client.IsConnected || devices.Count != 3 || devices[1] != new CecDevice(4, "Roku") || devices[2] != new CecDevice(10, "Tuner"))
                    throw new Exception("CEC discovery must exclude our address, preserve hex addresses, and handle missing OSD names.");
                await Task.WhenAll(client.PressAsync(4, CecKey.Up), client.PressAsync(4, CecKey.Select));
                await client.PowerAsync(0, true);
                await client.PowerAsync(0, false);
                await client.ScanAsync();
            }
            string[] commands = File.ReadAllLines(log);
            if (!commands.SequenceEqual(new[] { "self", "scan", "tx 14:44:01", "tx 14:45", "tx 14:44:00", "tx 14:45", "on 0", "standby 0", "scan", "q" }))
                throw new Exception("CEC commands interleaved, used the wrong address, or omitted key release/shutdown.");
            string rejectedLog = Path.Combine(folder, "reject.txt");
            await using (var client = Create("reject", rejectedLog))
            {
                await client.ConnectAsync();
                try { await client.PressAsync(4, CecKey.Up); throw new Exception("CEC transmission rejection was ignored."); }
                catch (IOException) { }
                if (client.IsConnected || !File.ReadAllLines(rejectedLog).Contains("tx 14:45"))
                    throw new Exception("Rejected key must still release and invalidate the connection.");
            }
            foreach (string scenario in new[] { "exit", "timeout" })
            {
                await using var client = Create(scenario, Path.Combine(folder, scenario + ".txt"));
                try { await client.ConnectAsync(); throw new Exception("CEC startup failure was ignored."); }
                catch (IOException) when (scenario == "exit") { }
                catch (TimeoutException) when (scenario == "timeout") { }
                if (client.IsConnected) throw new Exception("Failed CEC startup retained a connection.");
            }
            await using (var client = Create("normal", Path.Combine(folder, "invalid.txt")))
            {
                await client.ConnectAsync();
                try { await client.PowerAsync(15, false); throw new Exception("CEC broadcast was allowed."); }
                catch (ArgumentOutOfRangeException) { }
            }
            Console.WriteLine("CEC checks passed: discovery, address parsing, serialized key/release, power, rejection, timeout, exit, broadcast guard, cleanup.");
        }
        finally { Directory.Delete(folder, true); }
    }
}
