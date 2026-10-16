using System.Collections.ObjectModel;
using ScreenCapture.Commands;
using ScreenCapture.Models;
using SkiaSharp;

namespace ScreenCapture.Tests;

public class UndoRedoTests
{
    private static RectangleAnnotation Rect(float left) => new() { Bounds = new SKRect(left, 10, left + 20, 30) };

    [Fact]
    public void Add_remove_undo_redo_keep_order()
    {
        var shapes = new ObservableCollection<AnnotationShape>();
        var stack = new UndoRedoStack();
        var a = Rect(0);
        var b = Rect(50);
        stack.Do(new AddAnnotationCommand(shapes, a));
        stack.Do(new AddAnnotationCommand(shapes, b));
        stack.Do(new RemoveAnnotationCommand(shapes, a));
        Assert.Equal([b], shapes);
        Assert.Equal("Xoá Hình chữ nhật", stack.UndoDescription);

        stack.Undo();
        Assert.Equal([a, b], shapes); // xoá rồi Undo: trả về đúng vị trí cũ
        stack.Undo();
        stack.Undo();
        Assert.Empty(shapes);
        Assert.False(stack.CanUndo);

        stack.Redo();
        Assert.Equal([a], shapes);
        Assert.True(stack.CanRedo);
    }

    [Fact]
    public void New_edit_after_undo_clears_redo()
    {
        var shapes = new ObservableCollection<AnnotationShape>();
        var stack = new UndoRedoStack();
        stack.Do(new AddAnnotationCommand(shapes, Rect(0)));
        stack.Undo();
        stack.Do(new AddAnnotationCommand(shapes, Rect(40)));
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Reorder_moves_and_undo_moves_back()
    {
        var shapes = new ObservableCollection<AnnotationShape> { Rect(0), Rect(1), Rect(2) };
        var first = shapes[0];
        var stack = new UndoRedoStack();
        stack.Do(new ReorderAnnotationCommand(shapes, 0, 2));
        Assert.Same(first, shapes[2]);
        Assert.Equal("Đưa lên trên", stack.UndoDescription);
        stack.Undo();
        Assert.Same(first, shapes[0]);
    }

    [Fact]
    public void Dirty_tracks_position_relative_to_last_save()
    {
        var shapes = new ObservableCollection<AnnotationShape>();
        var stack = new UndoRedoStack();
        Assert.False(stack.IsDirty);
        stack.Do(new AddAnnotationCommand(shapes, Rect(0)));
        Assert.True(stack.IsDirty);
        stack.MarkClean(); // Lưu
        Assert.False(stack.IsDirty);
        stack.Do(new AddAnnotationCommand(shapes, Rect(10)));
        Assert.True(stack.IsDirty);
        stack.Undo(); // về đúng chỗ đã lưu
        Assert.False(stack.IsDirty);
        stack.Undo(); // lùi quá chỗ đã lưu
        Assert.True(stack.IsDirty);
    }

    [Fact]
    public void Crop_keeps_shapes_inside_offsets_them_and_undo_restores()
    {
        var oldBitmap = TestSupport.Solid(200, 100, SKColors.White);
        var newBitmap = TestSupport.Solid(100, 50, SKColors.White);
        var current = oldBitmap;
        var inside = Rect(60);   // 60..80 x 10..30
        var outside = Rect(170); // ngoài vùng cắt
        var shapes = new ObservableCollection<AnnotationShape> { inside, outside };
        var stack = new UndoRedoStack();

        stack.Do(new CropCommand(b => current = b, shapes, oldBitmap, newBitmap, new SKRect(50, 0, 150, 50)));
        Assert.Same(newBitmap, current);
        Assert.Equal([inside], shapes);
        Assert.Equal(new SKRect(10, 10, 30, 30), inside.Bounds); // dịch theo góc vùng cắt

        stack.Undo();
        Assert.Same(oldBitmap, current);
        Assert.Equal([inside, outside], shapes);
        Assert.Equal(new SKRect(60, 10, 80, 30), inside.Bounds);
    }
}
