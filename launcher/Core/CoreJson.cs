using System.Text.Json.Serialization;

namespace HookshotLauncher.Core;

/// The launcher's own files (builds.json, settings.json, a toolchain's record), camelCase and indented, compiled ahead so they
/// need no reflection
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
                             DefaultIgnoreCondition = JsonIgnoreCondition.Never, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip)]
[JsonSerializable(typeof(RegistryFile))]
[JsonSerializable(typeof(LauncherSettings))]
[JsonSerializable(typeof(Toolchains.Record))]
internal sealed partial class CoreJson : JsonSerializerContext
{
}
