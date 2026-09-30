using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTools.Core;
using FileTools.Services;
using Microsoft.Extensions.Logging;

namespace FileTools.ViewModels;

/// <summary>Khối "Đầu ra" dùng chung: encoding (mặc định UTF-8) + kiểu xuống dòng. Bind SelectedIndex của ComboBox
/// - thứ tự mục trong XAML trùng thứ tự enum.</summary>
public sealed partial class OutputSettings : ObservableObject
{
    public static readonly string[] EncodingNames = Enum.GetValues<OutputEncoding>().Select(TextEncodings.DisplayName).ToArray();
    public static readonly string[] NewlineNames = ["Giữ như nguồn", "CRLF (Windows)", "LF (Unix)"];

    [ObservableProperty]
    private int _encodingIndex;

    [ObservableProperty]
    private int _newlineIndex;

    public OutputEncoding Encoding => (OutputEncoding)Math.Max(0, EncodingIndex);
    public NewlineMode Newline => (NewlineMode)Math.Max(0, NewlineIndex);
}

/// <summary>
/// Nền chung cho mọi trang: chạy 1 thao tác trên luồng nền, tiến độ (gộp ~10 lần/giây ở Core), Huỷ, nhật ký,
/// mở kết quả. Lỗi được bắt, ghi nhật ký + log file - không bao giờ làm sập app.
/// </summary>
public abstract partial class JobViewModel : ObservableObject
{
    private static readonly ILogger Log = AppLog.For("Job");
    private CancellationTokenSource? _cts;

    public OutputSettings Output { get; } = new();

    public ObservableCollection<string> Messages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _status = "Sẵn sàng";

    /// <summary>File hoặc thư mục kết quả của lần chạy gần nhất.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyCanExecuteChangedFor(nameof(OpenResultCommand), nameof(RevealResultCommand))]
    private string? _resultPath;

    public bool HasResult => !string.IsNullOrEmpty(ResultPath);

    /// <summary>Chạy <paramref name="work"/> ở nền; nó trả về đường dẫn kết quả (hoặc null) và các dòng nhật ký.</summary>
    protected async Task RunAsync(string title, Func<IProgress<JobProgress>, CancellationToken, (string? Result, IEnumerable<string> Notes)> work)
    {
        if (IsBusy)
        {
            return;
        }
        _cts = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0;
        Status = title + "...";
        AddMessage($"▶ {title}");
        var progress = new Progress<JobProgress>(p =>
        {
            Progress = p.Fraction * 100;
            Status = p.Message is null ? $"{title}... {p.Fraction:P0}" : $"{title}... {p.Fraction:P0} · {p.Message}";
        });
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var token = _cts.Token;
            var (result, notes) = await Task.Run(() => work(progress, token), token);
            // Nhật ký mới nhất ở trên cùng, nhưng các dòng của 1 lần chạy vẫn đọc từ trên xuống đúng thứ tự.
            foreach (var note in notes.Reverse())
            {
                AddMessage(note);
            }
            ResultPath = result;
            Progress = 100;
            Status = $"Xong ({watch.Elapsed.TotalSeconds:0.0} s)";
            AddMessage($"✔ Xong sau {watch.Elapsed.TotalSeconds:0.0} s");
            Log.LogInformation("{Title}: xong sau {Seconds:0.0} s → {Result}", title, watch.Elapsed.TotalSeconds, result);
        }
        catch (OperationCanceledException)
        {
            Status = "Đã huỷ";
            AddMessage("✖ Đã huỷ - không để lại file dở.");
        }
        catch (Exception ex)
        {
            Status = "Lỗi: " + ex.Message;
            AddMessage("✖ Lỗi: " + ex.Message);
            Log.LogError(ex, "{Title} lỗi", title);
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    protected void AddMessage(string message) => Messages.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(HasResult))]
    private void OpenResult() => TryShell(() => Shell.Open(ResultPath!));

    [RelayCommand(CanExecute = nameof(HasResult))]
    private void RevealResult() => TryShell(() => Shell.Reveal(ResultPath!));

    [RelayCommand]
    private void ClearMessages() => Messages.Clear();

    private void TryShell(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AddMessage("✖ Không mở được: " + ex.Message);
        }
    }

    protected static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.##} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };
}
