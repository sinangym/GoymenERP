using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Subcontracts;
using GoymenERP.Modules.Purchasing.Domain.Subcontracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchasingSubcontractAdmissionV1Tests
{
    private static readonly Guid Company = Guid.Parse("91000000-0000-0000-0000-000000000001");
    private static readonly Guid Order = Guid.Parse("91000000-0000-0000-0000-000000000002");
    private static readonly Guid Line = Guid.Parse("91000000-0000-0000-0000-000000000003");
    private static readonly Guid Service = Guid.Parse("91000000-0000-0000-0000-000000000004");
    private static readonly Guid Unit = Guid.Parse("91000000-0000-0000-0000-000000000005");
    private static readonly Guid Supplier = Guid.Parse("91000000-0000-0000-0000-000000000006");
    private static readonly Guid Account = Guid.Parse("91000000-0000-0000-0000-000000000007");
    private static readonly Guid PhysicalRequest = Guid.Parse("91000000-0000-0000-0000-000000000008");
    private static readonly Guid Location = Guid.Parse("91000000-0000-0000-0000-000000000009");
    private static readonly Guid Operator = Guid.Parse("91000000-0000-0000-0000-000000000010");

    [Fact]
    public async Task Admit_consumes_authorized_service_and_preserves_physical_identity_without_stock_effect()
    {
        var subjectSource = new SubjectSource(FoundSubject());
        var sut = new PurchasingSubcontractAdmissionV1(subjectSource, new PartySource(FoundParty()));

        var admitted = await sut.AdmitAsync(ValidOrder(), Line, ValidPhysicalContext());

        Assert.Equal(Order, admitted.OrderId);
        Assert.Equal("order-v4", admitted.ExpectedOrderVersion);
        Assert.Equal(Service, admitted.Service.Id);
        Assert.Equal("service-v7", admitted.Service.Version);
        Assert.Equal(PhysicalRequest, admitted.PhysicalContext.SourcePhysicalRequestId);
        Assert.Equal(Operator, admitted.PhysicalContext.CustodianPartyId);
        Assert.Equal(Company, subjectSource.CompanyId);
        Assert.Equal(PurchasingCommercialLineSubjectKindV1.Service, subjectSource.Reference!.Kind);
        Assert.Equal(Service, subjectSource.Reference.Id);
        Assert.Equal("service-v7", subjectSource.Reference.ExpectedVersion);
        Assert.DoesNotContain(admitted.GetType().GetProperties(), property =>
            property.Name.Contains("Stock", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(PurchasingSubcontractSubjectOutcomeV1.Unavailable, PurchasingSubcontractAdmissionProblemV1.SubjectUnavailable)]
    [InlineData(PurchasingSubcontractSubjectOutcomeV1.Denied, PurchasingSubcontractAdmissionProblemV1.SubjectDenied)]
    [InlineData(PurchasingSubcontractSubjectOutcomeV1.NotFound, PurchasingSubcontractAdmissionProblemV1.SubjectNotFound)]
    public async Task Admit_fails_closed_when_subject_owner_does_not_publish_a_subject(
        PurchasingSubcontractSubjectOutcomeV1 outcome,
        PurchasingSubcontractAdmissionProblemV1 expected)
    {
        var sut = new PurchasingSubcontractAdmissionV1(
            new SubjectSource(new PurchasingSubcontractSubjectAnswerV1(outcome, null)),
            new PartySource(FoundParty()));

        var exception = await Assert.ThrowsAsync<PurchasingSubcontractAdmissionExceptionV1>(() =>
            sut.AdmitAsync(ValidOrder(), Line, ValidPhysicalContext()));

        Assert.Equal(expected, exception.Problem);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("identity")]
    [InlineData("version")]
    public async Task Admit_rejects_non_service_foreign_or_stale_subject(string mutation)
    {
        var kind = mutation == "kind"
            ? PurchasingCommercialLineSubjectKindV1.Product
            : PurchasingCommercialLineSubjectKindV1.Service;
        var id = mutation == "identity" ? Guid.NewGuid() : Service;
        var version = mutation == "version" ? "service-v6" : "service-v7";
        var answer = new PurchasingSubcontractSubjectAnswerV1(
            PurchasingSubcontractSubjectOutcomeV1.Found,
            new PurchasingCommercialLineSubjectSnapshotV1(kind, id, version, "SRV-01", "Fason hizmet"));
        var sut = new PurchasingSubcontractAdmissionV1(new SubjectSource(answer), new PartySource(FoundParty()));

        var exception = await Assert.ThrowsAsync<PurchasingSubcontractAdmissionExceptionV1>(() =>
            sut.AdmitAsync(ValidOrder(), Line, ValidPhysicalContext()));

        Assert.Equal(
            mutation == "version"
                ? PurchasingSubcontractAdmissionProblemV1.SubjectVersionConflict
                : PurchasingSubcontractAdmissionProblemV1.SubjectInvalid,
            exception.Problem);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("name")]
    public async Task Admit_rejects_a_found_subject_with_blank_presentation(string mutation)
    {
        var answer = new PurchasingSubcontractSubjectAnswerV1(
            PurchasingSubcontractSubjectOutcomeV1.Found,
            new PurchasingCommercialLineSubjectSnapshotV1(
                PurchasingCommercialLineSubjectKindV1.Service,
                Service,
                "service-v7",
                mutation == "code" ? " " : "SRV-01",
                mutation == "name" ? " " : "Fason hizmet"));
        var sut = new PurchasingSubcontractAdmissionV1(new SubjectSource(answer), new PartySource(FoundParty()));

        var exception = await Assert.ThrowsAsync<PurchasingSubcontractAdmissionExceptionV1>(() =>
            sut.AdmitAsync(ValidOrder(), Line, ValidPhysicalContext()));

        Assert.Equal(PurchasingSubcontractAdmissionProblemV1.SubjectInvalid, exception.Problem);
    }

    [Theory]
    [InlineData(PurchasingPartyAccountOutcomeV1.Unavailable, PurchasingSubcontractAdmissionProblemV1.PartyUnavailable)]
    [InlineData(PurchasingPartyAccountOutcomeV1.Denied, PurchasingSubcontractAdmissionProblemV1.PartyDenied)]
    [InlineData(PurchasingPartyAccountOutcomeV1.NotFound, PurchasingSubcontractAdmissionProblemV1.PartyNotFound)]
    public async Task Admit_fails_closed_when_party_owner_does_not_publish_an_account(
        PurchasingPartyAccountOutcomeV1 outcome,
        PurchasingSubcontractAdmissionProblemV1 expected)
    {
        var sut = new PurchasingSubcontractAdmissionV1(
            new SubjectSource(FoundSubject()),
            new PartySource(new PurchasingPartyAccountAnswerV1(outcome, null)));

        var exception = await Assert.ThrowsAsync<PurchasingSubcontractAdmissionExceptionV1>(() =>
            sut.AdmitAsync(ValidOrder(), Line, ValidPhysicalContext()));

        Assert.Equal(expected, exception.Problem);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("party")]
    [InlineData("account")]
    [InlineData("inactive-party")]
    [InlineData("inactive-account")]
    [InlineData("not-supplier")]
    [InlineData("party-version")]
    [InlineData("account-version")]
    public async Task Admit_rejects_foreign_inactive_non_supplier_or_stale_party_account(string mutation)
    {
        var value = FoundParty().Account!;
        value = value with
        {
            CompanyId = mutation == "company" ? Guid.NewGuid() : value.CompanyId,
            PartyId = mutation == "party" ? Guid.NewGuid() : value.PartyId,
            CompanyAccountId = mutation == "account" ? Guid.NewGuid() : value.CompanyAccountId,
            PartyIsActive = mutation != "inactive-party",
            AccountIsActive = mutation != "inactive-account",
            AccountHasSupplierRole = mutation != "not-supplier",
            PartyVersion = mutation == "party-version" ? "party-v2" : value.PartyVersion,
            AccountVersion = mutation == "account-version" ? "account-v3" : value.AccountVersion,
        };
        var sut = new PurchasingSubcontractAdmissionV1(
            new SubjectSource(FoundSubject()),
            new PartySource(new PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1.Found, value)));

        var exception = await Assert.ThrowsAsync<PurchasingSubcontractAdmissionExceptionV1>(() =>
            sut.AdmitAsync(ValidOrder(), Line, ValidPhysicalContext()));

        Assert.Equal(
            mutation.EndsWith("version", StringComparison.Ordinal)
                ? PurchasingSubcontractAdmissionProblemV1.PartyVersionConflict
                : PurchasingSubcontractAdmissionProblemV1.PartyInvalid,
            exception.Problem);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("request")]
    [InlineData("version")]
    public async Task Admit_rejects_foreign_or_stale_physical_context_before_owner_reads(string mutation)
    {
        var contextCompany = mutation == "company" ? Guid.NewGuid() : Company;
        var physical = SubcontractPhysicalContextV1.Create(
            contextCompany,
            SubcontractMaterialOwnerKindV1.Company,
            contextCompany,
            Location,
            "location-v5",
            Operator,
            "operator-v3",
            mutation == "request" ? Guid.NewGuid() : PhysicalRequest,
            mutation == "version" ? "physical-v3" : "physical-v2");
        var subjectSource = new SubjectSource(FoundSubject());
        var partySource = new PartySource(FoundParty());
        var sut = new PurchasingSubcontractAdmissionV1(subjectSource, partySource);

        var exception = await Assert.ThrowsAsync<PurchasingSubcontractAdmissionExceptionV1>(() =>
            sut.AdmitAsync(ValidOrder(), Line, physical));

        Assert.Equal(PurchasingSubcontractAdmissionProblemV1.PhysicalContextConflict, exception.Problem);
        Assert.Equal(0, subjectSource.CallCount);
        Assert.Equal(0, partySource.CallCount);
    }

    [Fact]
    public async Task Admit_rejects_a_line_that_is_not_part_of_the_order_before_owner_reads()
    {
        var subjectSource = new SubjectSource(FoundSubject());
        var partySource = new PartySource(FoundParty());
        var sut = new PurchasingSubcontractAdmissionV1(subjectSource, partySource);

        var exception = await Assert.ThrowsAsync<PurchasingSubcontractAdmissionExceptionV1>(() =>
            sut.AdmitAsync(ValidOrder(), Guid.NewGuid(), ValidPhysicalContext()));

        Assert.Equal(PurchasingSubcontractAdmissionProblemV1.InvalidRequest, exception.Problem);
        Assert.Equal(0, subjectSource.CallCount);
        Assert.Equal(0, partySource.CallCount);
    }

    private static PurchasingSubcontractOrderV1 ValidOrder() => new(
        Order,
        Company,
        Supplier,
        Account,
        "order-v4",
        "party-v1",
        "account-v2",
        [new PurchasingSubcontractOrderLineV1(
            Line,
            Service,
            "service-v7",
            Unit,
            "12",
            PhysicalRequest,
            "physical-v2")]);

    private static SubcontractPhysicalContextV1 ValidPhysicalContext() => SubcontractPhysicalContextV1.Create(
        Company,
        SubcontractMaterialOwnerKindV1.Company,
        Company,
        Location,
        "location-v5",
        Operator,
        "operator-v3",
        PhysicalRequest,
        "physical-v2");

    private static PurchasingSubcontractSubjectAnswerV1 FoundSubject() => new(
        PurchasingSubcontractSubjectOutcomeV1.Found,
        new PurchasingCommercialLineSubjectSnapshotV1(
            PurchasingCommercialLineSubjectKindV1.Service,
            Service,
            "service-v7",
            "SRV-01",
            "Fason hizmet"));

    private static PurchasingPartyAccountAnswerV1 FoundParty() => new(
        PurchasingPartyAccountOutcomeV1.Found,
        new PurchasingPartyAccountV1(
            Supplier,
            Account,
            Company,
            true,
            true,
            true,
            "party-v1",
            "account-v2"));

    private sealed class SubjectSource(PurchasingSubcontractSubjectAnswerV1 answer)
        : IPurchasingSubcontractSubjectSourceV1
    {
        public int CallCount { get; private set; }
        public Guid CompanyId { get; private set; }
        public PurchasingCommercialLineSubjectReferenceV1? Reference { get; private set; }

        public Task<PurchasingSubcontractSubjectAnswerV1> GetAuthorizedAsync(
            Guid companyId,
            PurchasingCommercialLineSubjectReferenceV1 subject,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            CompanyId = companyId;
            Reference = subject;
            return Task.FromResult(answer);
        }
    }

    private sealed class PartySource(PurchasingPartyAccountAnswerV1 answer) : IPurchasingPartyAccountPortV1
    {
        public int CallCount { get; private set; }

        public Task<PurchasingPartyAccountAnswerV1> GetAuthorizedAsync(
            Guid partyId,
            Guid companyAccountId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(answer);
        }
    }
}
