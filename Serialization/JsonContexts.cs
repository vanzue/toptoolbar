using System.Text.Json;
using System.Text.Json.Serialization;

namespace TopToolbar.Serialization;

internal sealed class CamelCaseStringEnumConverter : JsonStringEnumConverter
{
    public CamelCaseStringEnumConverter()
        : base(JsonNamingPolicy.CamelCase)
    {
    }
}

internal sealed class CamelCaseStringEnumConverterDisallowInts : JsonStringEnumConverter
{
    public CamelCaseStringEnumConverterDisallowInts()
        : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}

internal sealed class ProviderIconTypeConverter : JsonStringEnumConverter<TopToolbar.Models.Providers.ProviderIconType>
{
    public ProviderIconTypeConverter()
        : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(TopToolbar.Extensions.ExtensionManifest))]
internal partial class ExtensionManifestJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    Converters = new[] { typeof(ProviderIconTypeConverter) },
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(TopToolbar.Models.Providers.WorkspaceProviderConfig))]
[JsonSerializable(typeof(TopToolbar.Models.Providers.WorkspaceDefinition))]
internal partial class WorkspaceProviderJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    Converters = new[] { typeof(CamelCaseStringEnumConverter) },
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(TopToolbar.Providers.Configuration.ProviderConfig))]
[JsonSerializable(typeof(TopToolbar.Providers.Configuration.McpProviderConfig))]
internal partial class ProviderConfigJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    AllowTrailingCommas = true,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(TopToolbar.Models.ToolbarConfig))]
internal partial class ToolbarConfigJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    WriteIndented = true,
    Converters = new[] { typeof(CamelCaseStringEnumConverter) },
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(TopToolbar.Services.Profiles.Models.ProviderDefinitionFile))]
[JsonSerializable(typeof(TopToolbar.Services.Profiles.Models.ProfilesRegistry))]
[JsonSerializable(typeof(TopToolbar.Services.Profiles.Models.ProfileOverridesFile))]
internal partial class ProfilesJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(TopToolbar.Models.Profile))]
internal partial class ProfileFileJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    Converters = new[] { typeof(ProviderIconTypeConverter) })]
[JsonSerializable(typeof(TopToolbar.Models.Providers.WorkspaceDefinition))]
internal partial class DefaultJsonContext : JsonSerializerContext
{
}
