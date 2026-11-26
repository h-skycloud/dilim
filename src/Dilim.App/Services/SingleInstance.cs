// Keeps one Dilim per Windows session, or per user across Unix launcher/terminal sessions.
// Later launches forward arguments through a named pipe.

using System.IO.Pipes;
using System.Text.Json;
using Dilim.Core.Settings;

namespace Dilim.App.Services;

/// <summary>Named mutex + named pipe single-instance helper.</summary>
public sealed class SingleInstance : IDisposable
{
    // string.GetHashCode() is randomized per process, so a stable hash is required for both processes to agree.
    internal static readonly string DefaultName = NameFor(Environment.UserName, UserDataStore.OverrideDirectory);
    private readonly Mutex _mutex;
    private readonly string _name;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Mutex mutex, string name, bool isFirst)
    {
        _mutex = mutex;
        _name = name;
        IsFirst = isFirst;
    }

    public bool IsFirst { get; }

    /// <summary>Raised on a background thread with the arguments of a later launch.</summary>
    public event Action<string[]>? ArgumentsReceived;

    public static SingleInstance Acquire() => Acquire(DefaultName);

    /// <summary>
    /// The mutex and pipe name for a user. A copy started with DILIM_DATA_DIR (<paramref name="dataDirectoryOverride"/>)
    /// gets a name of its own, so it neither forwards to nor receives from the Dilim the user runs.
    /// </summary>
    internal static string NameFor(string userName, string? dataDirectoryOverride)
    {
        var key = dataDirectoryOverride is null ? userName : userName + "|" + dataDirectoryOverride.ToUpperInvariant();
        return Core.ProductIdentity.DataFolder + "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..12];
    }

    /// <summary>The instance under another name (tests, which must not reach a Dilim that is running).</summary>
    internal static SingleInstance Acquire(string name)
    {
        bool createdNew;
        var mutex = OperatingSystem.IsWindows()
            ? new Mutex(initiallyOwned: true, @"Local\" + name, out createdNew)
            : new Mutex(initiallyOwned: true, name,
                new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false }, out createdNew);
        return new SingleInstance(mutex, name, createdNew);
    }

    /// <summary>Starts listening for later launches (first instance only).</summary>
    public void StartListening()
    {
        if (!IsFirst)
            return;
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_name, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_cts.Token);
                    var args = await JsonSerializer.DeserializeAsync(server, AppJsonContext.Default.StringArray, _cts.Token);
                    if (args is not null)
                        ArgumentsReceived?.Invoke(args);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    // A client disconnected half-way; keep serving.
                }
                catch (UnauthorizedAccessException)
                {
                    // The pipe name is still held by a Dilim with other rights (one that just restarted this one
                    // without administrator rights and is closing): try again once it is gone.
                    try
                    {
                        await Task.Delay(500, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        });
    }

    /// <summary>
    /// Sends this process's arguments to the first instance. It only listens once its services are up, which can take
    /// over ten seconds on a slow or busy computer: a second launch (or the installer's --quit) right after the first
    /// lost its arguments after one 3-second try. Tries again until <paramref name="wait"/> has passed; false when the
    /// first instance never answered (it is shutting down, or hung) or refused this process.
    /// </summary>
    public bool ForwardToFirst(string[] args, TimeSpan wait) => Forward(args, wait) == ForwardResult.Delivered;

    /// <summary>
    /// Like <see cref="ForwardToFirst"/>, telling why it failed. A pipe that keeps refusing access belongs to a Dilim
    /// running with other rights (as administrator, while this one isn't): that never changes, so it gives up after a
    /// few tries instead of waiting the whole time.
    /// </summary>
    public ForwardResult Forward(string[] args, TimeSpan wait)
    {
        var deadline = DateTime.UtcNow + wait;
        var refusals = 0;
        while (true)
        {
            var left = deadline - DateTime.UtcNow;
            try
            {
                using var client = new NamedPipeClientStream(".", _name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect((int)Math.Clamp(left.TotalMilliseconds, 100, 1000));
                Dilim.Platform.Windows.Foreground.AllowOtherProcess(); // its donut takes the focus (Esc, click away)
                JsonSerializer.Serialize(client, args, AppJsonContext.Default.StringArray);
                return ForwardResult.Delivered;
            }
            catch (UnauthorizedAccessException)
            {
                // Also thrown for a moment while the pipe is being recreated between two launches: try a few times.
                if (++refusals >= RefusalsBeforeGivingUp)
                    return ForwardResult.AccessDenied;
                if (DateTime.UtcNow >= deadline)
                    return ForwardResult.NoAnswer;
                Thread.Sleep(100);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                if (DateTime.UtcNow >= deadline)
                    return ForwardResult.NoAnswer;
                Thread.Sleep(100); // the pipe is busy with another launch, or not there yet
            }
        }
    }

    /// <summary>Access-denied answers in a row after which <see cref="Forward"/> stops trying.</summary>
    private const int RefusalsBeforeGivingUp = 5;

    /// <summary>
    /// Waits until the first instance has exited (it releases the mutex then), e.g. after forwarding <c>--quit</c>.
    /// Returns false on timeout.
    /// </summary>
    public bool WaitForFirstToExit(TimeSpan timeout)
    {
        if (IsFirst)
            return true;
        try
        {
            if (!_mutex.WaitOne(timeout))
                return false;
        }
        catch (AbandonedMutexException)
        {
            // The first instance ended without releasing the mutex (killed): it is gone all the same.
        }
        _mutex.ReleaseMutex();
        return true;
    }

    /// <summary>
    /// Where the running Dilim notes its version and process (next to the settings). Dilim 0.4.0 and earlier don't
    /// write it, so a running Dilim without this note (or with a note from a process that is gone) is an older one.
    /// </summary>
    internal static string RunningNote => Path.Combine(UserDataStore.DefaultDirectory, "running.txt");

    /// <summary>The first instance writes its version, process id and executable.</summary>
    public void NoteRunning(string version)
    {
        if (!IsFirst) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RunningNote)!);
            File.WriteAllLines(RunningNote, [version, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), Environment.ProcessPath ?? ""]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The version of the Dilim that holds the lock, from its note; null when it wrote none (an older Dilim) or the
    /// note belongs to a process that has ended.
    /// </summary>
    public static string? RunningVersion(TimeSpan? settle = null)
    {
        // A Dilim started a moment ago (Explorer starts one per selected file) writes its note right after taking the
        // lock: wait a little for it before deciding the running one is old.
        var until = DateTime.UtcNow + (settle ?? TimeSpan.Zero);
        while (true)
        {
            var version = ReadNote();
            if (version is not null || DateTime.UtcNow >= until) return version;
            Thread.Sleep(100);
        }
    }

    private static string? ReadNote()
    {
        try
        {
            var lines = File.ReadAllLines(RunningNote);
            if (lines.Length < 2 || !int.TryParse(lines[1], out var pid)) return null;
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.HasExited ? null : lines[0];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a launch of <paramref name="mine"/> should replace the running Dilim: it is older (or too old to say).
    /// A launch of an older copy (an old shortcut) leaves a newer one running.
    /// </summary>
    internal static bool ShouldReplace(string? running, string mine) =>
        running is null
        || (Version.TryParse(running.Split('-', '+')[0], out var r) && Version.TryParse(mine.Split('-', '+')[0], out var m) && m > r);

    /// <summary>Stops listening and releases the mutex. Safe to call twice (a restart releases it before shutting down).</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cts.Cancel();
        if (IsFirst)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }

    private bool _disposed;
}

/// <summary>Outcome of <see cref="SingleInstance.Forward"/>.</summary>
public enum ForwardResult
{
    /// <summary>The running Dilim has the arguments.</summary>
    Delivered,

    /// <summary>Nobody took them in time: the running Dilim is starting, shutting down or hung.</summary>
    NoAnswer,

    /// <summary>The running Dilim's pipe refuses this process: it runs with other rights (as administrator).</summary>
    AccessDenied,
}
