namespace ModuleB.Models;

public sealed class SavedFolder
{
    public SavedFolder(int id, string path)
    {
        Id = id;
        Path = path;
        Name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
    }

    public int Id { get; }

    public string Path { get; }

    public string Name { get; }
}
