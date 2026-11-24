using Upload.Configuration;

namespace Upload.Api;

/// <summary>
/// Limita quantas partes esta instância recebe ao mesmo tempo (memória = partes × tamanho da parte).
/// Se estiver cheia, a resposta é 503 com Retry-After e o cliente tenta de novo, talvez em outra réplica.
/// </summary>
public sealed class PartUploadThrottle(UploadOptions options)
{
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.MaxConcurrentParts));

    public Task<bool> TryEnterAsync(CancellationToken cancellationToken) =>
        _slots.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

    public void Exit() => _slots.Release();
}
