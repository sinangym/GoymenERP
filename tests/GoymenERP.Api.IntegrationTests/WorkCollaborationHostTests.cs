using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Work.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>WP9 host composition of the Work record-access port (D8 and the new record kinds) and the disposal gate. SENTETIK.</summary>
public sealed class WorkCollaborationHostTests
{
    private static readonly Guid Company = Guid.Parse("c9000000-0000-4000-8000-000000000001");
    private static readonly Guid Actor = Guid.Parse("a9000000-0000-4000-8000-000000000001");
    private static readonly Guid Named = Guid.Parse("a9000000-0000-4000-8000-000000000002");
    private static readonly Guid Label = Guid.Parse("e9000000-0000-4000-8000-000000000001");

    [Theory]
    [InlineData(0, WorkRecordAccessOutcomeV1.Readable)]
    [InlineData(404, WorkRecordAccessOutcomeV1.NotReadable)]
    [InlineData(403, WorkRecordAccessOutcomeV1.NotReadable)]
    [InlineData(401, WorkRecordAccessOutcomeV1.NotReadable)]
    [InlineData(409, WorkRecordAccessOutcomeV1.Unavailable)]
    [InlineData(503, WorkRecordAccessOutcomeV1.Unavailable)]
    public async Task D8_the_stock_label_answer_comes_from_Inventorys_authorized_label_read_port_not_its_store(int status, WorkRecordAccessOutcomeV1 expected)
    {
        var labels = new FakeLabelDetails(status);
        // No IInventoryLabelStore is registered at all: reaching for the store would throw and answer Unavailable.
        var port = Port(services => services.AddSingleton<IInventoryLabelDetailsV1>(labels), new FakeHolders([]));
        Assert.Equal(expected, await port.CheckCurrentActorAsync(Company, new(WorkRecordTypeV1.StockLabel, Label)));
        Assert.Equal(new[] { Label }, labels.Asked);
    }

    [Fact]
    public void The_host_port_source_no_longer_names_the_Inventory_label_store()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "backend", "GoymenERP.Api", "WorkRuntime", "WorkCollaborationHostV1.cs"));
        Assert.DoesNotContain("IInventoryLabelStore", source, StringComparison.Ordinal);
        Assert.Contains("IInventoryLabelDetailsV1", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_named_user_reads_a_record_only_holding_every_owner_read_permission_now()
    {
        var label = new WorkRecordRefV1(WorkRecordTypeV1.StockLabel, Label);
        var onlyLabels = Port(_ => { }, new FakeHolders([ErpPermissions.InventoryLabelsRead]));
        Assert.Equal(WorkRecordAccessOutcomeV1.NotReadable, await onlyLabels.CheckUserAsync(Company, label, Named));
        var both = Port(_ => { }, new FakeHolders([ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead]));
        Assert.Equal(WorkRecordAccessOutcomeV1.Readable, await both.CheckUserAsync(Company, label, Named));
        var unavailable = Port(_ => { }, new FakeHolders([], UserDirectoryOutcomeV1.Unavailable));
        Assert.Equal(WorkRecordAccessOutcomeV1.Unavailable, await unavailable.CheckUserAsync(Company, label, Named));
        Assert.Equal(WorkRecordAccessOutcomeV1.NotReadable, await both.CheckUserAsync(Guid.NewGuid(), label, Named));
    }

    [Fact]
    public void Every_record_kind_names_registered_company_wide_owner_read_permissions()
    {
        foreach (var wire in WorkRecordTypesV1.WireNames)
        {
            Assert.True(WorkRecordTypesV1.TryParse(wire, out var type));
            var permissions = WorkOwnerRecordAccessPortV1.ReadPermissions(type);
            Assert.NotEmpty(permissions);
            Assert.All(permissions, permission =>
            {
                Assert.True(ErpPermissions.IsKnown(permission), permission);
                Assert.Contains(permission, ErpPermissions.CompanyWideOnly);
                Assert.EndsWith(".read", permission, StringComparison.Ordinal);
            });
        }

        Assert.Equal(new[] { ErpPermissions.SalesInvoicesRead, ErpPermissions.SalesOrdersPriceRead },
            WorkOwnerRecordAccessPortV1.ReadPermissions(WorkRecordTypeV1.SalesInvoice));
        Assert.Equal(new[] { ErpPermissions.LogisticsLoadingInstructionsRead },
            WorkOwnerRecordAccessPortV1.ReadPermissions(WorkRecordTypeV1.LoadingInstruction));
        Assert.Equal(new[] { ErpPermissions.FinanceManualBankPaymentDraftRead },
            WorkOwnerRecordAccessPortV1.ReadPermissions(WorkRecordTypeV1.FinanceManualBankPayment));
    }

    [Fact]
    public async Task YOL_D26_the_host_composes_the_disposal_path_closed_and_the_owner_disposal_port_unavailable()
    {
        var services = new ServiceCollection();
        services.AddWorkCollaborationHostV1(new ConfigurationBuilder().Build());
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkNoteDisposalV1));
        var disposal = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkRecordDisposalPortV1));
        var port = Assert.IsAssignableFrom<IWorkRecordDisposalPortV1>(Activator.CreateInstance(disposal.ImplementationType!));
        Assert.Equal(WorkRecordDisposalOutcomeV1.Unavailable, await port.CheckDisposedAsync(Company, new(WorkRecordTypeV1.SalesOrderDraft, Label)));
        Assert.Null(new ConfigurationBuilder().Build()[WorkNoteRetentionV1.DisposalEnabledKey]);
    }

    private static WorkOwnerRecordAccessPortV1 Port(Action<IServiceCollection> configure, IUserPermissionHolderReadPortV1 holders)
    {
        var services = new ServiceCollection();
        configure(services);
        var company = new CompanyContext();
        company.Set(Company);
        return new WorkOwnerRecordAccessPortV1(services.BuildServiceProvider(), company, new FakeUser(), new FakeCorrelation(), holders);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class FakeLabelDetails(int status) : IInventoryLabelDetailsV1
    {
        public List<Guid> Asked { get; } = [];

        public Task<InventoryLabelDetailV1> GetAsync(Guid labelId, CancellationToken cancellationToken = default)
        {
            Asked.Add(labelId);
            // A successful answer's body is never inspected by the port; only "no exception" means readable.
            return status == 0 ? Task.FromResult<InventoryLabelDetailV1>(null!) : throw new InventoryLabelDetailException(status, "SENTETIK");
        }
    }

    private sealed class FakeHolders(IReadOnlyCollection<string> held, UserDirectoryOutcomeV1 outcome = UserDirectoryOutcomeV1.Found)
        : IUserPermissionHolderReadPortV1
    {
        public Task<UserPermissionHolderAnswerV1> HoldsAsync(UserPermissionHolderRequestV1 request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserPermissionHolderAnswerV1(outcome, request.CompanyId == Company && held.Contains(request.Permission)));
    }

    private sealed class FakeUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class FakeCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "corr-wp9";
        public string? CausationId => null;
        public string SourceChannel => "api";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) { }
    }
}
