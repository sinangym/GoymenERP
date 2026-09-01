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
using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed class CounterpartyLinkCandidateReadService(
    CounterpartyDbContext dbContext,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IErpAuthorizationService authorizationService,
    ICounterpartyCursorCodec cursorCodec,
    ICounterpartyLinkCandidateSqlObserver sqlObserver,
    TimeProvider timeProvider)
    : ICounterpartyLinkCandidateReadService
{
    private const string ContractVersion = "counterparty.link-candidates.v1";
    private const string TurkishCollation = "Turkish_100_CI_AS";
    private const string CodeCollation = "Latin1_General_100_BIN2";
    private static readonly TimeSpan CursorLifetime = TimeSpan.FromMinutes(30);
    private static readonly CompareInfo TurkishCompare =
        CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    public async Task<CounterpartyLinkCandidateExecution> GetAuthorizedPageAsync(
        CounterpartyLinkCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Query);

        if (!currentUser.IsAuthenticated
            || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !string.Equals(
                request.ActorId,
                currentUser.UserId,
                StringComparison.Ordinal))
        {
            return CounterpartyLinkCandidateExecution.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(request.SessionBinding))
        {
            return CounterpartyLinkCandidateExecution.ServiceUnavailable();
        }

        if (!cursorCodec.IsAvailable)
        {
            return CounterpartyLinkCandidateExecution.ServiceUnavailable();
        }

        var authorization = await ResolveAuthorizationAsync(cancellationToken);
        if (authorization is null)
        {
            return CounterpartyLinkCandidateExecution.Forbidden();
        }

        var bindingHash = ComputeBindingHash(request, authorization);
        CounterpartyCursorState? cursor = null;
        if (request.Query.Cursor is not null
            && (!cursorCodec.TryUnprotect(request.Query.Cursor, out cursor)
                || cursor.Sort != CounterpartySort.Code
                || !string.Equals(
                    cursor.BindingHash,
                    bindingHash,
                    StringComparison.Ordinal)))
        {
            return CounterpartyLinkCandidateExecution.InvalidCursor();
        }

        if (authorization.ReadableSourceCompanyIds.Count == 0)
        {
            return CounterpartyLinkCandidateExecution.Succeeded(
                new CounterpartyLinkCandidatePage([], null, false));
        }

        var page = dbContext.Database.IsRelational()
            ? await QuerySqlServerAsync(
                request.Query,
                authorization,
                bindingHash,
                cursor,
                cancellationToken)
            : await QueryInMemoryTestProviderAsync(
                request.Query,
                authorization,
                bindingHash,
                cursor,
                cancellationToken);

        return CounterpartyLinkCandidateExecution.Succeeded(page);
    }

    private async Task<LinkCandidateAuthorization?> ResolveAuthorizationAsync(
        CancellationToken cancellationToken)
    {
        if (!companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            return null;
        }

        var targetCompanyId = companyContext.CompanyId;
        var allowedCompanyIds = currentUser.AllowedCompanyIds
            .Where(companyId => companyId != Guid.Empty)
            .Distinct()
            .Order()
            .ToArray();
        if (!allowedCompanyIds.Contains(targetCompanyId))
        {
            return null;
        }

        var targetSnapshot = await authorizationService.GetAuthorizationSnapshotAsync(
            targetCompanyId,
            [
                ErpPermissions.CounterpartyAccountsCreate,
                ErpPermissions.CounterpartyPartiesRead,
            ],
            cancellationToken);
        var targetCreateScope = GetScope(
            targetSnapshot,
            ErpPermissions.CounterpartyAccountsCreate);
        var targetReadScope = GetScope(
            targetSnapshot,
            ErpPermissions.CounterpartyPartiesRead);
        if (!targetSnapshot.AllowedCompanyIds.Contains(targetCompanyId)
            || !IsCompanyWideGranted(targetCreateScope)
            || !IsCompanyWideGranted(targetReadScope))
        {
            return null;
        }

        var sourceEntries = new List<SourceAuthorizationEntry>();
        foreach (var companyId in allowedCompanyIds.Where(id => id != targetCompanyId))
        {
            var snapshot = await authorizationService.GetAuthorizationSnapshotAsync(
                companyId,
                [ErpPermissions.CounterpartyPartiesRead],
                cancellationToken);
            sourceEntries.Add(new SourceAuthorizationEntry(
                companyId,
                snapshot.AllowedCompanyIds.Contains(companyId),
                GetScope(snapshot, ErpPermissions.CounterpartyPartiesRead),
                snapshot.Revision));
        }

        return new LinkCandidateAuthorization(
            targetCompanyId,
            allowedCompanyIds,
            targetCreateScope,
            targetReadScope,
            targetSnapshot.Revision,
            sourceEntries,
            sourceEntries
                .Where(entry => entry.IsAllowed && IsCompanyWideGranted(entry.ReadScope))
                .Select(entry => entry.CompanyId)
                .Order()
                .ToArray());
    }

    private async Task<CounterpartyLinkCandidatePage> QuerySqlServerAsync(
        CounterpartyLinkCandidateQuery query,
        LinkCandidateAuthorization authorization,
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
            var rows = await ReadCandidateRowsAsync(
                connection,
                query,
                authorization,
                cursor,
                cancellationToken);
            var hasMore = rows.Count > query.PageSize;
            if (hasMore)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            var accounts = rows.Count == 0
                ? []
                : await ReadVisibleSourceAccountsAsync(
                    connection,
                    rows.Select(row => row.Id).ToArray(),
                    authorization.ReadableSourceCompanyIds,
                    cancellationToken);
            var accountsByParty = accounts
                .GroupBy(account => account.PartyId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(ToSourceAccount).ToArray());
            var items = rows.Select(row => new CounterpartyLinkCandidateItem(
                    row.Id,
                    row.Code,
                    row.Kind,
                    row.LegalName,
                    row.TradeName,
                    row.IsActive,
                    accountsByParty.GetValueOrDefault(row.Id, [])))
                .ToArray();

            return new CounterpartyLinkCandidatePage(
                items,
                hasMore ? CreateNextCursor(rows[^1], bindingHash) : null,
                hasMore);
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<List<LinkCandidatePartyRow>> ReadCandidateRowsAsync(
        DbConnection connection,
        CounterpartyLinkCandidateQuery query,
        LinkCandidateAuthorization authorization,
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
                p.[IsActive]
            FROM [counterparty].[Parties] AS p
            WHERE p.[IsActive] = CAST(1 AS bit)
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM [counterparty].[CompanyPartyAccounts] AS targetAccount
                  WHERE targetAccount.[PartyId] = p.[Id]
                    AND targetAccount.[CompanyId] = @TargetCompanyId
              )
              AND EXISTS
              (
                  SELECT 1
                  FROM [counterparty].[CompanyPartyAccounts] AS sourceAccount
                  INNER JOIN OPENJSON(@SourceCompanyIds)
                      WITH ([Id] uniqueidentifier '$') AS readableSource
                      ON readableSource.[Id] = sourceAccount.[CompanyId]
                  WHERE sourceAccount.[PartyId] = p.[Id]
              )
            """);
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

        if (cursor is not null)
        {
            sql.AppendLine(
                $"AND (p.[Code] COLLATE {CodeCollation} > @CursorCode "
                + $"OR (p.[Code] COLLATE {CodeCollation} = @CursorCode "
                + "AND p.[Id] > @CursorPartyId))");
            AddParameter(command, "@CursorCode", cursor.PrimaryText!, DbType.String);
            AddParameter(command, "@CursorPartyId", cursor.PartyId, DbType.Guid);
        }

        sql.AppendLine(
            $"ORDER BY p.[Code] COLLATE {CodeCollation} ASC, p.[Id] ASC;");
        command.CommandText = sql.ToString();
        AddParameter(command, "@Take", query.PageSize + 1, DbType.Int32);
        AddParameter(
            command,
            "@TargetCompanyId",
            authorization.TargetCompanyId,
            DbType.Guid);
        AddParameter(
            command,
            "@SourceCompanyIds",
            JsonSerializer.Serialize(authorization.ReadableSourceCompanyIds),
            DbType.String);

        var rows = new List<LinkCandidatePartyRow>(query.PageSize + 1);
        sqlObserver.Observe(
            CounterpartyLinkCandidateSqlOperation.Page,
            command.CommandText);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LinkCandidatePartyRow(
                reader.GetGuid(0),
                reader.GetString(1),
                Enum.Parse<PartyKind>(reader.GetString(2), ignoreCase: false),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetBoolean(5)));
        }

        return rows;
    }

    private async Task<List<LinkCandidateAccountRow>> ReadVisibleSourceAccountsAsync(
        DbConnection connection,
        IReadOnlyList<Guid> partyIds,
        IReadOnlyList<Guid> sourceCompanyIds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                sourceAccount.[Id],
                sourceAccount.[CompanyId],
                sourceAccount.[PartyId],
                sourceAccount.[Roles],
                sourceAccount.[IsActive],
                sourceAccount.[Version]
            FROM [counterparty].[CompanyPartyAccounts] AS sourceAccount
            INNER JOIN OPENJSON(@PartyIds)
                WITH ([Id] uniqueidentifier '$') AS candidates
                ON candidates.[Id] = sourceAccount.[PartyId]
            INNER JOIN OPENJSON(@SourceCompanyIds)
                WITH ([Id] uniqueidentifier '$') AS readableSource
                ON readableSource.[Id] = sourceAccount.[CompanyId]
            ORDER BY sourceAccount.[PartyId], sourceAccount.[CompanyId], sourceAccount.[Id];
            """;
        AddParameter(
            command,
            "@PartyIds",
            JsonSerializer.Serialize(partyIds),
            DbType.String);
        AddParameter(
            command,
            "@SourceCompanyIds",
            JsonSerializer.Serialize(sourceCompanyIds),
            DbType.String);

        var rows = new List<LinkCandidateAccountRow>();
        sqlObserver.Observe(
            CounterpartyLinkCandidateSqlOperation.SourceAccounts,
            command.CommandText);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LinkCandidateAccountRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                (PartyRole)reader.GetInt32(3),
                reader.GetBoolean(4),
                reader.GetInt64(5)));
        }

        return rows;
    }

    private async Task<CounterpartyLinkCandidatePage> QueryInMemoryTestProviderAsync(
        CounterpartyLinkCandidateQuery query,
        LinkCandidateAuthorization authorization,
        string bindingHash,
        CounterpartyCursorState? cursor,
        CancellationToken cancellationToken)
    {
        var allAccounts = await dbContext.CompanyPartyAccounts
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var readableSourceIds = authorization.ReadableSourceCompanyIds.ToHashSet();
        var visibleSourceAccounts = allAccounts
            .Where(account => readableSourceIds.Contains(account.CompanyId))
            .ToArray();
        var visiblePartyIds = visibleSourceAccounts
            .Select(account => account.PartyId)
            .ToHashSet();
        var alreadyLinkedPartyIds = allAccounts
            .Where(account => account.CompanyId == authorization.TargetCompanyId)
            .Select(account => account.PartyId)
            .ToHashSet();
        var parties = await dbContext.Parties
            .AsNoTracking()
            .Where(party =>
                party.IsActive
                && visiblePartyIds.Contains(party.Id)
                && !alreadyLinkedPartyIds.Contains(party.Id))
            .ToListAsync(cancellationToken);
        var ordered = parties
            .Where(party =>
                query.Query is null
                || ContainsTurkish(party.Code.Value, query.Query)
                || ContainsTurkish(party.LegalName, query.Query)
                || party.TradeName is not null
                && ContainsTurkish(party.TradeName, query.Query))
            .OrderBy(party => party.Code.Value, StringComparer.Ordinal)
            .ThenBy(party => party.Id)
            .ToList();
        if (cursor is not null)
        {
            ordered = ordered
                .Where(party =>
                    string.CompareOrdinal(party.Code.Value, cursor.PrimaryText) > 0
                    || string.Equals(
                        party.Code.Value,
                        cursor.PrimaryText,
                        StringComparison.Ordinal)
                    && party.Id.CompareTo(cursor.PartyId) > 0)
                .ToList();
        }

        var pageRows = ordered.Take(query.PageSize + 1).ToList();
        var hasMore = pageRows.Count > query.PageSize;
        if (hasMore)
        {
            pageRows.RemoveAt(pageRows.Count - 1);
        }

        var items = pageRows.Select(party => new CounterpartyLinkCandidateItem(
                party.Id,
                party.Code.Value,
                party.Kind,
                party.LegalName,
                party.TradeName,
                party.IsActive,
                visibleSourceAccounts
                    .Where(account => account.PartyId == party.Id)
                    .OrderBy(account => account.CompanyId)
                    .ThenBy(account => account.Id)
                    .Select(account => new CounterpartyLinkCandidateSourceAccount(
                        account.Id,
                        account.CompanyId,
                        account.Roles,
                        account.IsActive,
                        FormatVersion(account.Version)))
                    .ToArray()))
            .ToArray();
        var last = pageRows.LastOrDefault();
        return new CounterpartyLinkCandidatePage(
            items,
            hasMore && last is not null
                ? CreateNextCursor(
                    new LinkCandidatePartyRow(
                        last.Id,
                        last.Code.Value,
                        last.Kind,
                        last.LegalName,
                        last.TradeName,
                        last.IsActive),
                    bindingHash)
                : null,
            hasMore);
    }

    private string CreateNextCursor(
        LinkCandidatePartyRow row,
        string bindingHash)
    {
        var now = timeProvider.GetUtcNow();
        return cursorCodec.Protect(new CounterpartyCursorState(
            1,
            bindingHash,
            CounterpartySort.Code,
            row.Code,
            null,
            null,
            row.Id,
            now,
            now + CursorLifetime));
    }

    private static string ComputeBindingHash(
        CounterpartyLinkCandidateRequest request,
        LinkCandidateAuthorization authorization)
    {
        var canonical = new StringBuilder();
        AppendCanonical(canonical, ContractVersion);
        AppendCanonical(canonical, request.ActorId);
        AppendCanonical(canonical, request.SessionBinding);
        AppendCanonical(canonical, $"target:{authorization.TargetCompanyId:D}");
        foreach (var companyId in authorization.AllowedCompanyIds)
        {
            AppendCanonical(canonical, $"allowed:{companyId:D}");
        }

        AppendCanonical(canonical, $"target-revision:{authorization.TargetRevision}");
        AppendPermissionScope(canonical, "target-create", authorization.TargetCreateScope);
        AppendPermissionScope(canonical, "target-read", authorization.TargetReadScope);
        foreach (var source in authorization.SourceEntries.OrderBy(entry => entry.CompanyId))
        {
            AppendCanonical(canonical, $"source:{source.CompanyId:D}");
            AppendCanonical(canonical, source.IsAllowed ? "allowed" : "denied");
            AppendCanonical(canonical, $"revision:{source.Revision}");
            AppendPermissionScope(canonical, "source-read", source.ReadScope);
        }

        foreach (var companyId in authorization.ReadableSourceCompanyIds)
        {
            AppendCanonical(canonical, $"readable-source:{companyId:D}");
        }

        AppendCanonical(canonical, request.Query.Query ?? "-");
        AppendCanonical(
            canonical,
            request.Query.PageSize.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void AppendPermissionScope(
        StringBuilder canonical,
        string name,
        PermissionScope scope)
    {
        AppendCanonical(canonical, $"{name}:granted:{scope.IsGranted}");
        AppendCanonical(canonical, $"{name}:company-wide:{scope.IsCompanyWide}");
        foreach (var role in scope.RoleCodes.Order(StringComparer.Ordinal))
        {
            AppendCanonical(canonical, $"{name}:role:{role}");
        }

        AppendScopeIds(canonical, $"{name}:site", scope.SiteIds);
        AppendScopeIds(canonical, $"{name}:location", scope.LocationIds);
        AppendScopeIds(canonical, $"{name}:work-center", scope.WorkCenterIds);
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

    private static PermissionScope GetScope(
        AuthorizationSnapshot snapshot,
        string permission)
    {
        return snapshot.PermissionScopes.TryGetValue(permission, out var scope)
            ? scope
            : PermissionScope.Denied;
    }

    private static bool IsCompanyWideGranted(PermissionScope scope) =>
        scope.IsGranted && scope.IsCompanyWide;

    private static CounterpartyLinkCandidateSourceAccount ToSourceAccount(
        LinkCandidateAccountRow account) =>
        new(
            account.Id,
            account.CompanyId,
            account.Roles,
            account.IsActive,
            FormatVersion(account.Version));

    private static string FormatVersion(long version) =>
        version.ToString(CultureInfo.InvariantCulture);

    private static bool ContainsTurkish(string value, string query) =>
        TurkishCompare.IndexOf(value, query, CompareOptions.IgnoreCase) >= 0;

    private static string EscapeLikePattern(string value) =>
        value
            .Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal)
            .Replace("_", "~_", StringComparison.Ordinal)
            .Replace("[", "~[", StringComparison.Ordinal);

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

    private sealed record LinkCandidateAuthorization(
        Guid TargetCompanyId,
        IReadOnlyList<Guid> AllowedCompanyIds,
        PermissionScope TargetCreateScope,
        PermissionScope TargetReadScope,
        string TargetRevision,
        IReadOnlyList<SourceAuthorizationEntry> SourceEntries,
        IReadOnlyList<Guid> ReadableSourceCompanyIds);

    private sealed record SourceAuthorizationEntry(
        Guid CompanyId,
        bool IsAllowed,
        PermissionScope ReadScope,
        string Revision);

    private sealed record LinkCandidatePartyRow(
        Guid Id,
        string Code,
        PartyKind Kind,
        string LegalName,
        string? TradeName,
        bool IsActive);

    private sealed record LinkCandidateAccountRow(
        Guid Id,
        Guid CompanyId,
        Guid PartyId,
        PartyRole Roles,
        bool IsActive,
        long Version);
}
