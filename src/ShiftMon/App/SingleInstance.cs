using System.IO.Pipes;
using System.Text;

namespace ShiftMon.App;

internal sealed class SingleInstance : IDisposable
{
    const string MutexName = @"Local\ShiftMon.Single";
    const string PipeName = "ShiftMon.Pipe.v1";

    readonly Mutex _mutex;
    readonly bool _owned;
    CancellationTokenSource? _cts;
    Task? _loop;
    NamedPipeServerStream? _listening;

    SingleInstance(Mutex mutex, bool owned)
    {
        _mutex = mutex;
        _owned = owned;
    }

    public static SingleInstance Acquire()
    {
        var mutex = new Mutex(false, MutexName);
        try
        {
            return new SingleInstance(mutex, mutex.WaitOne(0));
        }
        catch (AbandonedMutexException)
        {
            return new SingleInstance(mutex, true);
        }
    }

    public bool IsPrimary => _owned;

    public void StartServer(Action<string> handle, SynchronizationContext context)
    {
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => Listen(_cts.Token, handle, context));
    }

    public static bool TrySend(string command)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
                pipe.Connect(250);
                var outgoing = Encoding.UTF8.GetBytes(command + "\n");
                pipe.Write(outgoing, 0, outgoing.Length);
                pipe.Flush();
                var response = ReadLine(pipe);
                return response != null && response.StartsWith("OK", StringComparison.Ordinal);
            }
            catch (TimeoutException)
            {
                Thread.Sleep(150);
            }
            catch (IOException)
            {
                Thread.Sleep(150);
            }
        }

        return false;
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ukončení */ }
        try { _listening?.Dispose(); } catch { /* ukončení čekání na příkaz */ }
        try { _loop?.Wait(800); } catch { /* ukončení */ }
        _cts?.Dispose();
        if (_owned)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    void Listen(CancellationToken token, Action<string> handle, SynchronizationContext? context)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None);
                _listening = pipe;
                pipe.WaitForConnection();
                if (token.IsCancellationRequested)
                    break;
                var line = ReadLine(pipe);
                var response = "OK";
                if (!string.IsNullOrWhiteSpace(line))
                {
                    if (context == null)
                    {
                        response = "ERR bez vlákna rozhraní";
                    }
                    else
                    {
                        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                        context.Post(_ =>
                        {
                            try
                            {
                                handle(line);
                                done.TrySetResult("OK");
                            }
                            catch (Exception ex)
                            {
                                done.TrySetResult("ERR " + ex.Message);
                            }
                        }, null);
                        response = done.Task.Wait(TimeSpan.FromSeconds(20))
                            ? done.Task.Result
                            : "ERR časový limit";
                    }
                }

                var incoming = Encoding.UTF8.GetBytes(response + "\n");
                pipe.Write(incoming, 0, incoming.Length);
                pipe.Flush();
                pipe.Disconnect();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLog.Info("Příkazový kanál: " + ex.Message);
                Thread.Sleep(200);
            }
            finally
            {
                if (ReferenceEquals(_listening, pipe))
                    _listening = null;
                pipe?.Dispose();
            }
        }
    }

    static string? ReadLine(Stream stream)
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var value = stream.ReadByte();
            if (value < 0)
                return buffer.Length == 0 ? null : buffer.ToString();
            if (value == '\n')
                return buffer.ToString();
            if (value != '\r')
                buffer.Append((char)value);
        }
    }
}
