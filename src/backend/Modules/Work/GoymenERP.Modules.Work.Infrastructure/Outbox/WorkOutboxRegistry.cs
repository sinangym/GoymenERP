using GoymenERP.BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Work.Infrastructure.Outbox;

internal sealed record WorkOutboxSourceRegistration(
    string SourceName,
    Type ImplementationType,
    IReadOnlyList<WorkOutboxEventTypeV1> Publishes);

internal sealed record WorkInboxConsumerRegistration(
    string ConsumerName,
    Type ImplementationType,
    IReadOnlyList<WorkInboxSubscriptionV1> Subscriptions);

/// <summary>
/// Registration contract v1 (F-021): which source names and consumer names exist and what they publish/subscribe.
/// One instance lives in the service collection. Local rules (name shape, duplicates, empty lists) are refused at
/// registration; cross references (a subscription names a registered source and an event that source declares) are
/// checked at host start, so owners can register in any order.
/// </summary>
internal sealed class WorkOutboxRegistry
{
    private readonly Dictionary<string, WorkOutboxSourceRegistration> sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkInboxConsumerRegistration> consumers = new(StringComparer.Ordinal);

    public IReadOnlyCollection<WorkOutboxSourceRegistration> Sources => sources.Values;

    public IReadOnlyCollection<WorkInboxConsumerRegistration> Consumers => consumers.Values;

    public static WorkOutboxRegistry GetOrAdd(IServiceCollection services)
    {
        var existing = services.LastOrDefault(d => d.ServiceType == typeof(WorkOutboxRegistry))?.ImplementationInstance;
        if (existing is WorkOutboxRegistry registry)
        {
            return registry;
        }

        registry = new WorkOutboxRegistry();
        services.AddSingleton(registry);
        services.AddSingleton<IValidateOptions<WorkOutboxRegistrationOptions>>(new WorkOutboxRegistryValidator(registry));
        services.AddOptions<WorkOutboxRegistrationOptions>().ValidateOnStart();
        return registry;
    }

    public WorkOutboxSourceRegistration AddSource(string sourceName, Type implementationType, IReadOnlyList<WorkOutboxEventTypeV1>? publishes)
    {
        if (!WorkOutboxRegistrationNamesV1.IsValidName(sourceName))
        {
            throw new ArgumentException(
                $"Outbox source name must be lower-case ASCII segments of at most {WorkOutboxRegistrationNamesV1.MaxNameLength} characters.",
                nameof(sourceName));
        }

        if (sources.ContainsKey(sourceName))
        {
            throw new InvalidOperationException($"Outbox source '{sourceName}' is already registered.");
        }

        if (publishes is null || publishes.Count == 0)
        {
            throw new ArgumentException($"Outbox source '{sourceName}' must declare at least one event type.", nameof(publishes));
        }

        var declared = new HashSet<WorkOutboxEventTypeV1>();
        foreach (var eventType in publishes)
        {
            if (eventType is null
                || !WorkOutboxRegistrationNamesV1.IsValidEventType(eventType.EventType)
                || !WorkOutboxRegistrationNamesV1.IsValidVersion(eventType.Version))
            {
                throw new ArgumentException($"Outbox source '{sourceName}' declares an invalid event type or version.", nameof(publishes));
            }

            if (!declared.Add(eventType))
            {
                throw new ArgumentException(
                    $"Outbox source '{sourceName}' declares '{eventType.EventType}' v{eventType.Version} more than once.", nameof(publishes));
            }
        }

        var registration = new WorkOutboxSourceRegistration(sourceName, implementationType, publishes.ToArray());
        sources.Add(sourceName, registration);
        return registration;
    }

    public WorkInboxConsumerRegistration AddConsumer(
        string consumerName, Type implementationType, IReadOnlyList<WorkInboxSubscriptionV1>? subscriptions)
    {
        if (!WorkOutboxRegistrationNamesV1.IsValidName(consumerName))
        {
            throw new ArgumentException(
                $"Inbox consumer name must be lower-case ASCII segments of at most {WorkOutboxRegistrationNamesV1.MaxNameLength} characters.",
                nameof(consumerName));
        }

        if (consumers.ContainsKey(consumerName))
        {
            throw new InvalidOperationException($"Inbox consumer '{consumerName}' is already registered.");
        }

        if (subscriptions is null || subscriptions.Count == 0)
        {
            throw new ArgumentException($"Inbox consumer '{consumerName}' must subscribe to at least one event.", nameof(subscriptions));
        }

        var seen = new HashSet<WorkInboxSubscriptionV1>();
        foreach (var subscription in subscriptions)
        {
            if (subscription is null
                || !WorkOutboxRegistrationNamesV1.IsValidName(subscription.SourceName)
                || !WorkOutboxRegistrationNamesV1.IsValidEventType(subscription.EventType)
                || !WorkOutboxRegistrationNamesV1.IsValidVersion(subscription.Version))
            {
                throw new ArgumentException($"Inbox consumer '{consumerName}' has an invalid subscription.", nameof(subscriptions));
            }

            if (!seen.Add(subscription))
            {
                throw new ArgumentException(
                    $"Inbox consumer '{consumerName}' subscribes to '{subscription.SourceName}/{subscription.EventType}' "
                    + $"v{subscription.Version} more than once.",
                    nameof(subscriptions));
            }
        }

        var registration = new WorkInboxConsumerRegistration(consumerName, implementationType, subscriptions.ToArray());
        consumers.Add(consumerName, registration);
        return registration;
    }

    /// <summary>Cross-reference errors; empty when every subscription names a registered source and a declared event.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        foreach (var consumer in consumers.Values.OrderBy(c => c.ConsumerName, StringComparer.Ordinal))
        {
            foreach (var subscription in consumer.Subscriptions)
            {
                if (!sources.TryGetValue(subscription.SourceName, out var source))
                {
                    errors.Add($"Inbox consumer '{consumer.ConsumerName}' subscribes to unregistered source '{subscription.SourceName}'.");
                    continue;
                }

                if (!source.Publishes.Contains(new WorkOutboxEventTypeV1(subscription.EventType, subscription.Version)))
                {
                    errors.Add(
                        $"Inbox consumer '{consumer.ConsumerName}' subscribes to '{subscription.EventType}' v{subscription.Version}, "
                        + $"which source '{subscription.SourceName}' does not declare.");
                }
            }
        }

        return errors;
    }
}

/// <summary>Marker options whose start-up validation checks the registration cross references.</summary>
internal sealed class WorkOutboxRegistrationOptions
{
}

internal sealed class WorkOutboxRegistryValidator(WorkOutboxRegistry registry) : IValidateOptions<WorkOutboxRegistrationOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkOutboxRegistrationOptions options)
    {
        var errors = registry.Validate();
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

/// <summary>Presents a registered <see cref="IWorkOutboxEventSourceV1"/> to the dispatcher under its registered name.</summary>
internal sealed class WorkOutboxEventSourceAdapter(WorkOutboxSourceRegistration registration, IWorkOutboxEventSourceV1 source)
    : IOutboxSourceV1
{
    public string SourceName => registration.SourceName;

    public async Task<IReadOnlyList<OutboxMessageEnvelopeV1>> ReadAfterAsync(
        string? afterPosition, int maxCount, CancellationToken cancellationToken)
    {
        var events = await source.ReadAfterAsync(afterPosition, maxCount, cancellationToken)
            ?? throw new InvalidOperationException($"Outbox source '{registration.SourceName}' returned no event list.");
        var envelopes = new OutboxMessageEnvelopeV1[events.Count];
        for (var index = 0; index < events.Count; index++)
        {
            // A null entry has no position to skip past, so it is a source failure (the cursor does not move).
            var item = events[index]
                ?? throw new InvalidOperationException($"Outbox source '{registration.SourceName}' returned a null event.");
            envelopes[index] = new OutboxMessageEnvelopeV1(
                registration.SourceName,
                item.EventId,
                item.CompanyId,
                item.EventType,
                item.Version,
                item.PayloadJson,
                item.OccurredAtUtc,
                item.CorrelationId,
                item.CausationId,
                item.Position);
        }

        return envelopes;
    }
}

/// <summary>Presents a registered <see cref="IWorkInboxConsumerV1"/> to the dispatcher under its registered name.</summary>
internal sealed class WorkInboxConsumerAdapter(WorkInboxConsumerRegistration registration, Func<IWorkInboxConsumerV1> consumerFactory)
    : IOutboxConsumerV1
{
    private readonly Lazy<IWorkInboxConsumerV1> consumer = new(consumerFactory);
    private readonly IReadOnlyCollection<OutboxSubscriptionV1> subscriptions = registration.Subscriptions
        .Select(s => new OutboxSubscriptionV1(s.SourceName, s.EventType, s.Version))
        .ToArray();

    public string ConsumerName => registration.ConsumerName;

    public IReadOnlyCollection<OutboxSubscriptionV1> Subscriptions => subscriptions;

    public Task HandleAsync(OutboxMessageEnvelopeV1 message, CancellationToken cancellationToken) =>
        consumer.Value.HandleAsync(
            new WorkInboxEventV1(
                message.SourceName,
                message.MessageId,
                message.CompanyId,
                message.EventType,
                message.SchemaVersion,
                message.PayloadJson,
                message.OccurredAtUtc,
                message.CorrelationId,
                message.CausationId),
            cancellationToken);
}
