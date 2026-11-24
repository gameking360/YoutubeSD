using System.Security.Claims;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Upload.Configuration;
using Upload.Data;
using Upload.Domain;
using Upload.Messaging;
using Upload.Messaging.Persistence;
using Upload.Saga;
using Upload.Storage;

namespace Upload.Api;

/// <summary>
/// API de upload em partes, a mesma para vídeo curto e longo:
/// <list type="number">
/// <item>POST /videos → 202 {videoId, chunkSize, totalParts}; a cota é pedida em paralelo.</item>
/// <item>PUT /videos/{id}/parts/{n} → grava a parte n no S3. Idempotente: pode repetir em qualquer réplica.</item>
/// <item>POST /videos/{id}/complete → 202; o processamento segue em segundo plano.</item>
/// <item>GET /videos/{id} → status e partes recebidas (para acompanhar e retomar).</item>
/// </list>
/// Nenhuma réplica guarda estado em memória: tudo fica no MySQL e no S3.
/// </summary>
public sealed class UploadEndpoints
{
    private const string TemporarilyUnavailable = "Serviço temporariamente indisponível";

    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/upload").RequireAuthorization();

        group.MapPost("/videos", CreateAsync);
        group.MapPut("/videos/{videoId}/parts/{partNumber:int}", UploadPartAsync);
        group.MapPost("/videos/{videoId}/complete", CompleteAsync);
        group.MapGet("/videos/{videoId}", GetAsync);
        group.MapGet("/videos", ListAsync);
        group.MapDelete("/videos/{videoId}", DeleteAsync);
        group.MapGet("/notifications", NotificationsAsync);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateVideoRequest request,
        ClaimsPrincipal user,
        HttpContext http,
        UploadOptions options,
        RawStorage raw,
        VideoSagaService saga,
        ILogger<UploadEndpoints> logger,
        CancellationToken cancellationToken)
    {
        if (CurrentUser.Id(user) is not { } ownerId)
        {
            return Results.Unauthorized();
        }

        var errors = new Dictionary<string, string[]>();
        var fileName = Path.GetFileName(request.FileName?.Trim() ?? "");
        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(fileName))
        {
            errors["fileName"] = ["Informe o nome do arquivo."];
        }
        else if (!options.Extensions.Contains(extension))
        {
            errors["fileName"] = [$"Formato não aceito. Use: {string.Join(", ", options.Extensions)}."];
        }

        if (request.SizeBytes <= 0)
        {
            errors["sizeBytes"] = ["O tamanho do arquivo precisa ser maior que zero."];
        }
        else if (request.SizeBytes > options.MaxFileSizeBytes)
        {
            errors["sizeBytes"] = [$"O arquivo passa do limite de {options.MaxFileSizeBytes} bytes."];
        }
        else if (PartPlan.TotalParts(request.SizeBytes, options.ChunkSizeBytes) > PartPlan.MaxParts)
        {
            errors["sizeBytes"] = ["O arquivo precisaria de mais de 10.000 partes; aumente Upload:ChunkSizeBytes."];
        }

        var visibility = Visibility.PUBLIC;
        if (!string.IsNullOrWhiteSpace(request.Visibility) && !Enum.TryParse(request.Visibility.Trim(), ignoreCase: true, out visibility))
        {
            errors["visibility"] = ["Use PUBLIC, UNLISTED ou PRIVATE."];
        }

        var title = string.IsNullOrWhiteSpace(request.Title) ? Path.GetFileNameWithoutExtension(fileName) : request.Title.Trim();
        if (title.Length > 255)
        {
            errors["title"] = ["O título pode ter até 255 caracteres."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var videoId = Guid.NewGuid().ToString();
        var rawKey = $"{videoId}/original.{extension}";
        var contentType = string.IsNullOrWhiteSpace(request.ContentType) ? null : request.ContentType.Trim();

        string uploadId;
        try
        {
            uploadId = await raw.StartAsync(rawKey, contentType, cancellationToken);
        }
        catch (Exception ex) when (TransientErrors.IsTransient(ex))
        {
            logger.LogWarning("S3 indisponível ao iniciar upload: {Error} instancia={InstanceId}", ex.GetBaseException().Message, InstanceInfo.Id);
            return Unavailable(http, "O armazenamento está indisponível. Tente de novo em instantes.");
        }

        var now = DateTime.UtcNow;
        var video = new VideoUpload
        {
            VideoId = videoId,
            OwnerId = ownerId,
            Title = string.IsNullOrWhiteSpace(title) ? videoId : title,
            Visibility = visibility,
            Status = VideoStatus.PENDING,
            FileName = fileName,
            Extension = extension,
            ContentType = contentType,
            SizeBytes = request.SizeBytes,
            ChunkSize = options.ChunkSizeBytes,
            TotalParts = PartPlan.TotalParts(request.SizeBytes, options.ChunkSizeBytes),
            RawBucket = raw.Bucket,
            RawKey = rawKey,
            S3UploadId = uploadId,
            UploadState = UploadState.RECEIVING,
            QuotaState = QuotaState.REQUESTED,
            CreatedAt = now,
            LastActivityAt = now,
            UpdatedAt = now
        };

        try
        {
            await saga.CreateAsync(video, cancellationToken);
        }
        catch (Exception ex) when (TransientErrors.IsTransient(ex))
        {
            await raw.AbortQuietlyAsync(rawKey, uploadId);
            logger.LogWarning("MySQL indisponível ao criar upload: {Error} instancia={InstanceId}", ex.GetBaseException().Message, InstanceInfo.Id);
            return Unavailable(http, "O banco está indisponível. Tente de novo em instantes.");
        }

        logger.LogInformation(
            "Upload criado videoId={VideoId} ownerId={OwnerId} arquivo={FileName} bytes={Size} partes={Parts} correlationId={CorrelationId} instancia={InstanceId}",
            videoId, ownerId, fileName, video.SizeBytes, video.TotalParts, videoId, InstanceInfo.Id);

        return Results.Accepted($"/api/upload/videos/{videoId}", CreateVideoResponse.From(video));
    }

    private static async Task<IResult> UploadPartAsync(
        string videoId,
        int partNumber,
        ClaimsPrincipal user,
        HttpContext http,
        IDbContextFactory<UploadDbContext> factory,
        RawStorage raw,
        PartUploadThrottle throttle,
        ILogger<UploadEndpoints> logger,
        CancellationToken cancellationToken)
    {
        if (CurrentUser.Id(user) is not { } ownerId)
        {
            return Results.Unauthorized();
        }

        VideoUpload? video;
        await using (var context = await factory.CreateDbContextAsync(cancellationToken))
        {
            video = await context.Videos.AsNoTracking()
                .SingleOrDefaultAsync(v => v.VideoId == videoId && v.OwnerId == ownerId, cancellationToken);
        }

        if (video is null)
        {
            return Results.NotFound();
        }

        if (video.Status != VideoStatus.PENDING || video.UploadState != UploadState.RECEIVING || video.S3UploadId is null)
        {
            return Closed(video);
        }

        if (partNumber < 1 || partNumber > video.TotalParts)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Parte inválida",
                detail: $"As partes vão de 1 a {video.TotalParts}.");
        }

        var expected = PartPlan.ExpectedSize(video.SizeBytes, video.ChunkSize, video.TotalParts, partNumber);
        if (http.Request.ContentLength is not { } length)
        {
            return Results.StatusCode(StatusCodes.Status411LengthRequired);
        }

        if (length != expected)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Tamanho da parte incorreto",
                detail: $"A parte {partNumber} precisa ter {expected} bytes (recebido Content-Length {length}).");
        }

        if (!await throttle.TryEnterAsync(cancellationToken))
        {
            return Unavailable(http, "Instância ocupada recebendo outras partes.", retryAfterSeconds: 2);
        }

        try
        {
            using var buffer = new MemoryStream((int)expected);
            await http.Request.Body.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length != expected)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Parte incompleta",
                    detail: $"Chegaram {buffer.Length} de {expected} bytes.");
            }

            buffer.Position = 0;
            string etag;
            try
            {
                etag = await raw.UploadPartAsync(video.RawKey, video.S3UploadId, partNumber, buffer, expected, cancellationToken);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchUpload")
            {
                // A saga abortou o upload enquanto a parte chegava (cota recusada, cancelado...).
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Upload encerrado",
                    detail: "O upload foi encerrado; consulte o status do vídeo.");
            }
            catch (Exception ex) when (TransientErrors.IsTransient(ex))
            {
                logger.LogWarning("S3 indisponível ao gravar parte {Part} de {VideoId}: {Error} instancia={InstanceId}",
                    partNumber, videoId, ex.GetBaseException().Message, InstanceInfo.Id);
                return Unavailable(http, "O armazenamento está indisponível. Tente de novo em instantes.");
            }

            await SavePartAsync(factory, videoId, partNumber, etag, expected, cancellationToken);

            logger.LogInformation(
                "Parte recebida videoId={VideoId} parte={Part}/{Total} bytes={Size} correlationId={CorrelationId} instancia={InstanceId}",
                videoId, partNumber, video.TotalParts, expected, videoId, InstanceInfo.Id);

            return Results.Ok(new PartResponse(videoId, partNumber, expected, etag));
        }
        finally
        {
            throttle.Exit();
        }
    }

    private static async Task<IResult> CompleteAsync(
        string videoId,
        ClaimsPrincipal user,
        IDbContextFactory<UploadDbContext> factory,
        VideoSagaService saga,
        ILogger<UploadEndpoints> logger,
        CancellationToken cancellationToken)
    {
        if (CurrentUser.Id(user) is not { } ownerId)
        {
            return Results.Unauthorized();
        }

        VideoUpload? video;
        List<UploadPart> parts;
        await using (var context = await factory.CreateDbContextAsync(cancellationToken))
        {
            video = await context.Videos.AsNoTracking()
                .SingleOrDefaultAsync(v => v.VideoId == videoId && v.OwnerId == ownerId, cancellationToken);
            parts = video is null
                ? []
                : await context.Parts.AsNoTracking().Where(part => part.VideoId == videoId).ToListAsync(cancellationToken);
        }

        if (video is null)
        {
            return Results.NotFound();
        }

        if (video.UploadState is UploadState.COMPLETING or UploadState.COMPLETED && video.Status != VideoStatus.FAILED)
        {
            // Complete repetido (ex.: o cliente não recebeu a resposta): idempotente.
            return Results.Accepted($"/api/upload/videos/{videoId}", VideoStatusResponse.From(video, parts));
        }

        if (video.Status != VideoStatus.PENDING || video.UploadState != UploadState.RECEIVING || video.S3UploadId is null)
        {
            return Closed(video);
        }

        var missing = PartPlan.Missing(video.TotalParts, parts.Select(part => part.PartNumber));
        if (missing.Count > 0)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Envio incompleto",
                detail: $"Faltam {missing.Count} de {video.TotalParts} partes.",
                extensions: new Dictionary<string, object?> { ["missingParts"] = missing.Take(100).ToList() });
        }

        // Montar o arquivo no S3 pode demorar em vídeos grandes: vira tarefa durável (qualquer réplica
        // executa, e outra assume se ela cair) e a resposta é imediata.
        var result = await saga.HandleCommandAsync(videoId, ownerId, new CompleteRequestedEvent(), cancellationToken);
        if (result is null)
        {
            return Results.NotFound();
        }

        logger.LogInformation(
            "Envio concluído videoId={VideoId} bytes={Size} partes={Parts} uploadState={UploadState} correlationId={CorrelationId} instancia={InstanceId}",
            videoId, video.SizeBytes, parts.Count, result.Video.UploadState, videoId, InstanceInfo.Id);

        return Results.Accepted($"/api/upload/videos/{videoId}", VideoStatusResponse.From(result.Video, parts));
    }

    private static async Task<IResult> GetAsync(
        string videoId,
        ClaimsPrincipal user,
        IDbContextFactory<UploadDbContext> factory,
        CancellationToken cancellationToken)
    {
        if (CurrentUser.Id(user) is not { } ownerId)
        {
            return Results.Unauthorized();
        }

        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var video = await context.Videos.AsNoTracking()
            .SingleOrDefaultAsync(v => v.VideoId == videoId && v.OwnerId == ownerId, cancellationToken);
        if (video is null)
        {
            return Results.NotFound();
        }

        var parts = await context.Parts.AsNoTracking().Where(part => part.VideoId == videoId).ToListAsync(cancellationToken);
        return Results.Ok(VideoStatusResponse.From(video, parts));
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        IDbContextFactory<UploadDbContext> factory,
        string? owner,
        int? page,
        int? size,
        CancellationToken cancellationToken)
    {
        if (CurrentUser.Id(user) is not { } ownerId)
        {
            return Results.Unauthorized();
        }

        if (owner is not null && owner != "me")
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Filtro inválido", detail: "Use owner=me.");
        }

        var pageNumber = Math.Max(0, page ?? 0);
        var pageSize = Math.Clamp(size ?? 20, 1, 100);

        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var query = context.Videos.AsNoTracking().Where(video => video.OwnerId == ownerId);
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(video => video.CreatedAt)
            .Skip(pageNumber * pageSize)
            .Take(pageSize)
            .Select(video => new VideoSummary(
                video.VideoId,
                video.Title,
                video.Visibility.ToString(),
                video.Status.ToString(),
                video.FailureReason,
                video.SizeBytes,
                video.CreatedAt,
                video.PublishedAt,
                video.DurationSec))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PageResponse<VideoSummary>(items, pageNumber, pageSize, total));
    }

    private static async Task<IResult> DeleteAsync(
        string videoId,
        ClaimsPrincipal user,
        IDbContextFactory<UploadDbContext> factory,
        VideoSagaService saga,
        CancellationToken cancellationToken)
    {
        if (CurrentUser.Id(user) is not { } ownerId)
        {
            return Results.Unauthorized();
        }

        var result = await saga.HandleCommandAsync(videoId, ownerId, new DeleteRequestedEvent(), cancellationToken);
        if (result is null)
        {
            return Results.NotFound();
        }

        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var parts = await context.Parts.AsNoTracking().Where(part => part.VideoId == videoId).ToListAsync(cancellationToken);
        return Results.Accepted($"/api/upload/videos/{videoId}", VideoStatusResponse.From(result.Video, parts));
    }

    private static async Task<IResult> NotificationsAsync(
        ClaimsPrincipal user,
        IDbContextFactory<UploadDbContext> factory,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (CurrentUser.Id(user) is not { } ownerId)
        {
            return Results.Unauthorized();
        }

        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var items = await context.Notifications.AsNoTracking()
            .Where(notification => notification.UserId == ownerId)
            .OrderByDescending(notification => notification.Id)
            .Take(Math.Clamp(limit ?? 50, 1, 200))
            .Select(notification => new NotificationResponse(
                notification.Id, notification.VideoId, notification.Type, notification.Message, notification.CreatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(items);
    }

    /// <summary>Grava (ou regrava) a parte. Duas réplicas recebendo a mesma parte ao mesmo tempo é normal.</summary>
    private static async Task SavePartAsync(
        IDbContextFactory<UploadDbContext> factory,
        string videoId,
        int partNumber,
        string etag,
        long size,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            var now = DateTime.UtcNow;
            var part = await context.Parts.SingleOrDefaultAsync(p => p.VideoId == videoId && p.PartNumber == partNumber, cancellationToken);
            if (part is null)
            {
                context.Parts.Add(new UploadPart { VideoId = videoId, PartNumber = partNumber, ETag = etag, SizeBytes = size, UploadedAt = now });
            }
            else
            {
                part.ETag = etag;
                part.SizeBytes = size;
                part.UploadedAt = now;
            }

            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (attempt < 3 && DbErrors.IsDuplicateKey(ex))
            {
                // A mesma parte foi gravada por outra réplica agora: relê e atualiza.
            }
        }
    }

    private static IResult Closed(VideoUpload video) =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Upload encerrado",
            detail: video.FailureReason is null
                ? $"O vídeo está {video.Status} e não aceita mais partes."
                : $"O vídeo falhou: {video.FailureReason}.",
            extensions: new Dictionary<string, object?>
            {
                ["status"] = video.Status.ToString(),
                ["failureReason"] = video.FailureReason
            });

    private static IResult Unavailable(HttpContext http, string detail, int retryAfterSeconds = 5)
    {
        http.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: TemporarilyUnavailable, detail: detail);
    }
}

public static class CurrentUser
{
    /// <summary>O dono vem só da claim "sub" do JWT validado localmente pelo JWKS do Security.</summary>
    public static string? Id(ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value is { Length: > 0 } id ? id : null;
}
