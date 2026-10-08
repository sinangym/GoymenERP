using System.Data;
using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed class CounterpartySalesRecordPartyDisplayQueryV1(
    CounterpartyDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IOrganizationService organizationService,
    ICounterpartySalesRecordPartyDisplaySqlObserver sqlObserver)
    : ICounterpartySalesRecordPartyDisplayOwnerReaderV1
{
    public async Task<CounterpartySalesRecordPartyDisplayAnswerV1> ReadAsync(
        CounterpartySalesRecordPartyDisplayRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!RequestMatchesCurrentContext(request))
        {
            return Closed(CounterpartySalesRecordPartyDisplayOutcomeV1.Denied);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        if (!await HasFreshCompanyAccessAsync(request, companyId, actorId, cancellationToken))
        {
            return Closed(CounterpartySalesRecordPartyDisplayOutcomeV1.Denied);
        }

        var rows = await ReadRowsAsync(companyId, request.PartyIds, cancellationToken);
        if (!RemainsCurrent(request, companyId, actorId)
            || !await HasFreshCompanyAccessAsync(request, companyId, actorId, cancellationToken))
        {
            return Closed(CounterpartySalesRecordPartyDisplayOutcomeV1.Denied);
        }

        var seen = new HashSet<Guid>();
        var requested = request.PartyIds.ToHashSet();
        var items = new List<CounterpartySalesRecordPartyDisplayV1>(rows.Count);
        foreach (var row in rows)
        {
            if (row.PartyId == Guid.Empty
                || !requested.Contains(row.PartyId)
                || !seen.Add(row.PartyId)
                || string.IsNullOrWhiteSpace(row.Code)
                || string.IsNullOrWhiteSpace(row.LegalName))
            {
                return Closed(CounterpartySalesRecordPartyDisplayOutcomeV1.Unavailable);
            }

            items.Add(new CounterpartySalesRecordPartyDisplayV1(
                row.PartyId,
                row.Code,
                row.LegalName,
                row.TradeName));
        }

        return new CounterpartySalesRecordPartyDisplayAnswerV1(
            CounterpartySalesRecordPartyDisplayOutcomeV1.Found,
            items);
    }

    private async Task<bool> HasFreshCompanyAccessAsync(
        CounterpartySalesRecordPartyDisplayRequestV1 request,
        Guid expectedCompanyId,
        string expectedActorId,
        CancellationToken cancellationToken)
    {
        if (!RemainsCurrent(request, expectedCompanyId, expectedActorId))
        {
            return false;
        }

        var companies = await organizationService.GetAuthorizedCompaniesAsync(cancellationToken);
        return RemainsCurrent(request, expectedCompanyId, expectedActorId)
            && companies.Any(company => company.Id == expectedCompanyId);
    }

    private async Task<IReadOnlyList<PartyDisplayRow>> ReadRowsAsync(
        Guid companyId,
        IReadOnlyList<Guid> partyIds,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            throw new InvalidOperationException("Sales record party display source requires a relational provider.");
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
            AddParameter(command, "@PartyIds", JsonSerializer.Serialize(partyIds), DbType.String);
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

    private bool RequestMatchesCurrentContext(CounterpartySalesRecordPartyDisplayRequestV1 request) =>
        currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty
        && currentUser.CanAccessCompany(companyContext.CompanyId)
        && request.CompanyId == companyContext.CompanyId
        && string.Equals(request.ActorId, currentUser.UserId, StringComparison.Ordinal);

    private bool RemainsCurrent(
        CounterpartySalesRecordPartyDisplayRequestV1 request,
        Guid expectedCompanyId,
        string expectedActorId) =>
        RequestMatchesCurrentContext(request)
        && companyContext.CompanyId == expectedCompanyId
        && string.Equals(currentUser.UserId, expectedActorId, StringComparison.Ordinal);

    private static void AddParameter(DbCommand command, string name, object value, DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static CounterpartySalesRecordPartyDisplayAnswerV1 Closed(
        CounterpartySalesRecordPartyDisplayOutcomeV1 outcome) => new(outcome, []);

    private sealed record PartyDisplayRow(Guid PartyId, string Code, string LegalName, string? TradeName);
}

internal interface ICounterpartySalesRecordPartyDisplaySqlObserver
{
    void Observe(string commandText);
}

internal sealed class NullCounterpartySalesRecordPartyDisplaySqlObserver
    : ICounterpartySalesRecordPartyDisplaySqlObserver
{
    public void Observe(string commandText)
    {
    }
}
