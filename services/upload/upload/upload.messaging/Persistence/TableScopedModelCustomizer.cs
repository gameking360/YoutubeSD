using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Upload.Messaging.Persistence;

/// <summary>
/// Só em tempo de design (dotnet ef): limita o modelo às entidades listadas em EF_SCOPE_ENTITIES.
/// Permite gerar uma migration por tabela, como no Download. Em execução não faz nada.
/// </summary>
public sealed class TableScopedModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public const string Variable = "EF_SCOPE_ENTITIES";

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        var scope = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(scope))
        {
            return;
        }

        var allowed = scope.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (!allowed.Contains(entityType.ClrType.Name))
            {
                modelBuilder.Ignore(entityType.ClrType);
            }
        }
    }
}
