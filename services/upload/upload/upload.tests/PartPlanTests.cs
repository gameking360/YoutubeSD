using Upload.Api;

namespace Upload.Tests;

public sealed class PartPlanTests
{
    private const int Chunk = 8 * 1024 * 1024;

    [Theory]
    [InlineData(1, 1)]
    [InlineData(Chunk, 1)]
    [InlineData(Chunk + 1, 2)]
    [InlineData(10L * 1024 * 1024 * 1024, 1280)]
    public void Total_de_partes(long size, int expected) =>
        Assert.Equal(expected, PartPlan.TotalParts(size, Chunk));

    [Fact]
    public void Video_curto_e_uma_parte_so_com_o_tamanho_do_arquivo()
    {
        Assert.Equal(1, PartPlan.TotalParts(1234, Chunk));
        Assert.Equal(1234, PartPlan.ExpectedSize(1234, Chunk, 1, 1));
    }

    [Fact]
    public void Todas_as_partes_tem_o_tamanho_da_parte_menos_a_ultima()
    {
        const long size = 2L * Chunk + 100;
        var total = PartPlan.TotalParts(size, Chunk);

        Assert.Equal(Chunk, PartPlan.ExpectedSize(size, Chunk, total, 1));
        Assert.Equal(Chunk, PartPlan.ExpectedSize(size, Chunk, total, 2));
        Assert.Equal(100, PartPlan.ExpectedSize(size, Chunk, total, 3));
        Assert.Equal(size, Enumerable.Range(1, total).Sum(part => PartPlan.ExpectedSize(size, Chunk, total, part)));
    }

    [Fact]
    public void Parte_fora_do_intervalo_e_recusada() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PartPlan.ExpectedSize(100, Chunk, 1, 2));

    [Fact]
    public void Partes_que_faltam_para_retomar()
    {
        Assert.Equal([2, 4], PartPlan.Missing(4, [1, 3]));
        Assert.Empty(PartPlan.Missing(3, [3, 1, 2, 2]));
    }

    [Fact]
    public void Parte_padrao_respeita_o_minimo_do_s3() =>
        Assert.True(new Upload.Configuration.UploadOptions().ChunkSizeBytes >= PartPlan.MinChunkSize);
}
