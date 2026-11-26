using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Channels;

namespace ChessDatabase.Api.Engines;

/// <summary>An Engine program that could not be started or did not speak UCI.</summary>
public sealed class EngineException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// One running UCI engine process. Commands go to its standard input; its output lines are read
/// in order by one consumer at a time. Disposing sends "quit" and kills the process if it lingers.
/// </summary>
public sealed class UciEngine : IAsyncDisposable
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
    private readonly Process _process;
    private readonly Channel<string> _output = Channel.CreateUnbounded<string>(new() { SingleWriter = true });
    private readonly Lock _inputLock = new();
    private int _disposed;

    private UciEngine(Process process)
    {
        _process = process;
        _ = Task.Run(PumpOutputAsync);
    }

    /// <summary>The name the Engine reports ("id name"), e.g. "Stockfish 17.1".</summary>
    public string Name { get; private set; } = "";

    public int ProcessId => _process.Id;

    /// <summary>Raised once when the engine is disposed.</summary>
    public event Action<UciEngine>? Disposed;

    /// <summary>Starts the program and completes the UCI handshake.</summary>
    public static async Task<UciEngine> StartAsync(string path, string? arguments, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(path, arguments ?? "")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? "",
        };
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new EngineException(NotAnEngine);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            throw new EngineException(NotAnEngine, e);
        }
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();

        var engine = new UciEngine(process);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(HandshakeTimeout);
            engine.Send("uci");
            await engine.ReadUntilAsync(line =>
            {
                if (line.StartsWith("id name ", StringComparison.Ordinal)) engine.Name = line["id name ".Length..].Trim();
                return line == "uciok";
            }, timeout.Token);
            return engine;
        }
        catch (Exception e) when (e is OperationCanceledException or ChannelClosedException or IOException)
        {
            await engine.DisposeAsync();
            throw new EngineException(NotAnEngine, e);
        }
    }

    public const string NotAnEngine = "That program did not answer as a UCI engine.";

    public void Send(string command)
    {
        lock (_inputLock)
        {
            _process.StandardInput.WriteLine(command);
            _process.StandardInput.Flush();
        }
    }

    /// <summary>Sets up the position after these moves (UCI) from the starting position.</summary>
    public void SetPosition(IReadOnlyList<string> moves) =>
        Send(moves.Count == 0 ? "position startpos" : $"position startpos moves {string.Join(' ', moves)}");

    /// <summary>Sets the options, then waits until the Engine is ready (a large hash can take a while).</summary>
    public async Task ConfigureAsync(IEnumerable<(string Name, object Value)> options, CancellationToken cancellationToken = default)
    {
        foreach (var (name, value) in options) Send($"setoption name {name} value {value}");
        Send("isready");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(1));
        await ReadUntilAsync(line => line == "readyok", timeout.Token);
    }

    /// <summary>The next output line. Throws ChannelClosedException once the process has ended.</summary>
    public ValueTask<string> ReadLineAsync(CancellationToken cancellationToken = default) =>
        _output.Reader.ReadAsync(cancellationToken);

    /// <summary>Reads (and drops) lines up to and including the first that matches.</summary>
    public async Task ReadUntilAsync(Func<string, bool> match, CancellationToken cancellationToken = default)
    {
        while (!match(await ReadLineAsync(cancellationToken))) { }
    }

    private async Task PumpOutputAsync()
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync() is { } line)
                _output.Writer.TryWrite(line);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
        _output.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try
        {
            if (!_process.HasExited)
            {
                try { Send("quit"); } catch (IOException) { }
                using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await _process.WaitForExitAsync(grace.Token); }
                catch (OperationCanceledException) { _process.Kill(entireProcessTree: true); }
            }
        }
        catch (InvalidOperationException) { } // already gone
        finally
        {
            _process.Dispose();
            Disposed?.Invoke(this);
        }
    }
}
