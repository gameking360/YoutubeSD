// Os nomes ficam em maiúsculas porque são gravados assim no banco e nos eventos,
// no mesmo padrão do Download ('PUBLISHED', 'PUBLIC'...).
namespace Upload.Domain;

/// <summary>Status público do vídeo (GET /api/upload/videos/{id}).</summary>
public enum VideoStatus
{
    PENDING,
    PROCESSING,
    PUBLISHED,
    FAILED,
    REMOVED
}

/// <summary>Andamento do envio das partes para o S3.</summary>
public enum UploadState
{
    RECEIVING,
    /// <summary>Todas as partes chegaram; o arquivo está sendo montado no S3 em segundo plano.</summary>
    COMPLETING,
    COMPLETED,
    ABORTED
}

/// <summary>Andamento da reserva de cota no Identity.</summary>
public enum QuotaState
{
    REQUESTED,
    RESERVED,
    REJECTED,
    RELEASE_REQUESTED,
    CONFIRMED
}

/// <summary>Enquanto PROCESSING: na fila (sem processor) ou em execução (prazo armado).</summary>
public enum ProcessingState
{
    QUEUED,
    RUNNING
}

public enum Visibility
{
    PUBLIC,
    UNLISTED,
    PRIVATE
}

/// <summary>Efeitos no S3 (tabela storage_cleanup) executados pelo StorageTaskWorker.</summary>
public enum CleanupAction
{
    /// <summary>Monta o original a partir das partes (CompleteMultipartUpload) e avisa a saga.</summary>
    COMPLETE_MULTIPART,
    ABORT_MULTIPART,
    DELETE_OBJECT,
    DELETE_PREFIX
}
