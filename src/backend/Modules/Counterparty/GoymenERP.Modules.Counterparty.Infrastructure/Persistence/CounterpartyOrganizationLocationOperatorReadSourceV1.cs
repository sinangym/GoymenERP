using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed class CounterpartyOrganizationLocationOperatorReadSourceV1(
    CounterpartyDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ICounterpartyCursorCodec cursorCodec,
    TimeProvider timeProvider,
    ICounterpartyOrganizationLocationOperatorReadSqlObserver sqlObserver)
    : ICounterpartyOrganizationLocationOperatorReadSourceV1
{
    private const int MaximumBindingCount = 100;
    private const int MaximumPageSize = 25;
    private const string CursorContract = "counterparty.organization-location-operators.v1";
    private static readonly TimeSpan CursorLifetime = TimeSpan.FromMinutes(30);

    public async Task<CounterpartyOrganizationLocationOperatorDisplayAnswerV1> ReadAuthorizedDisplaysAsync(
        CounterpartyOrganizationLocationOperatorDisplayRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCanonicalize(request, out var canonical, out var invalidBindings))
        {
            return Display(invalidBindings
                ? CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable
                : CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied);
        }

        if (!RequestMatchesCurrentContext(canonical))
        {
            return Display(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        try
        {
            var locationIds = canonical.Bindings.Select(binding => binding.LocationId).ToArray();
            if (!await HasFreshLocationAuthorityAsync(
                    ErpPermissions.OrganizationLocationsRead,
                    companyId,
                    actorId,
                    locationIds,
                    cancellationToken))
            {
                return Display(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied);
            }

            var rows = await ReadDisplayRowsAsync(companyId, canonical.Bindings, cancellationToken);
            if (!await HasFreshLocationAuthorityAsync(
                    ErpPermissions.OrganizationLocationsRead,
                    companyId,
                    actorId,
                    locationIds,
                    cancellationToken))
            {
                return Display(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied);
            }

            var expected = canonical.Bindings.ToDictionary(binding => binding.LocationId);
            var locations = new HashSet<Guid>();
            var items = new List<CounterpartyOrganizationLocationOperatorDisplayV1>(rows.Count);
            foreach (var row in rows)
            {
                if (!expected.TryGetValue(row.LocationId, out var binding)
                    || binding.PartyId != row.PartyId
                    || !locations.Add(row.LocationId)
                    || row.PartyId == Guid.Empty
                    || row.CompanyAccountId == Guid.Empty
                    || string.IsNullOrWhiteSpace(row.Code)
                    || string.IsNullOrWhiteSpace(row.LegalName))
                {
                    return Display(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable);
                }

                items.Add(new CounterpartyOrganizationLocationOperatorDisplayV1(
                    row.LocationId,
                    row.PartyId,
                    row.Code,
                    row.LegalName,
                    row.TradeName,
                    row.PartyIsActive
                    && row.AccountIsActive
                    && (row.Roles & PartyRole.Supplier) == PartyRole.Supplier));
            }

            return items.Count == canonical.Bindings.Count
                ? new(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Success, items)
                : Display(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable);
        }
        catch (UnauthorizedAccessException)
        {
            return Display(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Display(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable);
        }
    }

    public async Task<CounterpartyOrganizationLocationOperatorCandidateAnswerV1> SearchAuthorizedCandidatesAsync(
        CounterpartyOrganizationLocationOperatorCandidateRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCanonicalize(request, out var canonical) || !RequestMatchesCurrentContext(canonical))
        {
            return Candidates(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        try
        {
            if (!await HasFreshLocationAuthorityAsync(
                    ErpPermissions.OrganizationLocationsWrite,
                    companyId,
                    actorId,
                    [canonical.LocationId],
                    cancellationToken))
            {
                return Candidates(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied);
            }

            if (!cursorCodec.IsAvailable)
            {
                return Candidates(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable);
            }

            var bindingHash = ComputeBindingHash(canonical);
            CounterpartyCursorState? cursor = null;
            if (canonical.Cursor is not null
                && (!cursorCodec.TryUnprotect(canonical.Cursor, out cursor)
                    || cursor.Sort != CounterpartySort.Code
                    || !string.Equals(cursor.BindingHash, bindingHash, StringComparison.Ordinal)))
            {
                return Candidates(CounterpartyOrganizationLocationOperatorReadOutcomeV1.InvalidCursor);
            }

            var rows = await ReadCandidateRowsAsync(companyId, canonical, cursor, cancellationToken);
            if (!await HasFreshLocationAuthorityAsync(
                    ErpPermissions.OrganizationLocationsWrite,
                    companyId,
                    actorId,
                    [canonical.LocationId],
                    cancellationToken))
            {
                return Candidates(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied);
            }

            var seenParties = new HashSet<Guid>();
            var seenAccounts = new HashSet<Guid>();
            foreach (var row in rows)
            {
                if (row.PartyId == Guid.Empty
                    || row.CompanyAccountId == Guid.Empty
                    || !row.PartyIsActive
                    || !row.AccountIsActive
                    || (row.Roles & PartyRole.Supplier) != PartyRole.Supplier
                    || string.IsNullOrWhiteSpace(row.Code)
                    || string.IsNullOrWhiteSpace(row.LegalName)
                    || !seenParties.Add(row.PartyId)
                    || !seenAccounts.Add(row.CompanyAccountId))
                {
                    return Candidates(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable);
                }
            }

            var hasMore = rows.Count > canonical.PageSize;
            var pageRows = hasMore ? rows.Take(canonical.PageSize).ToArray() : rows.ToArray();
            var items = pageRows.Select(row => new CounterpartyOrganizationLocationOperatorCandidateV1(
                row.PartyId,
                row.Code,
                row.LegalName,
                row.TradeName)).ToArray();
            var nextCursor = hasMore
                ? CreateNextCursor(pageRows[^1], bindingHash)
                : null;
            return new(
                CounterpartyOrganizationLocationOperatorReadOutcomeV1.Success,
                items,
                nextCursor,
                hasMore);
        }
        catch (UnauthorizedAccessException)
        {
            return Candidates(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Candidates(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable);
        }
    }

    private async Task<IReadOnlyList<DisplayRow>> ReadDisplayRowsAsync(
        Guid companyId,
        IReadOnlyList<CounterpartyOrganizationLocationOperatorBindingV1> bindings,
        CancellationToken cancellationToken)
    {
        var (connection, closeConnection) = await OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT requested.[LocationId], p.[Id], a.[Id], p.[Code], p.[LegalName], p.[TradeName],
                       p.[IsActive], a.[IsActive], a.[Roles]
                FROM OPENJSON(@Bindings)
                    WITH ([LocationId] uniqueidentifier '$.LocationId', [PartyId] uniqueidentifier '$.PartyId') AS requested
                INNER JOIN [counterparty].[CompanyPartyAccounts] AS a
                    ON a.[CompanyId] = @CompanyId AND a.[PartyId] = requested.[PartyId]
                INNER JOIN [counterparty].[Parties] AS p ON p.[Id] = a.[PartyId]
                ORDER BY requested.[LocationId], p.[Id];
                """;
            AddParameter(command, "@Bindings", JsonSerializer.Serialize(bindings), DbType.String);
            AddParameter(command, "@CompanyId", companyId, DbType.Guid);
            sqlObserver.Observe(CounterpartyOrganizationLocationOperatorReadSqlOperation.Display, command.CommandText);

            var rows = new List<DisplayRow>(bindings.Count);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetBoolean(6), reader.GetBoolean(7),
                    (PartyRole)reader.GetInt32(8)));
            }

            return rows;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<IReadOnlyList<CandidateRow>> ReadCandidateRowsAsync(
        Guid companyId,
        CounterpartyOrganizationLocationOperatorCandidateRequestV1 request,
        CounterpartyCursorState? cursor,
        CancellationToken cancellationToken)
    {
        var (connection, closeConnection) = await OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT TOP (@Take) p.[Id], a.[Id], p.[Code], p.[LegalName], p.[TradeName],
                       p.[IsActive], a.[IsActive], a.[Roles]
                FROM [counterparty].[CompanyPartyAccounts] AS a
                INNER JOIN [counterparty].[Parties] AS p ON p.[Id] = a.[PartyId]
                WHERE a.[CompanyId] = @CompanyId
                  AND p.[IsActive] = 1
                  AND a.[IsActive] = 1
                  AND (a.[Roles] & @SupplierRole) = @SupplierRole
                  AND (@SearchPattern IS NULL
                       OR p.[Code] COLLATE Turkish_100_CI_AS LIKE @SearchPattern ESCAPE N'~'
                       OR p.[LegalName] COLLATE Turkish_100_CI_AS LIKE @SearchPattern ESCAPE N'~'
                       OR p.[TradeName] COLLATE Turkish_100_CI_AS LIKE @SearchPattern ESCAPE N'~')
                  AND (@CursorCode IS NULL
                       OR p.[Code] COLLATE Latin1_General_100_BIN2 > @CursorCode
                       OR (p.[Code] COLLATE Latin1_General_100_BIN2 = @CursorCode AND p.[Id] > @CursorPartyId))
                ORDER BY p.[Code] COLLATE Latin1_General_100_BIN2, p.[Id];
                """;
            AddParameter(command, "@Take", request.PageSize + 1, DbType.Int32);
            AddParameter(command, "@CompanyId", companyId, DbType.Guid);
            AddParameter(command, "@SupplierRole", (int)PartyRole.Supplier, DbType.Int32);
            AddParameter(command, "@SearchPattern",
                request.Query is null ? DBNull.Value : $"%{EscapeLikePattern(request.Query)}%", DbType.String);
            AddParameter(command, "@CursorCode", cursor?.PrimaryText ?? (object)DBNull.Value, DbType.String);
            AddParameter(command, "@CursorPartyId", cursor?.PartyId ?? Guid.Empty, DbType.Guid);
            sqlObserver.Observe(CounterpartyOrganizationLocationOperatorReadSqlOperation.Candidates, command.CommandText);

            var rows = new List<CandidateRow>(request.PageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetBoolean(5), reader.GetBoolean(6),
                    (PartyRole)reader.GetInt32(7)));
            }

            return rows;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<(DbConnection Connection, bool CloseConnection)> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            throw new InvalidOperationException("Organization location-operator reads require a relational provider.");
        }

        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return (connection, closeConnection);
    }

    private string CreateNextCursor(CandidateRow row, string bindingHash)
    {
        var now = timeProvider.GetUtcNow();
        return cursorCodec.Protect(new CounterpartyCursorState(
            1, bindingHash, CounterpartySort.Code, row.Code, null, null, row.PartyId, now, now + CursorLifetime));
    }

    private static string ComputeBindingHash(CounterpartyOrganizationLocationOperatorCandidateRequestV1 request)
    {
        var canonical = new StringBuilder();
        AppendCanonical(canonical, CursorContract);
        AppendCanonical(canonical, request.ActorId);
        AppendCanonical(canonical, request.SessionBinding);
        AppendCanonical(canonical, request.CompanyId.ToString("D", CultureInfo.InvariantCulture));
        AppendCanonical(canonical, request.LocationId.ToString("D", CultureInfo.InvariantCulture));
        AppendCanonical(canonical, request.Query is null ? "query:null" : "query:value");
        if (request.Query is not null)
        {
            AppendCanonical(canonical, request.Query);
        }
        AppendCanonical(canonical, request.PageSize.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void AppendCanonical(StringBuilder target, string value) =>
        target.Append(value.Length).Append(':').Append(value).Append('|');

    private bool RequestMatchesCurrentContext(CounterpartyOrganizationLocationOperatorDisplayRequestV1 request) =>
        MatchesCurrentContext(request.CompanyId, request.ActorId);

    private bool RequestMatchesCurrentContext(CounterpartyOrganizationLocationOperatorCandidateRequestV1 request) =>
        MatchesCurrentContext(request.CompanyId, request.ActorId);

    private bool MatchesCurrentContext(Guid companyId, string actorId) =>
        currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty
        && currentUser.CanAccessCompany(companyContext.CompanyId)
        && companyId == companyContext.CompanyId
        && string.Equals(actorId, currentUser.UserId, StringComparison.Ordinal);

    private bool RemainsCurrent(Guid requestCompanyId, string requestActorId, Guid companyId, string actorId) =>
        MatchesCurrentContext(requestCompanyId, requestActorId)
        && companyContext.CompanyId == companyId
        && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal);

    private async Task<bool> HasFreshLocationAuthorityAsync(
        string permission,
        Guid companyId,
        string actorId,
        IReadOnlyCollection<Guid> locationIds,
        CancellationToken cancellationToken)
    {
        if (!RemainsCurrent(companyId, actorId, companyId, actorId))
        {
            return false;
        }

        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return RemainsCurrent(companyId, actorId, companyId, actorId)
            && scope.IsGranted
            && (scope.IsCompanyWide || locationIds.All(scope.LocationIds.Contains));
    }

    private static bool TryCanonicalize(
        CounterpartyOrganizationLocationOperatorDisplayRequestV1? request,
        out CounterpartyOrganizationLocationOperatorDisplayRequestV1 canonical,
        out bool invalidBindings)
    {
        canonical = null!;
        invalidBindings = false;
        if (request is null || request.CompanyId == Guid.Empty || !IsCanonical(request.ActorId))
        {
            return false;
        }

        if (request.Bindings is null || request.Bindings.Count is 0 or > MaximumBindingCount
            || request.Bindings.Any(binding => binding.LocationId == Guid.Empty || binding.PartyId == Guid.Empty))
        {
            invalidBindings = true;
            return false;
        }

        var bindings = request.Bindings.OrderBy(binding => binding.LocationId).ThenBy(binding => binding.PartyId).ToArray();
        if (bindings.Select(binding => binding.LocationId).Distinct().Count() != bindings.Length)
        {
            invalidBindings = true;
            return false;
        }

        canonical = request with { Bindings = bindings };
        return true;
    }

    private static bool TryCanonicalize(
        CounterpartyOrganizationLocationOperatorCandidateRequestV1? request,
        out CounterpartyOrganizationLocationOperatorCandidateRequestV1 canonical)
    {
        canonical = null!;
        if (request is null || request.CompanyId == Guid.Empty || request.LocationId == Guid.Empty
            || !IsCanonical(request.ActorId) || !IsCanonical(request.SessionBinding)
            || request.PageSize is < 1 or > MaximumPageSize
            || request.Cursor is not null && (!IsCanonical(request.Cursor)))
        {
            return false;
        }

        var query = request.Query?.Trim().Normalize(NormalizationForm.FormC);
        if (query?.Length == 0)
        {
            query = null;
        }
        else if (query is not null && query.EnumerateRunes().Count() > 200)
        {
            return false;
        }

        canonical = request with { Query = query };
        return true;
    }

    private static bool IsCanonical(string value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static string EscapeLikePattern(string value) => value
        .Replace("~", "~~", StringComparison.Ordinal)
        .Replace("%", "~%", StringComparison.Ordinal)
        .Replace("_", "~_", StringComparison.Ordinal)
        .Replace("[", "~[", StringComparison.Ordinal);

    private static void AddParameter(DbCommand command, string name, object value, DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static CounterpartyOrganizationLocationOperatorDisplayAnswerV1 Display(
        CounterpartyOrganizationLocationOperatorReadOutcomeV1 outcome) => new(outcome, []);

    private static CounterpartyOrganizationLocationOperatorCandidateAnswerV1 Candidates(
        CounterpartyOrganizationLocationOperatorReadOutcomeV1 outcome) => new(outcome, [], null, false);

    private sealed record DisplayRow(Guid LocationId, Guid PartyId, Guid CompanyAccountId, string Code,
        string LegalName, string? TradeName, bool PartyIsActive, bool AccountIsActive, PartyRole Roles);

    private sealed record CandidateRow(Guid PartyId, Guid CompanyAccountId, string Code, string LegalName,
        string? TradeName, bool PartyIsActive, bool AccountIsActive, PartyRole Roles);
}

internal enum CounterpartyOrganizationLocationOperatorReadSqlOperation
{
    Display = 1,
    Candidates = 2,
}

internal interface ICounterpartyOrganizationLocationOperatorReadSqlObserver
{
    void Observe(CounterpartyOrganizationLocationOperatorReadSqlOperation operation, string commandText);
}

internal sealed class NullCounterpartyOrganizationLocationOperatorReadSqlObserver
    : ICounterpartyOrganizationLocationOperatorReadSqlObserver
{
    public void Observe(CounterpartyOrganizationLocationOperatorReadSqlOperation operation, string commandText)
    {
    }
}
