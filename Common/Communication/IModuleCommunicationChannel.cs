namespace Common.Communication;

/// <summary>
/// Transport-agnostic contract for launcher &lt;-&gt; module communication. MainLauncher and modules
/// depend only on this interface, so the transport can move from Named Pipes (the reference
/// implementation here) to a local REST API or gRPC later without touching call sites.
/// </summary>
public interface IModuleCommunicationChannel : IAsyncDisposable
{
    string ChannelName { get; }

    event EventHandler<ModuleMessageReceivedEventArgs>? MessageReceived;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task SendAsync(string message, CancellationToken cancellationToken = default);
}

public sealed class ModuleMessageReceivedEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}
