using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed class CounterpartyService(
    CounterpartyDbContext dbContext,
    PartyCodeAllocator partyCodeAllocator,
    ICompanyContext companyContext,
    IErpAuthorizationService authorizationService,
    IAuditEventCollector auditEventCollector)
    : ICounterpartyService
{
    private const int MaximumPageSize = 100;
    private const int CommandExecutionSchemaVersion = 1;
    private const string CreatePartyCommandName = "counterparty.party.create.v1";

    public async Task<PartyPage> GetPartiesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePage(page, pageSize);
        await RequireCompanyWideScopeAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);

        var accounts = dbContext.CompanyPartyAccounts.AsNoTracking();
        var totalCount = await accounts.CountAsync(cancellationToken);
        var rows = await accounts
            .Join(
                dbContext.Parties.AsNoTracking(),
                account => account.PartyId,
                party => party.Id,
                (account, party) => new { Account = account, Party = party })
            .OrderBy(x => x.Account.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PartyPage(
            rows.Select(x => ToCoreView(x.Party, x.Account)).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<PartyCoreView?> GetPartyAsync(
        Guid partyId,
        CancellationToken cancellationToken)
    {
        if (partyId == Guid.Empty)
        {
            return null;
        }

        await RequireCompanyWideScopeAsync(ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        var row = await dbContext.CompanyPartyAccounts
            .AsNoTracking()
            .Where(account => account.PartyId == partyId)
            .Join(
                dbContext.Parties.AsNoTracking(),
                account => account.PartyId,
                party => party.Id,
                (account, party) => new { Account = account, Party = party })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : ToCoreView(row.Party, row.Account);
    }

    public async Task<CreatePartyExecutionResult> CreatePartyAsync(
        CreatePartyCommand command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateMetadata(metadata);
        var scope = await RequireCompanyWideScopeAsync(
            ErpPermissions.CounterpartyPartiesCreate,
            cancellationToken);
        var normalized = Normalize(command);
        var idempotencyKeyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new CreatePartyFingerprint(
            normalized.Kind,
            normalized.LegalName,
            normalized.TradeName,
            (int)normalized.InitialRoles,
            normalized.ExternalIntegrationCode));

        var existing = await FindCommandExecutionAsync(idempotencyKeyHash, cancellationToken);
        if (existing is not null)
        {
            return ResolveExistingExecution(existing, requestHash, metadata);
        }

        var code = await partyCodeAllocator.AllocateAsync(cancellationToken);
        var partyId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var party = new Party(
            partyId,
            code,
            normalized.Kind,
            normalized.LegalName,
            normalized.TradeName);
        var account = new CompanyPartyAccount(
            accountId,
            metadata.CompanyId,
            partyId,
            normalized.InitialRoles,
            normalized.ExternalIntegrationCode);
        dbContext.Parties.Add(party);
        dbContext.CompanyPartyAccounts.Add(account);

        var response = new CreatePartyResponse(
            party.Id,
            account.Id,
            party.Code.Value,
            party.Kind,
            party.LegalName,
            party.TradeName,
            account.Roles,
            account.ExternalIntegrationCode,
            account.IsActive,
            account.Version);
        var responseJson = JsonSerializer.Serialize(response);

        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            "Counterparty.PartyCreated",
            "Counterparty.Party",
            party.Id.ToString(),
            "Create",
            null,
            party.Version,
            AuditActorTypes.User,
            metadata.ActorId,
            ErpPermissions.CounterpartyPartiesCreate,
            scope.RoleCodes,
            "{}",
            JsonSerializer.Serialize(new
            {
                Party = new
                {
                    party.Id,
                    Code = party.Code.Value,
                    party.Kind,
                    party.LegalName,
                    party.TradeName,
                    party.IsActive,
                    party.Version,
                },
                CompanyPartyAccount = new
                {
                    account.Id,
                    account.CompanyId,
                    account.Roles,
                    account.ExternalIntegrationCode,
                    account.IsActive,
                    account.Version,
                },
            }),
            "Yeni cari kartı oluşturma.",
            null,
            AuditAiInvolvement.None,
            null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));

        dbContext.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(
            Guid.NewGuid(),
            metadata.CompanyId,
            CreatePartyCommandName,
            idempotencyKeyHash,
            requestHash,
            metadata.ActorId,
            CreatePartyResult.Succeeded.ToString(),
            201,
            responseJson,
            metadata.CorrelationId,
            DateTimeOffset.UtcNow,
            CommandExecutionSchemaVersion)));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CreatePartyExecutionResult(
                CreatePartyResult.Succeeded,
                response,
                false,
                metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var winner = await FindCommandExecutionAsync(idempotencyKeyHash, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return ResolveExistingExecution(winner, requestHash, metadata);
        }
    }

    private async Task<PermissionScope> RequireCompanyWideScopeAsync(
        string permission,
        CancellationToken cancellationToken)
    {
        if (!companyContext.IsResolved)
        {
            throw new UnauthorizedAccessException("Aktif şirket bağlamı zorunludur.");
        }

        var scope = await authorizationService.GetPermissionScopeAsync(
            permission,
            companyContext.CompanyId,
            cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide)
        {
            throw new UnauthorizedAccessException("Cari işlemi için şirket kapsamlı yetki zorunludur.");
        }

        return scope;
    }

    private Task<CommandExecution?> FindCommandExecutionAsync(
        string idempotencyKeyHash,
        CancellationToken cancellationToken)
    {
        return dbContext.CommandExecutions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                execution =>
                    execution.CommandName == CreatePartyCommandName
                    && execution.IdempotencyKeyHash == idempotencyKeyHash,
                cancellationToken);
    }

    private static CreatePartyExecutionResult ResolveExistingExecution(
        CommandExecution execution,
        string requestHash,
        CounterpartyCommandMetadata metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new CreatePartyExecutionResult(
                CreatePartyResult.IdempotencyConflict,
                null,
                false,
                metadata.CorrelationId);
        }

        var response = JsonSerializer.Deserialize<CreatePartyResponse>(execution.ResponseJson);
        if (response is null
            || execution.HttpStatusCode != 201
            || !string.Equals(
                execution.Outcome,
                CreatePartyResult.Succeeded.ToString(),
                StringComparison.Ordinal)
            || response.PartyId == Guid.Empty
            || response.CompanyPartyAccountId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Kalıcı cari idempotency sonucu command sözleşmesiyle uyumsuz.");
        }

        return new CreatePartyExecutionResult(
            CreatePartyResult.Succeeded,
            response,
            true,
            execution.OriginalCorrelationId);
    }

    private void ValidateMetadata(CounterpartyCommandMetadata metadata)
    {
        if (!companyContext.IsResolved
            || metadata.CompanyId == Guid.Empty
            || metadata.CompanyId != companyContext.CompanyId
            || string.IsNullOrWhiteSpace(metadata.ActorId)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new ArgumentException("Cari command metadata geçersiz.", nameof(metadata));
        }
    }

    private static CreatePartyCommand Normalize(CreatePartyCommand command)
    {
        if (!Enum.IsDefined(command.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(command.Kind));
        }

        const PartyRole knownRoles =
            PartyRole.Customer | PartyRole.Supplier | PartyRole.Tolling | PartyRole.Service;
        if (command.InitialRoles == PartyRole.None
            || (command.InitialRoles & ~knownRoles) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.InitialRoles));
        }

        var legalName = RequireText(command.LegalName, nameof(command.LegalName), 250);
        var tradeName = NormalizeOptional(command.TradeName, nameof(command.TradeName), 200);
        var externalCode = NormalizeOptional(
            command.ExternalIntegrationCode,
            nameof(command.ExternalIntegrationCode),
            128);
        return command with
        {
            LegalName = legalName,
            TradeName = tradeName,
            ExternalIntegrationCode = externalCode,
        };
    }

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName);
    }

    private static string? NormalizeOptional(
        string? value,
        string parameterName,
        int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }
    }

    private static PartyCoreView ToCoreView(Party party, CompanyPartyAccount account)
    {
        return new PartyCoreView(
            party.Id,
            party.Code.Value,
            party.Kind,
            party.LegalName,
            party.TradeName,
            party.IsActive,
            party.Version,
            [new CompanyPartyAccountView(
                account.Id,
                account.CompanyId,
                account.Roles,
                account.ExternalIntegrationCode,
                account.IsActive,
                account.Version)]);
    }

    private sealed record CreatePartyFingerprint(
        PartyKind Kind,
        string LegalName,
        string? TradeName,
        int InitialRoles,
        string? ExternalIntegrationCode);
}
