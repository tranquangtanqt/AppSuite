namespace ModuleG.Models;

/// <summary>POCO thuần, mutable (public get/set, constructor không tham số) - bắt buộc để
/// <see cref="System.Xml.Serialization.XmlSerializer"/> hoạt động, khác kiểu required/init dùng ở
/// các model khác trong module này.</summary>
public sealed class SourceFolderSettings
{
    public string RootFolder { get; set; } = string.Empty;
}
