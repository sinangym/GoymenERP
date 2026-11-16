using GoymenERP.Modules.Organization.Domain.Companies;

namespace GoymenERP.Organization.UnitTests;

/// <summary>
/// KIM-S23 (KIM-D28, DEC-ORG-005): the company VKN rules and the maker-checker change request in the domain. Every
/// VKN, actor and id here is synthetic.
/// </summary>
public sealed class CompanyTaxIdDomainTests
{
    private const string CurrentVkn = "9000000017";
    private const string ProposedVkn = "9000000025";
    private const string Maker = "SENTETIK-tax-maker";
    private const string Checker = "SENTETIK-tax-checker";
    private static readonly DateTimeOffset RequestedAt = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_real_company_cannot_be_created_without_a_vkn()
    {
        Assert.Throws<ArgumentException>(() => new Company(
            Guid.NewGuid(), "SYN-REAL", "Sentetik gerçek şirket", null, isPlaceholder: false, invoiceCapable: false));
        Assert.Throws<ArgumentException>(() => new Company(
            Guid.NewGuid(), "SYN-REAL", "Sentetik gerçek şirket", "   ", isPlaceholder: false, invoiceCapable: false));
    }

    [Theory]
    [InlineData("9000000001", true)]
    [InlineData(" 9000000001", false)]
    [InlineData("900000000", false)]
    [InlineData("90000000011", false)]
    [InlineData("90000O0001", false)]
    [InlineData("９０００００００01", false)]
    [InlineData(null, false)]
    public void Only_ten_ascii_digits_are_the_canonical_vkn(string? value, bool expected) =>
        Assert.Equal(expected, Company.IsCanonicalVkn(value));

    [Fact]
    public void Opening_a_request_does_not_move_the_company_vkn()
    {
        var company = RealCompany();

        var request = Request(company);

        Assert.Equal(CurrentVkn, company.Vkn);
        Assert.Equal(1, company.Version);
        Assert.True(request.IsPending);
        Assert.Equal(CompanyTaxIdChangeStatus.Pending, request.Status);
        Assert.Equal(CurrentVkn, request.CurrentVkn);
        Assert.Equal(ProposedVkn, request.ProposedVkn);
        Assert.Equal(1, request.CompanyVersionAtRequest);
        Assert.Equal(1, request.Version);
        Assert.Null(request.DecidedBy);
        Assert.Null(request.DecidedAtUtc);
        Assert.Null(request.DecisionNote);
        Assert.True(request.IsCurrentFor(company));
    }

    [Theory]
    [InlineData("900000000")]
    [InlineData("90000O0001")]
    [InlineData(" 9000000002 ")]
    public void A_request_refuses_a_non_canonical_proposed_vkn(string proposed)
    {
        var company = RealCompany();

        Assert.Throws<ArgumentException>(() => new CompanyTaxIdChangeRequest(
            Guid.NewGuid(), company.Id, company.Vkn, proposed, company.Version, "Sentetik gerekçe", Maker, RequestedAt));
    }

    [Theory]
    [InlineData("9000000026")]
    [InlineData("1111111111")]
    public void A_request_refuses_a_proposed_vkn_failing_the_check_digit(string proposed)
    {
        var company = RealCompany();

        Assert.Throws<ArgumentException>(() => new CompanyTaxIdChangeRequest(
            Guid.NewGuid(), company.Id, company.Vkn, proposed, company.Version, "Sentetik gerekçe", Maker, RequestedAt));
    }

    [Fact]
    public void A_request_refuses_the_current_vkn_an_empty_id_a_zero_version_and_blank_text()
    {
        var company = RealCompany();

        Assert.Throws<ArgumentException>(() => new CompanyTaxIdChangeRequest(
            Guid.NewGuid(), company.Id, CurrentVkn, CurrentVkn, 1, "Sentetik gerekçe", Maker, RequestedAt));
        Assert.Throws<ArgumentException>(() => new CompanyTaxIdChangeRequest(
            Guid.Empty, company.Id, CurrentVkn, ProposedVkn, 1, "Sentetik gerekçe", Maker, RequestedAt));
        Assert.Throws<ArgumentException>(() => new CompanyTaxIdChangeRequest(
            Guid.NewGuid(), Guid.Empty, CurrentVkn, ProposedVkn, 1, "Sentetik gerekçe", Maker, RequestedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompanyTaxIdChangeRequest(
            Guid.NewGuid(), company.Id, CurrentVkn, ProposedVkn, 0, "Sentetik gerekçe", Maker, RequestedAt));
        Assert.Throws<ArgumentException>(() => new CompanyTaxIdChangeRequest(
            Guid.NewGuid(), company.Id, CurrentVkn, ProposedVkn, 1, "  ", Maker, RequestedAt));
        Assert.Throws<ArgumentException>(() => new CompanyTaxIdChangeRequest(
            Guid.NewGuid(), company.Id, CurrentVkn, ProposedVkn, 1, "Sentetik gerekçe", " ", RequestedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompanyTaxIdChangeRequest(
            Guid.NewGuid(),
            company.Id,
            CurrentVkn,
            ProposedVkn,
            1,
            new string('g', CompanyTaxIdChangeRequest.ReasonMaximumLength + 1),
            Maker,
            RequestedAt));
    }

    [Fact]
    public void The_preparer_can_neither_approve_nor_reject_their_own_request()
    {
        var company = RealCompany();
        var request = Request(company);

        Assert.Throws<InvalidOperationException>(() => request.Approve(company, Maker, null, DecidedAt));
        Assert.Throws<InvalidOperationException>(() => request.Reject(Maker, null, DecidedAt));
        // Surrounding whitespace does not disguise the preparer.
        Assert.Throws<InvalidOperationException>(() => request.Approve(company, $" {Maker} ", null, DecidedAt));

        Assert.True(request.IsPending);
        Assert.Equal(CurrentVkn, company.Vkn);
        Assert.Equal(1, company.Version);
    }

    [Fact]
    public void Another_person_approves_and_the_proposed_vkn_is_applied_with_the_decision()
    {
        var company = RealCompany();
        var request = Request(company);

        request.Approve(company, Checker, "  Sentetik onay notu  ", DecidedAt);

        Assert.Equal(ProposedVkn, company.Vkn);
        Assert.Equal(2, company.Version);
        Assert.Equal(DecidedAt, company.UpdatedAtUtc);
        Assert.Equal(CompanyTaxIdChangeStatus.Approved, request.Status);
        Assert.False(request.IsPending);
        Assert.Equal(Checker, request.DecidedBy);
        Assert.Equal(DecidedAt, request.DecidedAtUtc);
        Assert.Equal("Sentetik onay notu", request.DecisionNote);
        Assert.Equal(2, request.Version);
    }

    [Fact]
    public void A_rejection_closes_the_request_and_leaves_the_vkn_untouched()
    {
        var company = RealCompany();
        var request = Request(company);

        request.Reject(Checker, "   ", DecidedAt);

        Assert.Equal(CurrentVkn, company.Vkn);
        Assert.Equal(1, company.Version);
        Assert.Equal(CompanyTaxIdChangeStatus.Rejected, request.Status);
        Assert.Equal(Checker, request.DecidedBy);
        Assert.Null(request.DecisionNote);
        Assert.Equal(2, request.Version);
    }

    [Fact]
    public void A_decided_request_cannot_be_decided_again()
    {
        var company = RealCompany();
        var approved = Request(company);
        approved.Approve(company, Checker, null, DecidedAt);

        Assert.Throws<InvalidOperationException>(() => approved.Reject("SENTETIK-tax-third", null, DecidedAt));
        Assert.Throws<InvalidOperationException>(() => approved.Approve(company, "SENTETIK-tax-third", null, DecidedAt));
        Assert.Equal(ProposedVkn, company.Vkn);
        Assert.Equal(2, company.Version);
    }

    [Fact]
    public void An_approval_never_applies_over_a_company_change_it_did_not_see()
    {
        var company = RealCompany();
        var stale = Request(company);
        // The company moved after the request was prepared (another approved change).
        company.ApplyApprovedVkn("9000000033", DecidedAt);

        Assert.False(stale.IsCurrentFor(company));
        Assert.Throws<InvalidOperationException>(() => stale.Approve(company, Checker, null, DecidedAt));
        Assert.Equal("9000000033", company.Vkn);
        Assert.True(stale.IsPending);

        // A stale request can still be rejected.
        stale.Reject(Checker, "Güncelliğini yitirdi", DecidedAt);
        Assert.Equal(CompanyTaxIdChangeStatus.Rejected, stale.Status);
    }

    [Fact]
    public void A_request_is_not_current_for_another_company()
    {
        var company = RealCompany();
        var other = new Company(
            Guid.NewGuid(), "SYN-OTHER", "Sentetik diğer şirket", CurrentVkn, isPlaceholder: false, invoiceCapable: false);
        var request = Request(company);

        Assert.False(request.IsCurrentFor(other));
        Assert.Throws<InvalidOperationException>(() => request.Approve(other, Checker, null, DecidedAt));
        Assert.Equal(CurrentVkn, other.Vkn);
    }

    [Fact]
    public void A_placeholder_company_gets_its_first_vkn_through_the_same_approval()
    {
        var placeholder = new Company(
            Guid.NewGuid(), "SYN-TMP", "Sentetik geçici şirket", null, isPlaceholder: true, invoiceCapable: false);
        var request = new CompanyTaxIdChangeRequest(
            Guid.NewGuid(), placeholder.Id, null, ProposedVkn, placeholder.Version, "İlk VKN", Maker, RequestedAt);

        Assert.Null(request.CurrentVkn);
        request.Approve(placeholder, Checker, null, DecidedAt);

        Assert.Equal(ProposedVkn, placeholder.Vkn);
        Assert.Equal(2, placeholder.Version);
    }

    [Fact]
    public void The_decision_note_has_an_upper_bound()
    {
        var company = RealCompany();
        var request = Request(company);

        Assert.Throws<ArgumentOutOfRangeException>(() => request.Reject(
            Checker,
            new string('n', CompanyTaxIdChangeRequest.NoteMaximumLength + 1),
            DecidedAt));
        Assert.True(request.IsPending);
    }

    [Fact]
    public void The_company_refuses_to_apply_its_current_vkn_or_a_non_canonical_one()
    {
        var company = RealCompany();

        Assert.Throws<InvalidOperationException>(() => company.ApplyApprovedVkn(CurrentVkn, DecidedAt));
        Assert.Throws<ArgumentException>(() => company.ApplyApprovedVkn("90000O0001", DecidedAt));
        Assert.Throws<ArgumentException>(() => company.ApplyApprovedVkn("  ", DecidedAt));
        Assert.Equal(CurrentVkn, company.Vkn);
        Assert.Equal(1, company.Version);
    }

    private static Company RealCompany() =>
        new(Guid.NewGuid(), "SYN-REAL", "Sentetik gerçek şirket", CurrentVkn, isPlaceholder: false, invoiceCapable: false);

    private static CompanyTaxIdChangeRequest Request(Company company) =>
        new(
            Guid.NewGuid(),
            company.Id,
            company.Vkn,
            ProposedVkn,
            company.Version,
            "Sentetik gerekçe",
            Maker,
            RequestedAt);
}
