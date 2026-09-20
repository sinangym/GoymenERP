using GoymenERP.Api.Shell;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WHICH period rule the composition loads — the question no other test in this repository asks.
/// <para>
/// The distinction matters, because a nearby test looks like it already covers this and does not.
/// <c>FinanceRecordSqlServerRelationalAcceptanceTests.Undecided_period_rule_refuses_every_record_and_stores_nothing</c>
/// proves the fallback's BEHAVIOUR — given the undecided rule, every Record is refused and nothing is
/// stored — but it hand-injects <c>new UndecidedFinanceRecordPeriodRule()</c>, so it cannot say which
/// rule a real host ends up with. Two different questions: <b>what happens if the wrong rule is
/// loaded</b> (answered there) and <b>can the wrong rule get loaded</b> (answered here). Neither test
/// is a duplicate of the other, and deleting this one as tidying would silently reopen the gap.
/// </para>
/// <para>
/// Why a test at all, when <c>Program.cs</c> is hash-pinned: a fingerprint says "this file changed,
/// re-review", never "this change broke the rule". The documented repair for that red is to recompute
/// the hash — which every lane has done repeatedly — so a tidy reorder can go green with the
/// composition altered. That is a review gate, not a behaviour gate. This is the behaviour gate.
/// </para>
/// SENTETIK: no database, no host, no HTTP — only the registration semantics the composition relies on.
/// </summary>
public sealed class FinanceRecordPeriodRuleCompositionTests
{
    private static List<ServiceDescriptor> RuleDescriptors(IServiceCollection services) =>
        [.. services.Where(descriptor => descriptor.ServiceType == typeof(IFinanceRecordPeriodRule))];

    /// <summary>
    /// The host's registration BEFORE <see cref="FinanceModuleExtensions.AddFinanceServices"/>: the module's
    /// TryAdd finds the service type already present and does nothing, so the undecided fallback never enters
    /// the container at all. This is what <c>Program.cs</c>'s ordering buys, stated as behaviour.
    /// </summary>
    [Fact]
    public void The_host_rule_registered_first_leaves_the_undecided_fallback_out_of_the_container()
    {
        var services = new ServiceCollection();
        services.AddScoped<IFinanceRecordPeriodRule, OccurredOnFinanceRecordPeriodRule>();

        services.AddFinanceServices();

        var descriptors = RuleDescriptors(services);
        var only = Assert.Single(descriptors);
        Assert.Equal(typeof(OccurredOnFinanceRecordPeriodRule), only.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, only.Lifetime);
    }

    /// <summary>
    /// The calibration: a composition that registers NO rule really does get the undecided one, and it really
    /// does refuse. Without this half the case above could pass while the fallback did not exist — a gate that
    /// cannot fail in the thing it guards.
    /// </summary>
    [Fact]
    public async Task A_composition_that_registers_no_rule_gets_the_undecided_one_and_it_refuses()
    {
        var services = new ServiceCollection();

        services.AddFinanceServices();

        var only = Assert.Single(RuleDescriptors(services));
        Assert.Equal(typeof(UndecidedFinanceRecordPeriodRule), only.ImplementationType);

        using var provider = services.BuildServiceProvider();
        var rule = provider.GetRequiredService<IFinanceRecordPeriodRule>();
        Assert.IsType<UndecidedFinanceRecordPeriodRule>(rule);

        var answer = await rule.CheckAsync(
            new FinanceContext(Guid.NewGuid(), "SENTETIK-actor", "SENTETIK-correlation", null),
            new DateOnly(2026, 9, 15),
            new DateOnly(2026, 8, 25),
            DateTimeOffset.Parse("2026-10-02T09:00:00Z"),
            CancellationToken.None);

        Assert.Equal(RecordPeriodOutcome.Unavailable, answer.Outcome);
        Assert.Equal(RecordPeriodAnswer.RuleUnavailableCode, answer.Code);
    }

    /// <summary>
    /// What the WRONG order actually costs, measured rather than assumed — and it is not what the ordering
    /// comment in the composition claims. A registration added AFTER <c>AddFinanceServices</c> does not lose to
    /// the fallback: the module's TryAdd has already put the fallback in, the later call adds a SECOND
    /// descriptor, and resolving a single service returns the LAST registration. So the reordered host would
    /// still run its own rule, while carrying a shadowed singleton fallback it can never use.
    /// <para>
    /// A stub stands in for the host rule here on purpose: this case is about registration semantics, and
    /// building the real one would drag in the company context, the current user and the period store, which
    /// would make a DI question look like a Finance question.
    /// </para>
    /// </summary>
    [Fact]
    public void A_rule_registered_after_the_module_shadows_the_fallback_instead_of_losing_to_it()
    {
        var services = new ServiceCollection();
        services.AddFinanceServices();

        services.AddScoped<IFinanceRecordPeriodRule, StubRule>();

        var descriptors = RuleDescriptors(services);
        Assert.Equal(2, descriptors.Count);
        Assert.Equal(typeof(UndecidedFinanceRecordPeriodRule), descriptors[0].ImplementationType);
        Assert.Equal(typeof(StubRule), descriptors[1].ImplementationType);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<StubRule>(scope.ServiceProvider.GetRequiredService<IFinanceRecordPeriodRule>());

        // Both are still reachable as a set, which is the real smell of the wrong order: the container holds a
        // rule nothing resolves. Registering first is what keeps that out.
        Assert.Equal(2, scope.ServiceProvider.GetServices<IFinanceRecordPeriodRule>().Count());
    }

    private sealed class StubRule : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset recordedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(RecordPeriodAnswer.Unavailable);
    }
}
