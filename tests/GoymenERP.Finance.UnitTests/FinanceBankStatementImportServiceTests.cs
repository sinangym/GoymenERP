using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// WP12 (Finance K3) SENTETIK / NOT_ENV service evidence with the in-memory provider: it does not replace the real SQL
/// unique index, check constraints, serializable race or atomic audit (FinanceBankStatementSqlServerRelationalAcceptanceTests).
/// </summary>
public sealed class FinanceBankStatementImportServiceTests
{
    private const string OwnIban = "TR420001000000000000000001";
    private const string OtherIban = "TR130006200000000000000002";
    private const string Csv = "SENTETIK BANKA\nTarih;Valor;Aciklama;Tutar;Bakiye;Referans\n"
                               + "17.11.2026;;Virman TR13 0006 2000 0000 0000 0000 02;100,00;;V-1\n"
                               + "17.11.2026;;Gelen EFT SENTETIK MUSTERI;250,00;;E-1\n"
                               + "18.11.2026;;Masraf;-3,50;;\n";

    [Fact]
    public async Task Reserve_then_parse_stores_lines_once_audits_twice_and_creates_no_money_fact()
    {
        await using var run = new Run();
        var reserved = await run.ReserveAsync("SENTETIK-reserve");
        Assert.Equal(CommandOutcome.Succeeded, reserved.Outcome);
        Assert.Equal((FinanceBankStatementImportStateV1.Reserved, "1"), (reserved.Resource!.State, reserved.Resource.Version));

        var parsed = await run.ParseAsync("SENTETIK-parse", Csv);
        Assert.Equal(CommandOutcome.Succeeded, parsed.Outcome);
        var view = parsed.Resource!;
        Assert.Equal((FinanceBankStatementImportStateV1.Parsed, "2", 3, 0, new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 18)),
            (view.State, view.Version, view.LineCount, view.DuplicateLineCount, view.FirstTransactionOn!.Value, view.LastTransactionOn!.Value));
        Assert.Equal((run.Mapping.Profile.ProfileId, (long?)run.Mapping.Profile.Version), (view.CsvMappingProfileId!.Value, view.CsvMappingProfileVersion));
        Assert.Equal(3, await run.Db.BankStatementLines.CountAsync());
        Assert.Empty(await run.Db.MoneyEvents.ToListAsync());
        Assert.Empty(await run.Db.OutgoingMoneyEvents.ToListAsync());
        Assert.Empty(await run.Db.CashTransfers.ToListAsync());
        Assert.Equal(["finance.bank-statement-import.reserved.v1", "finance.bank-statement-import.parsed.v1"],
            run.Audit.Events.Select(item => item.EventType).ToArray());
        Assert.All(run.Audit.Events, item => Assert.Equal(FinanceBankStatementPermissionsV1.Import, item.PermissionCode));
        Assert.Equal(2, await run.Db.BankStatementCommands.CountAsync());
    }

    [Fact]
    public async Task Same_key_replays_the_first_answer_and_another_payload_or_actor_conflicts()
    {
        await using var run = new Run();
        var first = await run.ReserveAsync("SENTETIK-key");
        var replay = await run.Service.ReserveAsync(run.Context, new("SENTETIK-key"),
            new(run.ImportId, run.BankAccount, FinanceBankStatementFormatV1.Csv, run.Mapping.BankKey), default);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Resource, replay.Resource);
        var other = await run.Service.ReserveAsync(run.Context, new("SENTETIK-key"),
            new(Guid.NewGuid(), run.BankAccount, FinanceBankStatementFormatV1.Csv, run.Mapping.BankKey), default);
        Assert.Equal(CommandOutcome.IdempotencyConflict, other.Outcome);
        Assert.Single(await run.Db.BankStatementImports.ToListAsync());
        Assert.Single(run.Audit.Events);
    }

    [Fact]
    public async Task A_second_file_re_reading_lines_stores_only_new_lines_and_the_same_file_twice_is_refused()
    {
        await using var run = new Run();
        await run.ReserveAsync("SENTETIK-r1");
        await run.ParseAsync("SENTETIK-p1", Csv);

        var second = Guid.NewGuid();
        await run.ReserveAsync("SENTETIK-r2", second);
        var overlap = Csv + "19.11.2026;;Yeni satir SENTETIK;10,00;;\n";
        var parsed = await run.ParseAsync("SENTETIK-p2", overlap, second);
        Assert.Equal(CommandOutcome.Succeeded, parsed.Outcome);
        Assert.Equal((1, 3), (parsed.Resource!.LineCount, parsed.Resource.DuplicateLineCount));
        Assert.Equal(4, await run.Db.BankStatementLines.CountAsync());

        var third = Guid.NewGuid();
        await run.ReserveAsync("SENTETIK-r3", third);
        var again = await run.ParseAsync("SENTETIK-p3", Csv, third);
        Assert.Equal((CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.FileAlreadyImported), (again.Outcome, again.Code));
        Assert.Equal(4, await run.Db.BankStatementLines.CountAsync());
    }

    [Fact]
    public async Task Two_identical_lines_in_one_file_are_both_kept_as_separate_occurrences()
    {
        await using var run = new Run();
        await run.ReserveAsync("SENTETIK-r");
        var file = "x\nh\n17.11.2026;;Ayni havale;5,00;;\n17.11.2026;;Ayni havale;5,00;;\n";
        var parsed = await run.ParseAsync("SENTETIK-p", file);
        Assert.Equal((2, 0), (parsed.Resource!.LineCount, parsed.Resource.DuplicateLineCount));
        var occurrences = (await run.Db.BankStatementLines.OrderBy(x => x.Occurrence).ToListAsync()).Select(x => x.Occurrence).ToArray();
        Assert.Equal(new[] { 1, 2 }, occurrences);
    }

    [Fact]
    public async Task An_invalid_file_is_refused_with_issues_and_stores_nothing()
    {
        await using var run = new Run();
        await run.ReserveAsync("SENTETIK-r");
        var parsed = await run.ParseAsync("SENTETIK-p", "x\nh\n17.11.2026;;Iyi;1,00;;\n32.11.2026;;Kotu tarih;1,00;;\n");
        Assert.Equal((CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.FileInvalid), (parsed.Outcome, parsed.Code));
        Assert.Contains(parsed.Issues, issue => issue.LineNumber == 4 && issue.Field == "transactionDate");
        Assert.Empty(await run.Db.BankStatementLines.ToListAsync());
        var row = await run.Db.BankStatementImports.AsNoTracking().SingleAsync();
        Assert.Equal((FinanceBankStatementImportStates.Reserved, 1L), (row.State, row.Version));
        Assert.Single(run.Audit.Events);
    }

    [Fact]
    public async Task Stale_version_unknown_import_and_mismatched_document_kind_are_refused()
    {
        await using var run = new Run();
        await run.ReserveAsync("SENTETIK-r");
        var stale = await run.Service.ParseAsync(run.Context, new("SENTETIK-stale"), run.ImportId,
            new("2", Guid.NewGuid(), 1), default);
        Assert.Equal(CommandOutcome.VersionConflict, stale.Outcome);
        var unknown = await run.Service.ParseAsync(run.Context, new("SENTETIK-unknown"), Guid.NewGuid(),
            new("1", Guid.NewGuid(), 1), default);
        Assert.Equal(FinanceBankStatementCodesV1.ImportNotFound, unknown.Code);
        run.Documents.Format = FinanceBankStatementFormatV1.Mt940;
        var mismatch = await run.ParseAsync("SENTETIK-kind", Csv);
        Assert.Equal(FinanceBankStatementCodesV1.DocumentFormatMismatch, mismatch.Code);
        run.Documents.Outcome = FinanceBankStatementDocumentOutcomeV1.Unavailable;
        Assert.Equal(CommandOutcome.Unavailable, (await run.ParseAsync("SENTETIK-down", Csv)).Outcome);
        Assert.Empty(await run.Db.BankStatementLines.ToListAsync());
    }

    [Fact]
    public async Task Reserve_refuses_xlsx_an_unapproved_account_a_bad_bank_key_and_an_inactive_mapping()
    {
        await using var run = new Run();
        var xlsx = await run.Service.ReserveAsync(run.Context, new("SENTETIK-x"),
            new(Guid.NewGuid(), run.BankAccount, FinanceBankStatementFormatV1.Xlsx, null), default);
        Assert.Equal(FinanceBankStatementCodesV1.FormatNotSupported, xlsx.Code);
        var mt940WithKey = await run.Service.ReserveAsync(run.Context, new("SENTETIK-m"),
            new(Guid.NewGuid(), run.BankAccount, FinanceBankStatementFormatV1.Mt940, "SENTETIK-BANKA"), default);
        Assert.Equal(FinanceBankStatementCodesV1.BankKeyInvalid, mt940WithKey.Code);
        var proposed = await run.Service.ReserveAsync(run.Context, new("SENTETIK-p"),
            new(Guid.NewGuid(), run.ProposedBankAccount, FinanceBankStatementFormatV1.Mt940, null), default);
        Assert.Equal(FinanceBankStatementCodesV1.BankAccountUnavailable, proposed.Code);
        run.MappingProvider.Code = SettingsPrerequisiteCodesV1.BankCsvMappingInactive;
        var inactive = await run.ReserveAsync("SENTETIK-i");
        Assert.Equal(FinanceBankStatementCodesV1.CsvMappingInactive, inactive.Code);
        Assert.Empty(await run.Db.BankStatementImports.ToListAsync());
        Assert.Empty(run.Audit.Events);
    }

    [Fact]
    public async Task Denied_or_unregistered_permission_refuses_before_anything_is_read_or_written()
    {
        await using var run = new Run();
        run.Permissions.Decision = PermissionDecision.Denied;
        Assert.Equal(CommandOutcome.Denied, (await run.ReserveAsync("SENTETIK-d")).Outcome);
        Assert.Equal(ReadOutcome.Denied, (await run.Service.ListAuthorizedAsync(run.Context, null, default)).Outcome);
        run.Permissions.Decision = PermissionDecision.Unavailable;
        Assert.Equal(CommandOutcome.Unavailable, (await run.ReserveAsync("SENTETIK-u")).Outcome);
        Assert.Equal(ReadOutcome.Unavailable, (await run.Service.GetAuthorizedAsync(run.Context, run.ImportId, default)).Outcome);
        Assert.Empty(await run.Db.BankStatementImports.ToListAsync());
        Assert.Equal(0, run.MappingProvider.Calls);
    }

    [Fact]
    public async Task Permission_lost_after_the_document_read_refuses_the_parse_without_a_write()
    {
        await using var run = new Run();
        await run.ReserveAsync("SENTETIK-r");
        run.Documents.AfterRead = () => run.Permissions.Decision = PermissionDecision.Denied;
        Assert.Equal(CommandOutcome.Denied, (await run.ParseAsync("SENTETIK-p", Csv)).Outcome);
        Assert.Empty(await run.Db.BankStatementLines.ToListAsync());
        Assert.Single(run.Audit.Events);
    }

    [Fact]
    public async Task Another_company_context_sees_nothing_of_the_import()
    {
        await using var run = new Run();
        await run.ReserveAsync("SENTETIK-r");
        var foreign = run.Context with { CompanyId = Guid.NewGuid() };
        Assert.Equal(ReadOutcome.Denied, (await run.Service.GetAuthorizedAsync(foreign, run.ImportId, default)).Outcome);
        Assert.Equal(ReadOutcome.Denied, await run.Service.CheckDocumentOwnerAsync(foreign, run.ImportId, default));
        Assert.Equal(ReadOutcome.Found, await run.Service.CheckDocumentOwnerAsync(run.Context, run.ImportId, default));
        Assert.Equal(ReadOutcome.NotFound, await run.Service.CheckDocumentOwnerAsync(run.Context, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Detail_suggests_only_the_exact_transfer_and_leaves_the_party_receipt_and_expense_manual()
    {
        await using var run = new Run();
        var transfer = await run.SeedTransferInAsync("100.00", new DateOnly(2026, 11, 17));
        await run.SeedReceiptAsync("250.00", new DateOnly(2026, 11, 17));
        await run.ReserveAsync("SENTETIK-r");
        await run.ParseAsync("SENTETIK-p", Csv);

        var detail = (await run.Service.GetAuthorizedAsync(run.Context, run.ImportId, default)).Detail!;
        Assert.Equal((1, 2), (detail.SuggestedCount, detail.ManualCount));
        var lines = detail.Lines.ToDictionary(line => line.Reference ?? line.Description);
        Assert.Equal(OtherIban, lines["V-1"].CounterpartyIban);
        Assert.Equal((FinanceBankStatementMatchStatusV1.Suggested, FinanceBankStatementCandidateKindV1.Transfer, transfer),
            (lines["V-1"].Suggestion.Status, lines["V-1"].Suggestion.Candidate!.Kind, lines["V-1"].Suggestion.Candidate!.RecordId));
        Assert.Equal(FinanceBankStatementManualReasonV1.NoCounterpartyIdentity, lines["E-1"].Suggestion.ManualReason);
        Assert.Equal(FinanceBankStatementManualReasonV1.NoExactCandidate, lines["Masraf"].Suggestion.ManualReason);
        Assert.Empty(await run.Db.MoneyEvents.Where(x => x.Kind != MoneyEventKinds.ManualBankReceiptRecorded).ToListAsync());
        Assert.Single(await run.Db.CashTransfers.ToListAsync());
    }

    [Fact]
    public async Task Mt940_of_another_iban_is_refused_and_of_the_own_iban_is_parsed()
    {
        await using var run = new Run();
        await run.Service.ReserveAsync(run.Context, new("SENTETIK-r"),
            new(run.ImportId, run.BankAccount, FinanceBankStatementFormatV1.Mt940, null), default);
        run.Documents.Format = FinanceBankStatementFormatV1.Mt940;
        string Mt940(string account) => ":20:S\n:25:" + account + "\n:60F:C261116TRY0,00\n:61:2611171117C7,00NTRFREF\n:86:SENTETIK\n";
        var foreign = await run.ParseAsync("SENTETIK-f", Mt940(OtherIban));
        Assert.Equal(FinanceBankStatementCodesV1.AccountMismatch, foreign.Code);
        var own = await run.ParseAsync("SENTETIK-o", Mt940(OwnIban));
        Assert.Equal(CommandOutcome.Succeeded, own.Outcome);
        Assert.Null(own.Resource!.CsvMappingProfileId);
        Assert.Equal(0, run.MappingProvider.Calls);
    }

    private sealed class Run : IAsyncDisposable
    {
        public Guid Company { get; } = Guid.NewGuid();
        public Guid BankAccount { get; } = Guid.NewGuid();
        public Guid ProposedBankAccount { get; } = Guid.NewGuid();
        public Guid OtherBankAccount { get; } = Guid.NewGuid();
        public Guid ImportId { get; } = Guid.NewGuid();
        public FinanceDbContext Db { get; }
        public FinanceContext Context { get; }
        public FinanceBankStatementImportServiceV1 Service { get; }
        public Permissions Permissions { get; } = new();
        public MappingProvider MappingProvider { get; }
        public DocumentRead Documents { get; } = new();
        public Collector Audit { get; } = new();
        public SettingsBankCsvMappingV1 Mapping { get; }

        public Run()
        {
            var company = new CompanyContext();
            company.Set(Company);
            var user = new User(Company);
            Db = new(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
            Context = new(Company, user.UserId, "SENTETIK-correlation", null);
            Mapping = FinanceBankStatementParserTests.SignedMapping(Company);
            MappingProvider = new MappingProvider(Mapping);
            Service = new(Db, company, user, Permissions, MappingProvider, Documents, Audit,
                new(FinanceBankStatementPermissionsV1.Import, ["SENTETIK-YETKI"], "Web"), TimeProvider.System);
            Db.BankAccounts.AddRange(Account(BankAccount, 2, OwnIban), Account(ProposedBankAccount, 1, null),
                Account(OtherBankAccount, 2, OtherIban));
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
        }

        private BankAccountRow Account(Guid id, int status, string? iban) => new()
        {
            Id = id, CompanyId = Company, CurrencyId = Guid.NewGuid(), DisplayName = "SENTETIK hesap", Status = status,
            Version = 1, ProposedBy = "SENTETIK-proposer", ProposedAtUtc = DateTimeOffset.UnixEpoch, Iban = iban,
            ApprovedBy = status == 2 ? "SENTETIK-approver" : null, ApprovedAtUtc = status == 2 ? DateTimeOffset.UnixEpoch : null,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        };

        public Task<FinanceBankStatementCommandResultV1> ReserveAsync(string key, Guid? importId = null) =>
            Service.ReserveAsync(Context, new(key), new(importId ?? ImportId, BankAccount, FinanceBankStatementFormatV1.Csv,
                Mapping.BankKey), default);

        public Task<FinanceBankStatementCommandResultV1> ParseAsync(string key, string content, Guid? importId = null)
        {
            var documentId = Guid.NewGuid();
            Documents.Next = (documentId, Encoding.UTF8.GetBytes(content));
            return Service.ParseAsync(Context, new(key), importId ?? ImportId, new("1", documentId, 1), default);
        }

        public async Task<Guid> SeedTransferInAsync(string amount, DateOnly on)
        {
            var id = Guid.NewGuid();
            Db.CashTransfers.Add(new CashTransferRow
            {
                Id = id, CompanyId = Company, Version = 1, Kind = CashTransferKinds.SameCurrency,
                SourceKind = CashTransferAccountKinds.Bank, SourceAccountId = OtherBankAccount, SourceCurrencyId = Guid.NewGuid(),
                SourceAmountText = amount, SourceProfileId = "p", SourceProfileVersion = "1",
                TargetKind = CashTransferAccountKinds.Bank, TargetAccountId = BankAccount, TargetCurrencyId = Guid.NewGuid(),
                TargetAmountText = amount, TargetProfileId = "p", TargetProfileVersion = "1", OccurredOn = on,
                TransferConfirmed = true, RecordedBy = "SENTETIK-actor", RecordedAtUtc = DateTimeOffset.UnixEpoch,
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return id;
        }

        public async Task SeedReceiptAsync(string amount, DateOnly on)
        {
            Db.MoneyEvents.Add(new MoneyEventRow
            {
                Id = Guid.NewGuid(), CompanyId = Company, SourceId = Guid.NewGuid(), Kind = MoneyEventKinds.ManualBankReceiptRecorded,
                PartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), TreasuryAccountId = BankAccount,
                CurrencyId = Guid.NewGuid(), AmountText = amount, ProfileId = "p", ProfileVersion = "1", OccurredOn = on,
                RecordedAtUtc = DateTimeOffset.UnixEpoch, RecordedBy = "SENTETIK-actor", CorrelationId = "SENTETIK-c",
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class User(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => "SENTETIK-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid id) => id == company;
    }

    private sealed class Permissions : IFinancePermissionPort
    {
        public PermissionDecision Decision { get; set; } = PermissionDecision.Allowed;

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? id, CancellationToken token) =>
            Task.FromResult(action is FinanceAction.ImportBankStatement or FinanceAction.ReadBankStatements
                ? Decision : PermissionDecision.Denied);
    }

    private sealed class MappingProvider(SettingsBankCsvMappingV1 mapping) : ISettingsBankCsvMappingProviderV1
    {
        public string? Code { get; set; }
        public int Calls { get; private set; }

        public Task<SettingsBankCsvMappingV1> GetBankCsvMappingAsync(string bankKey, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Code is not null) throw new SettingsPrerequisiteException(Code);
            return bankKey == mapping.BankKey
                ? Task.FromResult(mapping)
                : throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.BankCsvMappingNotFound);
        }

        public Task<IReadOnlyList<SettingsBankCsvMappingSummaryV1>> ListActiveBankCsvMappingsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SettingsBankCsvMappingSummaryV1>>([]);
    }

    private sealed class DocumentRead : IFinanceBankStatementDocumentReadV1
    {
        public (Guid Id, byte[] Content) Next { get; set; }
        public FinanceBankStatementFormatV1 Format { get; set; } = FinanceBankStatementFormatV1.Csv;
        public FinanceBankStatementDocumentOutcomeV1 Outcome { get; set; } = FinanceBankStatementDocumentOutcomeV1.Found;
        public Action? AfterRead { get; set; }

        public Task<FinanceBankStatementDocumentAnswerV1> ReadAsync(FinanceContext context, Guid importId, Guid documentId,
            int version, CancellationToken cancellationToken)
        {
            AfterRead?.Invoke();
            if (Outcome != FinanceBankStatementDocumentOutcomeV1.Found || documentId != Next.Id)
                return Task.FromResult(new FinanceBankStatementDocumentAnswerV1(Outcome == FinanceBankStatementDocumentOutcomeV1.Found
                    ? FinanceBankStatementDocumentOutcomeV1.NotFound : Outcome, null));
            return Task.FromResult(new FinanceBankStatementDocumentAnswerV1(FinanceBankStatementDocumentOutcomeV1.Found,
                new FinanceBankStatementDocumentV1(context.CompanyId, documentId, version,
                    Convert.ToHexString(SHA256.HashData(Next.Content)), Format, Next.Content)));
        }
    }

    private sealed class Collector : IAuditEventCollector
    {
        public List<AuditEventDraft> Events { get; } = [];
        public void Enqueue(AuditEventDraft item) => Events.Add(item);
    }
}
