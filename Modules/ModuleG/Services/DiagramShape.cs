namespace ModuleG.Services;

/// <summary>1 box or connector line parsed out of a worksheet's raw DrawingML XML by
/// <see cref="DiagramXmlReader"/>, in absolute EMU coordinates (English Metric Units, 914400/inch -
/// the unit DrawingML itself uses for `a:off`/`a:ext`), ready for <see cref="DiagramRenderer"/> to
/// scale/translate into pixels. Deliberately flat/untyped (no shape-specific subclasses) since the
/// renderer only branches on <see cref="Preset"/> - see its doc comment for which presets are
/// recognized and how everything else degrades.</summary>
internal sealed class DiagramShape
{
    public required bool IsConnector { get; init; }
    public required string Preset { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }

    /// <summary>0..1 fraction along the dominant axis where a "bentConnectorN" elbow bends - from the
    /// DrawingML `adj1` guide (default 50000 = 0.5 = midpoint) - unused for straight connectors/boxes.</summary>
    public double BendFraction { get; init; } = 0.5;

    public string? Text { get; init; }
    public uint? FillColorArgb { get; init; }
    public uint? LineColorArgb { get; init; }
    public double LineWidthEmu { get; init; }
    public bool HasStartArrow { get; init; }
    public bool HasEndArrow { get; init; }
}
