using ScreenCapture.Models;

namespace ScreenCapture.Tests;

/// <summary>Mở lại tab vừa đóng (Ctrl+Shift+T, 2026-10-19): thứ tự như trình duyệt, giới hạn số tab / bộ nhớ.</summary>
public class ClosedTabStackTests
{
    [Fact]
    public void Reopens_most_recently_closed_first_with_its_old_position()
    {
        var stack = new ClosedTabStack<string>();
        stack.Push("a", 0, 10);
        stack.Push("b", 3, 10);

        Assert.Equal("b", stack.Peek());
        Assert.Equal(("b", 3), stack.Pop() is { } e1 ? (e1.Item, e1.Index) : default);
        Assert.Equal(("a", 0), stack.Pop() is { } e2 ? (e2.Item, e2.Index) : default);
        Assert.Null(stack.Pop());
        Assert.Null(stack.Peek());
    }

    [Fact]
    public void Drops_oldest_beyond_max_count_and_reports_it()
    {
        var stack = new ClosedTabStack<string> { MaxCount = 3 };
        foreach (var name in new[] { "1", "2", "3" })
        {
            Assert.Empty(stack.Push(name, 0, 1));
        }
        Assert.Equal(["1"], stack.Push("4", 0, 1));
        Assert.Equal(3, stack.Count);
        Assert.Equal(["4", "3", "2"], [stack.Pop()!.Item, stack.Pop()!.Item, stack.Pop()!.Item]);
    }

    [Fact]
    public void Drops_oldest_beyond_memory_limit_but_always_keeps_the_newest()
    {
        var stack = new ClosedTabStack<string> { MaxBytes = 100 };
        stack.Push("a", 0, 40);
        stack.Push("b", 0, 40);
        Assert.Equal(["a"], stack.Push("c", 0, 40)); // 120 > 100 → bỏ a
        Assert.Equal(80, stack.TotalBytes);

        Assert.Equal(["b", "c"], stack.Push("huge", 0, 500)); // 1 ảnh lớn hơn cả giới hạn: vẫn mở lại được ảnh đó
        Assert.Equal(1, stack.Count);
        Assert.Equal("huge", stack.Peek());
    }

    [Fact]
    public void Limits_from_settings_trim_immediately_and_zero_turns_it_off()
    {
        var stack = new ClosedTabStack<string>();
        foreach (var name in new[] { "1", "2", "3", "4" })
        {
            stack.Push(name, 0, 10);
        }
        Assert.Equal(["1", "2"], stack.ApplyLimits(2, 1000)); // giảm còn 2 tab → bỏ ngay 2 tab cũ nhất
        Assert.Equal("4", stack.Peek());
        Assert.Equal(["3"], stack.ApplyLimits(10, 15));      // 20 byte > 15 → bỏ tab cũ
        Assert.Equal(["4"], stack.ApplyLimits(0, 1000));     // 0 = tắt: bỏ hết
        Assert.Equal(["5"], stack.Push("5", 0, 1));          // tắt rồi: đóng là bỏ luôn
        Assert.Equal(0, stack.Count);

        // cài đặt mặc định khớp giới hạn mặc định
        var settings = new AppSettings();
        Assert.Equal((10, 300), (settings.ClosedTabsMaxCount, settings.ClosedTabsMaxMegabytes));
    }

    [Fact]
    public void Default_limits_and_change_notifications()
    {
        var stack = new ClosedTabStack<string>();
        Assert.Equal(10, stack.MaxCount);
        Assert.Equal(300L * 1024 * 1024, stack.MaxBytes);
        int changes = 0;
        stack.Changed += (_, _) => changes++;
        stack.Push("a", -5, -1); // vị trí / byte âm → 0
        Assert.Equal((0, 0L), (stack.Pop()!.Index, stack.TotalBytes));
        stack.Pop(); // trống: không báo
        Assert.Equal(2, changes);
    }
}
