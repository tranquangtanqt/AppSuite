using System.Collections.ObjectModel;
using Common.Logging;
using Common.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace MainLauncher.ViewModels;

/// <summary>Tails the in-memory log sink so the Logs page shows launcher/module activity live.</summary>
public partial class LogsViewModel : ObservableObject
{
    private const int MaxEntries = 500;
    private readonly DispatcherQueue? _dispatcherQueue;

    public LogsViewModel(InMemoryLoggerProvider logProvider)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        logProvider.EntryLogged += (_, entry) => Append(entry);
    }

    public ObservableCollection<LogEntry> Entries { get; } = new();

    private void Append(LogEntry entry)
    {
        void Add()
        {
            Entries.Insert(0, entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(Entries.Count - 1);
        }

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
            Add();
        else
            _dispatcherQueue.TryEnqueue(Add);
    }
}
