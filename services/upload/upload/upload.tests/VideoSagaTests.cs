using Upload.Configuration;
using Upload.Data;
using Upload.Domain;
using Upload.Messaging.Contracts;
using Upload.Saga;

namespace Upload.Tests;

public sealed class VideoSagaTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
    private readonly UploadOptions _options = new();
    private readonly VideoSaga _saga;

    public VideoSagaTests()
    {
        _saga = new VideoSaga(_options, "videos-hls");
    }

    [Fact]
    public void Create_pede_a_cota()
    {
        var video = NewVideo();

        var decision = _saga.Create(video);

        var publish = Assert.Single(decision.Actions.OfType<PublishEvent>());
        Assert.Equal(SagaTopic.QuotaReserveRequested, publish.Topic);
        var payload = Assert.IsType<QuotaReserveRequestedPayload>(publish.Payload);
        Assert.Equal(video.VideoId, payload.VideoId);
        Assert.Equal(video.OwnerId, payload.OwnerId);
    }

    [Fact]
    public void Cota_reservada_antes_do_fim_do_envio_espera()
    {
        var video = NewVideo();

        var decision = _saga.Handle(video, new QuotaReservedEvent(), Now);

        Assert.True(decision.Applied);
        Assert.Equal(QuotaState.RESERVED, video.QuotaState);
        Assert.Equal(VideoStatus.PENDING, video.Status);
        Assert.Empty(decision.Actions.OfType<PublishEvent>());
    }

    [Fact]
    public void Complete_antes_da_cota_espera_e_a_cota_dispara_o_video_uploaded()
    {
        var video = NewVideo();

        var completed = _saga.Handle(video, new UploadCompletedEvent(), Now);
        Assert.True(completed.Applied);
        Assert.Equal(VideoStatus.PENDING, video.Status);
        Assert.Empty(completed.Actions.OfType<PublishEvent>());

        var reserved = _saga.Handle(video, new QuotaReservedEvent(), Now);

        var publish = Assert.Single(reserved.Actions.OfType<PublishEvent>());
        Assert.Equal(SagaTopic.VideoUploaded, publish.Topic);
        var payload = Assert.IsType<VideoUploadedPayload>(publish.Payload);
        Assert.Equal(Now + _options.Saga.QueueTimeout, payload.QueueDeadline);
        Assert.Equal($"{video.VideoId}/", payload.HlsPrefix);
        Assert.Equal(VideoStatus.PROCESSING, video.Status);
        Assert.Equal(ProcessingState.QUEUED, video.ProcessingState);
    }

    [Fact]
    public void Cota_antes_do_complete_dispara_no_complete()
    {
        var video = NewVideo();
        _saga.Handle(video, new QuotaReservedEvent(), Now);

        var completed = _saga.Handle(video, new UploadCompletedEvent(), Now);

        Assert.Equal(SagaTopic.VideoUploaded, Assert.Single(completed.Actions.OfType<PublishEvent>()).Topic);
        Assert.Equal(VideoStatus.PROCESSING, video.Status);
    }

    [Fact]
    public void Pedido_de_complete_agenda_a_montagem_no_s3_e_responde_na_hora()
    {
        var video = NewVideo();

        var decision = _saga.Handle(video, new CompleteRequestedEvent(), Now);

        Assert.Equal(UploadState.COMPLETING, video.UploadState);
        var task = Assert.Single(decision.Actions.OfType<ScheduleCleanup>());
        Assert.Equal(CleanupAction.COMPLETE_MULTIPART, task.Action);
        Assert.Equal(video.S3UploadId, task.UploadId);
        Assert.False(_saga.Handle(video, new CompleteRequestedEvent(), Now).Applied);
    }

    [Fact]
    public void Montagem_concluida_com_cota_reservada_vai_para_processamento()
    {
        var video = NewVideo();
        _saga.Handle(video, new QuotaReservedEvent(), Now);
        _saga.Handle(video, new CompleteRequestedEvent(), Now);
        Assert.Equal(VideoStatus.PENDING, video.Status);

        var decision = _saga.Handle(video, new UploadCompletedEvent(), Now);

        Assert.Equal(UploadState.COMPLETED, video.UploadState);
        Assert.Equal(VideoStatus.PROCESSING, video.Status);
        Assert.Equal(SagaTopic.VideoUploaded, Assert.Single(decision.Actions.OfType<PublishEvent>()).Topic);
    }

    [Fact]
    public void Cancelar_durante_a_montagem_aborta_o_multipart()
    {
        var video = NewVideo();
        _saga.Handle(video, new CompleteRequestedEvent(), Now);

        var decision = _saga.Handle(video, new DeleteRequestedEvent(), Now);

        Assert.Equal(UploadState.ABORTED, video.UploadState);
        Assert.Contains(decision.Actions.OfType<ScheduleCleanup>(), c => c.Action == CleanupAction.ABORT_MULTIPART);
        var late = _saga.Handle(video, new UploadCompletedEvent(), Now);
        Assert.Contains(late.Actions.OfType<ScheduleCleanup>(), c => c.Action == CleanupAction.DELETE_OBJECT);
    }

    [Fact]
    public void Complete_repetido_e_ignorado()
    {
        var video = NewVideo();
        _saga.Handle(video, new UploadCompletedEvent(), Now);

        Assert.False(_saga.Handle(video, new UploadCompletedEvent(), Now).Applied);
    }

    [Fact]
    public void Cota_recusada_falha_e_aborta_o_multipart_sem_devolver_cota()
    {
        var video = NewVideo();

        var decision = _saga.Handle(video, new QuotaRejectedEvent("QUOTA_EXCEEDED"), Now);

        Assert.Equal(VideoStatus.FAILED, video.Status);
        Assert.Equal("QUOTA_EXCEEDED", video.FailureReason);
        Assert.Equal(UploadState.ABORTED, video.UploadState);
        Assert.Contains(decision.Actions.OfType<ScheduleCleanup>(), cleanup => cleanup.Action == CleanupAction.ABORT_MULTIPART);
        Assert.Empty(decision.Actions.OfType<PublishEvent>());
        Assert.Single(decision.Actions.OfType<NotifyOwner>());
    }

    [Fact]
    public void Processing_started_arma_o_prazo_proporcional_ao_tamanho()
    {
        var video = Queued();

        _saga.Handle(video, new ProcessingStartedEvent(1, "media-processor-1"), Now);

        Assert.Equal(ProcessingState.RUNNING, video.ProcessingState);
        Assert.Equal(Now + _options.Saga.ProcessingTimeout(video.SizeBytes), video.DeadlineAt);
        Assert.Equal(1, video.ProcessingAttempts);
    }

    [Fact]
    public void Nova_tentativa_de_outra_replica_estende_o_prazo()
    {
        var video = Queued();
        _saga.Handle(video, new ProcessingStartedEvent(1, "media-processor-1"), Now);

        var later = Now.AddMinutes(1);
        _saga.Handle(video, new ProcessingStartedEvent(2, "media-processor-2"), later);

        Assert.Equal(later + _options.Saga.ProcessingTimeout(video.SizeBytes), video.DeadlineAt);
        Assert.Equal(2, video.ProcessingAttempts);
    }

    [Fact]
    public void Processed_no_prazo_publica_com_os_campos_do_download()
    {
        var video = Running();

        var decision = _saga.Handle(video, new ProcessedEvent(Result(video)), Now.AddSeconds(10));

        Assert.Equal(VideoStatus.PUBLISHED, video.Status);
        Assert.Equal(QuotaState.CONFIRMED, video.QuotaState);
        var publish = Assert.Single(decision.Actions.OfType<PublishEvent>());
        Assert.Equal(SagaTopic.VideoPublished, publish.Topic);
        var payload = Assert.IsType<VideoPublishedPayload>(publish.Payload);
        Assert.Equal($"{video.VideoId}/master.m3u8", payload.MasterKey);
        Assert.Equal("videos-hls", payload.HlsBucket);
        Assert.Equal("PUBLIC", payload.Visibility);
        Assert.Equal(20, payload.DurationSec);
        Assert.Equal(["360p", "720p"], payload.Renditions.Select(r => r.Name));
    }

    [Fact]
    public void Processed_repetido_depois_de_publicado_e_ignorado()
    {
        var video = Running();
        _saga.Handle(video, new ProcessedEvent(Result(video)), Now);

        var again = _saga.Handle(video, new ProcessedEvent(Result(video)), Now);

        Assert.False(again.Applied);
        Assert.Empty(again.Actions);
    }

    [Fact]
    public void F1_processing_failed_compensa_devolve_cota_e_limpa_o_s3()
    {
        var video = Running();

        var decision = _saga.Handle(video, new ProcessingFailedEvent(ProcessingFailureReasons.SimulatedFailure, "falha"), Now);

        Assert.Equal(VideoStatus.FAILED, video.Status);
        Assert.Equal(FailureReasons.ProcessingFailed, video.FailureReason);
        Assert.Equal(QuotaState.RELEASE_REQUESTED, video.QuotaState);
        var release = Assert.Single(decision.Actions.OfType<PublishEvent>());
        Assert.Equal(SagaTopic.QuotaReleaseRequested, release.Topic);
        var cleanups = decision.Actions.OfType<ScheduleCleanup>().ToList();
        Assert.Contains(cleanups, c => c.Action == CleanupAction.DELETE_OBJECT && c.KeyOrPrefix == video.RawKey);
        Assert.Contains(cleanups, c => c.Action == CleanupAction.DELETE_PREFIX && c.KeyOrPrefix == $"{video.VideoId}/");
        Assert.Single(decision.Actions.OfType<NotifyOwner>());
    }

    [Fact]
    public void F7_prazo_vencido_no_varredor_compensa()
    {
        var video = Running();

        var decision = _saga.Handle(video, new TimeoutCheckEvent(null), video.DeadlineAt!.Value.AddSeconds(1));

        Assert.Equal(VideoStatus.FAILED, video.Status);
        Assert.Equal(FailureReasons.DeadlineExpired, video.FailureReason);
        Assert.Contains(decision.Actions.OfType<PublishEvent>(), p => p.Topic == SagaTopic.QuotaReleaseRequested);
    }

    [Fact]
    public void F7_resultado_tardio_apaga_o_hls_e_nao_publica()
    {
        var video = Running();
        _saga.Handle(video, new TimeoutCheckEvent(null), video.DeadlineAt!.Value.AddSeconds(1));

        var late = _saga.Handle(video, new ProcessedEvent(Result(video)), video.DeadlineAt!.Value.AddMinutes(1));

        Assert.True(late.Applied);
        Assert.Equal(VideoStatus.FAILED, video.Status);
        Assert.Empty(late.Actions.OfType<PublishEvent>());
        Assert.Contains(late.Actions.OfType<ScheduleCleanup>(), c => c.Action == CleanupAction.DELETE_PREFIX);
    }

    [Fact]
    public void F7_resultado_que_chega_depois_do_prazo_antes_do_varredor_compensa()
    {
        var video = Running();

        var decision = _saga.Handle(video, new ProcessedEvent(Result(video)), video.DeadlineAt!.Value.AddSeconds(1));

        Assert.Equal(VideoStatus.FAILED, video.Status);
        Assert.Equal(FailureReasons.DeadlineExpired, video.FailureReason);
        Assert.DoesNotContain(decision.Actions.OfType<PublishEvent>(), p => p.Topic == SagaTopic.VideoPublished);
    }

    [Fact]
    public void Na_fila_sem_processor_espera_ate_o_timeout_de_fila()
    {
        var video = Queued();

        var within = _saga.Handle(video, new TimeoutCheckEvent(null), video.QueueDeadlineAt!.Value.AddSeconds(-1));
        Assert.False(within.Applied);
        Assert.Equal(VideoStatus.PROCESSING, video.Status);

        var expired = _saga.Handle(video, new TimeoutCheckEvent(null), video.QueueDeadlineAt!.Value.AddSeconds(1));
        Assert.True(expired.Applied);
        Assert.Equal(FailureReasons.QueueTimeout, video.FailureReason);
        Assert.Equal(QuotaState.RELEASE_REQUESTED, video.QuotaState);
    }

    [Fact]
    public void Upload_parado_e_abandonado_mas_parte_recente_mantem_vivo()
    {
        var video = NewVideo();
        _saga.Handle(video, new QuotaReservedEvent(), Now);
        var later = Now + _options.Saga.UploadIdleTimeout + TimeSpan.FromMinutes(1);

        Assert.False(_saga.Handle(video, new TimeoutCheckEvent(later.AddMinutes(-1)), later).Applied);

        var decision = _saga.Handle(video, new TimeoutCheckEvent(null), later);
        Assert.True(decision.Applied);
        Assert.Equal(FailureReasons.UploadAbandoned, video.FailureReason);
        Assert.Contains(decision.Actions.OfType<ScheduleCleanup>(), c => c.Action == CleanupAction.ABORT_MULTIPART);
        Assert.Contains(decision.Actions.OfType<PublishEvent>(), p => p.Topic == SagaTopic.QuotaReleaseRequested);
    }

    [Fact]
    public void Identity_sem_responder_por_tempo_demais_compensa()
    {
        var video = NewVideo();
        _saga.Handle(video, new UploadCompletedEvent(), Now);

        var decision = _saga.Handle(video, new TimeoutCheckEvent(null), Now + _options.Saga.QueueTimeout + TimeSpan.FromSeconds(1));

        Assert.True(decision.Applied);
        Assert.Equal(FailureReasons.QuotaTimeout, video.FailureReason);
        Assert.Contains(decision.Actions.OfType<ScheduleCleanup>(), c => c.Action == CleanupAction.DELETE_OBJECT);
    }

    [Fact]
    public void Reserva_que_chega_depois_do_abandono_e_devolvida()
    {
        var video = NewVideo();
        _saga.Handle(video, new DeleteRequestedEvent(), Now);
        Assert.Equal(VideoStatus.FAILED, video.Status);
        Assert.Equal(QuotaState.REQUESTED, video.QuotaState);

        var decision = _saga.Handle(video, new QuotaReservedEvent(), Now);

        Assert.Equal(QuotaState.RELEASE_REQUESTED, video.QuotaState);
        Assert.Equal(SagaTopic.QuotaReleaseRequested, Assert.Single(decision.Actions.OfType<PublishEvent>()).Topic);
    }

    [Fact]
    public void Remover_video_publicado_publica_video_removed_e_devolve_cota()
    {
        var video = Running();
        _saga.Handle(video, new ProcessedEvent(Result(video)), Now);

        var decision = _saga.Handle(video, new DeleteRequestedEvent(), Now);

        Assert.Equal(VideoStatus.REMOVED, video.Status);
        var topics = decision.Actions.OfType<PublishEvent>().Select(p => p.Topic).ToList();
        Assert.Contains(SagaTopic.VideoRemoved, topics);
        Assert.Contains(SagaTopic.QuotaReleaseRequested, topics);
    }

    [Fact]
    public void Processing_failed_de_video_ja_compensado_so_limpa()
    {
        var video = Running();
        _saga.Handle(video, new TimeoutCheckEvent(null), video.DeadlineAt!.Value.AddSeconds(1));

        var decision = _saga.Handle(video, new ProcessingFailedEvent("FFMPEG_ERROR", null), Now.AddHours(1));

        Assert.Empty(decision.Actions.OfType<PublishEvent>());
        Assert.All(decision.Actions, action => Assert.IsType<ScheduleCleanup>(action));
    }

    [Fact]
    public void Prazo_de_processamento_cresce_com_o_tamanho_e_tem_teto()
    {
        var saga = _options.Saga;
        Assert.Equal(TimeSpan.FromSeconds(60 + 100), saga.ProcessingTimeout(100 * UploadOptions.MiB));
        Assert.Equal(TimeSpan.FromMinutes(120), saga.ProcessingTimeout(1_000_000 * UploadOptions.MiB));
    }

    private VideoUpload Queued()
    {
        var video = NewVideo();
        _saga.Handle(video, new QuotaReservedEvent(), Now);
        _saga.Handle(video, new UploadCompletedEvent(), Now);
        return video;
    }

    private VideoUpload Running()
    {
        var video = Queued();
        _saga.Handle(video, new ProcessingStartedEvent(1, "media-processor-1"), Now);
        return video;
    }

    private static VideoUpload NewVideo()
    {
        var id = Guid.NewGuid().ToString();
        return new VideoUpload
        {
            VideoId = id,
            OwnerId = Guid.NewGuid().ToString(),
            Title = "Aula 1",
            FileName = "aula.mp4",
            Extension = "mp4",
            SizeBytes = 20 * UploadOptions.MiB,
            ChunkSize = (int)(8 * UploadOptions.MiB),
            TotalParts = 3,
            RawBucket = "videos-raw",
            RawKey = $"{id}/original.mp4",
            S3UploadId = "upload-1",
            CreatedAt = Now,
            LastActivityAt = Now,
            UpdatedAt = Now
        };
    }

    private static VideoProcessedPayload Result(VideoUpload video) => new(
        video.VideoId,
        "videos-hls",
        $"{video.VideoId}/master.m3u8",
        20,
        [new Rendition("360p", 640, 360, 928000), new Rendition("720p", 1280, 720, 2928000)],
        $"{video.VideoId}/thumb.jpg",
        Now,
        "media-processor-1");
}
