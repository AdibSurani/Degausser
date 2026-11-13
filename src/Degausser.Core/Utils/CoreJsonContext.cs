using System.Text.Json.Serialization;

namespace Degausser;

// Source-generated serializers keep Core trimming- and WebAssembly-safe:
// reflection-based System.Text.Json is disabled in trimmed builds.

[JsonSerializable(typeof(BbpCatalogue.CatalogueDocument))]
internal sealed partial class CatalogueJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ContentTranslation.Manifest))]
internal sealed partial class TranslationJsonContext : JsonSerializerContext;
