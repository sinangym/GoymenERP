using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingSubcontractCompletionHostPortTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-4000-8000-000000000018");
    private static readonly Guid Order = Guid.Parse("20000000-0000-4000-8000-000000000018");
    private static readonly Guid Customer = Guid.Parse("30000000-0000-4000-8000-000000000018");
    private static readonly Guid Label = Guid.Parse("40000000-0000-4000-8000-000000000018");
    private static ManufacturingSubcontractCompletionReadV1 Available(bool completed = true) =>
        new(ManufacturingSubcontractCompletionOutcomeV1.Available, Company, Order, Customer, [Label], completed);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Completion_and_output_membership_are_the_owners_answer_without_a_sale(bool completed)
    {
        var company = new CompanyContext();
        company.Set(Company);
        var answer = await new ManufacturingLogisticsSubcontractOrderReadPort(new Owner(Available(completed)), company)
            .GetAsync(Order);
        Assert.Equal((LogisticsSubcontractOrderReadOutcomeV1.Available, Company, Order, Customer, completed),
            (answer.Outcome, answer.CompanyId, answer.ManufacturingOrderId, answer.CustomerPartyId, answer.IsCompleted));
        Assert.Equal([Label], answer.OutputLabelIds);
    }

    [Fact]
    public async Task A_foreign_or_malformed_owner_answer_and_a_changed_context_are_unavailable()
    {
        var company = new MutableCompany(Company);
        var answers = new[] { Available() with { CompanyId = Guid.NewGuid() },
            Available() with { ManufacturingOrderId = Guid.NewGuid() }, Available() with { CustomerPartyId = Guid.Empty },
            Available() with { OutputLabelIds = [Label, Label] }, Available() with { OutputLabelIds = [Guid.Empty] } };
        foreach (var answer in answers)
        {
            var result = await new ManufacturingLogisticsSubcontractOrderReadPort(new Owner(answer), company).GetAsync(Order);
            Assert.Equal(LogisticsSubcontractOrderReadOutcomeV1.Unavailable, result.Outcome);
            Assert.Empty(result.OutputLabelIds);
        }
        var foreignCompany = Guid.NewGuid();
        var switched = new Owner(Available(), () => company.Set(foreignCompany));
        Assert.Equal(LogisticsSubcontractOrderReadOutcomeV1.Unavailable,
            (await new ManufacturingLogisticsSubcontractOrderReadPort(switched, company).GetAsync(Order)).Outcome);
        Assert.Equal(foreignCompany, company.CompanyId);
    }

    private sealed class MutableCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }
    private sealed class Owner(ManufacturingSubcontractCompletionReadV1 answer, Action? beforeAnswer = null)
        : IManufacturingSubcontractCompletionReadPortV1
    {
        public Task<ManufacturingSubcontractCompletionReadV1> GetAsync(Guid companyId, Guid orderId,
            CancellationToken cancellationToken = default)
        {
            beforeAnswer?.Invoke();
            return Task.FromResult(answer);
        }
    }
}
