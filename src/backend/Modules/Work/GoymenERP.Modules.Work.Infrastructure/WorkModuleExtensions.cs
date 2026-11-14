using GoymenERP.BuildingBlocks.Messaging;
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
    /// <see cref="AddWorkModule"/>; sources and consumers are registered by their owners with
    /// <see cref="AddWorkOutboxEventSource{TSource}"/> and <see cref="AddWorkInboxConsumer{TConsumer}"/>, the company
    /// directory by Organization.
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

    /// <summary>
    /// Registers a module outbox as a dispatcher source under <paramref name="sourceName"/> (registration contract v1,
    /// F-021). The name is unique, lower-case ASCII and is the cursor/inbox key, so it must never be renamed once
    /// deliveries exist. Registering a source does not enable the dispatcher.
    /// </summary>
    public static IServiceCollection AddWorkOutboxEventSource<TSource>(
        this IServiceCollection services,
        string sourceName,
        params WorkOutboxEventTypeV1[] publishes)
        where TSource : class, IWorkOutboxEventSourceV1
    {
        ArgumentNullException.ThrowIfNull(services);
        var registration = WorkOutboxRegistry.GetOrAdd(services).AddSource(sourceName, typeof(TSource), publishes);
        services.TryAddScoped<TSource>();
        services.AddScoped<IOutboxSourceV1>(sp =>
            new WorkOutboxEventSourceAdapter(registration, sp.GetRequiredService<TSource>()));
        return services;
    }

    /// <summary>
    /// Registers a module handler as an inbox consumer under <paramref name="consumerName"/> (registration contract v1,
    /// F-021). The dedup key is (company, consumer name, event id), so the name must never be renamed once deliveries
    /// exist. Every subscription must name a registered source and an event it declares; that is checked at host start.
    /// Registering a consumer does not enable the dispatcher.
    /// </summary>
    public static IServiceCollection AddWorkInboxConsumer<TConsumer>(
        this IServiceCollection services,
        string consumerName,
        params WorkInboxSubscriptionV1[] subscriptions)
        where TConsumer : class, IWorkInboxConsumerV1
    {
        ArgumentNullException.ThrowIfNull(services);
        var registration = WorkOutboxRegistry.GetOrAdd(services).AddConsumer(consumerName, typeof(TConsumer), subscriptions);
        services.TryAddScoped<TConsumer>();
        services.AddScoped<IOutboxConsumerV1>(sp =>
            new WorkInboxConsumerAdapter(registration, () => sp.GetRequiredService<TConsumer>()));
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
