using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// FNS-D121 (K3): the cari card's IBAN list. Permission fence (company-wide, re-measured after every await and right before
/// the save), Idempotency-Key replay before validation (same actor and payload; another answers IdempotencyConflict),
/// expected version on deactivation, one save of row + command record + audit event. IBANs are validated as TR mod-97;
/// the party must have an account in the context company. Nothing is deleted.
/// </summary>
internal sealed class CounterpartyPartyIbanService(
    CounterpartyDbContext dbContext,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IErpAuthorityRevalidationService authority,
    IAuditEventCollector audit,
    TimeProvider clock) : ICounterpartyPartyIbanService
{
    public const string AddCommand = "counterparty.party-iban.add.v1";
    public const string DeactivateCommand = "counterparty.party-iban.deactivate.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Fence(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);

    public async Task<CounterpartyPartyIbanListResultV1> ListAsync(Guid partyId, CancellationToken cancellationToken)
    {
        var fence = await FenceAsync(CounterpartyPartyIbanPermissionsV1.Read, null, cancellationToken);
        if (fence.Refusal is { } refusal) return new(refusal, null);
        if (partyId == Guid.Empty) return new(CounterpartyPartyIbanOutcomeV1.NotFound, null);
        try
        {
            var hasAccount = await dbContext.CompanyPartyAccounts.AsNoTracking()
                .AnyAsync(x => x.CompanyId == fence.Value!.CompanyId && x.PartyId == partyId, cancellationToken);
            var rows = hasAccount
                ? await dbContext.PartyIbans.AsNoTracking().Where(x => x.PartyId == partyId).ToListAsync(cancellationToken)
                : [];
            if ((await FenceAsync(CounterpartyPartyIbanPermissionsV1.Read, fence.Value, cancellationToken)).Refusal is { } late)
                return new(late, null);
            if (!hasAccount) return new(CounterpartyPartyIbanOutcomeV1.NotFound, null);
            return new(CounterpartyPartyIbanOutcomeV1.Succeeded, new CounterpartyPartyIbanListV1(fence.Value!.CompanyId, partyId,
                rows.OrderByDescending(x => x.IsActive).ThenBy(x => x.Iban, StringComparer.Ordinal).ThenBy(x => x.AddedAtUtc)
                    .Select(View).ToArray()));
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Data.Common.DbException)
        {
            return new(CounterpartyPartyIbanOutcomeV1.Unavailable, null);
        }
    }

    public async Task<CounterpartyPartyIbanCommandResultV1> AddAsync(Guid partyId, AddCounterpartyPartyIbanV1 request,
        string idempotencyKey, string correlationId, string? causationId, string sourceChannel, string? clientIpAddress,
        string? userAgent, CancellationToken cancellationToken)
    {
        var fence = await FenceAsync(CounterpartyPartyIbanPermissionsV1.Manage, null, cancellationToken);
        if (fence.Refusal is { } refusal) return Refused(refusal);
        if (request is null || partyId == Guid.Empty || request.IbanId == Guid.Empty || !Text(idempotencyKey, 128)
            || !Text(correlationId, 128))
            return Refused(CounterpartyPartyIbanOutcomeV1.ValidationFailed, CounterpartyPartyIbanCodesV1.RequestInvalid);
        var key = Hash(idempotencyKey);
        var payload = Hash(AddCommand + "\u001f" + partyId.ToString("D") + "\u001f" + JsonSerializer.Serialize(request, Json));
        if (await ReplayAsync(fence.Value!, key, payload, CounterpartyPartyIbanPermissionsV1.Manage, cancellationToken) is { } replay)
            return replay;

        var iban = CounterpartyIbanV1.Normalize(request.Iban);
        if (CounterpartyIbanV1.RefusalCode(iban) is { } ibanRefusal)
            return Refused(CounterpartyPartyIbanOutcomeV1.ValidationFailed, ibanRefusal);
        var bank = request.BankName?.Trim() ?? string.Empty;
        if (bank.Length == 0 || bank.Any(char.IsControl))
            return Refused(CounterpartyPartyIbanOutcomeV1.ValidationFailed, CounterpartyPartyIbanCodesV1.BankNameRequired);
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (bank.Length > CounterpartyPartyIbanLimitsV1.BankNameMaximumLength
            || description is { Length: > CounterpartyPartyIbanLimitsV1.DescriptionMaximumLength } || description?.Any(char.IsControl) == true)
            return Refused(CounterpartyPartyIbanOutcomeV1.ValidationFailed, CounterpartyPartyIbanCodesV1.TextTooLong);

        if (!await dbContext.CompanyPartyAccounts.AsNoTracking()
                .AnyAsync(x => x.CompanyId == fence.Value!.CompanyId && x.PartyId == partyId, cancellationToken))
            return Refused(CounterpartyPartyIbanOutcomeV1.NotFound, CounterpartyPartyIbanCodesV1.PartyNotFound);
        if (await dbContext.PartyIbans.AsNoTracking().AnyAsync(x => x.Id == request.IbanId, cancellationToken))
            return Refused(CounterpartyPartyIbanOutcomeV1.VersionConflict);
        var active = await dbContext.PartyIbans.AsNoTracking().Where(x => x.PartyId == partyId && x.IsActive)
            .Select(x => x.Iban).ToListAsync(cancellationToken);
        if (active.Contains(iban, StringComparer.Ordinal))
            return Refused(CounterpartyPartyIbanOutcomeV1.VersionConflict, CounterpartyPartyIbanCodesV1.AlreadyListed);
        if (active.Count >= CounterpartyPartyIbanLimitsV1.MaximumActivePerParty)
            return Refused(CounterpartyPartyIbanOutcomeV1.ValidationFailed, CounterpartyPartyIbanCodesV1.RequestInvalid);

        if ((await FenceAsync(CounterpartyPartyIbanPermissionsV1.Manage, fence.Value, cancellationToken)).Refusal is { } late)
            return Refused(late);
        var now = clock.GetUtcNow();
        var row = new CounterpartyPartyIbanRow
        {
            Id = request.IbanId, CompanyId = fence.Value!.CompanyId, PartyId = partyId, Version = 1, Iban = iban, BankName = bank,
            Description = description, IsActive = true, AddedBy = fence.Value.ActorId, AddedAtUtc = now,
        };
        dbContext.PartyIbans.Add(row);
        var result = new CounterpartyPartyIbanCommandResultV1(CounterpartyPartyIbanOutcomeV1.Succeeded, View(row), null);
        Stage(fence.Value, key, payload, AddCommand, result, null, row, "Add", "counterparty.party-iban.added.v1",
            "Cari IBAN listesine IBAN eklendi.", idempotencyKey, correlationId, causationId, sourceChannel, clientIpAddress,
            userAgent, now);
        return await SaveAsync(fence.Value, key, payload, result, cancellationToken);
    }

    public async Task<CounterpartyPartyIbanCommandResultV1> DeactivateAsync(Guid partyId, Guid ibanId,
        DeactivateCounterpartyPartyIbanV1 request, string idempotencyKey, string correlationId, string? causationId,
        string sourceChannel, string? clientIpAddress, string? userAgent, CancellationToken cancellationToken)
    {
        var fence = await FenceAsync(CounterpartyPartyIbanPermissionsV1.Manage, null, cancellationToken);
        if (fence.Refusal is { } refusal) return Refused(refusal);
        if (request is null || partyId == Guid.Empty || ibanId == Guid.Empty || !Text(idempotencyKey, 128)
            || !Text(correlationId, 128) || !TryVersion(request.ExpectedVersion, out var expected))
            return Refused(CounterpartyPartyIbanOutcomeV1.ValidationFailed, CounterpartyPartyIbanCodesV1.RequestInvalid);
        var key = Hash(idempotencyKey);
        var payload = Hash(DeactivateCommand + "\u001f" + partyId.ToString("D") + "\u001f" + ibanId.ToString("D") + "\u001f"
                           + JsonSerializer.Serialize(request, Json));
        if (await ReplayAsync(fence.Value!, key, payload, CounterpartyPartyIbanPermissionsV1.Manage, cancellationToken) is { } replay)
            return replay;
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0 || reason.Length > CounterpartyPartyIbanLimitsV1.ReasonMaximumLength)
            return Refused(CounterpartyPartyIbanOutcomeV1.ValidationFailed, CounterpartyPartyIbanCodesV1.ReasonRequired);

        var row = await dbContext.PartyIbans.SingleOrDefaultAsync(x => x.Id == ibanId && x.PartyId == partyId, cancellationToken);
        if (row is null) return Refused(CounterpartyPartyIbanOutcomeV1.NotFound, CounterpartyPartyIbanCodesV1.IbanNotFound);
        if (row.Version != expected || !row.IsActive) return Refused(CounterpartyPartyIbanOutcomeV1.VersionConflict);
        if ((await FenceAsync(CounterpartyPartyIbanPermissionsV1.Manage, fence.Value, cancellationToken)).Refusal is { } late)
            return Refused(late);
        var now = clock.GetUtcNow();
        var before = View(row);
        row.IsActive = false;
        row.Version = 2;
        row.DeactivatedBy = fence.Value!.ActorId;
        row.DeactivatedAtUtc = now;
        row.DeactivationReason = reason;
        var result = new CounterpartyPartyIbanCommandResultV1(CounterpartyPartyIbanOutcomeV1.Succeeded, View(row), null);
        Stage(fence.Value, key, payload, DeactivateCommand, result, before, row, "Deactivate",
            "counterparty.party-iban.deactivated.v1", reason, idempotencyKey, correlationId, causationId, sourceChannel,
            clientIpAddress, userAgent, now);
        return await SaveAsync(fence.Value, key, payload, result, cancellationToken);
    }

    private async Task<CounterpartyPartyIbanCommandResultV1> SaveAsync(Fence fence, string key, string payload,
        CounterpartyPartyIbanCommandResultV1 result, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException)
        {
            // A lost race (same key, same IBAN, same row): the winner's stored answer, else a conflict; nothing half-written.
            dbContext.ChangeTracker.Clear();
            return await ReplayAsync(fence, key, payload, CounterpartyPartyIbanPermissionsV1.Manage, cancellationToken)
                   ?? Refused(CounterpartyPartyIbanOutcomeV1.VersionConflict);
        }
    }

    private void Stage(Fence fence, string key, string payload, string commandName, CounterpartyPartyIbanCommandResultV1 result,
        CounterpartyPartyIbanViewV1? before, CounterpartyPartyIbanRow row, string operation, string eventType, string reason,
        string idempotencyKey, string correlationId, string? causationId, string sourceChannel, string? clientIpAddress,
        string? userAgent, DateTimeOffset now)
    {
        dbContext.PartyIbanCommands.Add(new CounterpartyPartyIbanCommandRow
        {
            CompanyId = fence.CompanyId, KeyHash = key, CommandName = commandName, ActorId = fence.ActorId, PayloadHash = payload,
            ResultJson = JsonSerializer.Serialize(result, Json), RecordedAtUtc = now,
        });
        audit.Enqueue(new AuditEventDraft(fence.CompanyId, eventType, "Counterparty.PartyIban", row.Id.ToString("D"), operation,
            before is null ? null : 1, row.Version, AuditActorTypes.User, fence.ActorId, CounterpartyPartyIbanPermissionsV1.Manage,
            fence.RoleCodes, before is null ? "{}" : JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(View(row), Json),
            reason, null, AuditAiInvolvement.None, null, sourceChannel, clientIpAddress, userAgent, correlationId, causationId,
            idempotencyKey));
    }

    private async Task<CounterpartyPartyIbanCommandResultV1?> ReplayAsync(Fence fence, string key, string payload,
        string permission, CancellationToken cancellationToken)
    {
        var prior = await dbContext.PartyIbanCommands.AsNoTracking().SingleOrDefaultAsync(x => x.KeyHash == key, cancellationToken);
        if (prior is null) return null;
        if ((await FenceAsync(permission, fence, cancellationToken)).Refusal is { } refusal) return Refused(refusal);
        if (prior.ActorId != fence.ActorId || prior.PayloadHash != payload)
            return Refused(CounterpartyPartyIbanOutcomeV1.IdempotencyConflict);
        try
        {
            return (JsonSerializer.Deserialize<CounterpartyPartyIbanCommandResultV1>(prior.ResultJson, Json)
                    ?? throw new JsonException()) with { Replayed = true };
        }
        catch (JsonException)
        {
            return Refused(CounterpartyPartyIbanOutcomeV1.Unavailable, CounterpartyPartyIbanCodesV1.Unavailable);
        }
    }

    /// <summary>Unregistered name → Unavailable (cannot tell); not granted or narrower than company-wide → Denied.</summary>
    private async Task<(CounterpartyPartyIbanOutcomeV1? Refusal, Fence? Value)> FenceAsync(string permission, Fence? captured,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = currentUser.UserId;
        var companyId = companyContext.CompanyId;
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(actor) || !companyContext.IsResolved
            || companyId == Guid.Empty || !currentUser.CanAccessCompany(companyId)
            || captured is not null && (captured.CompanyId != companyId || !string.Equals(captured.ActorId, actor, StringComparison.Ordinal)))
            return (CounterpartyPartyIbanOutcomeV1.Denied, null);
        if (!ErpPermissions.IsKnown(permission)) return (CounterpartyPartyIbanOutcomeV1.Unavailable, null);
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || companyContext.CompanyId != companyId
            || !string.Equals(currentUser.UserId, actor, StringComparison.Ordinal))
            return (CounterpartyPartyIbanOutcomeV1.Denied, null);
        return (null, new Fence(companyId, actor, [.. scope.RoleCodes.Order(StringComparer.Ordinal)]));
    }

    private static CounterpartyPartyIbanViewV1 View(CounterpartyPartyIbanRow row) => new(row.Id, row.CompanyId, row.PartyId,
        row.Iban, row.BankName, row.Description, row.IsActive, row.Version.ToString(CultureInfo.InvariantCulture), row.AddedBy,
        row.AddedAtUtc, row.DeactivatedBy, row.DeactivatedAtUtc, row.DeactivationReason);

    private static CounterpartyPartyIbanCommandResultV1 Refused(CounterpartyPartyIbanOutcomeV1 outcome, string? code = null) =>
        new(outcome, null, code);

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static bool Text(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && value == value.Trim();

    private static bool TryVersion(string? text, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(text) && text[0] != '0' && text.All(char.IsAsciiDigit)
               && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version >= 1;
    }
}

/// <summary>FNS-D121 owner read: active list rows of the server-resolved company for the asked IBANs. No authority, no writes.</summary>
internal sealed class CounterpartyPartyIbanSourceV1(CounterpartyDbContext dbContext, ICompanyContext companyContext)
    : ICounterpartyPartyIbanSourceV1
{
    private const int Chunk = 500;

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<Guid>>?> FindPartiesAsync(IReadOnlyCollection<string> ibans,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ibans is null || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty) return null;
        var companyId = companyContext.CompanyId;
        var asked = ibans.Select(CounterpartyIbanV1.Normalize).Where(CounterpartyIbanV1.IsValid)
            .Distinct(StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal);
        try
        {
            foreach (var chunk in asked.Chunk(Chunk))
            {
                var rows = await dbContext.PartyIbans.AsNoTracking()
                    .Where(x => x.CompanyId == companyId && x.IsActive && chunk.Contains(x.Iban))
                    .Select(x => new { x.Iban, x.PartyId }).ToListAsync(cancellationToken);
                foreach (var group in rows.GroupBy(x => x.Iban, StringComparer.Ordinal))
                    result[group.Key] = group.Select(x => x.PartyId).Distinct().ToArray();
            }

            return companyContext.CompanyId == companyId ? result : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Data.Common.DbException)
        {
            return null;
        }
    }
}
