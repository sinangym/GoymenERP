using System.Data;
using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed class CounterpartyPartyDisplayBatchReadServiceV1(
    CounterpartyDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorizationService,
    IOrganizationService organizationService,
    ICounterpartyPartyDisplayBatchSqlObserver sqlObserver)
    : ICounterpartyPartyDisplayBatchReadServiceV1
{
    private const int MaximumPartyCount = 100;

    public async Task<CounterpartyPartyDisplayBatchReadAnswerV1> ReadAuthorizedAsync(
        CounterpartyPartyDisplayBatchReadRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCanonicalize(request, out var partyIds)
            || !RequestMatchesCurrentContext(request))
        {
            return Closed(CounterpartyPartyDisplayBatchReadOutcomeV1.Denied);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        try
        {
            if (!await HasFreshAccessAsync(request, companyId, actorId, cancellationToken))
            {
                return Closed(CounterpartyPartyDisplayBatchReadOutcomeV1.Denied);
            }

            var rows = await ReadRowsAsync(companyId, partyIds, cancellationToken);
            if (!await HasFreshAccessAsync(request, companyId, actorId, cancellationToken))
            {
                return Closed(CounterpartyPartyDisplayBatchReadOutcomeV1.Denied);
            }

            var seen = new HashSet<Guid>();
            var items = new List<CounterpartyPartyDisplayV1>(rows.Count);
            foreach (var row in rows)
            {
                if (row.PartyId == Guid.Empty
                    || string.IsNullOrWhiteSpace(row.Code)
                    || string.IsNullOrWhiteSpace(row.LegalName)
                    || !seen.Add(row.PartyId))
                {
                    return Closed(CounterpartyPartyDisplayBatchReadOutcomeV1.Unavailable);
                }

                items.Add(new CounterpartyPartyDisplayV1(
                    row.PartyId,
                    row.Code,
                    row.LegalName,
                    row.TradeName));
            }

            return new CounterpartyPartyDisplayBatchReadAnswerV1(
                CounterpartyPartyDisplayBatchReadOutcomeV1.Found,
                items);
        }
        catch (UnauthorizedAccessException)
        {
            return Closed(CounterpartyPartyDisplayBatchReadOutcomeV1.Denied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Closed(CounterpartyPartyDisplayBatchReadOutcomeV1.Unavailable);
        }
    }

    private async Task<bool> HasFreshAccessAsync(
        CounterpartyPartyDisplayBatchReadRequestV1 request,
        Guid expectedCompanyId,
        string expectedActorId,
        CancellationToken cancellationToken)
    {
        if (!RemainsCurrent(request, expectedCompanyId, expectedActorId))
        {
            return false;
        }

        var companies = await organizationService.GetAuthorizedCompaniesAsync(cancellationToken);
        if (!RemainsCurrent(request, expectedCompanyId, expectedActorId)
            || !companies.Any(company => company.Id == expectedCompanyId))
        {
            return false;
        }

        var scope = await authorizationService.GetPermissionScopeAsync(
            ErpPermissions.CounterpartyPartiesRead,
            expectedCompanyId,
            cancellationToken);
        return RemainsCurrent(request, expectedCompanyId, expectedActorId)
            && scope.IsGranted
            && scope.IsCompanyWide;
    }

    private async Task<IReadOnlyList<PartyDisplayRow>> ReadRowsAsync(
        Guid companyId,
        IReadOnlyList<Guid> partyIds,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            throw new InvalidOperationException("Party display batch source requires a relational provider.");
        }

        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    p.[Id],
                    p.[Code],
                    p.[LegalName],
                    p.[TradeName]
                FROM [counterparty].[CompanyPartyAccounts] AS a
                INNER JOIN OPENJSON(@PartyIds)
                    WITH ([Id] uniqueidentifier '$') AS requested
                    ON requested.[Id] = a.[PartyId]
                INNER JOIN [counterparty].[Parties] AS p
                    ON p.[Id] = a.[PartyId]
                WHERE a.[CompanyId] = @CompanyId
                  AND a.[IsActive] = 1
                  AND p.[IsActive] = 1
                ORDER BY p.[Id];
                """;
            AddParameter(
                command,
                "@PartyIds",
                JsonSerializer.Serialize(partyIds),
                DbType.String);
            AddParameter(command, "@CompanyId", companyId, DbType.Guid);
            sqlObserver.Observe(command.CommandText);

            var rows = new List<PartyDisplayRow>(partyIds.Count);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new PartyDisplayRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
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

    private bool RequestMatchesCurrentContext(CounterpartyPartyDisplayBatchReadRequestV1 request) =>
        currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty
        && currentUser.CanAccessCompany(companyContext.CompanyId)
        && request.CompanyId == companyContext.CompanyId
        && string.Equals(request.ActorId, currentUser.UserId, StringComparison.Ordinal);

    private bool RemainsCurrent(
        CounterpartyPartyDisplayBatchReadRequestV1 request,
        Guid expectedCompanyId,
        string expectedActorId) =>
        RequestMatchesCurrentContext(request)
        && companyContext.CompanyId == expectedCompanyId
        && string.Equals(currentUser.UserId, expectedActorId, StringComparison.Ordinal);

    private static bool TryCanonicalize(
        CounterpartyPartyDisplayBatchReadRequestV1? request,
        out Guid[] partyIds)
    {
        partyIds = [];
        if (request is null
            || request.CompanyId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ActorId)
            || request.PartyIds is null
            || request.PartyIds.Count is 0 or > MaximumPartyCount
            || request.PartyIds.Any(id => id == Guid.Empty))
        {
            return false;
        }

        partyIds = request.PartyIds.Distinct().Order().ToArray();
        return partyIds.Length == request.PartyIds.Count;
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

    private static CounterpartyPartyDisplayBatchReadAnswerV1 Closed(
        CounterpartyPartyDisplayBatchReadOutcomeV1 outcome) => new(outcome, []);

    private sealed record PartyDisplayRow(
        Guid PartyId,
        string Code,
        string LegalName,
        string? TradeName);
}

internal interface ICounterpartyPartyDisplayBatchSqlObserver
{
    void Observe(string commandText);
}

internal sealed class NullCounterpartyPartyDisplayBatchSqlObserver
    : ICounterpartyPartyDisplayBatchSqlObserver
{
    public void Observe(string commandText)
    {
    }
}
