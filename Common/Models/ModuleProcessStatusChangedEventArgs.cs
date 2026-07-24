namespace Common.Models;

public sealed class ModuleProcessStatusChangedEventArgs : EventArgs
{
    public ModuleProcessStatusChangedEventArgs(ModuleProcessInfo processInfo) => ProcessInfo = processInfo;

    public ModuleProcessInfo ProcessInfo { get; }
}
