using GoymenERP.BuildingBlocks.OpenApi;
using GoymenERP.Modules.Work.Infrastructure.Outbox;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Work.Infrastructure;

public static class WorkModuleExtensions
{
    public static IServiceCollection AddWorkModule(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = configuration["DB_PROVIDER"] ?? "SqlServer";
        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Desteklenmeyen veritabani saglayicisi: {provider}");
        }

        var connectionString = configuration["DB_CONNECTION_STRING"]
            ?? configuration.GetConnectionString("GoymenErp");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server baglanti dizesi DB_CONNECTION_STRING secret/env degeriyle tanimlanmalidir.");
        }

        services.AddDbContext<WorkDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", WorkDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<WorkDesiredStateStoreV2>();
        return services;
    }

    /// <summary>
    /// Registers the ERP-000F outbox dispatcher. OFF by default: nothing runs unless
    /// <c>Work:OutboxDispatcher:Enabled</c> is true (production activation is a human gate). Call after
    /// <see cref="AddWorkModule"/>; sources, consumers and the company directory are registered by their owners.
    /// </summary>
    public static IServiceCollection AddWorkOutboxDispatcher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(WorkOutboxDispatcherOptions.SectionName);
        services.Configure<WorkOutboxDispatcherOptions>(section);
        var options = new WorkOutboxDispatcherOptions();
        section.Bind(options);
        if (!options.Enabled)
        {
            return services;
        }

        options.Resolve();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<WorkOutboxDispatcher>();
        if (!OpenApiDocumentGenerationMode.IsActive)
        {
            services.AddHostedService<WorkOutboxDispatcherHostedService>();
        }

        return services;
    }

    public static async Task InitializeWorkModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkDbContext>();
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
