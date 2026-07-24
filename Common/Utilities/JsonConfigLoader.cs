using System.Text.Json;
using Common.Models;

namespace Common.Utilities;

/// <summary>Reads and writes modules.json (or any equivalent list of <see cref="ModuleConfig"/>).</summary>
public static class JsonConfigLoader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new(ReadOptions) { WriteIndented = true };

    public static async Task<List<ModuleConfig>> LoadModulesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Module configuration file not found: {filePath}", filePath);

        await using var stream = File.OpenRead(filePath);
        var modules = await JsonSerializer.DeserializeAsync<List<ModuleConfig>>(stream, ReadOptions, cancellationToken);
        return modules ?? [];
    }

    public static async Task SaveModulesAsync(string filePath, IEnumerable<ModuleConfig> modules, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, modules, WriteOptions, cancellationToken);
    }
}
