using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Views;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed class CounterpartyReadService(
    CounterpartyDbContext dbContext,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IErpAuthorizationService authorizationService,
    ICounterpartyCursorCodec cursorCodec,
    ICounterpartyReadSqlObserver sqlObserver,
    TimeProvider timeProvider)
    : ICounterpartyReadService
{
    private const string CountScope = "filtered-authorized-visible-parties";
    private const string ContractVersion = "counterparty.read-page.v1";
    private const string TurkishCollation = "Turkish_100_CI_AS";
    private const string CodeCollation = "Latin1_General_100_BIN2";
    private static readonly TimeSpan CursorLifetime = TimeSpan.FromMinutes(30);
    private static readonly CompareInfo TurkishCompare =
        CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    public async Task<CounterpartyPageExecution> GetAuthorizedPageAsync(
        CounterpartyReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Query);

        if (!cursorCodec.IsAvailable)
        {
            return CounterpartyPageExecution.ServiceUnavailable();
        }

        var authorization = await ResolvePageAuthorizationAsync(cancellationToken);
        if (authorization is null)
        {
            return CounterpartyPageExecution.Forbidden();
        }

        var bindingHash = ComputeBindingHash(request, authorization);
        CounterpartyCursorState? cursor = null;
        if (request.Query.Cursor is not null
            && (!cursorCodec.TryUnprotect(request.Query.Cursor, out cursor)
                || !string.Equals(
                    cursor.BindingHash,
                    bindingHash,
                    StringComparison.Ordinal)
                || cursor.Sort != request.Query.Sort))
        {
            return CounterpartyPageExecution.InvalidCursor();
        }

        var page = dbContext.Database.IsRelational()
            ? await QuerySqlServerAsync(
                request.Query,
                authorization.ReadableCompanyIds,
                bindingHash,
                cursor,
                cancellationToken)
            : await QueryInMemoryTestProviderAsync(
                request.Query,
                authorization.ReadableCompanyIds,
                bindingHash,
                cursor,
                cancellationToken);

        return CounterpartyPageExecution.Succeeded(page);
    }

    public async Task<PartyCoreView?> GetAuthorizedPartyAsync(
        Guid partyId,
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        if (partyId == Guid.Empty)
        {
            return null;
        }

        var authorizedCompanyIds = await ResolveAuthorizedCompanyIdsForDetailAsync(
            companyId,
            cancellationToken);
        if (authorizedCompanyIds.Count == 0)
        {
            return null;
        }

        var companyIdArray = authorizedCompanyIds.ToArray();
        var accounts = await dbContext.CompanyPartyAccounts
            .AsNoTracking()
            .Where(account =>
                account.PartyId == partyId
                && companyIdArray.Contains(account.CompanyId))
            .OrderBy(account => account.CompanyId)
            .ThenBy(account => account.Id)
            .ToListAsync(cancellationToken);
        if (accounts.Count == 0)
        {
            return null;
        }

        var party = await dbContext.Parties
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == partyId, cancellationToken);
        return party is null
            ? null
            : PartyCoreViewComposer.ComposeAuthorized(
                party,
                accounts,
                authorizedCompanyIds);
    }

    private async Task<CounterpartyPage> QuerySqlServerAsync(
        CounterpartyPageQuery query,
        IReadOnlyList<Guid> companyIds,
        string bindingHash,
        CounterpartyCursorState? cursor,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var rows = await ReadPageRowsAsync(
                connection,
                query,
                companyIds,
                cursor,
                cancellationToken);
            var hasMore = rows.Count > query.PageSize;
            if (hasMore)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            var accounts = rows.Count == 0
                ? []
                : await ReadAccountsAsync(
                    connection,
                    rows.Select(row => row.Id).ToArray(),
                    companyIds,
                    query,
                    cancellationToken);
            var accountsByParty = accounts
                .GroupBy(account => account.PartyId)
                .ToDictionary(group => group.Key, group => group
                    .Select(account => new CounterpartyListAccount(
                        account.Id,
                        account.CompanyId,
                        account.Roles,
                        account.IsActive,
                        account.Version))
                    .ToArray());
            var items = rows.Select(row => new CounterpartyListItem(
                    row.Id,
                    row.Code,
                    row.Kind,
                    row.LegalName,
                    row.TradeName,
                    row.IsActive,
                    row.Version,
                    accountsByParty.GetValueOrDefault(row.Id, [])))
                .ToArray();

            long? totalCount = null;
            if (query.IncludeCount)
            {
                totalCount = await ReadCountAsync(
                    connection,
                    query,
                    companyIds,
                    cancellationToken);
            }

            return new CounterpartyPage(
                items,
                hasMore ? CreateNextCursor(query, rows[^1], bindingHash) : null,
                hasMore,
                totalCount,
                query.IncludeCount ? CountScope : null);
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<List<PartyPageRow>> ReadPageRowsAsync(
        DbConnection connection,
        CounterpartyPageQuery query,
        IReadOnlyList<Guid> companyIds,
        CounterpartyCursorState? cursor,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT TOP (@Take)
                p.[Id],
                p.[Code],
                p.[Kind],
                p.[LegalName],
                p.[TradeName],
                p.[IsActive],
                p.[Version],
                p.[UpdatedAtUtc]
            FROM [counterparty].[Parties] AS p
            WHERE EXISTS
            (
                SELECT 1
                FROM [counterparty].[CompanyPartyAccounts] AS a
                INNER JOIN OPENJSON(@CompanyIds)
                    WITH ([Id] uniqueidentifier '$') AS authorized
                    ON authorized.[Id] = a.[CompanyId]
                WHERE a.[PartyId] = p.[Id]
            """);
        AppendAccountFilters(sql, command, query);
        sql.AppendLine(")");
        AppendPartyFilters(sql, command, query);
        AppendCursorFilter(sql, command, query, cursor);
        AppendOrderBy(sql, query);

        command.CommandText = sql.ToString();
        AddParameter(command, "@Take", query.PageSize + 1, DbType.Int32);
        AddParameter(
            command,
            "@CompanyIds",
            JsonSerializer.Serialize(companyIds),
            DbType.String);

        var rows = new List<PartyPageRow>(query.PageSize + 1);
        sqlObserver.Observe(CounterpartyReadSqlOperation.Page, command.CommandText);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new PartyPageRow(
                reader.GetGuid(0),
                reader.GetString(1),
                Enum.Parse<PartyKind>(reader.GetString(2), ignoreCase: false),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetBoolean(5),
                reader.GetInt64(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return rows;
    }

    private async Task<long> ReadCountAsync(
        DbConnection connection,
        CounterpartyPageQuery query,
        IReadOnlyList<Guid> companyIds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT COUNT_BIG(*)
            FROM [counterparty].[Parties] AS p
            WHERE EXISTS
            (
                SELECT 1
                FROM [counterparty].[CompanyPartyAccounts] AS a
                INNER JOIN OPENJSON(@CompanyIds)
                    WITH ([Id] uniqueidentifier '$') AS authorized
                    ON authorized.[Id] = a.[CompanyId]
                WHERE a.[PartyId] = p.[Id]
            """);
        AppendAccountFilters(sql, command, query);
        sql.AppendLine(")");
        AppendPartyFilters(sql, command, query);
        command.CommandText = sql.ToString();
        AddParameter(
            command,
            "@CompanyIds",
            JsonSerializer.Serialize(companyIds),
            DbType.String);
        sqlObserver.Observe(CounterpartyReadSqlOperation.Count, command.CommandText);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private async Task<List<AccountPageRow>> ReadAccountsAsync(
        DbConnection connection,
        IReadOnlyList<Guid> partyIds,
        IReadOnlyList<Guid> companyIds,
        CounterpartyPageQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT
                a.[Id],
                a.[CompanyId],
                a.[PartyId],
                a.[Roles],
                a.[IsActive],
                a.[Version]
            FROM [counterparty].[CompanyPartyAccounts] AS a
            INNER JOIN OPENJSON(@PartyIds)
                WITH ([Id] uniqueidentifier '$') AS parties
                ON parties.[Id] = a.[PartyId]
            INNER JOIN OPENJSON(@CompanyIds)
                WITH ([Id] uniqueidentifier '$') AS companies
                ON companies.[Id] = a.[CompanyId]
            WHERE 1 = 1
            """);
        AppendAccountFilters(sql, command, query);
        sql.AppendLine("ORDER BY a.[PartyId], a.[CompanyId], a.[Id];");
        command.CommandText = sql.ToString();
        AddParameter(command, "@PartyIds", JsonSerializer.Serialize(partyIds), DbType.String);
        AddParameter(command, "@CompanyIds", JsonSerializer.Serialize(companyIds), DbType.String);

        var rows = new List<AccountPageRow>();
        sqlObserver.Observe(CounterpartyReadSqlOperation.Accounts, command.CommandText);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AccountPageRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                (PartyRole)reader.GetInt32(3),
                reader.GetBoolean(4),
                reader.GetInt64(5)));
        }

        return rows;
    }

    private static void AppendAccountFilters(
        StringBuilder sql,
        DbCommand command,
        CounterpartyPageQuery query)
    {
        if (query.AccountState != CounterpartyRecordState.All)
        {
            sql.AppendLine("AND a.[IsActive] = @AccountIsActive");
            AddParameter(
                command,
                "@AccountIsActive",
                query.AccountState == CounterpartyRecordState.Active,
                DbType.Boolean);
        }

        if (query.Roles != PartyRole.None)
        {
            sql.AppendLine(query.RoleMatch == CounterpartyRoleMatch.All
                ? "AND (a.[Roles] & @Roles) = @Roles"
                : "AND (a.[Roles] & @Roles) <> 0");
            AddParameter(command, "@Roles", (int)query.Roles, DbType.Int32);
        }
    }

    private static void AppendPartyFilters(
        StringBuilder sql,
        DbCommand command,
        CounterpartyPageQuery query)
    {
        if (query.PartyState != CounterpartyRecordState.All)
        {
            sql.AppendLine("AND p.[IsActive] = @PartyIsActive");
            AddParameter(
                command,
                "@PartyIsActive",
                query.PartyState == CounterpartyRecordState.Active,
                DbType.Boolean);
        }

        if (query.Kind is not null)
        {
            sql.AppendLine("AND p.[Kind] = @Kind");
            AddParameter(command, "@Kind", query.Kind.Value.ToString(), DbType.String);
        }

        if (query.Query is not null)
        {
            sql.AppendLine(
                $"AND (p.[Code] COLLATE {TurkishCollation} LIKE @Search ESCAPE N'~' "
                + $"OR p.[LegalName] COLLATE {TurkishCollation} LIKE @Search ESCAPE N'~' "
                + $"OR p.[TradeName] COLLATE {TurkishCollation} LIKE @Search ESCAPE N'~')");
            AddParameter(
                command,
                "@Search",
                $"%{EscapeLikePattern(query.Query)}%",
                DbType.String);
        }
    }

    private static void AppendCursorFilter(
        StringBuilder sql,
        DbCommand command,
        CounterpartyPageQuery query,
        CounterpartyCursorState? cursor)
    {
        if (cursor is null)
        {
            return;
        }

        var comparison = query.Direction == CounterpartySortDirection.Ascending ? ">" : "<";
        switch (query.Sort)
        {
            case CounterpartySort.Code:
                sql.AppendLine(
                    $"AND (p.[Code] COLLATE {CodeCollation} {comparison} @CursorPrimary "
                    + $"OR (p.[Code] COLLATE {CodeCollation} = @CursorPrimary "
                    + $"AND p.[Id] {comparison} @CursorPartyId))");
                AddParameter(command, "@CursorPrimary", cursor.PrimaryText!, DbType.String);
                break;
            case CounterpartySort.LegalName:
                sql.AppendLine(
                    $"AND (p.[LegalName] COLLATE {TurkishCollation} {comparison} @CursorPrimary "
                    + $"OR (p.[LegalName] COLLATE {TurkishCollation} = @CursorPrimary "
                    + $"AND (p.[Code] COLLATE {CodeCollation} {comparison} @CursorSecondary "
                    + $"OR (p.[Code] COLLATE {CodeCollation} = @CursorSecondary "
                    + $"AND p.[Id] {comparison} @CursorPartyId))))");
                AddParameter(command, "@CursorPrimary", cursor.PrimaryText!, DbType.String);
                AddParameter(command, "@CursorSecondary", cursor.SecondaryText!, DbType.String);
                break;
            case CounterpartySort.UpdatedAt:
                sql.AppendLine(
                    $"AND (p.[UpdatedAtUtc] {comparison} @CursorUpdatedAt "
                    + $"OR (p.[UpdatedAtUtc] = @CursorUpdatedAt "
                    + $"AND p.[Id] {comparison} @CursorPartyId))");
                AddParameter(
                    command,
                    "@CursorUpdatedAt",
                    new DateTimeOffset(cursor.PrimaryUtcTicks!.Value, TimeSpan.Zero),
                    DbType.DateTimeOffset);
                break;
            default:
                throw new InvalidOperationException("Bilinmeyen Cari sıralama alanı.");
        }

        AddParameter(command, "@CursorPartyId", cursor.PartyId, DbType.Guid);
    }

    private static void AppendOrderBy(StringBuilder sql, CounterpartyPageQuery query)
    {
        var direction = query.Direction == CounterpartySortDirection.Ascending
            ? "ASC"
            : "DESC";
        sql.AppendLine(query.Sort switch
        {
            CounterpartySort.Code =>
                $"ORDER BY p.[Code] COLLATE {CodeCollation} {direction}, p.[Id] {direction};",
            CounterpartySort.LegalName =>
                $"ORDER BY p.[LegalName] COLLATE {TurkishCollation} {direction}, "
                + $"p.[Code] COLLATE {CodeCollation} {direction}, p.[Id] {direction};",
            CounterpartySort.UpdatedAt =>
                $"ORDER BY p.[UpdatedAtUtc] {direction}, p.[Id] {direction};",
            _ => throw new InvalidOperationException("Bilinmeyen Cari sıralama alanı."),
        });
    }

    private async Task<CounterpartyPage> QueryInMemoryTestProviderAsync(
        CounterpartyPageQuery query,
        IReadOnlyList<Guid> companyIds,
        string bindingHash,
        CounterpartyCursorState? cursor,
        CancellationToken cancellationToken)
    {
        var accounts = await dbContext.CompanyPartyAccounts
            .AsNoTracking()
            .Where(account => companyIds.Contains(account.CompanyId))
            .ToListAsync(cancellationToken);
        var eligibleAccounts = accounts
            .Where(account => AccountMatches(account, query))
            .ToArray();
        var eligiblePartyIds = eligibleAccounts
            .Select(account => account.PartyId)
            .ToHashSet();
        var parties = await dbContext.Parties
            .AsNoTracking()
            .Where(party => eligiblePartyIds.Contains(party.Id))
            .ToListAsync(cancellationToken);
        var comparer = new InMemoryPartyComparer(query.Sort, query.Direction);
        var ordered = parties
            .Where(party => PartyMatches(party, query))
            .OrderBy(party => party, comparer)
            .ToList();
        var totalCount = query.IncludeCount ? ordered.Count : (long?)null;
        if (cursor is not null)
        {
            ordered = ordered
                .Where(party => comparer.Compare(party, cursor) > 0)
                .ToList();
        }

        var rows = ordered.Take(query.PageSize + 1).ToList();
        var hasMore = rows.Count > query.PageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var items = rows.Select(party => new CounterpartyListItem(
                party.Id,
                party.Code.Value,
                party.Kind,
                party.LegalName,
                party.TradeName,
                party.IsActive,
                party.Version,
                eligibleAccounts.Where(account => account.PartyId == party.Id)
                    .OrderBy(account => account.CompanyId)
                    .ThenBy(account => account.Id)
                    .Select(account => new CounterpartyListAccount(
                        account.Id,
                        account.CompanyId,
                        account.Roles,
                        account.IsActive,
                        account.Version))
                    .ToArray()))
            .ToArray();
        var last = rows.LastOrDefault();
        return new CounterpartyPage(
            items,
            hasMore && last is not null
                ? CreateNextCursor(
                    query,
                    new PartyPageRow(
                        last.Id,
                        last.Code.Value,
                        last.Kind,
                        last.LegalName,
                        last.TradeName,
                        last.IsActive,
                        last.Version,
                        last.UpdatedAtUtc),
                    bindingHash)
                : null,
            hasMore,
            totalCount,
            query.IncludeCount ? CountScope : null);
    }

    private async Task<PageAuthorization?> ResolvePageAuthorizationAsync(
        CancellationToken cancellationToken)
    {
        var allowedCompanyIds = currentUser.AllowedCompanyIds
            .Where(companyId => companyId != Guid.Empty)
            .Distinct()
            .Order()
            .ToArray();
        if (allowedCompanyIds.Length == 0)
        {
            return null;
        }

        var selectedCompanyId = companyContext.IsResolved
            ? companyContext.CompanyId
            : (Guid?)null;
        if (selectedCompanyId is not null
            && !currentUser.CanAccessCompany(selectedCompanyId.Value))
        {
            return null;
        }

        var candidates = selectedCompanyId is null
            ? allowedCompanyIds
            : [selectedCompanyId.Value];
        var permissionScopes = new List<CompanyPermissionScope>(candidates.Length);
        foreach (var companyId in candidates)
        {
            var snapshot = await authorizationService.GetAuthorizationSnapshotAsync(
                companyId,
                [ErpPermissions.CounterpartyPartiesRead],
                cancellationToken);
            permissionScopes.Add(new CompanyPermissionScope(
                companyId,
                snapshot.PermissionScopes[ErpPermissions.CounterpartyPartiesRead],
                snapshot.Revision));
        }

        var readableCompanyIds = permissionScopes
            .Where(entry => entry.Scope.IsGranted && entry.Scope.IsCompanyWide)
            .Select(entry => entry.CompanyId)
            .Order()
            .ToArray();
        if (readableCompanyIds.Length == 0
            || selectedCompanyId is not null && readableCompanyIds.Length != 1)
        {
            return null;
        }

        return new PageAuthorization(
            selectedCompanyId is null ? "authorized-multi-company" : "single-company",
            selectedCompanyId,
            allowedCompanyIds,
            readableCompanyIds,
            permissionScopes);
    }

    private async Task<HashSet<Guid>> ResolveAuthorizedCompanyIdsForDetailAsync(
        Guid? requestedCompanyId,
        CancellationToken cancellationToken)
    {
        Guid? effectiveCompanyId = requestedCompanyId;
        if (companyContext.IsResolved)
        {
            if (requestedCompanyId.HasValue
                && requestedCompanyId.Value != companyContext.CompanyId)
            {
                return [];
            }

            effectiveCompanyId = companyContext.CompanyId;
        }

        IEnumerable<Guid> candidates = currentUser.AllowedCompanyIds;
        if (effectiveCompanyId.HasValue)
        {
            if (effectiveCompanyId.Value == Guid.Empty
                || !currentUser.CanAccessCompany(effectiveCompanyId.Value))
            {
                return [];
            }

            candidates = [effectiveCompanyId.Value];
        }

        var result = new HashSet<Guid>();
        foreach (var candidate in candidates.Distinct())
        {
            var scope = await authorizationService.GetPermissionScopeAsync(
                ErpPermissions.CounterpartyPartiesRead,
                candidate,
                cancellationToken);
            if (scope.IsGranted && scope.IsCompanyWide)
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    private string CreateNextCursor(
        CounterpartyPageQuery query,
        PartyPageRow row,
        string bindingHash)
    {
        var now = timeProvider.GetUtcNow();
        var state = query.Sort switch
        {
            CounterpartySort.Code => new CounterpartyCursorState(
                1,
                bindingHash,
                query.Sort,
                row.Code,
                null,
                null,
                row.Id,
                now,
                now + CursorLifetime),
            CounterpartySort.LegalName => new CounterpartyCursorState(
                1,
                bindingHash,
                query.Sort,
                row.LegalName,
                row.Code,
                null,
                row.Id,
                now,
                now + CursorLifetime),
            CounterpartySort.UpdatedAt => new CounterpartyCursorState(
                1,
                bindingHash,
                query.Sort,
                null,
                null,
                row.UpdatedAtUtc.UtcTicks,
                row.Id,
                now,
                now + CursorLifetime),
            _ => throw new InvalidOperationException("Bilinmeyen Cari sıralama alanı."),
        };
        return cursorCodec.Protect(state);
    }

    private static string ComputeBindingHash(
        CounterpartyReadRequest request,
        PageAuthorization authorization)
    {
        var canonical = new StringBuilder();
        AppendCanonical(canonical, ContractVersion);
        AppendCanonical(canonical, request.ActorId);
        AppendCanonical(canonical, request.SessionBinding);
        AppendCanonical(canonical, authorization.ContextMode);
        AppendCanonical(canonical, authorization.SelectedCompanyId?.ToString("D") ?? "-");
        foreach (var companyId in authorization.AllowedCompanyIds)
        {
            AppendCanonical(canonical, $"allowed:{companyId:D}");
        }

        foreach (var companyId in authorization.ReadableCompanyIds)
        {
            AppendCanonical(canonical, $"readable:{companyId:D}");
        }

        foreach (var entry in authorization.PermissionScopes.OrderBy(entry => entry.CompanyId))
        {
            AppendCanonical(canonical, $"scope:{entry.CompanyId:D}");
            AppendCanonical(canonical, entry.Scope.IsGranted ? "granted" : "denied");
            AppendCanonical(canonical, entry.Scope.IsCompanyWide ? "company" : "narrow");
            AppendCanonical(canonical, entry.AuthorizationRevision);
            foreach (var role in entry.Scope.RoleCodes.Order(StringComparer.Ordinal))
            {
                AppendCanonical(canonical, $"role:{role}");
            }

            AppendScopeIds(canonical, "site", entry.Scope.SiteIds);
            AppendScopeIds(canonical, "location", entry.Scope.LocationIds);
            AppendScopeIds(canonical, "work-center", entry.Scope.WorkCenterIds);
        }

        var query = request.Query;
        AppendCanonical(canonical, query.Query ?? "-");
        AppendCanonical(canonical, query.Kind?.ToString() ?? "-");
        AppendCanonical(canonical, ((int)query.Roles).ToString(CultureInfo.InvariantCulture));
        AppendCanonical(canonical, query.RoleMatch.ToString());
        AppendCanonical(canonical, query.PartyState.ToString());
        AppendCanonical(canonical, query.AccountState.ToString());
        AppendCanonical(canonical, query.Sort.ToString());
        AppendCanonical(canonical, query.Direction.ToString());
        AppendCanonical(canonical, query.PageSize.ToString(CultureInfo.InvariantCulture));
        AppendCanonical(canonical, query.IncludeCount ? "count" : "no-count");

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void AppendScopeIds(
        StringBuilder canonical,
        string name,
        IReadOnlySet<Guid> values)
    {
        foreach (var value in values.Order())
        {
            AppendCanonical(canonical, $"{name}:{value:D}");
        }
    }

    private static void AppendCanonical(StringBuilder target, string value)
    {
        target.Append(value.Length)
            .Append(':')
            .Append(value)
            .Append('|');
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object value,
        DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal)
            .Replace("_", "~_", StringComparison.Ordinal)
            .Replace("[", "~[", StringComparison.Ordinal);
    }

    private static bool AccountMatches(
        CompanyPartyAccount account,
        CounterpartyPageQuery query)
    {
        if (query.AccountState == CounterpartyRecordState.Active && !account.IsActive
            || query.AccountState == CounterpartyRecordState.Inactive && account.IsActive)
        {
            return false;
        }

        if (query.Roles == PartyRole.None)
        {
            return true;
        }

        return query.RoleMatch == CounterpartyRoleMatch.All
            ? (account.Roles & query.Roles) == query.Roles
            : (account.Roles & query.Roles) != 0;
    }

    private static bool PartyMatches(Party party, CounterpartyPageQuery query)
    {
        if (query.PartyState == CounterpartyRecordState.Active && !party.IsActive
            || query.PartyState == CounterpartyRecordState.Inactive && party.IsActive
            || query.Kind is not null && party.Kind != query.Kind)
        {
            return false;
        }

        if (query.Query is null)
        {
            return true;
        }

        return ContainsTurkish(party.Code.Value, query.Query)
            || ContainsTurkish(party.LegalName, query.Query)
            || party.TradeName is not null && ContainsTurkish(party.TradeName, query.Query);
    }

    private static bool ContainsTurkish(string value, string query)
    {
        return TurkishCompare.IndexOf(value, query, CompareOptions.IgnoreCase) >= 0;
    }

    private sealed record PageAuthorization(
        string ContextMode,
        Guid? SelectedCompanyId,
        IReadOnlyList<Guid> AllowedCompanyIds,
        IReadOnlyList<Guid> ReadableCompanyIds,
        IReadOnlyList<CompanyPermissionScope> PermissionScopes);

    private sealed record CompanyPermissionScope(
        Guid CompanyId,
        PermissionScope Scope,
        string AuthorizationRevision);

    private sealed record PartyPageRow(
        Guid Id,
        string Code,
        PartyKind Kind,
        string LegalName,
        string? TradeName,
        bool IsActive,
        long Version,
        DateTimeOffset UpdatedAtUtc);

    private sealed record AccountPageRow(
        Guid Id,
        Guid CompanyId,
        Guid PartyId,
        PartyRole Roles,
        bool IsActive,
        long Version);

    private sealed class InMemoryPartyComparer(
        CounterpartySort sort,
        CounterpartySortDirection direction)
        : IComparer<Party>
    {
        private readonly int _direction = direction == CounterpartySortDirection.Ascending
            ? 1
            : -1;

        public int Compare(Party? left, Party? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var result = sort switch
            {
                CounterpartySort.Code => string.CompareOrdinal(
                    left.Code.Value,
                    right.Code.Value),
                CounterpartySort.LegalName => CompareLegalName(
                    left.LegalName,
                    left.Code.Value,
                    right.LegalName,
                    right.Code.Value),
                CounterpartySort.UpdatedAt => left.UpdatedAtUtc.CompareTo(right.UpdatedAtUtc),
                _ => throw new InvalidOperationException("Bilinmeyen Cari sıralama alanı."),
            };
            if (result == 0)
            {
                result = left.Id.CompareTo(right.Id);
            }

            return result * _direction;
        }

        public int Compare(Party party, CounterpartyCursorState cursor)
        {
            var result = sort switch
            {
                CounterpartySort.Code => string.CompareOrdinal(
                    party.Code.Value,
                    cursor.PrimaryText),
                CounterpartySort.LegalName => CompareLegalName(
                    party.LegalName,
                    party.Code.Value,
                    cursor.PrimaryText!,
                    cursor.SecondaryText!),
                CounterpartySort.UpdatedAt => party.UpdatedAtUtc.UtcTicks.CompareTo(
                    cursor.PrimaryUtcTicks!.Value),
                _ => throw new InvalidOperationException("Bilinmeyen Cari sıralama alanı."),
            };
            if (result == 0)
            {
                result = party.Id.CompareTo(cursor.PartyId);
            }

            return result * _direction;
        }

        private static int CompareLegalName(
            string leftName,
            string leftCode,
            string rightName,
            string rightCode)
        {
            var result = TurkishCompare.Compare(
                leftName,
                rightName,
                CompareOptions.IgnoreCase);
            return result != 0 ? result : string.CompareOrdinal(leftCode, rightCode);
        }
    }
}
