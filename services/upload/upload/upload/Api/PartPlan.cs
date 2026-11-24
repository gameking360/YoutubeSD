namespace Upload.Api;

/// <summary>
/// Divide o arquivo em partes de tamanho fixo (a última pode ser menor). Vídeo curto ou longo usa
/// o mesmo mecanismo: um vídeo pequeno é só uma parte.
/// </summary>
public static class PartPlan
{
    /// <summary>Limite do S3 para um multipart.</summary>
    public const int MaxParts = 10_000;

    /// <summary>Mínimo do S3 para toda parte menos a última.</summary>
    public const int MinChunkSize = 5 * 1024 * 1024;

    public static int TotalParts(long sizeBytes, int chunkSize)
    {
        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        }

        return (int)((sizeBytes + chunkSize - 1) / chunkSize);
    }

    public static long ExpectedSize(long sizeBytes, int chunkSize, int totalParts, int partNumber)
    {
        if (partNumber < 1 || partNumber > totalParts)
        {
            throw new ArgumentOutOfRangeException(nameof(partNumber));
        }

        return partNumber < totalParts
            ? chunkSize
            : sizeBytes - (long)chunkSize * (totalParts - 1);
    }

    /// <summary>Partes de 1 a total que ainda não chegaram.</summary>
    public static IReadOnlyList<int> Missing(int totalParts, IEnumerable<int> received)
    {
        var set = received.ToHashSet();
        return Enumerable.Range(1, totalParts).Where(part => !set.Contains(part)).ToList();
    }
}
