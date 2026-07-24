using System.IO.Pipes;
using System.Text;

namespace Common.Communication;

/// <summary>
/// Module-side half of the reference Named Pipe transport. A module connects out to the launcher's
/// pipe (named after the module itself) and sends a single handshake/status line. Optional: a
/// module only needs this if it wants to report readiness back to the launcher.
/// </summary>
public sealed class NamedPipeClientChannel(string channelName, string serverName = ".") : IModuleCommunicationChannel
{
    private NamedPipeClientStream? _pipe;

    public string ChannelName { get; } = channelName;

    public event EventHandler<ModuleMessageReceivedEventArgs>? MessageReceived
    {
        add { }
        remove { }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _pipe = new NamedPipeClientStream(serverName, ChannelName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(3000, cancellationToken);
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
        _pipe?.Dispose();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _pipe?.Dispose();
        return ValueTask.CompletedTask;
    }
}
