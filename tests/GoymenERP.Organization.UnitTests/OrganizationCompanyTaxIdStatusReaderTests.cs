using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Organization.UnitTests;

/// <summary>
/// KIM-D35: the module read of the company's "VKN girilmemiş" mark for modules that issue a VKN-bearing official
/// document. Company-scoped by the resolved context and nothing else. The rows and VKNs here are synthetic.
/// </summary>
public sealed class OrganizationCompanyTaxIdStatusReaderTests
{
    private static readonly Guid CompanyWithVkn = Guid.Parse("0f300000-0000-4000-8000-00000000000a");
    private static readonly Guid PlaceholderCompany = Guid.Parse("0f300000-0000-4000-8000-00000000000b");
    private const string SyntheticVkn = "1234567890";
    private const string SyntheticApprovedVkn = "4100046586";

    [Fact]
    public async Task A_company_with_a_vkn_is_entered_and_carries_its_vkn()
    {
        var database = await SeedAsync();

        var state = await Reader(database, Resolved(CompanyWithVkn)).ReadAsync(CompanyWithVkn);

        Assert.Equal(
            new OrganizationCompanyTaxIdStateV1(CompanyWithVkn, OrganizationCompanyTaxIdStatusV1.Entered, SyntheticVkn),
            state);
        Assert.True(state!.IsTaxIdEntered);
    }

    [Fact]
    public async Task The_placeholder_company_without_a_vkn_is_marked_not_entered_and_carries_no_vkn()
    {
        var database = await SeedAsync();

        var state = await Reader(database, Resolved(PlaceholderCompany)).ReadAsync(PlaceholderCompany);

        Assert.Equal(
            new OrganizationCompanyTaxIdStateV1(PlaceholderCompany, OrganizationCompanyTaxIdStatusV1.NotEntered, null),
            state);
        Assert.False(state!.IsTaxIdEntered);
    }

    [Fact]
    public async Task An_approved_vkn_lifts_the_not_entered_mark()
    {
        var database = await SeedAsync();
        await using (var context = Context(database, Resolved(PlaceholderCompany)))
        {
            var company = await context.Companies.SingleAsync(candidate => candidate.Id == PlaceholderCompany);
            // The maker-checker flow (KIM-D28) is the only caller of this in production; here it stands for an approval.
            company.ApplyApprovedVkn(SyntheticApprovedVkn, DateTimeOffset.UnixEpoch);
            await context.SaveChangesAsync();
        }

        var state = await Reader(database, Resolved(PlaceholderCompany)).ReadAsync(PlaceholderCompany);

        Assert.Equal(
            new OrganizationCompanyTaxIdStateV1(
                PlaceholderCompany,
                OrganizationCompanyTaxIdStatusV1.Entered,
                SyntheticApprovedVkn),
            state);
    }

    [Fact]
    public async Task A_pending_clear_hides_its_current_old_value_until_clear_and_first_vkn_approval_complete()
    {
        const string oldValue = "1111111111";
        var database = await SeedAsync(oldValue);
        var correctionId = await StageAsync(database, PlaceholderCompany, oldValue, null);
        Assert.Equal(new(PlaceholderCompany, OrganizationCompanyTaxIdStatusV1.NotEntered, null),
            await Reader(database, Resolved(PlaceholderCompany)).ReadAsync(PlaceholderCompany));

        await using (var context = Context(database, Resolved(PlaceholderCompany)))
        {
            var company = await context.Companies.SingleAsync(candidate => candidate.Id == PlaceholderCompany);
            var correction = await context.CompanyTaxIdCorrections.SingleAsync(candidate => candidate.Id == correctionId);
            correction.Apply(company, "SENTETIK-clear-approver", DateTimeOffset.UnixEpoch.AddHours(1));
            await context.SaveChangesAsync();
        }
        Assert.Equal(new(PlaceholderCompany, OrganizationCompanyTaxIdStatusV1.NotEntered, null),
            await Reader(database, Resolved(PlaceholderCompany)).ReadAsync(PlaceholderCompany));

        await ApproveFirstVknAsync(database);
        Assert.Equal(new(PlaceholderCompany, OrganizationCompanyTaxIdStatusV1.Entered, SyntheticApprovedVkn),
            await Reader(database, Resolved(PlaceholderCompany)).ReadAsync(PlaceholderCompany));
    }

    [Fact]
    public async Task A_stale_pending_clear_does_not_hide_a_subsequently_approved_vkn_or_another_company()
    {
        const string oldValue = "1111111111";
        var database = await SeedAsync(oldValue);
        var correctionId = await StageAsync(database, PlaceholderCompany, oldValue, null);
        await ApproveFirstVknAsync(database);
        await using (var context = Context(database, Resolved(PlaceholderCompany)))
        {
            var company = await context.Companies.SingleAsync(candidate => candidate.Id == PlaceholderCompany);
            var pending = await context.CompanyTaxIdCorrections.SingleAsync(candidate => candidate.Id == correctionId);
            Assert.True(pending.IsPending);
            Assert.False(pending.IsCurrentFor(company));
            // The flags did not decide the read: the first approved VKN is still entered on this existing company.
            Assert.True(company.IsPlaceholder);
            Assert.False(company.InvoiceCapable);
        }
        Assert.Equal(new(PlaceholderCompany, OrganizationCompanyTaxIdStatusV1.Entered, SyntheticApprovedVkn),
            await Reader(database, Resolved(PlaceholderCompany)).ReadAsync(PlaceholderCompany));
        Assert.Equal(new(CompanyWithVkn, OrganizationCompanyTaxIdStatusV1.Entered, SyntheticVkn),
            await Reader(database, Resolved(CompanyWithVkn)).ReadAsync(CompanyWithVkn));
    }

    [Fact]
    public async Task A_pending_real_replacement_keeps_the_current_normal_company_vkn_entered()
    {
        var database = await SeedAsync();
        await StageAsync(database, CompanyWithVkn, SyntheticVkn, "9000000033");
        Assert.Equal(new(CompanyWithVkn, OrganizationCompanyTaxIdStatusV1.Entered, SyntheticVkn),
            await Reader(database, Resolved(CompanyWithVkn)).ReadAsync(CompanyWithVkn));
    }

    [Fact]
    public async Task An_unknown_company_is_not_found()
    {
        var database = await SeedAsync();
        var unknown = Guid.Parse("0f300000-0000-4000-8000-0000000000ff");

        Assert.Null(await Reader(database, Resolved(unknown)).ReadAsync(unknown));
    }

    [Fact]
    public async Task A_company_that_is_not_the_resolved_context_is_refused_not_answered()
    {
        var database = await SeedAsync();

        // The context's company decides; a different company argument cannot switch it.
        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
            () => Reader(database, Resolved(CompanyWithVkn)).ReadAsync(PlaceholderCompany));
        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
            () => Reader(database, new CompanyContext()).ReadAsync(CompanyWithVkn));
        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
            () => Reader(database, Resolved(CompanyWithVkn)).ReadAsync(Guid.Empty));
    }

    [Fact]
    public void The_read_depends_on_no_user_or_permission_service()
    {
        // A module read: the calling module authorized its own command; no Organization user grant decides it.
        var parameters = typeof(OrganizationCompanyTaxIdStatusReader).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType);
        Assert.Equal([typeof(OrganizationDbContext), typeof(ICompanyContext)], parameters);
    }

    [Fact]
    public void The_refusal_code_and_the_statuses_are_stable_contract_values()
    {
        Assert.Equal("organization_company_tax_id_not_entered", OrganizationCompanyTaxIdProblemCodesV1.TaxIdNotEntered);
        Assert.Equal(1, (int)OrganizationCompanyTaxIdStatusV1.Entered);
        Assert.Equal(2, (int)OrganizationCompanyTaxIdStatusV1.NotEntered);
    }

    private static OrganizationCompanyTaxIdStatusReader Reader(string database, ICompanyContext context) =>
        new(Context(database, context), context);

    private static async Task<string> SeedAsync(string? placeholderVkn = null)
    {
        var database = $"organization-company-tax-id-status-{Guid.NewGuid():N}";
        // Company rows are created only by the bootstrap seed; the test opens the same narrow scope.
        var bootstrap = new OrganizationBootstrapWriteScope();
        using var lease = bootstrap.BeginBootstrapWrite();
        await using var context = new OrganizationDbContext(
            new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase(database).Options,
            new CompanyContext(),
            bootstrap);
        context.Companies.Add(new Company(
            CompanyWithVkn,
            "SYN-A",
            "Sentetik Şirket A",
            SyntheticVkn,
            isPlaceholder: false,
            invoiceCapable: true));
        context.Companies.Add(new Company(
            PlaceholderCompany,
            "SYN-P",
            "Sentetik Yer Tutucu Şirket",
            vkn: placeholderVkn,
            isPlaceholder: true,
            invoiceCapable: false));
        await context.SaveChangesAsync();
        return database;
    }

    private static async Task<Guid> StageAsync(string database, Guid companyId, string stagedVkn, string? correctedVkn)
    {
        await using var context = Context(database, Resolved(companyId));
        var company = await context.Companies.SingleAsync(candidate => candidate.Id == companyId);
        var correction = new CompanyTaxIdCorrection(Guid.NewGuid(), companyId, "SENTETIK-staging-source",
            stagedVkn, correctedVkn, company.Version, "SENTETIK staged correction", DateTimeOffset.UnixEpoch);
        context.CompanyTaxIdCorrections.Add(correction);
        await context.SaveChangesAsync();
        return correction.Id;
    }

    private static async Task ApproveFirstVknAsync(string database)
    {
        await using var context = Context(database, Resolved(PlaceholderCompany));
        var company = await context.Companies.SingleAsync(candidate => candidate.Id == PlaceholderCompany);
        var request = new CompanyTaxIdChangeRequest(Guid.NewGuid(), company.Id, company.Vkn, SyntheticApprovedVkn,
            company.Version, "SENTETIK first real VKN", "SENTETIK-maker", DateTimeOffset.UnixEpoch.AddHours(2));
        request.Approve(company, "SENTETIK-checker", null, DateTimeOffset.UnixEpoch.AddHours(3));
        context.CompanyTaxIdChangeRequests.Add(request);
        await context.SaveChangesAsync();
    }

    private static CompanyContext Resolved(Guid companyId)
    {
        var context = new CompanyContext();
        context.Set(companyId);
        return context;
    }

    private static OrganizationDbContext Context(string database, ICompanyContext companyContext) => new(
        new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase(database).Options,
        companyContext,
        new OrganizationBootstrapWriteScope());
}
