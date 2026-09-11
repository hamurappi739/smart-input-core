using System.Text.Json.Serialization;
using SmartInput.Core.Models;

namespace SmartInput.Infrastructure.Persistence;

/// <summary>
/// Compile-time JSON metadata for every locally persisted SmartInput document.
/// This removes runtime reflection from the resident host and is a prerequisite
/// for a safe NativeAOT experiment. The wire format remains camelCase with
/// string enum values, so existing local files stay compatible.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(JsonUserAutocorrectDictionaryPersistence.UserAutocorrectDictionaryDocument))]
[JsonSerializable(typeof(JsonCorrectionRejectionLearningPersistence.CorrectionRejectionLearningDocument))]
[JsonSerializable(typeof(JsonSnippetPersistence.SnippetDocument))]
internal partial class SmartInputPersistenceJsonContext : JsonSerializerContext;
