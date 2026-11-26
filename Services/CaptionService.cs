using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Channels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StreamGuard.Services;

public enum CaptionState { Stopped, Starting, Running, Paused, Stopping, Faulted }
public sealed record CaptionSegment(string Text, double Start, double End, string? Translation = null);
internal sealed record TranslationTask(string Text, double Start, double End);

/// <summary>Owns exactly one headless worker. No device preferences, global hotkeys or guarded app state.</summary>
public sealed class CaptionService : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<CancellationToken, Task<ProcessStartInfo>> _startInfo;
    private readonly string _outputDirectory;
    private Task _session = Task.CompletedTask;
    private CancellationTokenSource? _cancel;
    private Process? _process;
    private Channel<string>? _commands;
    private Channel<TranslationTask>? _translationQueue;
    private bool _disposed;
    private bool _requestedPaused;
    private volatile bool _stopRequested;
    private CaptionState _state;
    public CaptionState State { get { lock (_gate) return _state; } }
    public bool CanStart { get { lock (_gate) return !_disposed && _session.IsCompleted; } }
    public bool RequestedPaused { get { lock (_gate) return _requestedPaused; } }
    public string Detail { get; private set; } = "";
    public string OutputDirectory => _outputDirectory;
    public string? LastTranscriptPath { get; private set; }
    public Func<string, Task<string?>>? Translator { get; set; }
    internal int? WorkerProcessId { get { lock (_gate) return _process?.Id; } }
    public event Action? Changed;
    public event Action<CaptionSegment>? Segment;
    public event Action<CaptionSegment>? SegmentTranslated;
    public event Action<double>? Level;
    public CaptionService(string outputDirectory) : this(outputDirectory, CaptionRuntime.CreateStartInfoAsync) { }
    internal CaptionService(string outputDirectory, Func<CancellationToken, Task<ProcessStartInfo>> startInfo)
    { _outputDirectory = Path.GetFullPath(outputDirectory); _startInfo = startInfo; }
    public bool Start()
    {
        Task session;
        lock (_gate)
        {
            if (_disposed || !_session.IsCompleted) return false;
            _cancel?.Dispose(); _cancel = new(); _stopRequested = false;
            _requestedPaused = false;
            _state = CaptionState.Starting; Detail = "";
            session = _session = Task.Run(() => RunAsync(_cancel.Token));
        }
        _ = session.ContinueWith(_ => Changed?.Invoke(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Changed?.Invoke(); return true;
    }
    private void SetState(CaptionState state, string detail = "")
    {
        lock (_gate)
        {
            if (_disposed || (_stopRequested && state is CaptionState.Running or CaptionState.Paused)) return;
            _state = state; Detail = detail;
        }
        Changed?.Invoke();
    }
    private async Task RunAsync(CancellationToken cancellation)
    {
        var failed = false;
        Process? process = null; CaptionWorkerJob? job = null;
        Channel<string>? commands = null; Task? commandWriter = null;
        Channel<TranslationTask>? translationQueue = null; Task? translationWorker = null;
        CaptionTranscriptWriter? writer = null;
        try
        {
            var info = await _startInfo(cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            info.UseShellExecute = false; info.CreateNoWindow = true;
            info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
            info.StandardInputEncoding = new UTF8Encoding(false); info.StandardOutputEncoding = info.StandardErrorEncoding = Encoding.UTF8;
            Directory.CreateDirectory(_outputDirectory); info.WorkingDirectory = _outputDirectory;
            writer = new CaptionTranscriptWriter(_outputDirectory);
            LastTranscriptPath = writer.TextPath;
            process = new Process { StartInfo = info };
            if (!process.Start()) throw new IOException("The caption worker could not start.");
            job = new CaptionWorkerJob(process);
            commands = Channel.CreateBounded<string>(new BoundedChannelOptions(8)
            { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
            translationQueue = Channel.CreateUnbounded<TranslationTask>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = true });
            lock (_gate) { _process = process; _commands = commands; _translationQueue = translationQueue; }
            using var registration = cancellation.Register(() =>
            { commands.Writer.TryWrite("{\"command\":\"stop\"}"); commands.Writer.TryComplete(); });
            commandWriter = WriteCommandsAsync(process.StandardInput, commands.Reader);
            translationWorker = ProcessTranslationsAsync(translationQueue.Reader, writer, cancellation);
            var stderr = DrainErrorsAsync(process.StandardError);
            var stdout = ReadMessagesAsync(process.StandardOutput, translationQueue.Writer);
            var exit = process.WaitForExitAsync();
            // A broken reader must not leave a worker running with full output pipes.
            var first = await Task.WhenAny(stdout, exit).ConfigureAwait(false);
            if (first == stdout && (stdout.IsFaulted || await Task.WhenAny(exit, Task.Delay(1000)).ConfigureAwait(false) != exit)) TryKill(process);
            await exit.ConfigureAwait(false);
            await stdout.ConfigureAwait(false);
            translationQueue.Writer.TryComplete();
            if (translationWorker != null) await translationWorker.ConfigureAwait(false);
            var errors = await stderr.ConfigureAwait(false);
            if (!cancellation.IsCancellationRequested && !_stopRequested && !failed)
                throw new IOException("The caption worker stopped unexpectedly." + (errors.Length == 0 ? "" : " " + errors));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        { failed = true; if (!cancellation.IsCancellationRequested) SetState(CaptionState.Faulted, error.Message); }
        finally
        {
            if (process != null) TryKill(process);
            commands?.Writer.TryComplete();
            translationQueue?.Writer.TryComplete();
            if (commandWriter != null) await commandWriter.ConfigureAwait(false);
            if (translationWorker != null)
            {
                try { await Task.WhenAny(translationWorker, Task.Delay(1000)).ConfigureAwait(false); } catch { }
            }
            writer?.Dispose();
            job?.Dispose();
            lock (_gate) { if (ReferenceEquals(_process, process)) { _process = null; _commands = null; _translationQueue = null; } }
            process?.Dispose(); Level?.Invoke(0);
            if (!failed || cancellation.IsCancellationRequested) SetState(CaptionState.Stopped);
        }
    }
    private static async Task WriteCommandsAsync(StreamWriter input, ChannelReader<string> commands)
    {
        try
        {
            await foreach (var command in commands.ReadAllAsync().ConfigureAwait(false))
            {
                await input.WriteLineAsync(command).ConfigureAwait(false);
                await input.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (IOException) { } // Exited/unresponsive workers are owned by RunAsync/StopAsync.
        catch (ObjectDisposedException) { }
    }
    private async Task ProcessTranslationsAsync(ChannelReader<TranslationTask> reader, CaptionTranscriptWriter writer, CancellationToken cancellation)
    {
        try
        {
            await foreach (var task in reader.ReadAllAsync().ConfigureAwait(false))
            {
                string? translation = null;
                var translator = Translator;
                if (translator != null && !cancellation.IsCancellationRequested && !_stopRequested)
                {
                    try
                    {
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                        timeoutCts.CancelAfter(3000);
                        var translateOp = translator(task.Text);
                        if (await Task.WhenAny(translateOp, Task.Delay(3000, timeoutCts.Token)).ConfigureAwait(false) == translateOp)
                        {
                            timeoutCts.Cancel();
                            translation = await translateOp.ConfigureAwait(false);
                        }
                    }
                    catch { }
                }

                writer.Write(task.Text, task.Start, task.End, translation);
                SegmentTranslated?.Invoke(new CaptionSegment(task.Text, task.Start, task.End, translation));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }
    private async Task ReadMessagesAsync(StreamReader reader, ChannelWriter<TranslationTask> translationWriter)
    {
        await foreach (var line in ReadProtocolAsync(reader).ConfigureAwait(false))
        {
            using var input = new JsonTextReader(new StringReader(line)) { MaxDepth = 8, DateParseHandling = DateParseHandling.None };
            var message = JObject.Load(input);
            if (input.Read()) throw new IOException("Invalid caption worker message.");
            var type = message.Value<string>("type");
            if (_stopRequested) continue;
            switch (type)
            {
                case "loading": break;
                case "ready": SetState(CaptionState.Running); break;
                case "paused": SetState(message.Value<bool>("value") ? CaptionState.Paused : CaptionState.Running); Level?.Invoke(0); break;
                case "level":
                    var level = message.Value<double>("value");
                    if (double.IsFinite(level)) Level?.Invoke(Math.Clamp(level, 0, 1));
                    break;
                case "segment" when State == CaptionState.Running:
                    var text = message.Value<string>("text") ?? "";
                    var start = message.Value<double>("start"); var end = message.Value<double>("end");
                    if (text.Length > 8000 || !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end < start) throw new IOException("Invalid caption segment.");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // Stage 1: Zero-latency instantaneous English segment delivery (< 10ms)
                        Segment?.Invoke(new CaptionSegment(text, start, end, null));
                        // Stage 2: Asynchronous background translation & in-order file writing
                        translationWriter.TryWrite(new TranslationTask(text, start, end));
                    }
                    break;
                case "error":
                    var error = message.Value<string>("message") ?? "Caption recognition failed.";
                    // Terminal errors also terminate the owned process. Do not let a stuck
                    // worker keep capturing or revive the session with a late ready message.
                    throw new IOException(error[..Math.Min(2000, error.Length)]);
                case "stopped": break;
                default: break; // Forward-compatible diagnostics never become caption text.
            }
        }
    }
    private static async IAsyncEnumerable<string> ReadProtocolAsync(StreamReader reader)
    {
        var buffer = new char[4096]; var line = new StringBuilder(); int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            for (var index = 0; index < count; index++)
            {
                if (buffer[index] == '\n') { yield return line.ToString().TrimEnd('\r'); line.Clear(); }
                else
                {
                    if (line.Length >= 65536) throw new IOException("The caption worker returned an oversized message.");
                    line.Append(buffer[index]);
                }
            }
        }
        if (line.Length != 0) yield return line.ToString();
    }
    private static async Task<string> DrainErrorsAsync(StreamReader reader)
    {
        var tail = new StringBuilder(); var buffer = new char[2048]; int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        { tail.Append(buffer, 0, count); if (tail.Length > 4000) tail.Remove(0, tail.Length - 4000); }
        return tail.ToString().Trim();
    }
    public bool SetPaused(bool paused)
    {
        lock (_gate)
        {
            if (_disposed || _stopRequested || _commands == null || _state is not (CaptionState.Running or CaptionState.Paused)) return false;
            if (!_commands.Writer.TryWrite(paused ? "{\"command\":\"pause\",\"paused\":true}" : "{\"command\":\"pause\",\"paused\":false}")) return false;
            _requestedPaused = paused; return true;
        }
    }
    public async Task StopAsync()
    {
        Task session;
        lock (_gate)
        {
            _stopRequested = true; _cancel?.Cancel(); session = _session;
            if (!session.IsCompleted && !_disposed) _state = CaptionState.Stopping;
        }
        Changed?.Invoke();
        if (await Task.WhenAny(session, Task.Delay(3000)).ConfigureAwait(false) != session)
        { lock (_gate) if (_process != null) TryKill(_process); }
        await session.ConfigureAwait(false);
    }
    private static void TryKill(Process process)
    { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _stopRequested = true; _cancel?.Cancel();
            _translationQueue?.Writer.TryComplete();
            if (_process != null) TryKill(_process);
        }
        // The async finally owns stream/job disposal. Do not block the WPF dispatcher.
    }
}
