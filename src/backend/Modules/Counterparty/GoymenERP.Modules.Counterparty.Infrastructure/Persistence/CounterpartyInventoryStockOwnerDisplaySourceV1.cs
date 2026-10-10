using System.Data;
using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// Internal runtime for the record-bound Inventory stock-owner projection. The only public DI surface is
/// <see cref="ICounterpartyInventoryStockOwnerDisplaySourceV1"/>; no owner reader is resolvable.
/// Historical records keep their same-company Party/account display after deactivation; this source
/// is not a selection or transaction-eligibility surface.
/// </summary>
internal sealed class CounterpartyInventoryStockOwnerDisplaySourceV1(
    CounterpartyDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IOrganizationService organizationService,
    ICounterpartyInventoryStockOwnerDisplaySqlObserver sqlObserver)
    : ICounterpartyInventoryStockOwnerDisplaySourceV1
{
    private const int MaximumPartyCount = 100;

    public async Task<CounterpartyInventoryStockOwnerDisplayAnswerV1> ReadForAuthorizedInventoryStockOwnersAsync(
        CounterpartyInventoryStockOwnerDisplayRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCanonicalize(request, out var canonical)
            || !RequestMatchesCurrentContext(canonical))
        {
            return Closed(CounterpartyInventoryStockOwnerDisplayOutcomeV1.Denied);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        try
        {
            if (!await HasFreshCompanyAccessAsync(canonical, companyId, actorId, cancellationToken))
            {
                return Closed(CounterpartyInventoryStockOwnerDisplayOutcomeV1.Denied);
            }

            var rows = await ReadRowsAsync(companyId, canonical.PartyIds, cancellationToken);
            if (!RemainsCurrent(canonical, companyId, actorId)
                || !await HasFreshCompanyAccessAsync(canonical, companyId, actorId, cancellationToken))
            {
                return Closed(CounterpartyInventoryStockOwnerDisplayOutcomeV1.Denied);
            }

            var requested = canonical.PartyIds.ToHashSet();
            var seen = new HashSet<Guid>();
            var items = new List<CounterpartyInventoryStockOwnerDisplayV1>(rows.Count);
            foreach (var row in rows)
            {
                if (row.PartyId == Guid.Empty
                    || !requested.Contains(row.PartyId)
                    || !seen.Add(row.PartyId)
                    || string.IsNullOrWhiteSpace(row.Code)
                    || string.IsNullOrWhiteSpace(row.LegalName))
                {
                    return Closed(CounterpartyInventoryStockOwnerDisplayOutcomeV1.Unavailable);
                }

                items.Add(new CounterpartyInventoryStockOwnerDisplayV1(
                    row.PartyId,
                    row.Code,
                    row.LegalName,
                    row.TradeName));
            }

            return new CounterpartyInventoryStockOwnerDisplayAnswerV1(
                CounterpartyInventoryStockOwnerDisplayOutcomeV1.Found,
                items);
        }
        catch (UnauthorizedAccessException)
        {
            return Closed(CounterpartyInventoryStockOwnerDisplayOutcomeV1.Denied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Closed(CounterpartyInventoryStockOwnerDisplayOutcomeV1.Unavailable);
        }
    }

    private async Task<bool> HasFreshCompanyAccessAsync(
        CounterpartyInventoryStockOwnerDisplayRequestV1 request,
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
            throw new InvalidOperationException("Inventory stock-owner record party display source requires a relational provider.");
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

    private bool RequestMatchesCurrentContext(CounterpartyInventoryStockOwnerDisplayRequestV1 request) =>
        currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty
        && currentUser.CanAccessCompany(companyContext.CompanyId)
        && request.CompanyId == companyContext.CompanyId
        && string.Equals(request.ActorId, currentUser.UserId, StringComparison.Ordinal);

    private bool RemainsCurrent(
        CounterpartyInventoryStockOwnerDisplayRequestV1 request,
        Guid expectedCompanyId,
        string expectedActorId) =>
        RequestMatchesCurrentContext(request)
        && companyContext.CompanyId == expectedCompanyId
        && string.Equals(currentUser.UserId, expectedActorId, StringComparison.Ordinal);

    private static bool TryCanonicalize(
        CounterpartyInventoryStockOwnerDisplayRequestV1? request,
        out CounterpartyInventoryStockOwnerDisplayRequestV1 canonical)
    {
        canonical = null!;
        if (request is null
            || request.CompanyId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ActorId)
            || request.PartyIds is null
            || request.PartyIds.Count is 0 or > MaximumPartyCount
            || request.PartyIds.Any(id => id == Guid.Empty))
        {
            return false;
        }

        var partyIds = request.PartyIds.Distinct().Order().ToArray();
        if (partyIds.Length != request.PartyIds.Count)
        {
            return false;
        }

        canonical = request with { PartyIds = partyIds };
        return true;
    }

    private static void AddParameter(DbCommand command, string name, object value, DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static CounterpartyInventoryStockOwnerDisplayAnswerV1 Closed(
        CounterpartyInventoryStockOwnerDisplayOutcomeV1 outcome) => new(outcome, []);

    private sealed record PartyDisplayRow(Guid PartyId, string Code, string LegalName, string? TradeName);
}

internal interface ICounterpartyInventoryStockOwnerDisplaySqlObserver
{
    void Observe(string commandText);
}

internal sealed class NullCounterpartyInventoryStockOwnerDisplaySqlObserver
    : ICounterpartyInventoryStockOwnerDisplaySqlObserver
{
    public void Observe(string commandText)
    {
    }
}
