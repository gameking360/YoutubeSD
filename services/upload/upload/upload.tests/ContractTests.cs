using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Upload.Data;
using Upload.Messaging.Contracts;
using Upload.Messaging.Kafka;
using Upload.Messaging.Outbox;
using Upload.Messaging.Persistence;

namespace Upload.Tests;

/// <summary>
/// Confere o que o Upload e o media-processor publicam contra os JSON Schemas de contracts/,
/// e que os consumidores entendem os exemplos dos contratos.
/// </summary>
public sealed class ContractTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Rendition[] Renditions = [new("360p", 640, 360, 928000), new("720p", 1280, 720, 2928000)];

    public static TheoryData<string, string, object> Payloads => new()
    {
        { "quota-reserve-requested", EventTypes.QuotaReserveRequested, new QuotaReserveRequestedPayload("v1", "u1", "Aula") },
        { "quota-release-requested", EventTypes.QuotaReleaseRequested, new QuotaReleaseRequestedPayload("v1", "u1", "PROCESSING_FAILED") },
        {
            "video-uploaded", EventTypes.VideoUploaded,
            new VideoUploadedPayload("v1", "u1", "Aula", "videos-raw", "v1/original.mp4", 1024, "video/mp4", "videos-hls", "v1/", Now, Now)
        },
        { "video-processing-started", EventTypes.VideoProcessingStarted, new VideoProcessingStartedPayload("v1", 1, "media-processor-1", Now) },
        {
            "video-processed", EventTypes.VideoProcessed,
            new VideoProcessedPayload("v1", "videos-hls", "v1/master.m3u8", 20, Renditions, "v1/thumb.jpg", Now, "media-processor-1")
        },
        {
            "video-processing-failed", EventTypes.VideoProcessingFailed,
            new VideoProcessingFailedPayload("v1", ProcessingFailureReasons.SimulatedFailure, "falha", 3, Now, "media-processor-1")
        },
        {
            "video-published", EventTypes.VideoPublished,
            new VideoPublishedPayload("v1", "u1", "Aula", "PUBLIC", 20, "videos-hls", "v1/master.m3u8", Renditions, "v1/thumb.jpg", Now)
        },
        { "video-removed", EventTypes.VideoRemoved, new VideoRemovedPayload("v1", "Removido pelo dono", Now) }
    };

    [Theory]
    [MemberData(nameof(Payloads))]
    public void Mensagem_do_outbox_segue_o_envelope_e_o_schema(string contract, string type, object payload)
    {
        using var context = NewContext();
        OutboxWriter.Enqueue(context, "topico", "v1", type, "v1", payload, Now);
        using var message = JsonDocument.Parse(context.Outbox.Local.Single().Payload);
        var root = message.RootElement;

        foreach (var field in Required(LoadSchema("envelope")))
        {
            Assert.True(root.TryGetProperty(field, out _), $"envelope sem {field}");
        }

        Assert.Equal(type, root.GetProperty("type").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.True(Guid.TryParse(root.GetProperty("messageId").GetString(), out _));

        var schema = LoadSchema(contract + ".v1");
        var payloadSchema = schema.GetProperty("properties").GetProperty("payload");
        var body = root.GetProperty("payload");
        foreach (var field in Required(payloadSchema))
        {
            Assert.True(body.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null,
                $"{contract}: payload sem {field}");
        }

        if (payloadSchema.GetProperty("properties").TryGetProperty("renditions", out var renditionsSchema))
        {
            var itemRequired = Required(renditionsSchema.GetProperty("items")).ToList();
            foreach (var rendition in body.GetProperty("renditions").EnumerateArray())
            {
                Assert.All(itemRequired, field => Assert.True(rendition.TryGetProperty(field, out _), $"rendition sem {field}"));
            }
        }

        if (payloadSchema.GetProperty("properties").TryGetProperty("reason", out var reasonSchema) &&
            reasonSchema.TryGetProperty("enum", out var allowed))
        {
            Assert.Contains(body.GetProperty("reason").GetString(), allowed.EnumerateArray().Select(value => value.GetString()));
        }
    }

    [Fact]
    public void Upload_entende_o_exemplo_de_video_processed()
    {
        var message = MessageEnvelope.Parse(File.ReadAllText(Path.Combine(ContractsDirectory, "examples", "video-processed.v1.json")));

        var payload = message.PayloadAs<VideoProcessedPayload>();

        Assert.Equal("456", payload.VideoId);
        Assert.Equal("456/master.m3u8", payload.MasterKey);
        Assert.Equal(2, payload.Renditions.Count);
        Assert.Equal(1820, payload.DurationSec);
    }

    [Fact]
    public void Processor_entende_o_exemplo_de_video_uploaded()
    {
        var message = MessageEnvelope.Parse(File.ReadAllText(Path.Combine(ContractsDirectory, "examples", "video-uploaded.v1.json")));

        var payload = message.PayloadAs<VideoUploadedPayload>();

        Assert.Equal("456/original.mp4", payload.RawKey);
        Assert.Equal(new DateTime(2026, 11, 20, 10, 50, 0, DateTimeKind.Utc), payload.QueueDeadline.ToUniversalTime());
    }

    [Fact]
    public void Mensagem_invalida_e_recusada_para_ir_a_dlq()
    {
        Assert.Throws<InvalidMessageException>(() => MessageEnvelope.Parse("{ isto não é json"));
        Assert.Throws<InvalidMessageException>(() => MessageEnvelope.Parse("""{"messageId":"x","correlationId":"c","type":"T","version":1,"occurredAt":"2026-11-24T00:00:00Z","payload":{}}"""));
    }

    private static UploadDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>();
        options.UseYoutubeMySql("Server=localhost;Port=3306;Database=contract_tests;User ID=x;Password=x");
        return new UploadDbContext(options.Options);
    }

    private static IEnumerable<string> Required(JsonElement schema) =>
        schema.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(field => field.GetString()!)
            : [];

    private static JsonElement LoadSchema(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(ContractsDirectory, $"{name}.schema.json"))).RootElement.Clone();

    private static string ContractsDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "contracts")))
            {
                directory = directory.Parent;
            }

            return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("contracts/ não encontrado"), "contracts");
        }
    }
}
