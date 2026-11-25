using Upload.Configuration;
using Upload.Data;
using Upload.Domain;
using Upload.Messaging.Contracts;

namespace Upload.Saga;

/// <summary>
/// Regras da saga do vídeo, sem banco e sem Kafka: recebe o estado atual e um evento, altera o
/// estado e devolve os efeitos (eventos no outbox, limpezas no S3, notificação, histórico).
/// Quem chama grava tudo num único SaveChanges.
///
/// O vídeo vai para processamento só quando a cota está RESERVED e o upload está COMPLETED; o
/// que chegar por último dispara o video.uploaded. Na fila vale o timeout de fila; quando um
/// processor começa (video.processing-started) é armado o prazo de processamento.
/// </summary>
public sealed class VideoSaga(UploadOptions options, string hlsBucket)
{
    private SagaOptions Saga => options.Saga;

    public SagaDecision Create(VideoUpload video) =>
        SagaDecision.Apply("upload criado; cota pedida ao Identity",
        [
            new PublishEvent(SagaTopic.QuotaReserveRequested, EventTypes.QuotaReserveRequested,
                new QuotaReserveRequestedPayload(video.VideoId, video.OwnerId, video.Title)),
            new RecordHistory(null, VideoStatus.PENDING, "UPLOAD_CREATED",
                $"{video.SizeBytes} bytes em {video.TotalParts} partes de até {video.ChunkSize} bytes")
        ]);

    public SagaDecision Handle(VideoUpload video, SagaEvent sagaEvent, DateTime now) => sagaEvent switch
    {
        QuotaReservedEvent => OnQuotaReserved(video, now),
        QuotaRejectedEvent rejected => OnQuotaRejected(video, rejected.Reason, now),
        CompleteRequestedEvent => OnCompleteRequested(video, now),
        UploadCompletedEvent => OnUploadCompleted(video, now),
        ProcessingStartedEvent started => OnProcessingStarted(video, started, now),
        ProcessedEvent processed => OnProcessed(video, processed.Result, now),
        ProcessingFailedEvent failed => OnProcessingFailed(video, failed, now),
        TimeoutCheckEvent check => OnTimeoutCheck(video, check, now),
        DeleteRequestedEvent => OnDeleteRequested(video, now),
        _ => SagaDecision.Ignore($"evento desconhecido {sagaEvent.Name}")
    };

    private SagaDecision OnQuotaReserved(VideoUpload video, DateTime now)
    {
        if (video.QuotaState != QuotaState.REQUESTED)
        {
            return SagaDecision.Ignore($"cota já estava {video.QuotaState}");
        }

        if (video.Status is VideoStatus.FAILED or VideoStatus.REMOVED)
        {
            // A reserva chegou depois de a saga já ter falhado (upload abandonado, cancelado...).
            video.QuotaState = QuotaState.RELEASE_REQUESTED;
            return SagaDecision.Apply("reserva chegou depois da falha; vaga devolvida",
            [
                ReleaseQuota(video, $"LATE_RESERVATION_{video.FailureReason ?? video.Status.ToString()}"),
                new RecordHistory(video.Status, video.Status, "QUOTA_RESERVED_LATE", "vaga devolvida ao Identity")
            ]);
        }

        video.QuotaState = QuotaState.RESERVED;
        var actions = new List<SagaAction> { new RecordHistory(video.Status, video.Status, "QUOTA_RESERVED", null) };

        if (video.UploadState == UploadState.COMPLETED)
        {
            actions.AddRange(StartProcessing(video, now));
            return SagaDecision.Apply("cota reservada; vídeo enviado para processamento", actions);
        }

        return SagaDecision.Apply("cota reservada; aguardando o fim do envio", actions);
    }

    private SagaDecision OnQuotaRejected(VideoUpload video, string reason, DateTime now)
    {
        if (video.QuotaState != QuotaState.REQUESTED)
        {
            return SagaDecision.Ignore($"cota já estava {video.QuotaState}");
        }

        video.QuotaState = QuotaState.REJECTED;

        if (video.Status != VideoStatus.PENDING)
        {
            return SagaDecision.Apply("cota recusada para vídeo já encerrado",
                [new RecordHistory(video.Status, video.Status, "QUOTA_REJECTED", reason)]);
        }

        return Compensate(video, reason, "cota recusada pelo Identity", now);
    }

    /// <summary>
    /// Todas as partes chegaram. Montar o arquivo no S3 pode levar dezenas de segundos em vídeos
    /// grandes, então vira uma tarefa durável executada por qualquer réplica, e a API responde 202 na hora.
    /// </summary>
    private SagaDecision OnCompleteRequested(VideoUpload video, DateTime now)
    {
        if (video.Status != VideoStatus.PENDING || video.UploadState != UploadState.RECEIVING)
        {
            return SagaDecision.Ignore($"envio já está {video.UploadState} ({video.Status})");
        }

        video.UploadState = UploadState.COMPLETING;
        video.LastActivityAt = now;
        return SagaDecision.Apply("todas as partes recebidas; montando o original no S3",
        [
            new ScheduleCleanup(CleanupAction.COMPLETE_MULTIPART, video.RawBucket, video.RawKey, video.S3UploadId, TimeSpan.Zero),
            new RecordHistory(VideoStatus.PENDING, VideoStatus.PENDING, "UPLOAD_PARTS_RECEIVED", $"{video.TotalParts} partes")
        ]);
    }

    private SagaDecision OnUploadCompleted(VideoUpload video, DateTime now)
    {
        if (video.UploadState == UploadState.COMPLETED)
        {
            return SagaDecision.Ignore("envio já estava concluído");
        }

        if (video.UploadState == UploadState.ABORTED || video.Status != VideoStatus.PENDING)
        {
            // O cliente terminou o envio de um vídeo que a saga já encerrou: descarta o original.
            video.UploadState = UploadState.COMPLETED;
            return SagaDecision.Apply("envio concluído para vídeo já encerrado; original descartado",
            [
                DeleteRaw(video),
                new RecordHistory(video.Status, video.Status, "UPLOAD_COMPLETED_AFTER_FAILURE", null)
            ]);
        }

        video.UploadState = UploadState.COMPLETED;
        video.LastActivityAt = now;
        var actions = new List<SagaAction>
        {
            new RecordHistory(VideoStatus.PENDING, VideoStatus.PENDING, "UPLOAD_COMPLETED", $"{video.SizeBytes} bytes")
        };

        if (video.QuotaState == QuotaState.RESERVED)
        {
            actions.AddRange(StartProcessing(video, now));
            return SagaDecision.Apply("envio concluído; vídeo enviado para processamento", actions);
        }

        return SagaDecision.Apply("envio concluído; aguardando a reserva de cota", actions);
    }

    private List<SagaAction> StartProcessing(VideoUpload video, DateTime now)
    {
        video.Status = VideoStatus.PROCESSING;
        video.ProcessingState = ProcessingState.QUEUED;
        video.QueueDeadlineAt = now + Saga.QueueTimeout;
        video.DeadlineAt = null;

        return
        [
            new PublishEvent(SagaTopic.VideoUploaded, EventTypes.VideoUploaded, new VideoUploadedPayload(
                video.VideoId,
                video.OwnerId,
                video.Title,
                video.RawBucket,
                video.RawKey,
                video.SizeBytes,
                video.ContentType,
                hlsBucket,
                HlsPrefix(video),
                video.QueueDeadlineAt.Value,
                now)),
            new RecordHistory(VideoStatus.PENDING, VideoStatus.PROCESSING, "VIDEO_UPLOADED",
                $"na fila até {video.QueueDeadlineAt:O}")
        ];
    }

    private SagaDecision OnProcessingStarted(VideoUpload video, ProcessingStartedEvent started, DateTime now)
    {
        if (video.Status != VideoStatus.PROCESSING)
        {
            return SagaDecision.Ignore($"vídeo está {video.Status}");
        }

        // Cada tentativa (inclusive de outra réplica, depois de uma queda) ganha a sua janela.
        var deadline = now + Saga.ProcessingTimeout(video.SizeBytes);
        if (video.DeadlineAt is null || deadline > video.DeadlineAt)
        {
            video.DeadlineAt = deadline;
        }

        video.ProcessingState = ProcessingState.RUNNING;
        video.ProcessingAttempts = Math.Max(video.ProcessingAttempts, started.Attempt);

        return SagaDecision.Apply($"processamento iniciado (tentativa {started.Attempt} em {started.InstanceId}); prazo {video.DeadlineAt:O}",
        [
            new RecordHistory(VideoStatus.PROCESSING, VideoStatus.PROCESSING, "PROCESSING_STARTED",
                $"tentativa {started.Attempt} em {started.InstanceId}; prazo até {video.DeadlineAt:O}")
        ]);
    }

    private SagaDecision OnProcessed(VideoUpload video, VideoProcessedPayload result, DateTime now)
    {
        switch (video.Status)
        {
            case VideoStatus.PUBLISHED:
                return SagaDecision.Ignore("vídeo já publicado; resultado repetido");
            case VideoStatus.FAILED:
                // F7: o resultado chegou depois da compensação. Apaga o HLS gerado e não publica.
                return SagaDecision.Apply("resultado tardio descartado; HLS apagado e nada publicado",
                [
                    DeleteHls(video, result.HlsBucket),
                    new RecordHistory(VideoStatus.FAILED, VideoStatus.FAILED, "LATE_RESULT_DISCARDED",
                        $"processado por {result.InstanceId} em {result.ProcessedAt:O}")
                ]);
            case VideoStatus.PROCESSING:
                break;
            default:
                return SagaDecision.Ignore($"vídeo está {video.Status}");
        }

        var expired = ExpiredReason(video, now);
        if (expired is not null)
        {
            return Compensate(video, expired, $"resultado de {result.InstanceId} chegou depois do prazo", now);
        }

        video.Status = VideoStatus.PUBLISHED;
        video.ProcessingState = null;
        video.QuotaState = QuotaState.CONFIRMED;
        video.DurationSec = result.DurationSec;
        video.HlsBucket = result.HlsBucket;
        video.MasterKey = result.MasterKey;
        video.Renditions = result.Renditions.ToList();
        video.ThumbnailKey = result.ThumbnailKey;
        video.PublishedAt = now;

        return SagaDecision.Apply("vídeo publicado",
        [
            new PublishEvent(SagaTopic.VideoPublished, EventTypes.VideoPublished, new VideoPublishedPayload(
                video.VideoId,
                video.OwnerId,
                video.Title,
                video.Visibility.ToString(),
                result.DurationSec,
                result.HlsBucket,
                result.MasterKey,
                result.Renditions,
                result.ThumbnailKey,
                now)),
            new NotifyOwner("VIDEO_PUBLISHED", $"Seu vídeo \"{video.Title}\" foi publicado e já pode ser assistido."),
            new RecordHistory(VideoStatus.PROCESSING, VideoStatus.PUBLISHED, "VIDEO_PUBLISHED",
                $"{result.DurationSec}s em {string.Join("/", result.Renditions.Select(r => r.Name))}, processado por {result.InstanceId}")
        ]);
    }

    private SagaDecision OnProcessingFailed(VideoUpload video, ProcessingFailedEvent failed, DateTime now)
    {
        var detail = failed.Detail is null ? failed.Reason : $"{failed.Reason}: {failed.Detail}";

        if (video.Status == VideoStatus.PROCESSING)
        {
            var reason = failed.Reason == ProcessingFailureReasons.QueueTimeout
                ? FailureReasons.QueueTimeout
                : FailureReasons.ProcessingFailed;
            return Compensate(video, reason, detail, now);
        }

        if (video.Status == VideoStatus.FAILED)
        {
            // Já compensado: só garante que nada parcial ficou no bucket de HLS.
            return SagaDecision.Apply("falha de processamento para vídeo já compensado; HLS parcial apagado",
                [DeleteHls(video, hlsBucket)]);
        }

        return SagaDecision.Ignore($"vídeo está {video.Status}");
    }

    private SagaDecision OnTimeoutCheck(VideoUpload video, TimeoutCheckEvent check, DateTime now)
    {
        if (video.Status == VideoStatus.PROCESSING)
        {
            var expired = ExpiredReason(video, now);
            if (expired == FailureReasons.QueueTimeout)
            {
                return Compensate(video, expired, $"nenhum media-processor pegou o vídeo até {video.QueueDeadlineAt:O}", now);
            }

            if (expired == FailureReasons.DeadlineExpired)
            {
                return Compensate(video, expired, $"processamento não terminou até {video.DeadlineAt:O}", now);
            }
        }

        if (video.Status == VideoStatus.PENDING)
        {
            var lastActivity = check.LastPartAt is { } lastPart && lastPart > video.LastActivityAt ? lastPart : video.LastActivityAt;
            if (video.UploadState is UploadState.RECEIVING or UploadState.COMPLETING && lastActivity < now - Saga.UploadIdleTimeout)
            {
                var detail = video.UploadState == UploadState.COMPLETING
                    ? $"o original não foi montado no S3 desde {lastActivity:O}"
                    : $"nenhuma parte recebida desde {lastActivity:O}";
                return Compensate(video, FailureReasons.UploadAbandoned, detail, now);
            }

            if (video.QuotaState == QuotaState.REQUESTED && video.CreatedAt < now - Saga.QueueTimeout)
            {
                return Compensate(video, FailureReasons.QuotaTimeout, "o Identity não respondeu ao pedido de cota", now);
            }
        }

        return SagaDecision.Ignore("dentro do prazo");
    }

    private SagaDecision OnDeleteRequested(VideoUpload video, DateTime now)
    {
        switch (video.Status)
        {
            case VideoStatus.PUBLISHED:
            {
                video.Status = VideoStatus.REMOVED;
                video.RemovedAt = now;

                var actions = new List<SagaAction>
                {
                    new PublishEvent(SagaTopic.VideoRemoved, EventTypes.VideoRemoved,
                        new VideoRemovedPayload(video.VideoId, "Removido pelo dono", now))
                };

                if (video.QuotaState is QuotaState.CONFIRMED or QuotaState.RESERVED)
                {
                    actions.Add(ReleaseQuota(video, "VIDEO_REMOVED"));
                    video.QuotaState = QuotaState.RELEASE_REQUESTED;
                }

                // Pequeno atraso: as réplicas do Download tiram o vídeo do catálogo antes de os arquivos sumirem.
                actions.Add(DeleteRaw(video, TimeSpan.FromSeconds(30)));
                actions.Add(DeleteHls(video, video.HlsBucket ?? hlsBucket, TimeSpan.FromSeconds(30)));
                actions.Add(new NotifyOwner("VIDEO_REMOVED", $"Seu vídeo \"{video.Title}\" foi removido."));
                actions.Add(new RecordHistory(VideoStatus.PUBLISHED, VideoStatus.REMOVED, "VIDEO_REMOVED", null));
                return SagaDecision.Apply("vídeo removido", actions);
            }
            case VideoStatus.PENDING:
            case VideoStatus.PROCESSING:
                return Compensate(video, FailureReasons.CancelledByUser, "cancelado pelo dono", now);
            default:
                return SagaDecision.Ignore($"vídeo já está {video.Status}");
        }
    }

    /// <summary>
    /// Compensação (F1/F7): marca FAILED, devolve a cota reservada, apaga o original e o HLS e
    /// notifica o usuário. Os efeitos no S3 viram tarefas gravadas na mesma transação.
    /// </summary>
    private SagaDecision Compensate(VideoUpload video, string reason, string? detail, DateTime now)
    {
        var from = video.Status;
        var actions = new List<SagaAction>();

        video.Status = VideoStatus.FAILED;
        video.FailureReason = reason;
        video.ProcessingState = null;

        var quotaReleased = false;
        if (video.QuotaState == QuotaState.RESERVED)
        {
            actions.Add(ReleaseQuota(video, reason));
            video.QuotaState = QuotaState.RELEASE_REQUESTED;
            quotaReleased = true;
        }

        if (video.UploadState is UploadState.RECEIVING or UploadState.COMPLETING)
        {
            // O abort também apaga o objeto, caso a montagem em andamento termine antes dele.
            video.UploadState = UploadState.ABORTED;
            actions.Add(new ScheduleCleanup(CleanupAction.ABORT_MULTIPART, video.RawBucket, video.RawKey, video.S3UploadId, TimeSpan.Zero));
        }
        else
        {
            actions.Add(DeleteRaw(video));
        }

        if (from == VideoStatus.PROCESSING)
        {
            actions.Add(DeleteHls(video, hlsBucket));
        }

        actions.Add(new NotifyOwner("VIDEO_FAILED", FailureMessage(video, reason, quotaReleased)));
        actions.Add(new RecordHistory(from, VideoStatus.FAILED, reason, detail));

        return SagaDecision.Apply($"compensado ({reason}){(quotaReleased ? "; cota devolvida" : "")}", actions);
    }

    private static string? ExpiredReason(VideoUpload video, DateTime now) => video.ProcessingState switch
    {
        ProcessingState.RUNNING when video.DeadlineAt < now => FailureReasons.DeadlineExpired,
        ProcessingState.QUEUED when video.QueueDeadlineAt < now => FailureReasons.QueueTimeout,
        _ => null
    };

    private static PublishEvent ReleaseQuota(VideoUpload video, string reason) =>
        new(SagaTopic.QuotaReleaseRequested, EventTypes.QuotaReleaseRequested,
            new QuotaReleaseRequestedPayload(video.VideoId, video.OwnerId, reason));

    private static ScheduleCleanup DeleteRaw(VideoUpload video, TimeSpan delay = default) =>
        new(CleanupAction.DELETE_OBJECT, video.RawBucket, video.RawKey, null, delay);

    private static ScheduleCleanup DeleteHls(VideoUpload video, string bucket, TimeSpan delay = default) =>
        new(CleanupAction.DELETE_PREFIX, bucket, HlsPrefix(video), null, delay);

    public static string HlsPrefix(VideoUpload video) => $"{video.VideoId}/";

    private static string FailureMessage(VideoUpload video, string reason, bool quotaReleased)
    {
        var text = reason switch
        {
            "QUOTA_EXCEEDED" => "sua cota de uploads acabou",
            "USER_NOT_FOUND" => "o usuário não tem cota cadastrada",
            "RESERVATION_RELEASED" => "a reserva de cota já tinha sido liberada",
            FailureReasons.ProcessingFailed => "o processamento do vídeo falhou",
            FailureReasons.DeadlineExpired => "o processamento passou do prazo",
            FailureReasons.QueueTimeout => "nenhum processador ficou disponível a tempo",
            FailureReasons.UploadAbandoned => "o envio ficou parado por tempo demais",
            FailureReasons.QuotaTimeout => "a cota não pôde ser verificada a tempo",
            FailureReasons.CancelledByUser => "o upload foi cancelado",
            _ => reason
        };

        return $"O vídeo \"{video.Title}\" não foi publicado: {text}.{(quotaReleased ? " A vaga da sua cota foi devolvida." : "")}";
    }
}
