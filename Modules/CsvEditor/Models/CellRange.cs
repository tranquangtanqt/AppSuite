namespace CsvEditor.Models;

/// <summary>Vùng ô chữ nhật trên bảng đang hiện: <see cref="Anchor"/> là ô bắt đầu chọn, <see cref="Active"/> là ô đang kéo /
/// mũi tên tới, có thể nằm ở bất kỳ phía nào của Anchor.</summary>
public readonly record struct CellRange(CellRef Anchor, CellRef Active)
{
    public int Top => Math.Min(Anchor.RowIndex, Active.RowIndex);

    public int Bottom => Math.Max(Anchor.RowIndex, Active.RowIndex);

    public int Left => Math.Min(Anchor.ColumnIndex, Active.ColumnIndex);

    public int Right => Math.Max(Anchor.ColumnIndex, Active.ColumnIndex);

    public bool IsSingleCell => Anchor == Active;

    public bool Contains(CellRef cell) =>
        cell.RowIndex >= Top && cell.RowIndex <= Bottom && cell.ColumnIndex >= Left && cell.ColumnIndex <= Right;
}
