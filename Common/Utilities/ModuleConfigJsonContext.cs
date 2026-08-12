using System.Text.Json;
using System.Text.Json.Serialization;
using Common.Models;

namespace Common.Utilities;

/// <summary>
/// Source-generated JSON metadata for modules.json. Reflection-based JsonSerializer calls throw at
/// runtime once PublishTrimmed is on (MainLauncher's Release config), so JsonConfigLoader must use
/// this context instead of the reflection-based overloads.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(List<ModuleConfig>))]
internal sealed partial class ModuleConfigJsonContext : JsonSerializerContext
{
}
