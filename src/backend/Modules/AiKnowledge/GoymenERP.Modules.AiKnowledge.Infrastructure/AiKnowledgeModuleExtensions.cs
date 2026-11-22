using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.AiKnowledge.Infrastructure;

public static class AiKnowledgeModuleExtensions
{
    /// <summary>
    /// WP-A4 knowledge package approvals. The host supplies <see cref="IAiKnowledgePackageCatalogV1"/> (the packaged files are
    /// API content) and the audit interceptor. Nothing here grants <c>ai.knowledge.approve</c> or approves anything by itself.
    /// </summary>
    public static IServiceCollection AddAiKnowledgeModule(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = configuration["DB_PROVIDER"] ?? "SqlServer";
        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Desteklenmeyen veritabanı sağlayıcısı: {provider}");
        }

        var connectionString = configuration["DB_CONNECTION_STRING"]
            ?? configuration.GetConnectionString("GoymenErp");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server bağlantı dizesi DB_CONNECTION_STRING secret/env değeriyle tanımlanmalıdır.");
        }

        services.AddDbContext<AiKnowledgeDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AiKnowledgeDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAiKnowledgeApprovalServiceV1, AiKnowledgeApprovalServiceV1>();
        services.AddScoped<IAiKnowledgeApprovalReaderV1, AiKnowledgeApprovalReaderV1>();
        return services;
    }

    public static async Task InitializeAiKnowledgeModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AiKnowledgeDbContext>();
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
    }
}
