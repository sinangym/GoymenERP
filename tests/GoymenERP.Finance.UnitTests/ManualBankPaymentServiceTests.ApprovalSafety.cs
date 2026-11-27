using System.Data;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class ManualBankPaymentServiceTests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("dirty")] [InlineData("audit")] [InlineData("ambient")]
    [InlineData("nonrelational-clean")] [InlineData("nonrelational-audit")]
    public async Task Production_approval_boundary_refuses_caller_work_without_native_reads_or_cleanup(string boundary)
    {
        var company = new CompanyContext(); company.Set(Company);
        var options = new DbContextOptionsBuilder<FinanceDbContext>();
        if (boundary.StartsWith("nonrelational", StringComparison.Ordinal)) options.UseInMemoryDatabase("SENTETIK-manual-entry-" + Guid.NewGuid());
        else options.UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;");
        await using var database = new FinanceDbContext(options.Options, company);
        var row = new ManualBankPaymentRow { CompanyId = Company, Id = Guid.NewGuid() };
        if (boundary == "dirty") database.ManualBankPayments.Add(row);
        var permissions = new ApprovalEntryPermissions();
        var writer = new ApprovalEntryWriter { HasPendingDrafts = boundary is "audit" or "nonrelational-audit" };
        var core = ApprovalCore(database, company, permissions, writer);
        var service = new GuardedManualBankPaymentService(core, database, boundary == "missing-writer" ? null : writer);
        using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        var result = await service.ApproveAsync(new(Company, "checker", "SENTETIK", null), new("original-key"), new(row.Id, "1"), default);
        Assert.Equal(CommandOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(0, permissions.Calls);
        Assert.Equal((0, 0), (writer.Writes, writer.Discards));
        Assert.Equal(boundary is "audit" or "nonrelational-audit", writer.HasPendingDrafts);
        Assert.Equal(boundary == "dirty", database.ChangeTracker.HasChanges());
        if (boundary == "dirty") Assert.Same(row, Assert.Single(database.ChangeTracker.Entries()).Entity);
    }

    [Fact]
    public async Task Actual_Finance_module_factory_registers_only_the_guarded_interface_not_the_raw_synthetic_core()
    {
        var company = new CompanyContext(); company.Set(Company);
        await using var database = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("SENTETIK-manual-registration-" + Guid.NewGuid()).Options, company);
        var services = new ServiceCollection();
        services.AddFinanceServices();
        services.AddSingleton<ICompanyContext>(company);
        services.AddSingleton<ICurrentUser>(new User("checker"));
        services.AddSingleton<IErpAuthorityRevalidationService>(new ApprovalRegistrationAuthority());
        services.AddSingleton(database);
        services.AddSingleton<ICorrelationContext>(new CorrelationContext());
        services.AddSingleton<IAuditEventCollector>(new AuditEventCollector());
        services.AddSingleton<IAuditTransactionWriterV1>(new ApprovalEntryWriter());
        services.AddSingleton<IFinancePartyAccountPort>(new PartyPort());
        services.AddSingleton<ISettingsPrerequisiteProvider>(new SettingsPort());
        services.AddSingleton<IFinanceRecordPeriodRule>(new OpenPeriod());
        services.AddSingleton<IFinanceTreasuryBalanceGuard>(new Balance(TreasuryBalanceOutcome.Sufficient));
        services.AddSingleton(TimeProvider.System);
        Assert.Single(services, item => item.ServiceType == typeof(IManualBankPaymentService));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = Assert.IsType<GuardedManualBankPaymentService>(scope.ServiceProvider.GetRequiredService<IManualBankPaymentService>());
        Assert.Null(scope.ServiceProvider.GetService<ManualBankPaymentService>());
        Assert.Equal(CommandOutcome.Unavailable, (await service.ApproveAsync(new(Company, "checker", "SENTETIK", null),
            new("entry"), new(Guid.NewGuid(), "1"), default)).Outcome);
    }

    private static ManualBankPaymentService ApprovalCore(FinanceDbContext database, ICompanyContext company,
        IFinancePermissionPort permissions, IAuditTransactionWriterV1? writer) => new(database, company, new User("checker"), permissions,
        new PartyPort(), new DocumentPort(ManualBankPaymentDocumentValidationOutcome.Found), new SettingsPort(), new OpenPeriod(),
        new AuditEventCollector(), new(FinanceMoneyOperations.ManualBankPaymentV1),
        new(new(FinancePermissionNames.ManualBankPaymentDraftSave, [], "Web"),
            new(FinancePermissionNames.ManualBankPaymentApprove, [], "Web"), new(FinancePermissionNames.ManualBankPaymentRecord, [], "Web")),
        TimeProvider.System, new Balance(TreasuryBalanceOutcome.Sufficient), writer);

    private sealed class ApprovalEntryPermissions : IFinancePermissionPort
    {
        public int Calls;
        public Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken t)
        { Calls++; return Task.FromResult(PermissionDecision.Allowed); }
    }
    // Controlled entry probe only: never claims to flush a real collector or to provide a SQL transaction.
    private sealed class ApprovalEntryWriter : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts { get; set; }
        public int Writes, Discards;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default)
        { Writes++; throw new InvalidOperationException("No write at entry refusal."); }
        public void DiscardPending() { Discards++; HasPendingDrafts = false; }
    }
    private sealed class ApprovalRegistrationAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(PermissionScope.Denied);
    }
}
