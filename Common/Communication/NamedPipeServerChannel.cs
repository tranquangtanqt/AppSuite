using System.IO.Pipes;
using System.Text;

namespace Common.Communication;

/// <summary>
/// Launcher-side half of the reference Named Pipe transport. One instance listens on
/// "AppSuite.Module.{moduleName}" and accepts a single line of text per connection from the
/// matching module process (see NamedPipeClientChannel).
/// </summary>
public sealed class NamedPipeServerChannel(string channelName) : IModuleCommunicationChannel
{
    private readonly CancellationTokenSource _cts = new();
    private NamedPipeServerStream? _pipe;
    private Task? _listenTask;

    public string ChannelName { get; } = channelName;

    public event EventHandler<ModuleMessageReceivedEventArgs>? MessageReceived;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token), cancellationToken);
        return Task.CompletedTask;
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _pipe = new NamedPipeServerStream(ChannelName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                await _pipe.WaitForConnectionAsync(cancellationToken);

                using var reader = new StreamReader(_pipe, Encoding.UTF8, leaveOpen: true);
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is not null)
                    MessageReceived?.Invoke(this, new ModuleMessageReceivedEventArgs(line));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                // Client disconnected before/while reading; accept the next connection.
            }
            finally
            {
                _pipe?.Dispose();
            }
        }
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        if (_pipe is not { IsConnected: true })
            return;

        var writer = new StreamWriter(_pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(message.AsMemory(), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cts.Cancel();
        _pipe?.Dispose();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_listenTask is not null)
        {
            try { await _listenTask; }
            catch (OperationCanceledException) { /* expected on shutdown */ }
        }
        _cts.Dispose();
    }
}
