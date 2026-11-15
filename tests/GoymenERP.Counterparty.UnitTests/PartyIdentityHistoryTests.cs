using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Counterparty.UnitTests;

/// <summary>
/// KLN-D38 (Sinan, 2026-11-14): the dated VKN/TCKN, legal name and e-belge status history, the as-of resolution an invoice
/// reads on its document date (rule 4), the correction shape (rule 1) and the succession link (rule 2). Pure domain.
/// </summary>
public sealed class PartyIdentityHistoryTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid PartyId = Guid.NewGuid();
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public void A_document_reads_the_baseline_before_the_first_dated_change_and_the_change_from_its_day()
    {
        var revisions = new[]
        {
            PartyIdentityRevision.Baseline(Guid.NewGuid(), PartyId, PartyIdentityAttribute.LegalName, 3, "Eski Unvan", RecordedAt),
            Dated(PartyIdentityAttribute.LegalName, 4, "Yeni Unvan", new DateOnly(2026, 11, 14)),
        };

        Assert.Equal("Eski Unvan",
            PartyIdentityAsOf.Resolve(revisions, PartyIdentityAttribute.LegalName, new DateOnly(2026, 11, 13))!.Value);
        Assert.Equal("Yeni Unvan",
            PartyIdentityAsOf.Resolve(revisions, PartyIdentityAttribute.LegalName, new DateOnly(2026, 11, 14))!.Value);
        Assert.Equal("Yeni Unvan",
            PartyIdentityAsOf.Resolve(revisions, PartyIdentityAttribute.LegalName, new DateOnly(2027, 1, 1))!.Value);
    }

    [Fact]
    public void Two_changes_on_the_same_day_resolve_to_the_newest_sequence_whatever_the_order()
    {
        var day = new DateOnly(2026, 11, 14);
        var newer = Dated(PartyIdentityAttribute.LegalName, 6, "İkinci", day);
        var older = Dated(PartyIdentityAttribute.LegalName, 5, "Birinci", day);

        Assert.Same(newer, PartyIdentityAsOf.Resolve([newer, older], PartyIdentityAttribute.LegalName, day));
        Assert.Same(newer, PartyIdentityAsOf.Resolve([older, newer], PartyIdentityAttribute.LegalName, day));
    }

    [Fact]
    public void A_date_before_every_dated_revision_with_no_baseline_has_no_value_and_other_attributes_are_ignored()
    {
        var revisions = new[]
        {
            Dated(PartyIdentityAttribute.LegalName, 2, "Unvan", new DateOnly(2026, 11, 14)),
            PartyIdentityRevision.Baseline(
                Guid.NewGuid(), PartyId, PartyIdentityAttribute.TaxIdentityNumber, 1, "1234567890", RecordedAt),
        };

        Assert.Null(PartyIdentityAsOf.Resolve(revisions, PartyIdentityAttribute.LegalName, new DateOnly(2026, 11, 13)));
        Assert.Equal("1234567890",
            PartyIdentityAsOf.Resolve(revisions, PartyIdentityAttribute.TaxIdentityNumber, new DateOnly(2000, 1, 1))!.Value);
    }

    [Fact]
    public void A_correction_is_dated_and_carries_the_approved_request_and_its_reason()
    {
        var requestId = Guid.NewGuid();
        var correction = new PartyIdentityRevision(
            Guid.NewGuid(), PartyId, PartyIdentityAttribute.TaxIdentityNumber, 7, "1234567890", new DateOnly(2026, 11, 14),
            PartyIdentityRevisionKind.Correction, "  Yanlış girilmiş  ", requestId, "checker", CompanyId, RecordedAt);

        Assert.Equal(requestId, correction.SourceChangeRequestId);
        Assert.Equal("Yanlış girilmiş", correction.Reason);
        Assert.Equal(new DateOnly(2026, 11, 14), correction.EffectiveFrom);
    }

    [Fact]
    public void A_correction_without_a_request_or_reason_or_of_the_legal_name_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new PartyIdentityRevision(
            Guid.NewGuid(), PartyId, PartyIdentityAttribute.TaxIdentityNumber, 2, "1234567890", new DateOnly(2026, 11, 14),
            PartyIdentityRevisionKind.Correction, "Neden", null, "checker", CompanyId, RecordedAt));
        Assert.Throws<ArgumentException>(() => new PartyIdentityRevision(
            Guid.NewGuid(), PartyId, PartyIdentityAttribute.TaxIdentityNumber, 2, "1234567890", new DateOnly(2026, 11, 14),
            PartyIdentityRevisionKind.Correction, "   ", Guid.NewGuid(), "checker", CompanyId, RecordedAt));
        Assert.Throws<ArgumentException>(() => new PartyIdentityRevision(
            Guid.NewGuid(), PartyId, PartyIdentityAttribute.LegalName, 2, "Unvan", new DateOnly(2026, 11, 14),
            PartyIdentityRevisionKind.Correction, "Neden", Guid.NewGuid(), "checker", CompanyId, RecordedAt));
        Assert.Throws<ArgumentException>(() => new PartyIdentityRevision(
            Guid.NewGuid(), PartyId, PartyIdentityAttribute.LegalName, 2, "Unvan", new DateOnly(2026, 11, 14),
            PartyIdentityRevisionKind.DatedChange, null, Guid.NewGuid(), "maker", CompanyId, RecordedAt));
    }

    [Fact]
    public void Baseline_and_initial_entry_are_undated_while_a_dated_change_needs_a_date_and_a_recorder()
    {
        Assert.Throws<ArgumentException>(() => new PartyIdentityRevision(
            Guid.NewGuid(), PartyId, PartyIdentityAttribute.TaxIdentityNumber, 1, "1234567890", new DateOnly(2026, 11, 14),
            PartyIdentityRevisionKind.InitialEntry, null, null, "maker", CompanyId, RecordedAt));
        Assert.Throws<ArgumentException>(() => new PartyIdentityRevision(
            Guid.NewGuid(), PartyId, PartyIdentityAttribute.LegalName, 2, "Unvan", null,
            PartyIdentityRevisionKind.DatedChange, null, null, "maker", CompanyId, RecordedAt));
        Assert.Throws<ArgumentException>(() => new PartyIdentityRevision(
            Guid.NewGuid(), PartyId, PartyIdentityAttribute.LegalName, 2, "Unvan", new DateOnly(2026, 11, 14),
            PartyIdentityRevisionKind.DatedChange, null, null, null, CompanyId, RecordedAt));
        Assert.Throws<ArgumentException>(() => new PartyIdentityRevision(
            Guid.NewGuid(), PartyId, PartyIdentityAttribute.LegalName, 2, "Unvan", new DateOnly(2026, 11, 14),
            PartyIdentityRevisionKind.DatedChange, null, null, "maker", null, RecordedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PartyIdentityRevision(
            Guid.NewGuid(), PartyId, PartyIdentityAttribute.LegalName, 0, "Unvan", new DateOnly(2026, 11, 14),
            PartyIdentityRevisionKind.DatedChange, null, null, "maker", CompanyId, RecordedAt));

        var baseline = PartyIdentityRevision.Baseline(
            Guid.NewGuid(), PartyId, PartyIdentityAttribute.LegalName, 1, "Unvan", RecordedAt);
        Assert.Null(baseline.EffectiveFrom);
        Assert.Null(baseline.RecordedByUserId);
        Assert.Null(baseline.RecordedInCompanyId);
    }

    [Fact]
    public void The_e_document_status_on_a_date_is_the_latest_check_on_or_before_it_and_Unknown_before_every_check()
    {
        var profileId = Guid.NewGuid();
        var revisions = new[]
        {
            Status(profileId, 2, EDocumentStatus.EArchive, new DateOnly(2026, 11, 1)),
            Status(profileId, 3, EDocumentStatus.EInvoiceTaxpayer, new DateOnly(2026, 11, 10)),
            Status(profileId, 4, EDocumentStatus.EArchive, new DateOnly(2026, 11, 10)),
        };

        Assert.Equal(EDocumentStatus.Unknown,
            CustomerEDocumentStatusRevision.StatusAsOf(revisions, new DateOnly(2026, 10, 31)));
        Assert.Equal(EDocumentStatus.EArchive,
            CustomerEDocumentStatusRevision.StatusAsOf(revisions, new DateOnly(2026, 11, 9)));
        // Same day: the newest sequence wins.
        Assert.Equal(EDocumentStatus.EArchive,
            CustomerEDocumentStatusRevision.StatusAsOf(revisions, new DateOnly(2026, 11, 10)));
        Assert.Equal(EDocumentStatus.Unknown, CustomerEDocumentStatusRevision.StatusAsOf([], new DateOnly(2026, 11, 10)));
    }

    [Fact]
    public void A_recorded_e_document_status_needs_its_checker_and_company_while_a_baseline_does_not()
    {
        Assert.Throws<ArgumentException>(() => new CustomerEDocumentStatusRevision(
            Guid.NewGuid(), Guid.NewGuid(), 1, EDocumentStatus.EArchive, new DateOnly(2026, 11, 1),
            CustomerEDocumentStatusRevisionKind.Recorded, " ", CompanyId, RecordedAt));
        Assert.Throws<ArgumentException>(() => new CustomerEDocumentStatusRevision(
            Guid.NewGuid(), Guid.NewGuid(), 1, EDocumentStatus.EArchive, new DateOnly(2026, 11, 1),
            CustomerEDocumentStatusRevisionKind.Recorded, "checker", null, RecordedAt));

        var baseline = new CustomerEDocumentStatusRevision(
            Guid.NewGuid(), Guid.NewGuid(), 1, EDocumentStatus.EArchive, new DateOnly(2026, 11, 1),
            CustomerEDocumentStatusRevisionKind.Baseline, null, null, RecordedAt);
        Assert.Null(baseline.RecordedByUserId);
    }

    [Fact]
    public void A_succession_needs_two_different_parties_a_known_kind_and_a_trimmed_bounded_legal_basis()
    {
        var predecessor = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var link = new PartySuccession(
            Guid.NewGuid(), CompanyId, predecessor, successor, PartySuccessionKind.Conversion, "  Sicil ilanı  ",
            new DateOnly(2026, 11, 14), "maker", RecordedAt);
        Assert.Equal("Sicil ilanı", link.LegalBasis);
        Assert.Equal(predecessor, link.PredecessorPartyId);
        Assert.Equal(successor, link.SuccessorPartyId);

        Assert.Throws<ArgumentException>(() => new PartySuccession(
            Guid.NewGuid(), CompanyId, predecessor, predecessor, PartySuccessionKind.Merger, "Sicil ilanı",
            new DateOnly(2026, 11, 14), "maker", RecordedAt));
        Assert.Throws<ArgumentException>(() => new PartySuccession(
            Guid.NewGuid(), Guid.Empty, predecessor, successor, PartySuccessionKind.Merger, "Sicil ilanı",
            new DateOnly(2026, 11, 14), "maker", RecordedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PartySuccession(
            Guid.NewGuid(), CompanyId, predecessor, successor, (PartySuccessionKind)99, "Sicil ilanı",
            new DateOnly(2026, 11, 14), "maker", RecordedAt));
        Assert.Throws<ArgumentException>(() => PartySuccession.NormalizeLegalBasis("   "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PartySuccession.NormalizeLegalBasis(new string('a', PartySuccession.LegalBasisMaximumLength + 1)));
        Assert.Equal(PartySuccession.LegalBasisMaximumLength,
            PartySuccession.NormalizeLegalBasis(new string('a', PartySuccession.LegalBasisMaximumLength)).Length);
    }

    [Fact]
    public void The_business_day_is_the_Istanbul_calendar_date_of_the_instant()
    {
        // 21:30 UTC is already the next day in Europe/Istanbul (UTC+3).
        Assert.Equal(new DateOnly(2026, 11, 15),
            CounterpartyBusinessDay.DateOf(new DateTimeOffset(2026, 11, 14, 21, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 11, 14),
            CounterpartyBusinessDay.DateOf(new DateTimeOffset(2026, 11, 14, 20, 59, 0, TimeSpan.Zero)));
    }

    private static PartyIdentityRevision Dated(PartyIdentityAttribute attribute, long sequence, string value, DateOnly from) =>
        new(Guid.NewGuid(), PartyId, attribute, sequence, value, from, PartyIdentityRevisionKind.DatedChange, null, null,
            "maker", CompanyId, RecordedAt);

    private static CustomerEDocumentStatusRevision Status(Guid profileId, long sequence, EDocumentStatus status, DateOnly from) =>
        new(Guid.NewGuid(), profileId, sequence, status, from, CustomerEDocumentStatusRevisionKind.Recorded, "checker",
            CompanyId, RecordedAt);
}
