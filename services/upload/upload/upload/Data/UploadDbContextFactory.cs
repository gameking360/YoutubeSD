using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Upload.Messaging.Persistence;

namespace Upload.Data;

/// <summary>
/// Usado só pelo "dotnet ef" para gerar migrations. Com EF_SCOPE_ENTITIES, o modelo fica limitado
/// às tabelas listadas, o que permite uma migration por tabela (como no Download).
/// </summary>
public sealed class UploadDbContextFactory : IDesignTimeDbContextFactory<UploadDbContext>
{
    public UploadDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__uploadDb")
            ?? "Server=localhost;Port=3306;User ID=root;Password=root123;Database=upload_db";

        var builder = new DbContextOptionsBuilder<UploadDbContext>();
        builder.UseYoutubeMySql(connectionString);
        builder.ReplaceService<IModelCustomizer, TableScopedModelCustomizer>();
        return new UploadDbContext(builder.Options);
    }
}
