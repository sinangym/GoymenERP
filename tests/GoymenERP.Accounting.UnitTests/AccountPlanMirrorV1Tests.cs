using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountPlanMirrorV1Tests
{
    private static readonly Guid Company = Guid.Parse("a0000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Approver = Guid.Parse("a0000000-0000-4000-8000-0000000000b1");
    private static readonly DateTimeOffset At = new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Checksum = new('a', 64); // Synthetic contract input, never a real file claim.

    [Fact]
    public void Keeps_exact_approved_provenance_account_id_code_name_and_order()
    {
        var row = Row(1, "SENTETIK-001", "Örnek hesap / €");
        var mirror = Make([row]);
        Assert.Equal(Company, mirror.CompanyId);
        Assert.Equal("SENTETIK-MIKRO-v1", mirror.SourceVersion);
        Assert.Equal(Checksum, mirror.SourceSha256);
        Assert.Equal(Approver, mirror.ApprovedByUserId);
        Assert.Equal(At, mirror.ApprovedAtUtc);
        Assert.Same(row, Assert.Single(mirror.Rows));
        Assert.Same(row, mirror.FindByCode("SENTETIK-001"));
        Assert.Same(row, mirror.Lookup(row.Account));
        Assert.Equal("Örnek hesap / €", row.Name);
    }

    [Fact]
    public void Snapshots_mutable_input_and_exposes_no_writable_collection_or_setter()
    {
        var first = Row(1);
        var input = new[] { first };
        var mirror = Make(input);
        input[0] = Row(2);
        Assert.Same(first, Assert.Single(mirror.Rows));
        var collection = Assert.IsAssignableFrom<IList<AccountPlanMirrorRowV1>>(mirror.Rows);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection[0] = Row(3));
        Assert.All(typeof(AccountPlanMirrorV1).GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.All(typeof(AccountPlanMirrorRowV1).GetProperties(), p => Assert.Null(p.SetMethod));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" v1")]
    [InlineData("v1 ")]
    public void Rejects_missing_or_rewritten_source_version(string? version) =>
        Assert.Throws<AccountingDomainException>(() => new AccountPlanMirrorV1(Company, version!, Checksum,
            Approver, At, [Row(1)]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-checksum")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaZ")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa０")]
    public void Rejects_non_sha256_provenance(string? checksum) =>
        Assert.Throws<AccountingDomainException>(() => new AccountPlanMirrorV1(Company, "v1", checksum!,
            Approver, At, [Row(1)]));

    [Fact]
    public void Preserves_digest_representation_without_silent_normalization()
    {
        var digest = new string('A', 64);
        var mirror = new AccountPlanMirrorV1(Company, "v1", digest, Approver, At, [Row(1)]);
        Assert.Equal(digest, mirror.SourceSha256);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" SENTETIK-001")]
    [InlineData("SENTETIK-001 ")]
    public void Rejects_blank_or_noncanonical_code(string code) =>
        Assert.Throws<AccountingDomainException>(() => Row(1, code));

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    public void Rejects_blank_name(string name) =>
        Assert.Throws<AccountingDomainException>(() => Row(1, "SENTETIK-001", name));

    [Theory]
    [InlineData("company")]
    [InlineData("account")]
    public void Rejects_partially_empty_account_identity_before_mirror_indexing(string emptyPart)
    {
        var companyId = emptyPart == "company" ? Guid.Empty : Company;
        var accountId = emptyPart == "account" ? Guid.Empty : Guid.NewGuid();

        var exception = Assert.Throws<AccountingDomainException>(() =>
            new AccountPlanMirrorRowV1(new AccountRef(companyId, accountId),
                "SENTETIK-001", "Synthetic account"));

        Assert.Equal(AccountingErrorCodes.InvalidReference, exception.Code);
    }

    [Fact]
    public void Rejects_duplicate_codes_or_ids_instead_of_picking_a_winner()
    {
        Assert.Throws<AccountingDomainException>(() => Make([Row(1, "same"), Row(2, "same")]));
        Assert.Throws<AccountingDomainException>(() => Make([Row(1, "one"), Row(1, "two")]));
    }

    [Fact]
    public void Rejects_missing_approval_company_empty_or_foreign_rows()
    {
        Assert.Throws<AccountingDomainException>(() => new AccountPlanMirrorV1(Guid.Empty, "v1", Checksum,
            Approver, At, [Row(1)]));
        Assert.Throws<AccountingDomainException>(() => new AccountPlanMirrorV1(Company, "v1", Checksum,
            Guid.Empty, At, [Row(1)]));
        Assert.Throws<AccountingDomainException>(() => new AccountPlanMirrorV1(Company, "v1", Checksum,
            Approver, default, [Row(1)]));
        Assert.Throws<AccountingDomainException>(() => new AccountPlanMirrorV1(Company, "v1", Checksum,
            Approver, At.ToOffset(TimeSpan.FromHours(3)), [Row(1)]));
        Assert.Throws<AccountingDomainException>(() => Make([]));
        Assert.Throws<AccountingDomainException>(() => Make([null!]));
        var foreign = new AccountPlanMirrorRowV1(new AccountRef(Guid.NewGuid(), Guid.NewGuid()), "foreign", "Synthetic");
        Assert.Equal(AccountingErrorCodes.CompanyMismatch,
            Assert.Throws<AccountingDomainException>(() => Make([foreign])).Code);
    }

    [Fact]
    public void Unknown_or_inexact_lookup_never_creates_or_guesses_an_account()
    {
        var mirror = Make([Row(1)]);
        Assert.Null(mirror.FindByCode(null));
        Assert.Null(mirror.FindByCode("SENTETIK-999"));
        Assert.Null(mirror.FindByCode(" SENTETIK-1"));
        Assert.Null(mirror.Lookup(new AccountRef(Guid.NewGuid(), mirror.Rows[0].Account.AccountId)));
        Assert.Null(mirror.Lookup(default));
        Assert.Single(mirror.Rows);
    }

    private static AccountPlanMirrorV1 Make(AccountPlanMirrorRowV1[] rows) =>
        new(Company, "SENTETIK-MIKRO-v1", Checksum, Approver, At, rows);

    private static AccountPlanMirrorRowV1 Row(int number, string? code = null, string name = "Synthetic account") =>
        new(new AccountRef(Company, Guid.Parse($"b0000000-0000-4000-8000-{number:D12}")),
            code ?? $"SENTETIK-{number}", name);
}
