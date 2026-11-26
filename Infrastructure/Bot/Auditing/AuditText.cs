using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WeekChgkSPB.Infrastructure.Bot.Auditing;

internal static class AuditText
{
    private static readonly Regex Secrets = new(
        """\b\d{5,}:[-\w]{30,}\b|\bsk-[\w-]{16,}\b|\bBearer\s+[^\s"<>]+|["']?\b(?:api[_-]?key|token|password|secret)["']?\s*[=:]\s*(?:"[^"]*"|'[^']*'|[^\s&,;"<>]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    internal static string Snapshot(object value)
    {
        var node = JsonSerializer.SerializeToNode(value)!;
        RedactStrings(node);
        return node.ToJsonString();
    }

    /// <summary>Маскирует значения строк без нарушения структуры снимка JSON.</summary>
    private static void RedactStrings(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(pair => pair.Key).ToArray())
            {
                if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text)) obj[key] = Redact(text);
                else if (obj[key] is JsonNode child) RedactStrings(child);
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text)) array[i] = Redact(text);
                else if (array[i] is JsonNode child) RedactStrings(child);
            }
        }
    }

    internal static string? Redact(string? value)
    {
        if (value is null) return null;
        try
        {
            return Secrets.Replace(value, "[REDACTED]");
        }
        catch (RegexMatchTimeoutException)
        {
            return "[REDACTED: regex timeout]";
        }
    }
}
