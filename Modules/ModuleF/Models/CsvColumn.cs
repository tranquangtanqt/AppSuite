namespace ModuleF.Models;

/// <summary>Metadata of one column: its header name. RenameColumn mutates <see cref="Name"/> in place
/// so existing bindings/commands keep referring to the same instance across a rename.</summary>
public sealed class CsvColumn
{
    public CsvColumn(string name)
    {
        Name = name;
    }

    public string Name { get; set; }
}
