using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// WP12 (DALGA_PLANI_v2 1.13; FNS-D13, FNS-D52; Finance K3). Bank statement import and exact match suggestions:
/// <list type="bullet">
/// <item><b>Reserve</b> creates a Reserved import for an APPROVED Finance bank account; the client then uploads the file to
/// the vault on this import (B-26 type list, scan and owner check stay the vault's).</item>
/// <item><b>Parse</b> reads that exact vault version once: CSV with the bank's active Settings column mapping (no fallback),
/// MT940 by the SWIFT layout, XLSX only through the known headers (NEEDS-SINAN WP12-S2). Any issue refuses the whole file. New lines are stored;
/// a line already stored for the account (same fingerprint and occurrence) is counted as a duplicate, and the same file
/// twice is refused.</item>
/// <item><b>Suggestions</b> are a READ (<see cref="FinanceBankStatementMatcher"/>; FNS-D113 exact amount, no tolerance).
/// A match becomes final only with <b>ConfirmMatch</b> (a person); <b>ReverseMatch</b> undoes it with a reversal record, never a
/// deletion. FNS-D119: the person who confirmed may reverse too (no second person); the reason is mandatory and audited. No movement and no journal is created. A missing movement is recorded by a person with Finance's existing
/// commands.</item>
/// </list>
/// Every command: permission fence before and after each await, Idempotency-Key replay (same actor and payload) before
/// validation, expected version (Parse), one serializable save of row + lines + command record + audit.
/// </summary>
public sealed class FinanceBankStatementImportServiceV1(FinanceDbContext database, ICompanyContext company,
    ICurrentUser user, IFinancePermissionPort permissions, ISettingsBankCsvMappingProviderV1 csvMappings,
    IFinanceBankStatementDocumentReadV1 documents, IFinancePartyIbanReadPortV1 partyIbans, IAuditEventCollector audit,
    FinanceAuditEnvelope auditEnvelope,
    TimeProvider clock, IAuditTransactionWriterV1? auditOwner = null, FinanceAuditEnvelope? matchAuditEnvelope = null)
    : IFinanceBankStatementImportServiceV1
{
    private readonly FinanceAuditEnvelope matchEnvelope = matchAuditEnvelope
        ?? auditEnvelope with { PermissionCode = FinanceBankStatementPermissionsV1.Match, RoleCodes = [] };
    public const string ReserveCommand = "finance.bank-statement-import.reserve.v1";
    public const string ParseCommand = "finance.bank-statement-import.parse.v1";
    public const string ConfirmMatchCommand = "finance.bank-statement-match.confirm.v1";
    public const string ReverseMatchCommand = "finance.bank-statement-match.reverse.v1";
    private const int ApprovedBankAccount = 2;
    private const int FingerprintChunk = 500;
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<FinanceBankStatementCommandResultV1> ReserveAsync(FinanceContext context, CommandMetadata metadata,
        ReserveFinanceBankStatementImportV1 request, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, FinanceAction.ImportBankStatement, cancellationToken) is { } refusal) return Refused(refusal);
        if (metadata is null || !Text(metadata.IdempotencyKey, 128) || request is null || request.ImportId == Guid.Empty
            || request.BankAccountId == Guid.Empty || !Enum.IsDefined(request.Format))
            return Refused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.RequestInvalid);
        return await FinanceTreasuryTransaction.ExecuteAsync(database,
            () => ReserveCoreAsync(context, metadata, request, cancellationToken),
            () => Task.FromResult(Refused(CommandOutcome.Unavailable, FinanceBankStatementCodesV1.Unavailable)),
            auditOwner, cancellationToken);
    }

    private async Task<FinanceBankStatementCommandResultV1> ReserveCoreAsync(FinanceContext context, CommandMetadata metadata,
        ReserveFinanceBankStatementImportV1 request, CancellationToken cancellationToken)
    {
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payload = PayloadHash(ReserveCommand, request.ImportId, request);
        if (await ReplayAsync(context, key, payload, cancellationToken) is { } replay) return replay;

        if (await database.BankStatementImports.AsNoTracking().AnyAsync(x => x.Id == request.ImportId, cancellationToken))
            return Refused(CommandOutcome.VersionConflict);
        if (request.Format == FinanceBankStatementFormatV1.Csv ? !BankCsvMappingRulesV1.IsBankKey(request.BankKey) : request.BankKey is not null)
            return Refused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.BankKeyInvalid);
        if (await ApprovedBankAccountAsync(request.BankAccountId, cancellationToken) is null)
            return Refused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.BankAccountUnavailable);
        if (request.Format == FinanceBankStatementFormatV1.Csv)
        {
            var mapping = await MappingAsync(context, request.BankKey!, cancellationToken);
            if (mapping.Refusal is { } mappingRefusal) return mappingRefusal;
        }

        if (await FenceAsync(context, FinanceAction.ImportBankStatement, cancellationToken) is { } late) return Refused(late);
        var now = clock.GetUtcNow();
        var row = new FinanceBankStatementImportRow
        {
            Id = request.ImportId, CompanyId = context.CompanyId, Version = 1, BankAccountId = request.BankAccountId,
            Format = (int)request.Format, BankKey = request.BankKey, State = FinanceBankStatementImportStates.Reserved,
            ReservedBy = context.ActorId, ReservedAtUtc = now,
        };
        database.BankStatementImports.Add(row);
        var result = new FinanceBankStatementCommandResultV1(CommandOutcome.Succeeded, View(row), null, []);
        Stage(context, metadata, key, payload, ReserveCommand, row, result, null, "Reserve",
            "finance.bank-statement-import.reserved.v1", "Banka ekstresi içe alma kaydı açıldı.", now);
        if (await FenceAsync(context, FinanceAction.ImportBankStatement, cancellationToken) is { } final) return Refused(final);
        await database.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<FinanceBankStatementCommandResultV1> ParseAsync(FinanceContext context, CommandMetadata metadata,
        Guid importId, ParseFinanceBankStatementImportV1 request, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, FinanceAction.ImportBankStatement, cancellationToken) is { } refusal) return Refused(refusal);
        if (metadata is null || !Text(metadata.IdempotencyKey, 128) || request is null || importId == Guid.Empty
            || !TryVersion(request.ExpectedVersion, out _) || request.DocumentId == Guid.Empty || request.DocumentVersion < 1)
            return Refused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.RequestInvalid);
        return await FinanceTreasuryTransaction.ExecuteAsync(database,
            () => ParseCoreAsync(context, metadata, importId, request, cancellationToken),
            () => Task.FromResult(Refused(CommandOutcome.Unavailable, FinanceBankStatementCodesV1.Unavailable)),
            auditOwner, cancellationToken);
    }

    private async Task<FinanceBankStatementCommandResultV1> ParseCoreAsync(FinanceContext context, CommandMetadata metadata,
        Guid importId, ParseFinanceBankStatementImportV1 request, CancellationToken cancellationToken)
    {
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payload = PayloadHash(ParseCommand, importId, request);
        if (await ReplayAsync(context, key, payload, cancellationToken) is { } replay) return replay;

        var row = await database.BankStatementImports.SingleOrDefaultAsync(x => x.Id == importId, cancellationToken);
        if (row is null) return Refused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.ImportNotFound);
        TryVersion(request.ExpectedVersion, out var expected);
        if (row.Version != expected || row.State != FinanceBankStatementImportStates.Reserved)
            return Refused(CommandOutcome.VersionConflict);
        var format = (FinanceBankStatementFormatV1)row.Format;
        var account = await ApprovedBankAccountAsync(row.BankAccountId, cancellationToken);
        if (account is null) return Refused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.BankAccountUnavailable);

        var answer = await documents.ReadAsync(context, importId, request.DocumentId, request.DocumentVersion, cancellationToken);
        if (await FenceAsync(context, FinanceAction.ImportBankStatement, cancellationToken) is { } documentFence)
            return Refused(documentFence);
        switch (answer.Outcome)
        {
            case FinanceBankStatementDocumentOutcomeV1.Denied:
                return Refused(CommandOutcome.Denied);
            case FinanceBankStatementDocumentOutcomeV1.NotFound:
                return Refused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.DocumentUnavailable);
            case not FinanceBankStatementDocumentOutcomeV1.Found:
                return Refused(CommandOutcome.Unavailable, FinanceBankStatementCodesV1.Unavailable);
        }

        var document = answer.Document;
        if (document is null || document.CompanyId != context.CompanyId || document.DocumentId != request.DocumentId
            || document.Version != request.DocumentVersion || document.Sha256 is not { Length: 64 } sha
            || !sha.All(Uri.IsHexDigit))
            return Refused(CommandOutcome.Unavailable, FinanceBankStatementCodesV1.Unavailable);
        if (document.Format != format)
            return Refused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.DocumentFormatMismatch);
        var shaUpper = sha.ToUpperInvariant();
        if (await database.BankStatementImports.AsNoTracking().AnyAsync(x => x.BankAccountId == row.BankAccountId
                && x.State == FinanceBankStatementImportStates.Parsed && x.DocumentSha256 == shaUpper, cancellationToken))
            return Refused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.FileAlreadyImported);

        SettingsBankCsvMappingV1? csv = null;
        FinanceStatementParseResult parsed;
        if (format == FinanceBankStatementFormatV1.Csv)
        {
            var mapping = await MappingAsync(context, row.BankKey ?? string.Empty, cancellationToken);
            if (mapping.Refusal is { } mappingRefusal) return mappingRefusal;
            csv = mapping.Mapping!;
            parsed = FinanceBankStatementParser.ParseCsv(document.Content.Span, csv);
        }
        else if (format == FinanceBankStatementFormatV1.Xlsx)
        {
            parsed = FinanceBankStatementParser.ParseXlsx(document.Content.Span);
        }
        else
        {
            parsed = FinanceBankStatementParser.ParseMt940(document.Content.Span);
            if (parsed.IsValid && account.Iban is { } ownIban
                && parsed.AccountIdentifiers.Any(identifier => FinanceIban.IsStructurallyValid(identifier)
                    && !string.Equals(identifier, ownIban, StringComparison.Ordinal)))
                return Refused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.AccountMismatch);
        }

        if (!parsed.IsValid)
            return new FinanceBankStatementCommandResultV1(CommandOutcome.ValidationFailed, null,
                FinanceBankStatementCodesV1.FileInvalid, parsed.Issues);
        if (parsed.Lines.Count == 0) return Refused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.NoLines);

        var keyed = new List<(FinanceParsedStatementLine Line, string Fingerprint, int Occurrence)>(parsed.Lines.Count);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in parsed.Lines)
        {
            var fingerprint = FinanceBankStatementLinePolicy.Fingerprint(line);
            var occurrence = occurrences.TryGetValue(fingerprint, out var seen) ? seen + 1 : 1;
            occurrences[fingerprint] = occurrence;
            keyed.Add((line, fingerprint, occurrence));
        }

        var stored = new HashSet<(string, int)>();
        foreach (var chunk in occurrences.Keys.Chunk(FingerprintChunk))
        {
            var existing = await database.BankStatementLines.AsNoTracking()
                .Where(x => x.BankAccountId == row.BankAccountId && chunk.Contains(x.Fingerprint))
                .Select(x => new { x.Fingerprint, x.Occurrence }).ToListAsync(cancellationToken);
            foreach (var item in existing) stored.Add((item.Fingerprint, item.Occurrence));
        }

        if (await FenceAsync(context, FinanceAction.ImportBankStatement, cancellationToken) is { } late) return Refused(late);
        var now = clock.GetUtcNow();
        var before = View(row);
        var added = 0;
        foreach (var (line, fingerprint, occurrence) in keyed)
        {
            if (stored.Contains((fingerprint, occurrence))) continue;
            database.BankStatementLines.Add(new FinanceBankStatementLineRow
            {
                Id = Guid.NewGuid(), CompanyId = context.CompanyId, ImportId = row.Id, BankAccountId = row.BankAccountId,
                LineNumber = line.LineNumber, Fingerprint = fingerprint, Occurrence = occurrence,
                TransactionOn = line.TransactionOn, ValueOn = line.ValueOn, Direction = (int)line.Direction,
                AmountText = line.Amount, BalanceText = line.Balance, CurrencyText = line.CurrencyText,
                Reference = line.Reference, Description = line.Description,
                CounterpartyIban = FinanceBankStatementLinePolicy.CounterpartyIban(line.Reference, line.Description, account.Iban),
            });
            added++;
        }

        row.State = FinanceBankStatementImportStates.Parsed;
        row.Version = checked(row.Version + 1);
        row.DocumentId = document.DocumentId;
        row.DocumentVersion = document.Version;
        row.DocumentSha256 = shaUpper;
        row.CsvMappingProfileId = csv?.Profile.ProfileId;
        row.CsvMappingProfileVersion = csv?.Profile.Version;
        row.LineCount = added;
        row.DuplicateLineCount = keyed.Count - added;
        row.FirstTransactionOn = parsed.Lines.Min(line => line.TransactionOn);
        row.LastTransactionOn = parsed.Lines.Max(line => line.TransactionOn);
        row.ParsedBy = context.ActorId;
        row.ParsedAtUtc = now;
        var result = new FinanceBankStatementCommandResultV1(CommandOutcome.Succeeded, View(row), null, []);
        Stage(context, metadata, key, payload, ParseCommand, row, result, before, "Parse",
            "finance.bank-statement-import.parsed.v1", "Banka ekstresi dosyası okundu; satırlar yalnız kontrol içindir.", now);
        if (await FenceAsync(context, FinanceAction.ImportBankStatement, cancellationToken) is { } final) return Refused(final);
        await database.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<FinanceBankStatementDetailResultV1> GetAuthorizedAsync(FinanceContext context, Guid importId,
        CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, FinanceAction.ReadBankStatements, cancellationToken) is { } refusal)
            return new(ReadRefusal(refusal), null);
        if (importId == Guid.Empty) return new(ReadOutcome.NotFound, null);
        try
        {
            var row = await database.BankStatementImports.AsNoTracking().SingleOrDefaultAsync(x => x.Id == importId, cancellationToken);
            if (row is null)
            {
                return await FenceAsync(context, FinanceAction.ReadBankStatements, cancellationToken) is { } gone
                    ? new(ReadRefusal(gone), null) : new(ReadOutcome.NotFound, null);
            }

            var lines = await database.BankStatementLines.AsNoTracking().Where(x => x.ImportId == importId)
                .OrderBy(x => x.LineNumber).ThenBy(x => x.Occurrence).ToListAsync(cancellationToken);
            var movements = row.FirstTransactionOn is { } from && row.LastTransactionOn is { } through
                ? await MovementsAsync(row.BankAccountId, from, through, cancellationToken)
                : [];
            var activeOnAccount = await database.BankStatementMatches.AsNoTracking()
                .Where(x => x.BankAccountId == row.BankAccountId && x.ReversalId == null).ToListAsync(cancellationToken);
            var lineIds = lines.Select(x => x.Id).ToHashSet();
            var elsewhere = activeOnAccount.Where(x => !lineIds.Contains(x.LineId))
                .Select(x => ((FinanceBankStatementCandidateKindV1)x.CandidateKind, x.RecordId)).ToHashSet();
            var ownMatches = activeOnAccount.Where(x => lineIds.Contains(x.LineId))
                .ToDictionary(x => x.LineId, x => MatchView(x, null));
            var parties = await PartyIbansAsync(context, lines.Select(x => x.CounterpartyIban), cancellationToken);
            if (await FenceAsync(context, FinanceAction.ReadBankStatements, cancellationToken) is { } late)
                return new(ReadRefusal(late), null);
            var suggestions = FinanceBankStatementMatcher.Suggest(
                lines.Select(x => new FinanceStatementMatchLine(x.Id, x.TransactionOn, Direction(x.Direction), x.AmountText,
                    x.CounterpartyIban)).ToArray(),
                movements.Where(x => !elsewhere.Contains((x.Kind, x.RecordId))).ToArray(), parties, ownMatches);
            var views = lines.Select(x => new FinanceBankStatementLineViewV1(x.Id, x.LineNumber, x.TransactionOn, x.ValueOn,
                Direction(x.Direction), x.AmountText, x.BalanceText, x.CurrencyText, x.Reference, x.Description,
                x.CounterpartyIban, suggestions[x.Id])).ToArray();
            var suggested = views.Count(x => x.Suggestion.Status == FinanceBankStatementMatchStatusV1.Suggested);
            return new(ReadOutcome.Found, new FinanceBankStatementImportDetailV1(View(row), views, suggested, views.Length - suggested));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException
                                              or DbUpdateException or System.Data.Common.DbException)
        {
            return new(ReadOutcome.Unavailable, null);
        }
    }

    public async Task<FinanceBankStatementListResultV1> ListAuthorizedAsync(FinanceContext context, Guid? bankAccountId,
        CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, FinanceAction.ReadBankStatements, cancellationToken) is { } refusal)
            return new(ReadRefusal(refusal), null);
        try
        {
            var query = database.BankStatementImports.AsNoTracking();
            if (bankAccountId is { } account) query = query.Where(x => x.BankAccountId == account);
            var rows = await query.OrderByDescending(x => x.ReservedAtUtc).ThenBy(x => x.Id)
                .Take(FinanceBankStatementLimitsV1.MaximumListedImports).ToListAsync(cancellationToken);
            if (await FenceAsync(context, FinanceAction.ReadBankStatements, cancellationToken) is { } late)
                return new(ReadRefusal(late), null);
            return new(ReadOutcome.Found, new FinanceBankStatementImportListV1(context.CompanyId, rows.Select(View).ToArray()));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException
                                              or System.Data.Common.DbException)
        {
            return new(ReadOutcome.Unavailable, null);
        }
    }

    public async Task<ReadOutcome> CheckDocumentOwnerAsync(FinanceContext context, Guid importId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Same(context)) return ReadOutcome.Denied;
        var read = await permissions.CheckAsync(context, FinanceAction.ReadBankStatements, null, cancellationToken);
        var import = read == PermissionDecision.Allowed
            ? read
            : await permissions.CheckAsync(context, FinanceAction.ImportBankStatement, null, cancellationToken);
        if (!Same(context)) return ReadOutcome.Denied;
        if (import != PermissionDecision.Allowed)
            return read == PermissionDecision.Unavailable && import == PermissionDecision.Unavailable
                ? ReadOutcome.Unavailable : ReadOutcome.Denied;
        if (importId == Guid.Empty) return ReadOutcome.NotFound;
        try
        {
            var exists = await database.BankStatementImports.AsNoTracking()
                .AnyAsync(x => x.Id == importId && x.CompanyId == context.CompanyId, cancellationToken);
            if (!Same(context)) return ReadOutcome.Denied;
            return exists ? ReadOutcome.Found : ReadOutcome.NotFound;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Data.Common.DbException)
        {
            return ReadOutcome.Unavailable;
        }
    }

    public async Task<FinanceBankStatementMatchCommandResultV1> ConfirmMatchAsync(FinanceContext context, CommandMetadata metadata,
        Guid importId, ConfirmFinanceBankStatementMatchV1 request, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, FinanceAction.MatchBankStatement, cancellationToken) is { } refusal) return MatchRefused(refusal);
        if (metadata is null || !Text(metadata.IdempotencyKey, 128) || request is null || importId == Guid.Empty
            || request.MatchId == Guid.Empty || request.LineId == Guid.Empty || request.RecordId == Guid.Empty
            || !Enum.IsDefined(request.Kind))
            return MatchRefused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.RequestInvalid);
        return await FinanceTreasuryTransaction.ExecuteAsync(database,
            () => ConfirmCoreAsync(context, metadata, importId, request, cancellationToken),
            () => Task.FromResult(MatchRefused(CommandOutcome.Unavailable, FinanceBankStatementCodesV1.Unavailable)),
            auditOwner, cancellationToken);
    }

    private async Task<FinanceBankStatementMatchCommandResultV1> ConfirmCoreAsync(FinanceContext context, CommandMetadata metadata,
        Guid importId, ConfirmFinanceBankStatementMatchV1 request, CancellationToken cancellationToken)
    {
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payload = PayloadHash(ConfirmMatchCommand, importId, request);
        if (await MatchReplayAsync(context, key, payload, cancellationToken) is { } replay) return replay;

        var import = await database.BankStatementImports.AsNoTracking().SingleOrDefaultAsync(x => x.Id == importId, cancellationToken);
        if (import is null || import.State != FinanceBankStatementImportStates.Parsed)
            return MatchRefused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.ImportNotFound);
        var line = await database.BankStatementLines.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.LineId && x.ImportId == importId, cancellationToken);
        if (line is null) return MatchRefused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.LineNotFound);
        if (await database.BankStatementMatches.AsNoTracking().AnyAsync(x => x.Id == request.MatchId, cancellationToken))
            return MatchRefused(CommandOutcome.VersionConflict);
        var kind = (int)request.Kind;
        if (await database.BankStatementMatches.AsNoTracking().AnyAsync(x => x.ReversalId == null
                && (x.LineId == line.Id || (x.CandidateKind == kind && x.RecordId == request.RecordId)), cancellationToken))
            return MatchRefused(CommandOutcome.VersionConflict, FinanceBankStatementCodesV1.AlreadyMatched);

        // FNS-D113: the record must be a recorded movement of the same bank account, same direction, exactly the same amount.
        // The date is the person's decision (a value-date difference is not a tolerance on the amount).
        var movement = (await MovementsAsync(line.BankAccountId, DateOnly.MinValue, DateOnly.MaxValue, request.RecordId,
                cancellationToken))
            .SingleOrDefault(x => x.Kind == request.Kind && x.RecordId == request.RecordId);
        if (movement is null || movement.Direction != Direction(line.Direction)
            || !FinanceExactDecimalText.Equal(movement.Amount, line.AmountText))
            return MatchRefused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.RecordNotEligible);
        var parties = await PartyIbansAsync(context, [line.CounterpartyIban], cancellationToken);
        var check = FinanceBankStatementMatcher.Check(line.CounterpartyIban, movement, parties);
        if (check == FinanceBankStatementCounterpartyCheckV1.Mismatch)
            return MatchRefused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.CounterpartyMismatch);

        if (await FenceAsync(context, FinanceAction.MatchBankStatement, cancellationToken) is { } late) return MatchRefused(late);
        var now = clock.GetUtcNow();
        var row = new FinanceBankStatementMatchRow
        {
            Id = request.MatchId, CompanyId = context.CompanyId, Version = 1, ImportId = importId, LineId = line.Id,
            BankAccountId = line.BankAccountId, CandidateKind = kind, RecordId = request.RecordId, CounterpartyCheck = (int)check,
            ConfirmedBy = context.ActorId, ConfirmedAtUtc = now,
        };
        database.BankStatementMatches.Add(row);
        var view = MatchView(row, null);
        var result = new FinanceBankStatementMatchCommandResultV1(CommandOutcome.Succeeded, view, null);
        StageMatch(context, metadata, key, payload, ConfirmMatchCommand, importId, row.Id, result, null, view, "Confirm",
            "finance.bank-statement-match.confirmed.v1", "Banka ekstresi satırı kullanıcı onayıyla kayda eşlendi.", now);
        if (await FenceAsync(context, FinanceAction.MatchBankStatement, cancellationToken) is { } final) return MatchRefused(final);
        await database.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<FinanceBankStatementMatchCommandResultV1> ReverseMatchAsync(FinanceContext context, CommandMetadata metadata,
        Guid matchId, ReverseFinanceBankStatementMatchV1 request, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, FinanceAction.MatchBankStatement, cancellationToken) is { } refusal) return MatchRefused(refusal);
        if (metadata is null || !Text(metadata.IdempotencyKey, 128) || request is null || matchId == Guid.Empty
            || request.ReversalId == Guid.Empty || !TryVersion(request.ExpectedVersion, out _))
            return MatchRefused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.RequestInvalid);
        if (!Text(request.Reason, FinanceBankStatementLimitsV1.ReasonMaximumLength))
            return MatchRefused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.ReasonRequired);
        return await FinanceTreasuryTransaction.ExecuteAsync(database,
            () => ReverseCoreAsync(context, metadata, matchId, request, cancellationToken),
            () => Task.FromResult(MatchRefused(CommandOutcome.Unavailable, FinanceBankStatementCodesV1.Unavailable)),
            auditOwner, cancellationToken);
    }

    private async Task<FinanceBankStatementMatchCommandResultV1> ReverseCoreAsync(FinanceContext context, CommandMetadata metadata,
        Guid matchId, ReverseFinanceBankStatementMatchV1 request, CancellationToken cancellationToken)
    {
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payload = PayloadHash(ReverseMatchCommand, matchId, request);
        if (await MatchReplayAsync(context, key, payload, cancellationToken) is { } replay) return replay;

        var row = await database.BankStatementMatches.SingleOrDefaultAsync(x => x.Id == matchId, cancellationToken);
        if (row is null) return MatchRefused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.MatchNotFound);
        TryVersion(request.ExpectedVersion, out var expected);
        if (row.Version != expected || row.ReversalId is not null
            || await database.BankStatementMatchReversals.AsNoTracking().AnyAsync(x => x.Id == request.ReversalId, cancellationToken))
            return MatchRefused(CommandOutcome.VersionConflict);

        if (await FenceAsync(context, FinanceAction.MatchBankStatement, cancellationToken) is { } late) return MatchRefused(late);
        var now = clock.GetUtcNow();
        var before = MatchView(row, null);
        var reversal = new FinanceBankStatementMatchReversalRow
        {
            Id = request.ReversalId, CompanyId = context.CompanyId, MatchId = row.Id, Reason = request.Reason,
            ReversedBy = context.ActorId, ReversedAtUtc = now,
        };
        database.BankStatementMatchReversals.Add(reversal);
        row.ReversalId = reversal.Id;
        row.Version = checked(row.Version + 1);
        var view = MatchView(row, reversal);
        var result = new FinanceBankStatementMatchCommandResultV1(CommandOutcome.Succeeded, view, null);
        StageMatch(context, metadata, key, payload, ReverseMatchCommand, row.ImportId, row.Id, result, before, view, "Reverse",
            "finance.bank-statement-match.reversed.v1", request.Reason, now);
        if (await FenceAsync(context, FinanceAction.MatchBankStatement, cancellationToken) is { } final) return MatchRefused(final);
        await database.SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <summary>FNS-D114: the cari IBAN lists for the lines' IBANs; Off when no line has one or no source answers.</summary>
    private async Task<FinanceStatementPartyIbans> PartyIbansAsync(FinanceContext context, IEnumerable<string?> ibans,
        CancellationToken cancellationToken)
    {
        var asked = ibans.Where(iban => iban is not null).Select(iban => iban!).Distinct(StringComparer.Ordinal).ToArray();
        if (asked.Length == 0) return FinanceStatementPartyIbans.Off;
        var answer = await partyIbans.ReadAsync(context, asked, cancellationToken);
        if (!Same(context) || answer is null || answer.Outcome != FinancePartyIbanOutcomeV1.Available || answer.PartiesByIban is null)
            return FinanceStatementPartyIbans.Off;
        var listed = answer.PartiesByIban.Where(pair => asked.Contains(pair.Key, StringComparer.Ordinal))
            .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<Guid>)pair.Value.Where(id => id != Guid.Empty).Distinct().ToArray(),
                StringComparer.Ordinal);
        return new FinanceStatementPartyIbans(true, listed);
    }

    private void StageMatch(FinanceContext context, CommandMetadata metadata, string key, string payload, string commandName,
        Guid importId, Guid matchId, FinanceBankStatementMatchCommandResultV1 result, FinanceBankStatementMatchViewV1? before,
        FinanceBankStatementMatchViewV1 after, string operation, string eventType, string reason, DateTimeOffset now)
    {
        database.BankStatementCommands.Add(new FinanceBankStatementCommandRow
        {
            CompanyId = context.CompanyId, KeyHash = key, CommandName = commandName, ActorId = context.ActorId,
            PayloadHash = payload, ImportId = importId, ResultJson = JsonSerializer.Serialize(result, Json), RecordedAtUtc = now,
        });
        audit.Enqueue(new AuditEventDraft(context.CompanyId, eventType, "Finance.BankStatementMatch", matchId.ToString("D"),
            operation, before is null ? null : 1, before is null ? 1 : 2, AuditActorTypes.User, context.ActorId,
            matchEnvelope.PermissionCode, matchEnvelope.RoleCodes,
            before is null ? "{}" : JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(after, Json), reason,
            null, AuditAiInvolvement.None, null, matchEnvelope.SourceChannel, matchEnvelope.ClientIpAddress,
            matchEnvelope.UserAgent, context.CorrelationId, context.CausationId, metadata.IdempotencyKey));
    }

    private static FinanceBankStatementMatchViewV1 MatchView(FinanceBankStatementMatchRow row,
        FinanceBankStatementMatchReversalRow? reversal) => new(
        row.Id, row.CompanyId, row.ImportId, row.LineId, (FinanceBankStatementCandidateKindV1)row.CandidateKind, row.RecordId,
        row.Version.ToString(CultureInfo.InvariantCulture),
        row.ReversalId is null ? FinanceBankStatementMatchStateV1.Confirmed : FinanceBankStatementMatchStateV1.Reversed,
        (FinanceBankStatementCounterpartyCheckV1)row.CounterpartyCheck, row.ConfirmedBy, row.ConfirmedAtUtc, row.ReversalId,
        reversal?.ReversedBy, reversal?.ReversedAtUtc, reversal?.Reason);

    private static FinanceBankStatementMatchCommandResultV1 MatchRefused(CommandOutcome outcome, string? code = null) =>
        new(outcome, null, code);

    /// <summary>
    /// The recorded movements of the bank account within the statement's dates, each read once: a receipt at its bank net
    /// (POS), a party payment, a recorded expense payment and every transfer leg. Reversals, opening balances and credit
    /// legs are not candidates (they are not what a person records from a statement line), so such a line stays Manual.
    /// </summary>
    private async Task<IReadOnlyList<FinanceStatementMovement>> MovementsAsync(Guid bankAccountId, DateOnly from,
        DateOnly through, CancellationToken cancellationToken) =>
        await MovementsAsync(bankAccountId, from, through, null, cancellationToken);

    private async Task<IReadOnlyList<FinanceStatementMovement>> MovementsAsync(Guid bankAccountId, DateOnly from,
        DateOnly through, Guid? recordId, CancellationToken cancellationToken)
    {
        var result = new List<FinanceStatementMovement>();
        var receipts = await database.MoneyEvents.AsNoTracking()
            .Where(x => x.TreasuryAccountId == bankAccountId && x.OccurredOn >= from && x.OccurredOn <= through
                && (recordId == null || x.SourceId == recordId)
                && (x.Kind == MoneyEventKinds.ManualBankReceiptRecorded || x.Kind == MoneyEventKinds.CustomerInstrumentCollected))
            .Select(x => new { x.SourceId, x.PartyId, x.OccurredOn, x.AmountText, x.PosBankNetAmountText }).ToListAsync(cancellationToken);
        result.AddRange(receipts.Select(x => new FinanceStatementMovement(FinanceBankStatementCandidateKindV1.Receipt, x.SourceId,
            x.OccurredOn, FinanceBankStatementDirectionV1.Inflow, x.PosBankNetAmountText ?? x.AmountText, x.PartyId, null)));

        var payments = await database.OutgoingMoneyEvents.AsNoTracking()
            .Where(x => x.TreasuryAccountId == bankAccountId && x.OccurredOn >= from && x.OccurredOn <= through
                && (recordId == null || x.PaymentId == recordId))
            .Select(x => new { x.PaymentId, x.PartyId, x.OccurredOn, x.AmountText }).ToListAsync(cancellationToken);
        result.AddRange(payments.Select(x => new FinanceStatementMovement(FinanceBankStatementCandidateKindV1.Payment, x.PaymentId,
            x.OccurredOn, FinanceBankStatementDirectionV1.Outflow, x.AmountText, x.PartyId, null)));

        var expenses = await database.ExpensePayments.AsNoTracking()
            .Where(x => x.SourceKind == ExpensePaymentSourceKinds.Bank && x.TreasuryAccountId == bankAccountId
                && x.Status == ExpensePaymentStatuses.Recorded && x.OccurredOn >= from && x.OccurredOn <= through
                && (recordId == null || x.Id == recordId))
            .Select(x => new { x.Id, x.OccurredOn, x.AmountText }).ToListAsync(cancellationToken);
        result.AddRange(expenses.Select(x => new FinanceStatementMovement(FinanceBankStatementCandidateKindV1.ExpensePayment, x.Id,
            x.OccurredOn, FinanceBankStatementDirectionV1.Outflow, x.AmountText, null, null)));

        var transfers = await database.CashTransfers.AsNoTracking()
            .Where(x => x.OccurredOn >= from && x.OccurredOn <= through && (recordId == null || x.Id == recordId)
                && ((x.SourceKind == CashTransferAccountKinds.Bank && x.SourceAccountId == bankAccountId)
                    || (x.TargetKind == CashTransferAccountKinds.Bank && x.TargetAccountId == bankAccountId)))
            .Select(x => new { x.Id, x.OccurredOn, x.SourceKind, x.SourceAccountId, x.SourceAmountText,
                x.TargetKind, x.TargetAccountId, x.TargetAmountText })
            .ToListAsync(cancellationToken);
        var otherBankIds = transfers.SelectMany(x => new[]
            {
                x.SourceKind == CashTransferAccountKinds.Bank && x.SourceAccountId != bankAccountId ? x.SourceAccountId : Guid.Empty,
                x.TargetKind == CashTransferAccountKinds.Bank && x.TargetAccountId != bankAccountId ? x.TargetAccountId : Guid.Empty,
            }).Where(id => id != Guid.Empty).Distinct().ToArray();
        var approvedIbans = otherBankIds.Length == 0
            ? new Dictionary<Guid, string?>()
            : await database.BankAccounts.AsNoTracking()
                .Where(x => otherBankIds.Contains(x.Id) && x.Status == ApprovedBankAccount)
                .ToDictionaryAsync(x => x.Id, x => x.Iban, cancellationToken);
        foreach (var transfer in transfers)
        {
            if (transfer.SourceKind == CashTransferAccountKinds.Bank && transfer.SourceAccountId == bankAccountId)
            {
                var iban = Counterparty(transfer.TargetKind, transfer.TargetAccountId, approvedIbans);
                result.Add(new FinanceStatementMovement(FinanceBankStatementCandidateKindV1.Transfer, transfer.Id, transfer.OccurredOn,
                    FinanceBankStatementDirectionV1.Outflow, transfer.SourceAmountText, null, iban));
            }

            if (transfer.TargetKind == CashTransferAccountKinds.Bank && transfer.TargetAccountId == bankAccountId)
            {
                var iban = Counterparty(transfer.SourceKind, transfer.SourceAccountId, approvedIbans);
                result.Add(new FinanceStatementMovement(FinanceBankStatementCandidateKindV1.Transfer, transfer.Id, transfer.OccurredOn,
                    FinanceBankStatementDirectionV1.Inflow, transfer.TargetAmountText, null, iban));
            }
        }

        return result;

        static string? Counterparty(int kind, Guid accountId, IReadOnlyDictionary<Guid, string?> ibans) =>
            kind == CashTransferAccountKinds.Bank && ibans.TryGetValue(accountId, out var iban) && iban is not null
                && FinanceIban.IsStructurallyValid(iban) ? iban : null;
    }

    private async Task<BankAccountRow?> ApprovedBankAccountAsync(Guid bankAccountId, CancellationToken cancellationToken) =>
        await database.BankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == bankAccountId && x.Status == ApprovedBankAccount, cancellationToken);

    private async Task<(SettingsBankCsvMappingV1? Mapping, FinanceBankStatementCommandResultV1? Refusal)> MappingAsync(
        FinanceContext context, string bankKey, CancellationToken cancellationToken)
    {
        try
        {
            var mapping = await csvMappings.GetBankCsvMappingAsync(bankKey, cancellationToken);
            if (await FenceAsync(context, FinanceAction.ImportBankStatement, cancellationToken) is { } fence)
                return (null, Refused(fence));
            return mapping.CompanyId == context.CompanyId && string.Equals(mapping.BankKey, bankKey, StringComparison.Ordinal)
                ? (mapping, null)
                : (null, Refused(CommandOutcome.Unavailable, FinanceBankStatementCodesV1.Unavailable));
        }
        catch (SettingsPrerequisiteException exception)
        {
            return (null, exception.Code switch
            {
                SettingsPrerequisiteCodesV1.BankCsvMappingNotFound =>
                    Refused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.CsvMappingNotFound),
                SettingsPrerequisiteCodesV1.BankCsvMappingInactive =>
                    Refused(CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.CsvMappingInactive),
                "settings_access_denied" => Refused(CommandOutcome.Denied),
                _ => Refused(CommandOutcome.Unavailable, FinanceBankStatementCodesV1.Unavailable),
            });
        }
    }

    private async Task<FinanceBankStatementCommandResultV1?> ReplayAsync(FinanceContext context, string key, string payload,
        CancellationToken cancellationToken)
    {
        var (refusal, json) = await PriorAsync(context, FinanceAction.ImportBankStatement, key, payload, cancellationToken);
        if (refusal is { } outcome) return Refused(outcome, outcome == CommandOutcome.Unavailable ? FinanceBankStatementCodesV1.Unavailable : null);
        if (json is null) return null;
        try
        {
            return (JsonSerializer.Deserialize<FinanceBankStatementCommandResultV1>(json, Json)
                    ?? throw new JsonException()) with { Replayed = true };
        }
        catch (JsonException)
        {
            return Refused(CommandOutcome.Unavailable, FinanceBankStatementCodesV1.Unavailable);
        }
    }

    private async Task<FinanceBankStatementMatchCommandResultV1?> MatchReplayAsync(FinanceContext context, string key,
        string payload, CancellationToken cancellationToken)
    {
        var (refusal, json) = await PriorAsync(context, FinanceAction.MatchBankStatement, key, payload, cancellationToken);
        if (refusal is { } outcome)
            return MatchRefused(outcome, outcome == CommandOutcome.Unavailable ? FinanceBankStatementCodesV1.Unavailable : null);
        if (json is null) return null;
        try
        {
            return (JsonSerializer.Deserialize<FinanceBankStatementMatchCommandResultV1>(json, Json)
                    ?? throw new JsonException()) with { Replayed = true };
        }
        catch (JsonException)
        {
            return MatchRefused(CommandOutcome.Unavailable, FinanceBankStatementCodesV1.Unavailable);
        }
    }

    /// <summary>The stored answer of the same key: a refusal (fence lost, other actor/payload) or the result JSON; both null when new.</summary>
    private async Task<(CommandOutcome? Refusal, string? Json)> PriorAsync(FinanceContext context, FinanceAction action, string key,
        string payload, CancellationToken cancellationToken)
    {
        var prior = await database.BankStatementCommands.AsNoTracking().SingleOrDefaultAsync(x => x.KeyHash == key, cancellationToken);
        if (prior is null) return (null, null);
        if (await FenceAsync(context, action, cancellationToken) is { } fence) return (fence, null);
        if (prior.ActorId != context.ActorId || prior.PayloadHash != payload) return (CommandOutcome.IdempotencyConflict, null);
        return (null, prior.ResultJson);
    }

    private void Stage(FinanceContext context, CommandMetadata metadata, string key, string payload, string commandName,
        FinanceBankStatementImportRow row, FinanceBankStatementCommandResultV1 result, FinanceBankStatementImportViewV1? before,
        string operation, string eventType, string reason, DateTimeOffset now)
    {
        database.BankStatementCommands.Add(new FinanceBankStatementCommandRow
        {
            CompanyId = context.CompanyId, KeyHash = key, CommandName = commandName, ActorId = context.ActorId,
            PayloadHash = payload, ImportId = row.Id, ResultJson = JsonSerializer.Serialize(result, Json), RecordedAtUtc = now,
        });
        audit.Enqueue(new AuditEventDraft(context.CompanyId, eventType, "Finance.BankStatementImport", row.Id.ToString("D"),
            operation, before is null ? null : row.Version - 1, row.Version, AuditActorTypes.User, context.ActorId,
            auditEnvelope.PermissionCode, auditEnvelope.RoleCodes,
            before is null ? "{}" : JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(View(row), Json), reason,
            null, AuditAiInvolvement.None, null, auditEnvelope.SourceChannel, auditEnvelope.ClientIpAddress,
            auditEnvelope.UserAgent, context.CorrelationId, context.CausationId, metadata.IdempotencyKey));
    }

    private async Task<CommandOutcome?> FenceAsync(FinanceContext context, FinanceAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Same(context)) return CommandOutcome.Denied;
        var permission = await permissions.CheckAsync(context, action, null, cancellationToken);
        if (!Same(context)) return CommandOutcome.Denied;
        return permission switch
        {
            PermissionDecision.Allowed => null,
            PermissionDecision.Denied => CommandOutcome.Denied,
            _ => CommandOutcome.Unavailable,
        };
    }

    private bool Same(FinanceContext context) => context is not null && company.IsResolved && company.CompanyId == context.CompanyId
        && context.CompanyId != Guid.Empty && user.IsAuthenticated && user.UserId == context.ActorId
        && user.CanAccessCompany(context.CompanyId) && Text(context.ActorId, 128) && Text(context.CorrelationId, 128)
        && (context.CausationId is null || Text(context.CausationId, 128));

    private static string PayloadHash<T>(string commandName, Guid importId, T request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(commandName + "\u001f" + importId.ToString("D") + "\u001f"
            + JsonSerializer.Serialize(request, Json))));

    private static bool TryVersion(string? text, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit) && text[0] != '0'
               && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version >= 1;
    }

    private static bool Text(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && value == value.Trim();

    private static FinanceBankStatementDirectionV1 Direction(int value) => value switch
    {
        1 => FinanceBankStatementDirectionV1.Inflow,
        2 => FinanceBankStatementDirectionV1.Outflow,
        _ => throw new InvalidOperationException("Stored statement direction is invalid."),
    };

    private static ReadOutcome ReadRefusal(CommandOutcome refusal) =>
        refusal == CommandOutcome.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable;

    private static FinanceBankStatementCommandResultV1 Refused(CommandOutcome outcome, string? code = null) =>
        new(outcome, null, code, []);

    private static FinanceBankStatementImportViewV1 View(FinanceBankStatementImportRow row) => new(
        row.Id, row.CompanyId, row.Version.ToString(CultureInfo.InvariantCulture), row.BankAccountId,
        row.Format switch
        {
            1 => FinanceBankStatementFormatV1.Csv,
            2 => FinanceBankStatementFormatV1.Mt940,
            3 => FinanceBankStatementFormatV1.Xlsx,
            _ => throw new InvalidOperationException("Stored statement format is invalid."),
        },
        row.BankKey,
        row.State switch
        {
            FinanceBankStatementImportStates.Reserved => FinanceBankStatementImportStateV1.Reserved,
            FinanceBankStatementImportStates.Parsed => FinanceBankStatementImportStateV1.Parsed,
            _ => throw new InvalidOperationException("Stored statement state is invalid."),
        },
        row.DocumentId, row.DocumentVersion, row.DocumentSha256, row.CsvMappingProfileId, row.CsvMappingProfileVersion,
        row.LineCount, row.DuplicateLineCount, row.FirstTransactionOn, row.LastTransactionOn, row.ReservedBy,
        row.ReservedAtUtc, row.ParsedBy, row.ParsedAtUtc);
}
