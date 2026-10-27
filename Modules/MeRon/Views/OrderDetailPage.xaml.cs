using System.Runtime.InteropServices.WindowsRuntime;
using MeRon.Services;
using MeRon.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MeRon.Views;

public sealed partial class OrderDetailPage : Page, IOrderListPage
{
    public OrderDetailViewModel ViewModel { get; } = new();

    private OrderDetailArgs _args = new(Core.Models.OrderFilter.Today());

    public OrderDetailPage()
    {
        InitializeComponent();
    }

    /// <summary>Mỗi lần vào (kể cả quay lại từ form sửa đơn) đọc lại theo bộ lọc đã truyền.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _args = e.Parameter as OrderDetailArgs ?? new OrderDetailArgs(Core.Models.OrderFilter.Today());
        ReloadOrders();
    }

    public void ReloadOrders()
    {
        var args = _args;
        try
        {
            ViewModel.Load(args.Filter, args.Title);
        }
        catch (Exception ex)
        {
            AppLog.For("Orders").LogError(ex, "Tải Chi tiết lỗi");
            ViewModel.Message = "Không đọc được đơn hàng: " + ex.Message;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void Order_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OrderCardViewModel card })
        {
            Frame.Navigate(typeof(OrderFormPage), new OrderFormArgs(card.Id));
        }
    }

    private async void PayAll_Click(object sender, RoutedEventArgs e)
    {
        if (!await Dialogs.ConfirmAsync(XamlRoot, "Xác nhận",
                $"Bạn có chắc chắn muốn Thanh toán toàn bộ? {ViewModel.ShippingCount} đơn \"Đã lên đơn\" sẽ chuyển sang \"Đã thanh toán\".",
                "Thanh toán"))
        {
            return;
        }
        if (ViewModel.PayAll() is { } error)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, "Không thanh toán được", error);
        }
    }

    /// <summary>
    /// Như nút "Chụp ảnh" của điện thoại (chụp danh sách gộp theo khách để gửi): chụp toàn bộ danh sách (cả phần phải cuộn)
    /// → chép vào clipboard + lưu PNG vào Pictures\MeRon.
    /// </summary>
    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Groups.Count == 0)
        {
            await Dialogs.ShowMessageAsync(XamlRoot, "Chụp ảnh", "Không có đơn hàng để chụp ảnh.");
            return;
        }
        try
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(CaptureArea);
            var pixels = await bitmap.GetPixelsAsync();
            var scale = XamlRoot.RasterizationScale;

            var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight,
                96 * scale, 96 * scale, pixels.ToArray());
            await encoder.FlushAsync();

            // Tên file theo Core\CaptureFiles (ảnh cũ tự vào Thùng rác sau số ngày ở Cài đặt); mở từ trang Công nợ thì tên riêng.
            Directory.CreateDirectory(Core.CaptureFiles.Folder);
            var file = Core.CaptureFiles.NewPath(
                string.IsNullOrEmpty(ViewModel.CaptureTitle) ? Core.CaptureFiles.DetailPrefix : Core.CaptureFiles.DebtPrefix, DateTime.Now);
            stream.Seek(0);
            await using (var output = File.Create(file))
            {
                await stream.AsStreamForRead().CopyToAsync(output);
            }

            stream.Seek(0);
            var package = new DataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            Clipboard.SetContent(package);

            AppLog.For("Orders").LogInformation("Chụp ảnh Chi tiết {W}x{H}: {File}", bitmap.PixelWidth, bitmap.PixelHeight, file);
            ViewModel.Message = $"Đã chép ảnh vào clipboard (Ctrl+V để dán) và lưu {file}";
        }
        catch (Exception ex)
        {
            AppLog.For("Orders").LogError(ex, "Chụp ảnh lỗi");
            await Dialogs.ShowMessageAsync(XamlRoot, "Không chụp được ảnh", ex.Message);
        }
    }
}
