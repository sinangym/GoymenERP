using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Views;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed class CounterpartyCommandService(
    CounterpartyDbContext dbContext,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IErpAuthorizationService authorizationService,
    IAuditEventCollector auditEventCollector,
    ILogger<CounterpartyCommandService> logger)
    : ICounterpartyCommandService
{
    private const int CommandExecutionSchemaVersion = 1;
    private const string CreateCommandName = "counterparty.party.create.v1";
    private const string SetAccountActiveStateCommandName =
        "counterparty.company-account.set-active-state.v1";

    public async Task<CounterpartyCommandExecution> CreateAsync(
        CreateCounterparty command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);

        var scope = await RequireCompanyWidePermissionAsync(
            ErpPermissions.CounterpartyPartiesCreate,
            cancellationToken);
        var normalized = NormalizeCreate(command);
        var idempotencyKeyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new CreateFingerprint(
            normalized.Kind,
            normalized.LegalName,
            normalized.TradeName,
            normalized.Roles));
        var existingExecution = await FindCommandExecutionAsync(
            CreateCommandName,
            idempotencyKeyHash,
            cancellationToken);
        if (existingExecution is not null)
        {
            return ResolveExistingExecution(existingExecution, requestHash, metadata);
        }

        var partyCode = await AllocatePartyCodeAsync(cancellationToken);
        var party = new Party(
            Guid.NewGuid(),
            partyCode,
            normalized.Kind,
            normalized.LegalName,
            normalized.TradeName);
        var account = new CompanyPartyAccount(
            Guid.NewGuid(),
            metadata.CompanyId,
            party.Id,
            normalized.Roles);
        dbContext.Parties.Add(party);
        dbContext.CompanyPartyAccounts.Add(account);

        var resource = PartyCoreViewComposer.ComposeAuthorized(
            party,
            [account],
            new HashSet<Guid> { metadata.CompanyId })
            ?? throw new InvalidOperationException("Yeni cari görünümü oluşturulamadı.");
        var response = Responses.Created(resource);

        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            "counterparty.party.created.v1",
            "Counterparty.Party",
            party.Id.ToString("D"),
            "Create",
            EntityVersionBefore: null,
            EntityVersionAfter: party.Version,
            AuditActorTypes.User,
            metadata.ActorId,
            ErpPermissions.CounterpartyPartiesCreate,
            scope.RoleCodes,
            BeforeJson: "{}",
            AfterJson: SerializeCreateAuditSnapshot(party, account),
            Reason: "Cari oluşturma",
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));

        var result = await CompleteCommandAsync(
            CreateCommandName,
            response,
            idempotencyKeyHash,
            requestHash,
            metadata,
            concurrencyContext: null,
            cancellationToken);
        logger.LogInformation(
            "Counterparty create command completed. CompanyId={CompanyId} PartyId={PartyId} ActorId={ActorId} CorrelationId={CorrelationId} Result={Result} IsReplay={IsReplay}",
            metadata.CompanyId,
            party.Id,
            metadata.ActorId,
            metadata.CorrelationId,
            result.Response.Result,
            result.IsReplay);
        return result;
    }

    public async Task<CounterpartyCommandExecution> SetCompanyAccountActiveStateAsync(
        Guid partyId,
        SetCompanyPartyAccountActiveState command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        ValidateReason(command.Reason);
        if (partyId == Guid.Empty)
        {
            throw new ArgumentException("Cari kimliği boş olamaz.", nameof(partyId));
        }

        if (command.ExpectedVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "Beklenen sürüm sıfırdan büyük olmalıdır.");
        }

        var scope = await RequireCompanyWidePermissionAsync(
            ErpPermissions.CounterpartyAccountsStatusUpdate,
            cancellationToken);
        var idempotencyKeyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new SetAccountActiveStateFingerprint(
            partyId,
            command.IsActive,
            command.ExpectedVersion,
            command.Reason.Trim()));
        var account = await dbContext.CompanyPartyAccounts
            .SingleOrDefaultAsync(
                x => x.CompanyId == metadata.CompanyId && x.PartyId == partyId,
                cancellationToken);
        var existingExecution = await FindCommandExecutionAsync(
            SetAccountActiveStateCommandName,
            idempotencyKeyHash,
            cancellationToken);
        if (existingExecution is not null)
        {
            if (account is null)
            {
                return new CounterpartyCommandExecution(
                    Responses.NotFound(),
                    false,
                    metadata.CorrelationId);
            }

            return ResolveExistingExecution(existingExecution, requestHash, metadata);
        }

        if (account is null)
        {
            return await CompleteCommandAsync(
                SetAccountActiveStateCommandName,
                Responses.NotFound(),
                idempotencyKeyHash,
                requestHash,
                metadata,
                concurrencyContext: null,
                cancellationToken);
        }

        if (account.Version != command.ExpectedVersion)
        {
            return await CompleteCommandAsync(
                SetAccountActiveStateCommandName,
                Responses.VersionConflict(command.ExpectedVersion, account.Version),
                idempotencyKeyHash,
                requestHash,
                metadata,
                concurrencyContext: null,
                cancellationToken);
        }

        var versionBefore = account.Version;
        var isActiveBefore = account.IsActive;
        if (command.IsActive)
        {
            account.Reactivate();
        }
        else
        {
            account.Deactivate();
        }

        var stateChanged = isActiveBefore != account.IsActive;
        var operation = stateChanged
            ? account.IsActive ? "Activate" : "Deactivate"
            : "NoChange";
        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            "counterparty.company-account.active-state.changed.v1",
            "Counterparty.CompanyPartyAccount",
            account.Id.ToString("D"),
            operation,
            versionBefore,
            account.Version,
            AuditActorTypes.User,
            metadata.ActorId,
            ErpPermissions.CounterpartyAccountsStatusUpdate,
            scope.RoleCodes,
            SerializeAccountAuditSnapshot(account, isActiveBefore, versionBefore),
            SerializeAccountAuditSnapshot(account, account.IsActive, account.Version),
            command.Reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));

        var result = await CompleteCommandAsync(
            SetAccountActiveStateCommandName,
            Responses.Succeeded(),
            idempotencyKeyHash,
            requestHash,
            metadata,
            new AccountConcurrencyContext(
                partyId,
                command.ExpectedVersion),
            cancellationToken);
        logger.LogInformation(
            "Counterparty company-account active-state command completed. CompanyId={CompanyId} PartyId={PartyId} ActorId={ActorId} CorrelationId={CorrelationId} Result={Result} IsReplay={IsReplay}",
            metadata.CompanyId,
            partyId,
            metadata.ActorId,
            metadata.CorrelationId,
            result.Response.Result,
            result.IsReplay);
        return result;
    }

    private async Task<CounterpartyCommandExecution> CompleteCommandAsync(
        string commandName,
        CounterpartyCommandResponse response,
        string idempotencyKeyHash,
        string requestHash,
        CounterpartyCommandMetadata metadata,
        AccountConcurrencyContext? concurrencyContext,
        CancellationToken cancellationToken)
    {
        dbContext.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(
            Guid.NewGuid(),
            metadata.CompanyId,
            commandName,
            idempotencyKeyHash,
            requestHash,
            metadata.ActorId,
            response.Result.ToString(),
            response.StatusCode,
            JsonSerializer.Serialize(response),
            metadata.CorrelationId,
            DateTimeOffset.UtcNow,
            CommandExecutionSchemaVersion)));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CounterpartyCommandExecution(
                response,
                false,
                metadata.CorrelationId);
        }
        catch (DbUpdateConcurrencyException) when (concurrencyContext is not null)
        {
            dbContext.ChangeTracker.Clear();
            var winner = await FindCommandExecutionAsync(
                commandName,
                idempotencyKeyHash,
                cancellationToken);
            if (winner is not null)
            {
                return ResolveExistingExecution(winner, requestHash, metadata);
            }

            var currentVersion = await dbContext.CompanyPartyAccounts
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == metadata.CompanyId
                    && x.PartyId == concurrencyContext.PartyId)
                .Select(x => (long?)x.Version)
                .SingleOrDefaultAsync(cancellationToken);
            var terminalResponse = currentVersion.HasValue
                ? Responses.VersionConflict(
                    concurrencyContext.ExpectedVersion,
                    currentVersion.Value)
                : Responses.NotFound();
            return await CompleteCommandAsync(
                commandName,
                terminalResponse,
                idempotencyKeyHash,
                requestHash,
                metadata,
                concurrencyContext: null,
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var winner = await FindCommandExecutionAsync(
                commandName,
                idempotencyKeyHash,
                cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return ResolveExistingExecution(winner, requestHash, metadata);
        }
    }

    private Task<CommandExecution?> FindCommandExecutionAsync(
        string commandName,
        string idempotencyKeyHash,
        CancellationToken cancellationToken)
    {
        return dbContext.CommandExecutions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CommandName == commandName
                     && x.IdempotencyKeyHash == idempotencyKeyHash,
                cancellationToken);
    }

    private static CounterpartyCommandExecution ResolveExistingExecution(
        CommandExecution execution,
        string requestHash,
        CounterpartyCommandMetadata metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new CounterpartyCommandExecution(
                Responses.IdempotencyConflict(),
                false,
                metadata.CorrelationId);
        }

        var response = JsonSerializer.Deserialize<CounterpartyCommandResponse>(
            execution.ResponseJson);
        if (response is null
            || !string.Equals(
                response.Result.ToString(),
                execution.Outcome,
                StringComparison.Ordinal)
            || response.StatusCode != execution.HttpStatusCode
            || response.Result == CounterpartyCommandResult.IdempotencyConflict)
        {
            throw new InvalidOperationException(
                "Kalıcı cari idempotency sonucu command sözleşmesiyle uyumsuz.");
        }

        return new CounterpartyCommandExecution(
            response,
            true,
            execution.OriginalCorrelationId);
    }

    private async Task<PartyCode> AllocatePartyCodeAsync(
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            var codes = await dbContext.Parties
                .AsNoTracking()
                .Select(x => x.Code.Value)
                .ToListAsync(cancellationToken);
            var next = codes.Count == 0
                ? 1L
                : codes.Max(ParsePartyCodeSequence) + 1;
            return PartyCode.FromSequence(next);
        }

        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT NEXT VALUE FOR [counterparty].[PartyCodeSequence];";
            var value = await command.ExecuteScalarAsync(cancellationToken);
            var sequence = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            return PartyCode.FromSequence(sequence);
        }
        finally
        {
            if (openedHere)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private async Task<PermissionScope> RequireCompanyWidePermissionAsync(
        string permission,
        CancellationToken cancellationToken)
    {
        var scope = await authorizationService.GetPermissionScopeAsync(
            permission,
            companyContext.CompanyId,
            cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide)
        {
            throw new UnauthorizedAccessException(
                "Cari kritik command için şirket kapsamlı yetki zorunludur.");
        }

        return scope;
    }

    private void ValidateCommandMetadata(CounterpartyCommandMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!companyContext.IsResolved
            || metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Cari command yürütme bağlamı geçersiz.");
        }
    }

    private static CreateCounterparty NormalizeCreate(CreateCounterparty command)
    {
        if (!Enum.IsDefined(command.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Geçersiz taraf türü.");
        }

        var legalName = NormalizeRequiredText(command.LegalName, 250, nameof(command.LegalName));
        var tradeName = NormalizeOptionalText(command.TradeName, 200, nameof(command.TradeName));
        var roles = ValidateRoles(command.Roles);
        return new CreateCounterparty(command.Kind, legalName, tradeName, roles);
    }

    private static PartyRole ValidateRoles(PartyRole roles)
    {
        const PartyRole knownRoles =
            PartyRole.Customer | PartyRole.Supplier | PartyRole.Tolling | PartyRole.Service;
        if (roles == PartyRole.None || (roles & ~knownRoles) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roles),
                roles,
                "Cari en az bir bilinen ticari rol taşımalıdır.");
        }

        return roles;
    }

    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
        {
            throw new ArgumentException(
                "Kritik değişiklik için 1-1000 karakter arasında bir gerekçe zorunludur.",
                nameof(reason));
        }
    }

    private static string NormalizeRequiredText(
        string value,
        int maximumLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentOutOfRangeException(
                parameterName,
                $"Değer {maximumLength} karakterden uzun olamaz.");
    }

    private static string? NormalizeOptionalText(
        string? value,
        int maximumLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentOutOfRangeException(
                parameterName,
                $"Değer {maximumLength} karakterden uzun olamaz.");
    }

    private static long ParsePartyCodeSequence(string code)
    {
        var separator = code.IndexOf('-', StringComparison.Ordinal);
        return separator >= 0
            && long.TryParse(
                code[(separator + 1)..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value)
            ? value
            : throw new InvalidOperationException("Kalıcı cari kodu kanonik değil.");
    }

    private static string SerializeCreateAuditSnapshot(
        Party party,
        CompanyPartyAccount account)
    {
        return JsonSerializer.Serialize(new
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
            CompanyAccount = new
            {
                account.Id,
                account.CompanyId,
                account.PartyId,
                account.Roles,
                account.ExternalIntegrationCode,
                account.IsActive,
                account.Version,
            },
        });
    }

    private static string SerializeAccountAuditSnapshot(
        CompanyPartyAccount account,
        bool isActive,
        long version)
    {
        return JsonSerializer.Serialize(new
        {
            account.Id,
            account.CompanyId,
            account.PartyId,
            account.Roles,
            account.ExternalIntegrationCode,
            IsActive = isActive,
            Version = version,
        });
    }

    private sealed record CreateFingerprint(
        PartyKind Kind,
        string LegalName,
        string? TradeName,
        PartyRole Roles);

    private sealed record SetAccountActiveStateFingerprint(
        Guid PartyId,
        bool IsActive,
        long ExpectedVersion,
        string Reason);

    private sealed record AccountConcurrencyContext(
        Guid PartyId,
        long ExpectedVersion);

    private static class Responses
    {
        public static CounterpartyCommandResponse Created(PartyCoreView resource) => new(
            CounterpartyCommandResult.Created,
            201,
            Title: null,
            Detail: null,
            resource,
            Conflict: null);

        public static CounterpartyCommandResponse Succeeded() => new(
            CounterpartyCommandResult.Succeeded,
            204,
            Title: null,
            Detail: null,
            Resource: null,
            Conflict: null);

        public static CounterpartyCommandResponse NotFound() => new(
            CounterpartyCommandResult.NotFound,
            404,
            "Kayıt bulunamadı.",
            "İstenen cari kayıt bulunamadı veya bu şirket bağlamında kullanılamıyor.",
            Resource: null,
            Conflict: null);

        public static CounterpartyCommandResponse VersionConflict(
            long expectedVersion,
            long currentVersion) => new(
                CounterpartyCommandResult.VersionConflict,
                409,
                "Sürüm çakışması.",
                "Cari kayıt başka bir işlem tarafından değiştirilmiş. Güncel veriyi yenileyip tekrar deneyin.",
                Resource: null,
                new CounterpartyVersionConflict(expectedVersion, currentVersion));

        public static CounterpartyCommandResponse IdempotencyConflict() => new(
            CounterpartyCommandResult.IdempotencyConflict,
            409,
            "Idempotency çakışması.",
            "Aynı işlem anahtarı farklı bir istek için kullanılamaz.",
            Resource: null,
            Conflict: null);
    }
}
