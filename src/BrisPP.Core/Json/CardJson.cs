using System.Text.Json;
using System.Text.Json.Serialization;
using BrisPP.Core.Model;

namespace BrisPP.Core.Json;

/// <summary>
/// Serializes a parsed <see cref="RaceCard"/> to JSON. The model's computed
/// getters (furlongs, decoded calls/times/margins, pace figures) ride along
/// next to their raw fields, so the document carries both the original tokens
/// and the decoded values — the audit trail the parser promises.
/// </summary>
public static class CardJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(RaceCard card) => JsonSerializer.Serialize(card, Options);

    public static void Write(RaceCard card, string path) =>
        File.WriteAllText(path, Serialize(card));
}
