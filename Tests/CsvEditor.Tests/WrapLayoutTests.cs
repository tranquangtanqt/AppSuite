using SharedUI.Helpers;

namespace CsvEditor.Tests;

public class WrapLayoutTests
{
    [Fact]
    public void Empty_list_has_no_size()
    {
        var (slots, width, height) = WrapLayout.Compute([], 500, 8);

        Assert.Empty(slots);
        Assert.Equal(0, width);
        Assert.Equal(0, height);
    }

    [Fact]
    public void Items_that_fit_stay_on_one_row_with_spacing()
    {
        var (slots, width, height) = WrapLayout.Compute([(100, 30), (50, 30)], 158, 8);

        Assert.Equal(new WrapLayout.Slot(0, 0, 30), slots[0]);
        Assert.Equal(new WrapLayout.Slot(108, 0, 30), slots[1]);
        Assert.Equal(158, width);
        Assert.Equal(30, height);
    }

    [Fact]
    public void Item_past_the_edge_moves_to_next_row()
    {
        var (slots, width, height) = WrapLayout.Compute([(100, 30), (51, 30)], 158, 8);

        Assert.Equal(new WrapLayout.Slot(0, 38, 30), slots[1]);
        Assert.Equal(100, width);
        Assert.Equal(68, height);
    }

    [Fact]
    public void Row_height_is_the_tallest_item_of_that_row()
    {
        var (slots, _, height) = WrapLayout.Compute([(50, 20), (50, 34), (200, 10)], 110, 8);

        Assert.Equal(34, slots[0].RowHeight);
        Assert.Equal(34, slots[1].RowHeight);
        Assert.Equal(new WrapLayout.Slot(0, 42, 10), slots[2]);
        Assert.Equal(52, height);
    }

    [Fact]
    public void Item_wider_than_the_row_stays_alone_without_an_empty_row_above()
    {
        var (slots, _, height) = WrapLayout.Compute([(300, 30), (50, 30)], 100, 8);

        Assert.Equal(new WrapLayout.Slot(0, 0, 30), slots[0]);
        Assert.Equal(new WrapLayout.Slot(0, 38, 30), slots[1]);
        Assert.Equal(68, height);
    }

    [Fact]
    public void Infinite_width_never_wraps()
    {
        var (slots, width, height) = WrapLayout.Compute([(100, 30), (100, 30)], double.PositiveInfinity, 8);

        Assert.Equal(0, slots[1].Y);
        Assert.Equal(208, width);
        Assert.Equal(30, height);
    }
}
