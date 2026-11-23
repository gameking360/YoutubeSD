using System.Globalization;
using System.Text.Json;

namespace identity.Messaging;

public sealed record MessageEnvelope(
    string MessageId,
    string CorrelationId,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    JsonElement Payload)
{
    public static MessageEnvelope Parse(string raw)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException ex)
        {
            throw new InvalidMessageException($"JSON inválido: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidMessageException("A mensagem precisa ser um objeto JSON.");
            }

            var messageId = RequiredString(root, "messageId");
            if (!Guid.TryParse(messageId, out _))
            {
                throw new InvalidMessageException("messageId precisa ser um UUID.");
            }

            var correlationId = RequiredString(root, "correlationId");
            var type = RequiredString(root, "type");

            if (!root.TryGetProperty("version", out var versionElement) || !versionElement.TryGetInt32(out var version) || version < 1)
            {
                throw new InvalidMessageException("version precisa ser um inteiro maior ou igual a 1.");
            }

            var occurredAtRaw = RequiredString(root, "occurredAt");
            if (!DateTimeOffset.TryParse(occurredAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var occurredAt))
            {
                throw new InvalidMessageException("occurredAt precisa ser uma data ISO-8601.");
            }

            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidMessageException("payload precisa ser um objeto.");
            }

            return new MessageEnvelope(messageId, correlationId, type, version, occurredAt, payload.Clone());
        }
    }

    public string RequireString(string name)
    {
        if (!Payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidMessageException($"payload.{name} precisa ser uma string.");
        }

        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidMessageException($"payload.{name} não pode ser vazio.");
        }

        return text;
    }

    public string? OptionalString(string name)
    {
        if (!Payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidMessageException($"Campo {name} ausente ou inválido.");
        }

        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidMessageException($"Campo {name} não pode ser vazio.");
        }

        return text;
    }
}
