namespace MediaProcessor.Processing;

/// <summary>Falha de processamento com o motivo do contrato video-processing-failed.</summary>
/// <param name="Retryable">false = não adianta tentar de novo (arquivo inválido, original apagado...).</param>
public sealed class ProcessingException(string reason, string message, bool retryable, Exception? inner = null)
    : Exception(message, inner)
{
    public string Reason { get; } = reason;

    public bool Retryable { get; } = retryable;
}
