using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// API-85b (F018 plan §L2; FNS-D57, FNS-D31, FNS-D58) without a database: the collateral model (live and revision
/// indexes, payload and kind checks, the command target), the forward-fix Down, the write guard, the payload hash, and
/// the real service in memory: the one-step record, change and release (no proposer/approver, no threshold), each audited
/// with the old and new values; the Settings type catalog (unavailable by default, a cheque/note class refused, a rate
/// Finance cannot read refused); stale revisions, replay, company isolation; the weighted collateral and the separately
/// shown documentary risk. What SQL Server enforces is proven by the relational acceptance tests. SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinancePartyCollateralTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherCurrency = Guid.Parse("23232323-2323-2323-2323-232323232323");
    private static readonly Guid Party = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherParty = Guid.Parse("34343434-3434-3434-3434-343434343434");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Guarantee = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Mortgage = Guid.Parse("56565656-5656-5656-5656-565656565656");
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 15);
    private const string Recorder = "SENTETIK-recorder";
    private const string Reader = "SENTETIK-reader";
    private const string Reason = "SENTETIK iade nedeni";
    private const string Guard = "Teminat kaydı yalnız yerine yeni sürüm geçerek, sürümü bir artarak değişir.";
    private const string Immutable = "Finans kaydının yalnız izinli alanı değişir; komut kayıtları değişmez.";

    [Fact]
    public void A_collateral_is_a_company_revision_row_whose_live_state_is_held_by_the_database()
    {
        using var context = Context();
        var collaterals = context.Model.FindEntityType(typeof(PartyCollateralRow))!;
        Assert.Equal((FinanceDbContext.Schema, "PartyCollaterals"), (collaterals.GetSchema(), collaterals.GetTableName()));
        Assert.Single(collaterals.GetDeclaredQueryFilters());
        Assert.True(collaterals.FindProperty(nameof(PartyCollateralRow.Version))!.IsConcurrencyToken);
        // The amount is exact text and the rate is never stored: no column could round either.
        Assert.DoesNotContain(collaterals.GetProperties(), property => property.ClrType == typeof(decimal)
            || property.ClrType == typeof(decimal?) || property.ClrType == typeof(double));
        Assert.DoesNotContain(collaterals.GetProperties(), property => property.Name.Contains("Rate", StringComparison.Ordinal));

        var design = context.GetService<IDesignTimeModel>().Model;
        var designCollaterals = design.FindEntityType(typeof(PartyCollateralRow))!;
        var payload = Assert.Single(designCollaterals.GetCheckConstraints(), check => check.Name == "CK_PartyCollaterals_Payload");
        Assert.Equal(FinanceDbContext.PartyCollateralPayloadCheck, payload.Sql);
        Assert.Contains("[AmountText] LIKE '%[1-9]%'", payload.Sql, StringComparison.Ordinal);
        Assert.Contains("([ValidUntil] IS NULL OR [ValidUntil] >= [ValidFrom])", payload.Sql, StringComparison.Ordinal);
        var kind = Assert.Single(designCollaterals.GetCheckConstraints(), check => check.Name == "CK_PartyCollaterals_Kind");
        Assert.Equal(FinanceDbContext.PartyCollateralKindCheck, kind.Sql);
        Assert.Contains("[Kind] = 3 AND [Revision] > 1 AND [ReleaseReason] IS NOT NULL", kind.Sql, StringComparison.Ordinal);
        Assert.Single(designCollaterals.GetCheckConstraints(), check => check.Name == "CK_PartyCollaterals_Identity");
        Assert.Single(designCollaterals.GetCheckConstraints(), check => check.Name == "CK_PartyCollaterals_Status");
        Assert.Equal(FinanceDbContext.IdentityCollation,
            designCollaterals.FindProperty(nameof(PartyCollateralRow.RecordedBy))!.GetCollation());
        Assert.Equal("date", designCollaterals.FindProperty(nameof(PartyCollateralRow.ValidFrom))!.GetColumnType());

        // One effective revision per collateral; Status is a key column so the leaving revision is written first.
        var live = Assert.Single(designCollaterals.GetIndexes(),
            index => index.GetDatabaseName() == FinanceDbContext.PartyCollateralLiveIndex);
        Assert.True(live.IsUnique);
        Assert.Equal([nameof(PartyCollateralRow.CompanyId), nameof(PartyCollateralRow.CollateralId),
            nameof(PartyCollateralRow.Status)], live.Properties.Select(property => property.Name));
        Assert.Equal("[Status] = 1", live.GetFilter());

        // Two changes from the same revision cannot both win.
        var revisions = Assert.Single(designCollaterals.GetIndexes(),
            index => index.GetDatabaseName() == "UX_PartyCollaterals_Revision");
        Assert.True(revisions.IsUnique);
        Assert.Equal([nameof(PartyCollateralRow.CompanyId), nameof(PartyCollateralRow.CollateralId),
            nameof(PartyCollateralRow.Revision)], revisions.Properties.Select(property => property.Name));
        Assert.Null(revisions.GetFilter());

        var commands = design.FindEntityType(typeof(PartyCollateralCommandRow))!;
        Assert.Equal((FinanceDbContext.Schema, "PartyCollateralCommands"), (commands.GetSchema(), commands.GetTableName()));
        var target = Assert.Single(commands.GetCheckConstraints(), check => check.Name == "CK_PartyCollateralCommands_Target");
        Assert.EndsWith("JSON_VALUE([ResultJson], '$.outcome') IN ('ValidationFailed', 'VersionConflict'))", target.Sql,
            StringComparison.Ordinal);
        Assert.Equal([nameof(PartyCollateralCommandRow.CompanyId), nameof(PartyCollateralCommandRow.CommandName),
            nameof(PartyCollateralCommandRow.IdempotencyKeyHash)],
            commands.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(context.Model.FindEntityType(typeof(PartyCollateralCommandRow))!.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_collateral_migration_is_pure_DDL_up_and_refuses_to_drop_stored_collaterals()
    {
        var migration = new AddPartyCollaterals();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(["PartyCollaterals", "PartyCollateralCommands"],
            migration.UpOperations.OfType<CreateTableOperation>().Select(operation => operation.Name));

        // Forward fix: the first Down operation is the one guard; only an empty store rolls back.
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 51585", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[PartyCollaterals]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[PartyCollateralCommands]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("AddPartyCollaterals is not rolled back (forward fix).", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(["PartyCollateralCommands", "PartyCollaterals"],
            migration.DownOperations.OfType<DropTableOperation>().Select(operation => operation.Name));
    }

    [Theory]
    [InlineData("amount-changed")]
    [InlineData("type-changed")]
    [InlineData("two-version-steps")]
    [InlineData("superseded-without-time")]
    [InlineData("superseded-made-effective")]
    [InlineData("command-changed")]
    public async Task A_collateral_revision_only_leaves_the_effective_state_and_its_values_never_change(string fault)
    {
        await using var run = new Run();
        var seeded = await run.SeedAsync(Recorded(status: fault == "superseded-made-effective"
            ? FinancePartyCollateralService.Superseded : FinancePartyCollateralService.Effective));
        await run.RecordAsync(Recorder, Request());
        await using var context = run.Memory(CompanyA);
        var row = await context.PartyCollaterals.SingleAsync(candidate => candidate.Id == seeded.Id);
        switch (fault)
        {
            case "amount-changed":
                Supersede(row);
                row.AmountText = "1.00";
                break;
            case "type-changed":
                Supersede(row);
                row.CollateralTypeId = Mortgage;
                break;
            case "two-version-steps":
                Supersede(row);
                row.Version += 1;
                break;
            case "superseded-without-time":
                Supersede(row);
                row.SupersededAtUtc = null;
                break;
            case "superseded-made-effective":
                row.Status = FinancePartyCollateralService.Effective;
                row.SupersededAtUtc = null;
                row.Version += 1;
                break;
            default:
                var command = await context.PartyCollateralCommands.FirstAsync();
                command.ResultJson = "{}";
                break;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Equal(fault is "amount-changed" or "type-changed" or "command-changed" ? Immutable : Guard,
            exception.Message);
    }

    [Fact]
    public async Task A_superseded_revision_passes_the_write_guard()
    {
        await using var run = new Run();
        var seeded = await run.SeedAsync(Recorded(FinancePartyCollateralService.Effective));
        await using (var context = run.Memory(CompanyA))
        {
            Supersede(await context.PartyCollaterals.SingleAsync(candidate => candidate.Id == seeded.Id));
            await context.SaveChangesAsync();
        }

        var stored = await run.RowAsync(seeded.Id);
        Assert.Equal((FinancePartyCollateralService.Superseded, 2L), (stored.Status, stored.Version));
    }

    [Fact]
    public void The_payload_hash_names_the_command_and_every_field()
    {
        var request = Request();
        var hash = FinancePartyCollateralService.PayloadHash(FinancePartyCollateralService.RecordCommand, request);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, FinancePartyCollateralService.PayloadHash(FinancePartyCollateralService.RecordCommand, Request()));
        string[] others =
        [
            FinancePartyCollateralService.PayloadHash(FinancePartyCollateralService.ChangeCommand, request),
            Hash(request with { PartyId = OtherParty }),
            Hash(request with { CompanyAccountId = Guid.NewGuid() }),
            Hash(request with { CollateralTypeId = Mortgage }),
            Hash(request with { CurrencyId = OtherCurrency }),
            // The exact text is the value: "1000.0" is another payload than "1000.00".
            Hash(request with { Amount = "1000.0" }),
            Hash(request with { ValidFrom = Today.AddDays(-2) }),
            Hash(request with { ValidUntil = Today.AddDays(30) }),
            Hash(request with { DocumentReference = "SENTETIK-BTM-2" }),
        ];
        Assert.All(others, other => Assert.NotEqual(hash, other));
        Assert.Equal(others.Length, others.Distinct(StringComparer.Ordinal).Count());

        static string Hash(PartyCollateralRecordRequest request) =>
            FinancePartyCollateralService.PayloadHash(FinancePartyCollateralService.RecordCommand, request);
    }

    [Theory]
    [InlineData("key", "idempotencyKey", "idempotency_key_invalid")]
    [InlineData("party", "partyId", "identity_required")]
    [InlineData("account", "companyAccountId", "identity_required")]
    [InlineData("type", "collateralTypeId", "identity_required")]
    [InlineData("currency", "currencyId", "identity_required")]
    [InlineData("zero", "amount", "collateral_amount_invalid")]
    [InlineData("negative", "amount", "collateral_amount_invalid")]
    [InlineData("comma", "amount", "collateral_amount_invalid")]
    [InlineData("null-amount", "amount", "collateral_amount_invalid")]
    [InlineData("ends-before-start", "validUntil", "collateral_validity_invalid")]
    [InlineData("blank-reference", "documentReference", "collateral_document_reference_invalid")]
    [InlineData("padded-reference", "documentReference", "collateral_document_reference_invalid")]
    [InlineData("long-reference", "documentReference", "collateral_document_reference_invalid")]
    public async Task A_record_is_refused_before_Settings_or_Counterparty_is_asked(string fault, string field, string code)
    {
        await using var run = new Run();
        var request = fault switch
        {
            "party" => Request() with { PartyId = Guid.Empty },
            "account" => Request() with { CompanyAccountId = Guid.Empty },
            "type" => Request() with { CollateralTypeId = Guid.Empty },
            "currency" => Request() with { CurrencyId = Guid.Empty },
            "zero" => Request() with { Amount = "0.00" },
            "negative" => Request() with { Amount = "-100" },
            "comma" => Request() with { Amount = "100,00" },
            "null-amount" => Request() with { Amount = null! },
            "ends-before-start" => Request() with { ValidUntil = Today.AddDays(-11) },
            "blank-reference" => Request() with { DocumentReference = " " },
            "padded-reference" => Request() with { DocumentReference = " SENTETIK-BTM" },
            "long-reference" => Request() with { DocumentReference = new string('B', 129) },
            _ => Request(),
        };

        var result = await run.RecordAsync(Recorder, request, key: fault == "key" ? " SENTETIK-key" : null,
            catalog: new NoCatalogRead(), currencies: new NoCandidates(), parties: new NoPartyRead());

        AssertIssue(result, CommandOutcome.ValidationFailed, field, code);
        Assert.Empty(await run.CommandsAsync());
        Assert.Empty(await run.AllRowsAsync());
    }

    [Fact]
    public async Task A_recorder_records_changes_and_releases_a_collateral_in_one_step_each_audited_with_old_and_new_value()
    {
        await using var run = new Run();
        var permissions = new RecordingPermission(PermissionDecision.Allowed);

        var recorded = await run.RecordAsync(Recorder, Request(), permissions: permissions);

        Assert.Equal((CommandOutcome.Succeeded, false), (recorded.Outcome, recorded.IsReplay));
        var first = recorded.Resource!;
        Assert.Equal((PartyCollateralKind.Record, PartyCollateralStatus.Effective, "1", "1", "1000.00", Currency, "7"),
            (first.Kind, first.Status, first.Revision, first.Version, first.Amount, first.CurrencyId, first.CollateralTypeVersion));
        Assert.Equal((CompanyA, Party, Account, PartyCollateralDirection.Received, PartyCollateralSource.FinanceManualEntry),
            (first.CompanyId, first.PartyId, first.CompanyAccountId, first.Direction, first.Source));
        // FNS-D57: one authority, no proposer/approver; nothing else is measured for the command.
        Assert.All(permissions.Actions, action => Assert.Equal(FinanceAction.RecordCollateral, action));

        var changed = await run.ChangeAsync(Recorder, Change(first.CollateralId, "1") with { Amount = "1500" });
        Assert.Equal((CommandOutcome.Succeeded, PartyCollateralKind.Change, "2", first.CollateralId),
            (changed.Outcome, changed.Resource!.Kind, changed.Resource.Revision, changed.Resource.CollateralId));
        var superseded = await run.RowAsync(first.Id);
        Assert.Equal((FinancePartyCollateralService.Superseded, 2L, "1000.00"),
            (superseded.Status, superseded.Version, superseded.AmountText));

        var released = await run.ReleaseAsync(Recorder, new PartyCollateralReleaseRequest(first.CollateralId, "2", Reason));
        Assert.Equal((CommandOutcome.Succeeded, PartyCollateralKind.Release, "3", "1500", Reason),
            (released.Outcome, released.Resource!.Kind, released.Resource.Revision, released.Resource.Amount,
                released.Resource.ReleaseReason));

        // A released collateral is no longer held; its history keeps every revision.
        var account = await run.GetAccountAsync(Reader, Account, new ReadOnlyPermission());
        Assert.Equal((ReadOutcome.Found, 0), (account.Outcome, account.Resource!.Held.Count));
        var history = await run.HistoryAsync(Reader, Account, new ReadOnlyPermission());
        Assert.Equal([first.Id, changed.Resource.Id, released.Resource.Id], history.Resource!.Items.Select(item => item.Id));

        var recordAudit = Assert.Single(await run.AuditsAsync(first.Id));
        Assert.Equal(("finance.collateral.recorded.v1", "Record", ErpPermissions.FinanceCollateralsRecord, Recorder,
                (long?)null, (long?)1),
            (recordAudit.EventType, recordAudit.Operation, recordAudit.PermissionCode, recordAudit.ActorId,
                recordAudit.EntityVersionBefore, recordAudit.EntityVersionAfter));
        var changeAudit = Assert.Single(await run.AuditsAsync(changed.Resource.Id));
        Assert.Equal(("finance.collateral.changed.v1", (long?)1), (changeAudit.EventType, changeAudit.EntityVersionBefore));
        // The old value beside the new one.
        Assert.Contains("1000.00", changeAudit.BeforeJson, StringComparison.Ordinal);
        Assert.Contains("1500", changeAudit.AfterJson, StringComparison.Ordinal);
        using var approval = JsonDocument.Parse(changeAudit.ApprovalContextJson!);
        Assert.Equal(("FNS-D57", Recorder, true), (approval.RootElement.GetProperty("rule").GetString(),
            approval.RootElement.GetProperty("recordedBy").GetString(), approval.RootElement.GetProperty("direct").GetBoolean()));
        Assert.Equal("finance.collateral.released.v1", Assert.Single(await run.AuditsAsync(released.Resource.Id)).EventType);
        Assert.Equal(3, (await run.CommandsAsync()).Count);
    }

    [Theory]
    [InlineData("default-reader", CommandOutcome.Unavailable, "collateral_type_catalog_unavailable")]
    [InlineData("catalog-unavailable", CommandOutcome.Unavailable, "collateral_type_catalog_unavailable")]
    [InlineData("listed-twice", CommandOutcome.Unavailable, "collateral_type_catalog_unavailable")]
    [InlineData("not-listed", CommandOutcome.ValidationFailed, "collateral_type_not_a_candidate")]
    [InlineData("inactive", CommandOutcome.ValidationFailed, "collateral_type_not_a_candidate")]
    [InlineData("cheque-or-note", CommandOutcome.ValidationFailed, "collateral_type_is_instrument")]
    [InlineData("rate-above-hundred", CommandOutcome.Unavailable, "collateral_type_rate_unavailable")]
    [InlineData("rate-not-exact", CommandOutcome.Unavailable, "collateral_type_rate_unavailable")]
    [InlineData("version-blank", CommandOutcome.Unavailable, "collateral_type_rate_unavailable")]
    public async Task The_type_must_be_an_active_Settings_collateral_type_with_a_readable_rate(string fault,
        CommandOutcome outcome, string code)
    {
        await using var run = new Run();
        IFinanceCollateralTypeCatalogReader catalog = fault switch
        {
            "default-reader" => new UnavailableFinanceCollateralTypeCatalogReader(),
            "catalog-unavailable" => new FixedCatalog(FinanceCollateralTypeCatalogAnswer.NotAvailable),
            "listed-twice" => Catalog(Type(Guarantee, "50"), Type(Guarantee, "60")),
            "not-listed" => Catalog(Type(Mortgage, "50")),
            "inactive" => Catalog(Type(Guarantee, "50") with { IsActive = false }),
            "cheque-or-note" => Catalog(Type(Guarantee, "100") with { IsNegotiableInstrument = true }),
            "rate-above-hundred" => Catalog(Type(Guarantee, "100.5")),
            "rate-not-exact" => Catalog(Type(Guarantee, "50%")),
            _ => Catalog(Type(Guarantee, "50") with { Version = " " }),
        };

        var result = await run.RecordAsync(Recorder, Request(), catalog: catalog, currencies: new NoCandidates(),
            parties: new NoPartyRead());

        AssertIssue(result, outcome, "collateralTypeId", code);
        Assert.Empty(await run.AllRowsAsync());
    }

    [Theory]
    [InlineData("inactive-account", "party_account_ineligible", CommandOutcome.ValidationFailed)]
    [InlineData("inactive-party", "party_account_ineligible", CommandOutcome.ValidationFailed)]
    [InlineData("another-party", "party_account_ineligible", CommandOutcome.ValidationFailed)]
    [InlineData("not-found", "party_account_ineligible", CommandOutcome.ValidationFailed)]
    [InlineData("unavailable", "party_account_unavailable", CommandOutcome.Unavailable)]
    public async Task A_record_needs_Counterpartys_active_party_account_pair(string fault, string code, CommandOutcome outcome)
    {
        await using var run = new Run();
        IFinancePartyAccountPort parties = fault switch
        {
            "inactive-account" => new FixedParty(accountActive: false),
            "inactive-party" => new FixedParty(partyActive: false),
            "another-party" => new FixedParty(party: OtherParty),
            "not-found" => new FixedParty(outcome: ReadOutcome.NotFound),
            _ => new FixedParty(outcome: ReadOutcome.Unavailable),
        };

        var result = await run.RecordAsync(Recorder, Request(), parties: parties);

        AssertIssue(result, outcome, "companyAccountId", code);
        Assert.Empty(await run.AllRowsAsync());
    }

    [Fact]
    public async Task A_release_is_allowed_for_a_passive_account_and_an_account_held_for_another_party_is_refused()
    {
        await using var run = new Run();
        var recorded = await run.RecordAsync(Recorder, Request());
        var released = await run.ReleaseAsync(Recorder,
            new PartyCollateralReleaseRequest(recorded.Resource!.CollateralId, "1", Reason),
            parties: new FixedParty(accountActive: false, partyActive: false));
        Assert.Equal(CommandOutcome.Succeeded, released.Outcome);

        // Counterparty answers the account for another party now: refused rather than guessed.
        var moved = await run.RecordAsync(Recorder, Request() with { PartyId = OtherParty },
            parties: new FixedParty(party: OtherParty));
        AssertIssue(moved, CommandOutcome.ValidationFailed, "companyAccountId", "party_account_ineligible");
    }

    [Theory]
    [InlineData("not-a-candidate", CommandOutcome.ValidationFailed, "currency_not_a_candidate")]
    [InlineData("settings-unavailable", CommandOutcome.Unavailable, "settings_unavailable")]
    public async Task The_currency_must_be_one_of_Settings_candidates_now(string fault, CommandOutcome outcome, string code)
    {
        await using var run = new Run();
        ISettingsMoneyCandidateReader currencies = fault == "not-a-candidate"
            ? new Candidates([new MoneyCurrencyCandidateV1(OtherCurrency, "USD", "SENTETIK Dolar")])
            : new FailingCandidates("settings_unavailable");

        var result = await run.RecordAsync(Recorder, Request(), currencies: currencies, parties: new NoPartyRead());

        AssertIssue(result, outcome, "currencyId", code);
    }

    [Fact]
    public async Task A_change_or_release_names_a_held_received_collateral_and_its_current_revision()
    {
        await using var run = new Run();
        var recorded = (await run.RecordAsync(Recorder, Request())).Resource!;

        AssertIssue(await run.ChangeAsync(Recorder, Change(Guid.NewGuid(), "1")), CommandOutcome.ValidationFailed,
            "collateralId", "collateral_not_found");
        AssertIssue(await run.ChangeAsync(Recorder, Change(Guid.Empty, "1")), CommandOutcome.ValidationFailed,
            "collateralId", "identity_required");
        AssertIssue(await run.ChangeAsync(Recorder, Change(recorded.CollateralId, " 1")), CommandOutcome.ValidationFailed,
            "expectedRevision", "identity_required");
        // The same values again change nothing.
        AssertIssue(await run.ChangeAsync(Recorder, Change(recorded.CollateralId, "1")), CommandOutcome.ValidationFailed,
            "request", "collateral_unchanged");
        AssertIssue(await run.ReleaseAsync(Recorder, new PartyCollateralReleaseRequest(recorded.CollateralId, "1", " ")),
            CommandOutcome.ValidationFailed, "reason", "reason_invalid");
        AssertIssue(await run.ReleaseAsync(Recorder,
                new PartyCollateralReleaseRequest(recorded.CollateralId, "1", new string('N', 501))),
            CommandOutcome.ValidationFailed, "reason", "finance_value_does_not_fit");
        Assert.Single(await run.CommandsAsync());

        // A stale revision is a recorded terminal conflict naming the current revision; nothing changes.
        var before = await run.SnapshotAsync(recorded.Id);
        var stale = await run.ChangeAsync(Recorder, Change(recorded.CollateralId, "0") with { Amount = "2000" });
        AssertIssue(stale, CommandOutcome.VersionConflict, "expectedRevision", "version_conflict");
        Assert.Equal((before.Version, before.Commands + 1, before.Audits), await run.SnapshotAsync(recorded.Id));
        Assert.Contains(await run.CommandsAsync(), command => command.PartyCollateralRowId == recorded.Id
            && command.ResultJson.Contains("VersionConflict", StringComparison.Ordinal));

        await run.ReleaseAsync(Recorder, new PartyCollateralReleaseRequest(recorded.CollateralId, "1", Reason));
        AssertIssue(await run.ChangeAsync(Recorder, Change(recorded.CollateralId, "2") with { Amount = "3000" }),
            CommandOutcome.ValidationFailed, "collateralId", "collateral_released");
        AssertIssue(await run.ReleaseAsync(Recorder, new PartyCollateralReleaseRequest(recorded.CollateralId, "2", Reason)),
            CommandOutcome.ValidationFailed, "collateralId", "collateral_released");
    }

    [Fact]
    public async Task The_same_key_answers_its_first_result_and_another_payload_or_actor_is_a_conflict()
    {
        await using var run = new Run();
        const string key = "SENTETIK-same-key";
        var first = await run.RecordAsync(Recorder, Request(), key: key, correlation: "SENTETIK-first");
        var again = await run.RecordAsync(Recorder, Request(), key: key, correlation: "SENTETIK-second");

        Assert.Equal((CommandOutcome.Succeeded, true, "SENTETIK-first", first.Resource!.Id),
            (again.Outcome, again.IsReplay, again.OriginalCorrelationId, again.Resource!.Id));
        Assert.Single(await run.AllRowsAsync());

        var otherPayload = await run.RecordAsync(Recorder, Request() with { Amount = "1" }, key: key);
        Assert.Equal((CommandOutcome.IdempotencyConflict, (PartyCollateralRecordView?)null),
            (otherPayload.Outcome, otherPayload.Resource));
        var otherActor = await run.RecordAsync("SENTETIK-other-recorder", Request(), key: key);
        Assert.Equal(CommandOutcome.IdempotencyConflict, otherActor.Outcome);

        // The authority is measured afresh before a replay.
        var revoked = await run.RecordAsync(Recorder, Request(), key: key,
            permissions: new RecordingPermission(PermissionDecision.Denied));
        Assert.Equal((CommandOutcome.Denied, false), (revoked.Outcome, revoked.IsReplay));
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, CommandOutcome.Denied)]
    [InlineData(PermissionDecision.Unavailable, CommandOutcome.Unavailable)]
    public async Task Without_the_record_authority_nothing_is_asked_or_stored(PermissionDecision decision,
        CommandOutcome outcome)
    {
        await using var run = new Run();

        var result = await run.RecordAsync(Recorder, Request(), permissions: new RecordingPermission(decision),
            catalog: new NoCatalogRead(), currencies: new NoCandidates(), parties: new NoPartyRead());

        Assert.Equal(outcome, result.Outcome);
        Assert.Empty(await run.CommandsAsync());
    }

    [Fact]
    public async Task Authority_lost_before_the_save_stores_nothing()
    {
        await using var run = new Run();

        // Allowed at the start, after the type, currency and party ports and after the account read (five fences),
        // then denied at the fence immediately before the save.
        var result = await run.RecordAsync(Recorder, Request(),
            permissions: new RevokingPermission(FinanceAction.RecordCollateral, allowed: 5));

        Assert.Equal(CommandOutcome.Denied, result.Outcome);
        Assert.Empty(await run.AllRowsAsync());
        Assert.Empty(await run.CommandsAsync());
    }

    [Fact]
    public async Task The_account_shows_the_weighted_collateral_and_the_documentary_risk_separately()
    {
        await using var run = new Run();
        var catalog = Catalog(Type(Guarantee, "80"), Type(Mortgage, "50.5"));
        await run.RecordAsync(Recorder, Request(), catalog: catalog);
        await run.RecordAsync(Recorder, Request() with { CollateralTypeId = Mortgage, Amount = "200" }, catalog: catalog);
        // Not yet in force: held, listed, but not weighed.
        await run.RecordAsync(Recorder, Request() with { ValidFrom = Today.AddDays(5), Amount = "999" }, catalog: catalog);
        await run.SeedInstrumentAsync(CustomerInstrumentStatus.InPortfolio, "300");
        await run.SeedInstrumentAsync(CustomerInstrumentStatus.Bounced, "20.5");
        await run.SeedInstrumentAsync(CustomerInstrumentStatus.Collected, "5000");
        await run.SeedInstrumentAsync(CustomerInstrumentStatus.ReturnedToCustomer, "7000");

        var withInstruments = await run.GetAccountAsync(Reader, Account, new InstrumentReaderPermission(), catalog);

        var view = withInstruments.Resource!;
        Assert.Equal(3, view.Held.Count);
        // 1000.00 x 80% + 200 x 50.5% = 800 + 101.
        Assert.Equal((PartyCollateralFigureState.Available, (string?)null), (view.WeightedCollateral.State,
            view.WeightedCollateral.ReasonCode));
        Assert.Equal((Currency, "901"), (Assert.Single(view.WeightedCollateral.Amounts).CurrencyId,
            view.WeightedCollateral.Amounts[0].Amount));
        // FNS-D58: neither collected nor returned, never added to the collateral.
        Assert.Equal((PartyCollateralFigureState.Available, Currency, "320.5"), (view.DocumentaryRisk.State,
            Assert.Single(view.DocumentaryRisk.Amounts).CurrencyId, view.DocumentaryRisk.Amounts[0].Amount));

        // A reader without the instrument amount authority sees the figure withheld.
        var reader = await run.GetAccountAsync(Reader, Account, new ReadOnlyPermission(), catalog);
        Assert.Equal((PartyCollateralFigureState.Withheld, 0), (reader.Resource!.DocumentaryRisk.State,
            reader.Resource.DocumentaryRisk.Amounts.Count));

        // The default catalog (no Settings adapter): the weighted figure cannot be calculated, the collaterals still show.
        var unavailable = await run.GetAccountAsync(Reader, Account, new ReadOnlyPermission(),
            new UnavailableFinanceCollateralTypeCatalogReader());
        Assert.Equal((3, PartyCollateralFigureState.Unavailable, FinancePartyCollateralFigures.TypeRateUnavailable),
            (unavailable.Resource!.Held.Count, unavailable.Resource.WeightedCollateral.State,
                unavailable.Resource.WeightedCollateral.ReasonCode));
    }

    [Fact]
    public async Task Reads_follow_the_read_authority_and_candidates_the_record_authority()
    {
        await using var run = new Run();
        await run.RecordAsync(Recorder, Request());

        Assert.Equal(ReadOutcome.Denied,
            (await run.GetAccountAsync(Reader, Account, new RecordingPermission(PermissionDecision.Denied))).Outcome);
        Assert.Equal(ReadOutcome.Unavailable,
            (await run.GetAccountAsync(Reader, Account, new RecordingPermission(PermissionDecision.Unavailable))).Outcome);
        Assert.Equal(ReadOutcome.NotFound, (await run.HistoryAsync(Reader, Guid.NewGuid(), new ReadOnlyPermission())).Outcome);
        // An account without collaterals is still answered: an empty list and a recorded zero.
        var empty = await run.GetAccountAsync(Reader, Guid.NewGuid(), new ReadOnlyPermission());
        Assert.Equal((ReadOutcome.Found, 0, PartyCollateralFigureState.Available, 0),
            (empty.Outcome, empty.Resource!.Held.Count, empty.Resource.WeightedCollateral.State,
                empty.Resource.WeightedCollateral.Amounts.Count));

        Assert.Equal(ReadOutcome.Denied, (await run.TypeCandidatesAsync(Reader, new ReadOnlyPermission())).Outcome);
        Assert.Equal(ReadOutcome.Denied, (await run.CurrencyCandidatesAsync(Reader, new ReadOnlyPermission())).Outcome);
        Assert.Equal(ReadOutcome.Unavailable,
            (await run.TypeCandidatesAsync(Recorder, null, new UnavailableFinanceCollateralTypeCatalogReader())).Outcome);

        // A cheque/note class, an inactive type and an unreadable rate are not offered.
        var types = await run.TypeCandidatesAsync(Recorder, null, Catalog(Type(Guarantee, "80"),
            Type(Mortgage, "50") with { IsNegotiableInstrument = true }, Type(Guid.NewGuid(), "50") with { IsActive = false },
            Type(Guid.NewGuid(), "150")));
        Assert.Equal((ReadOutcome.Found, Guarantee, "80"), (types.Outcome,
            Assert.Single(types.Resource!.Items).CollateralTypeId, types.Resource.Items[0].RatePercent));
        var currencies = await run.CurrencyCandidatesAsync(Recorder, null);
        Assert.Equal((ReadOutcome.Found, Currency), (currencies.Outcome,
            Assert.Single(currencies.Resource!.Items).CurrencyId));
    }

    [Fact]
    public async Task Another_company_never_sees_or_changes_the_collateral()
    {
        await using var run = new Run();
        var recorded = (await run.RecordAsync(Recorder, Request())).Resource!;

        var foreign = await run.ChangeAsync(Recorder, Change(recorded.CollateralId, "1") with { Amount = "5" },
            company: CompanyB);
        AssertIssue(foreign, CommandOutcome.ValidationFailed, "collateralId", "collateral_not_found");
        Assert.Equal(ReadOutcome.NotFound,
            (await run.HistoryAsync(Reader, Account, new ReadOnlyPermission(), company: CompanyB)).Outcome);
        Assert.Empty((await run.GetAccountAsync(Reader, Account, new ReadOnlyPermission(), company: CompanyB))
            .Resource!.Held);
        Assert.Equal((1L, FinancePartyCollateralService.Effective),
            ((await run.RowAsync(recorded.Id)).Version, (await run.RowAsync(recorded.Id)).Status));
    }

    private static PartyCollateralRecordRequest Request() =>
        new(Party, Account, Guarantee, Currency, "1000.00", Today.AddDays(-10), null, "SENTETIK-BTM-1");

    private static PartyCollateralChangeRequest Change(Guid collateralId, string revision) =>
        new(collateralId, revision, Guarantee, Currency, "1000.00", Today.AddDays(-10), null, "SENTETIK-BTM-1");

    private static FinanceCollateralTypeCatalogItem Type(Guid id, string rate) =>
        new(id, "SENTETIK-TUR", "SENTETIK teminat türü", IsActive: true, rate, "7", IsNegotiableInstrument: false);

    private static FixedCatalog Catalog(params FinanceCollateralTypeCatalogItem[] items) =>
        new(new FinanceCollateralTypeCatalogAnswer(FinanceCollateralTypeCatalogState.Available, items));

    private static FinancePartyCollateralEnvelopes Envelopes() => new(
        new FinanceAuditEnvelope(ErpPermissions.FinanceCollateralsRecord, ["SENTETIK-FINANCE"], SourceChannels.Web));

    private static void AssertIssue(PartyCollateralCommandResult result, CommandOutcome outcome, string field, string code)
    {
        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Resource);
        var issue = Assert.Single(result.Issues);
        Assert.Equal((field, code), (issue.Field, issue.Code));
    }

    private static string Key() => $"SENTETIK-{Guid.NewGuid():N}";

    private static PartyCollateralRow Recorded(int status) => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, CollateralId = Guid.NewGuid(), Revision = 1,
        Direction = FinancePartyCollateralService.Received, SourceKind = FinancePartyCollateralService.ManualEntry,
        PartyId = Party, CompanyAccountId = Account, Kind = FinancePartyCollateralService.RecordKind,
        CollateralTypeId = Guarantee, CollateralTypeVersion = "7", CurrencyId = Currency, AmountText = "500",
        ValidFrom = Today, Status = status, Version = 1, RecordedBy = Recorder, RecordedAtUtc = At,
        SupersededAtUtc = status == FinancePartyCollateralService.Superseded ? At : null, UpdatedAtUtc = At,
    };

    private static void Supersede(PartyCollateralRow row)
    {
        row.Status = FinancePartyCollateralService.Superseded;
        row.SupersededAtUtc = At.AddMinutes(5);
        row.UpdatedAtUtc = At.AddMinutes(5);
        row.Version += 1;
    }

    private static FinanceDbContext Context()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new FinanceDbContext(options, company);
    }

    /// <summary>
    /// The real service over one in-memory store: every call is a fresh scope (context, company, user, audit collector
    /// and interceptor) like a request, on a fixed clock; seeds and checks read the store through the write guard.
    /// </summary>
    private sealed class Run : IAsyncDisposable
    {
        private readonly string databaseName = $"finance-collaterals-{Guid.NewGuid():N}";

        public FinanceDbContext Memory(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName).Options, companyContext);
        }

        public async Task<PartyCollateralRow> SeedAsync(PartyCollateralRow row)
        {
            await using var context = Memory(row.CompanyId);
            context.PartyCollaterals.Add(row);
            await context.SaveChangesAsync();
            return row;
        }

        public async Task SeedInstrumentAsync(CustomerInstrumentStatus status, string amount)
        {
            await using var context = Memory(CompanyA);
            context.CustomerInstruments.Add(new CustomerInstrumentRow
            {
                Id = Guid.NewGuid(), CompanyId = CompanyA, Version = 1, Kind = 1, Status = (int)status, PartyId = Party,
                CompanyAccountId = Account, SerialNumber = $"SENTETIK-{Guid.NewGuid():N}", Drawer = "SENTETIK keşideci",
                DueOn = Today.AddDays(30), ReceivedOn = Today, CurrencyId = Currency, AmountText = amount,
                ProfileId = "SENTETIK-profile", ProfileVersion = "1", StatusChangedOn = Today, ReceivedBy = Recorder,
                ReceivedAtUtc = At, UpdatedBy = Recorder, UpdatedAtUtc = At,
            });
            await context.SaveChangesAsync();
        }

        public Task<PartyCollateralCommandResult> RecordAsync(string actor, PartyCollateralRecordRequest request,
            string? key = null, IFinancePermissionPort? permissions = null, IFinanceCollateralTypeCatalogReader? catalog = null,
            ISettingsMoneyCandidateReader? currencies = null, IFinancePartyAccountPort? parties = null,
            string? correlation = null) =>
            CallAsync(actor, CompanyA, permissions, catalog, currencies, parties, correlation,
                (service, finance) => service.RecordAsync(finance, new CommandMetadata(key ?? Key()), request,
                    CancellationToken.None));

        public Task<PartyCollateralCommandResult> ChangeAsync(string actor, PartyCollateralChangeRequest request,
            Guid? company = null) =>
            CallAsync(actor, company ?? CompanyA, null, null, null, null, null,
                (service, finance) => service.ChangeAsync(finance, new CommandMetadata(Key()), request,
                    CancellationToken.None));

        public Task<PartyCollateralCommandResult> ReleaseAsync(string actor, PartyCollateralReleaseRequest request,
            IFinancePartyAccountPort? parties = null) =>
            CallAsync(actor, CompanyA, null, null, null, parties, null,
                (service, finance) => service.ReleaseAsync(finance, new CommandMetadata(Key()), request,
                    CancellationToken.None));

        public Task<ReadResult<PartyCollateralAccountView>> GetAccountAsync(string actor, Guid account,
            IFinancePermissionPort? permissions = null, IFinanceCollateralTypeCatalogReader? catalog = null,
            Guid? company = null) =>
            CallAsync(actor, company ?? CompanyA, permissions, catalog, null, null, null,
                (service, finance) => service.GetAccountAsync(finance, account, CancellationToken.None));

        public Task<ReadResult<PartyCollateralHistoryPage>> HistoryAsync(string actor, Guid account,
            IFinancePermissionPort? permissions = null, Guid? company = null) =>
            CallAsync(actor, company ?? CompanyA, permissions, null, null, null, null,
                (service, finance) => service.HistoryAsync(finance, account, CancellationToken.None));

        public Task<ReadResult<PartyCollateralTypeCandidates>> TypeCandidatesAsync(string actor,
            IFinancePermissionPort? permissions, IFinanceCollateralTypeCatalogReader? catalog = null) =>
            CallAsync(actor, CompanyA, permissions, catalog, null, null, null,
                (service, finance) => service.TypeCandidatesAsync(finance, CancellationToken.None));

        public Task<ReadResult<PartyCollateralCurrencyCandidates>> CurrencyCandidatesAsync(string actor,
            IFinancePermissionPort? permissions) =>
            CallAsync(actor, CompanyA, permissions, null, null, null, null,
                (service, finance) => service.CurrencyCandidatesAsync(finance, CancellationToken.None));

        public async Task<PartyCollateralRow> RowAsync(Guid id)
        {
            await using var context = Memory(CompanyA);
            return await context.PartyCollaterals.AsNoTracking().SingleAsync(row => row.Id == id);
        }

        public async Task<List<PartyCollateralRow>> AllRowsAsync()
        {
            await using var context = Memory(CompanyA);
            return await context.PartyCollaterals.AsNoTracking().ToListAsync();
        }

        public async Task<List<PartyCollateralCommandRow>> CommandsAsync()
        {
            await using var context = Memory(CompanyA);
            return await context.PartyCollateralCommands.AsNoTracking().ToListAsync();
        }

        public async Task<List<AuditEvent>> AuditsAsync(Guid id)
        {
            var entityId = id.ToString("D");
            await using var context = Memory(CompanyA);
            return await context.Set<AuditEvent>().AsNoTracking().Where(audit => audit.EntityId == entityId)
                .OrderBy(audit => audit.EntityVersionAfter).ToListAsync();
        }

        /// <summary>The revision's version, the company's stored commands and the revision's audit events.</summary>
        public async Task<(long Version, int Commands, int Audits)> SnapshotAsync(Guid id) =>
            ((await RowAsync(id)).Version, (await CommandsAsync()).Count, (await AuditsAsync(id)).Count);

        public async ValueTask DisposeAsync()
        {
            await using var context = Memory(CompanyA);
            await context.Database.EnsureDeletedAsync();
        }

        private async Task<T> CallAsync<T>(string actor, Guid companyId, IFinancePermissionPort? permissions,
            IFinanceCollateralTypeCatalogReader? catalog, ISettingsMoneyCandidateReader? currencies,
            IFinancePartyAccountPort? parties, string? correlation,
            Func<FinancePartyCollateralService, FinanceContext, Task<T>> call)
        {
            var company = new CompanyContext();
            company.Set(companyId);
            var collector = new AuditEventCollector();
            await using var context = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
            var service = new FinancePartyCollateralService(context, company, new FixedUser(actor, companyId),
                permissions ?? new RecordingPermission(PermissionDecision.Allowed),
                currencies ?? new Candidates([new MoneyCurrencyCandidateV1(Currency, "TRY", "SENTETIK Lira")]),
                parties ?? new FixedParty(), catalog ?? Catalog(Type(Guarantee, "100"), Type(Mortgage, "50")),
                collector, Envelopes(), new FixedClock());
            return await call(service, new FinanceContext(companyId, actor, correlation ?? "SENTETIK-correlation", null));
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => At;
    }

    private sealed class FixedCatalog(FinanceCollateralTypeCatalogAnswer answer) : IFinanceCollateralTypeCatalogReader
    {
        public Task<FinanceCollateralTypeCatalogAnswer> ListAsync(Guid companyId, DateOnly asOf,
            CancellationToken cancellationToken) =>
            Task.FromResult(answer);
    }

    /// <summary>Never reached: the refusal comes before the Settings catalog is asked.</summary>
    private sealed class NoCatalogRead : IFinanceCollateralTypeCatalogReader
    {
        public Task<FinanceCollateralTypeCatalogAnswer> ListAsync(Guid companyId, DateOnly asOf,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The collateral type catalog must not be asked before the refusal.");
    }

    private sealed class RecordingPermission(PermissionDecision decision) : IFinancePermissionPort
    {
        public List<FinanceAction> Actions { get; } = [];

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken)
        {
            Actions.Add(action);
            return Task.FromResult(decision);
        }
    }

    /// <summary>A caller allowed to read collaterals and nothing else.</summary>
    private sealed class ReadOnlyPermission : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(action == FinanceAction.ReadCollaterals ? PermissionDecision.Allowed : PermissionDecision.Denied);
    }

    /// <summary>A collateral reader who may also read customer instruments and their amounts.</summary>
    private sealed class InstrumentReaderPermission : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(action is FinanceAction.ReadCollaterals or FinanceAction.ReadCustomerInstruments
                or FinanceAction.ReadCustomerInstrumentAmount
                ? PermissionDecision.Allowed
                : PermissionDecision.Denied);
    }

    /// <summary>Allows <paramref name="revoked"/> for its first <paramref name="allowed"/> checks, then denies it.</summary>
    private sealed class RevokingPermission(FinanceAction revoked, int allowed) : IFinancePermissionPort
    {
        private int checks;

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(action != revoked || Interlocked.Increment(ref checks) <= allowed
                ? PermissionDecision.Allowed
                : PermissionDecision.Denied);
    }

    /// <summary>Counterparty's answer for the (party, company account) pair the request names.</summary>
    private sealed class FixedParty(ReadOutcome outcome = ReadOutcome.Found, Guid? party = null, bool accountActive = true,
        bool partyActive = true) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(outcome == ReadOutcome.Found
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found, new PartyAccountEligibility(party ?? partyId,
                    companyAccountId, context.CompanyId, accountActive, Label: null, IsPartyActive: partyActive))
                : new ReadResult<PartyAccountEligibility>(outcome, null));
    }

    /// <summary>Never reached: the refusal comes before Counterparty is asked.</summary>
    private sealed class NoPartyRead : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Counterparty must not be asked before the refusal.");
    }

    private sealed class Candidates(IReadOnlyList<MoneyCurrencyCandidateV1> candidates) : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(candidates);
    }

    private sealed class FailingCandidates(string code) : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
            CancellationToken cancellationToken = default) =>
            throw new SettingsPrerequisiteException(code);
    }

    /// <summary>Never reached by these tests: every refusal comes before Settings is asked.</summary>
    private sealed class NoCandidates : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Settings must not be asked before the refusal.");
    }

    private sealed class FixedUser(string userId, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid company) => AllowedCompanyIds.Contains(company);
    }
}
