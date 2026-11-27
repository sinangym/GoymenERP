using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceOfficialInvoiceImportHostAuthorityTests
{
    [Fact]
    public async Task Runtime_approval_interface_is_guarded_and_uses_the_real_scoped_audit_collector_writer_pair()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = "Server=unused.invalid;Database=unused;Connect Timeout=1;",
            // Existing Audit retention fixture/shipped timeout; synthetic DI binding, no production setting change.
            ["Audit:RefusedAttempts:Retention:Period"] = "P2Y",
            ["Audit:RefusedAttempts:Retention:PolicyRef"] = "policy-ref-1",
            ["Audit:RefusedAttempts:Retention:AutoDisposalEnabled"] = "false",
            ["Audit:RefusedAttempts:StandaloneWrite:Timeout"] = "00:00:02",
        }).Build();
        services.AddAuditModule(config);
        services.AddScoped<ICompanyContext>(_ => new MutableCompanyContext(Company));
        services.AddSingleton<ICurrentUser>(new User(Actor, Company));
        services.AddSingleton<IErpAuthorityRevalidationService>(new Revalidation(Allowed("SENTETIK-APPROVE")));
        services.AddScoped<FinancePermissionPort>();
        services.AddScoped<ICorrelationContext>(_ =>
        { var value = new CorrelationContext(); value.Set("SENTETIK-host-safety", null, "test", null, null); return value; });
        services.AddDbContext<FinanceDbContext>(options => options.UseSqlServer(config["DB_CONNECTION_STRING"]!));
        services.AddFinanceOfficialInvoiceImportHost();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var owner = provider.CreateScope();
        var collector = owner.ServiceProvider.GetRequiredService<AuditEventCollector>();
        Assert.Same(collector, owner.ServiceProvider.GetRequiredService<IAuditEventCollector>());
        var writer = owner.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        var service = owner.ServiceProvider.GetRequiredService<IFinanceOfficialInvoiceImportApprovalServiceV1>();
        Assert.Same(service, owner.ServiceProvider.GetRequiredService<FinanceOfficialInvoiceImportApprovalServiceV1>());
        var draft = new AuditEventDraft(Company, "SENTETIK.caller.v1", "SENTETIK", "SENTETIK", "Caller", null, 1,
            AuditActorTypes.User, Actor, FinanceOfficialInvoiceImportPermissionNamesV1.Approve, [], "{}", "{}",
            "SENTETIK caller", null, AuditAiInvolvement.None, null, "test", null, null, "SENTETIK-host-safety", null, "caller-key");
        collector.Enqueue(draft); Assert.True(writer.HasPendingDrafts);
        var result = await service.ApproveAsync(Context(), new("approval-key"), new(Guid.NewGuid(), "1"), default);
        Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.Null(result.Resource);
        Assert.True(writer.HasPendingDrafts);
        Assert.Null(owner.ServiceProvider.GetRequiredService<FinanceDbContext>().Database.CurrentTransaction);
        writer.DiscardPending(); Assert.False(writer.HasPendingDrafts);
        using var another = provider.CreateScope();
        Assert.NotSame(collector, another.ServiceProvider.GetRequiredService<AuditEventCollector>());
        Assert.False(another.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>().HasPendingDrafts);
    }
}
