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
/// Internal runtime for the record-bound Purchasing projection. The only public DI surface is
/// <see cref="ICounterpartyPurchasingRecordPartyDisplaySourceV1"/>; no owner reader is resolvable.
/// Historical records keep their same-company Party/account display after deactivation; this source
/// is not a selection or transaction-eligibility surface.
/// </summary>
internal sealed class CounterpartyPurchasingRecordPartyDisplaySourceV1(
    CounterpartyDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IOrganizationService organizationService,
    ICounterpartyPurchasingRecordPartyDisplaySqlObserver sqlObserver)
    : ICounterpartyPurchasingRecordPartyDisplaySourceV1
{
    private const int MaximumPartyCount = 100;

    public async Task<CounterpartyPurchasingRecordPartyDisplayAnswerV1> ReadForAuthorizedPurchasingRecordsAsync(
        CounterpartyPurchasingRecordPartyDisplayRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCanonicalize(request, out var canonical)
            || !RequestMatchesCurrentContext(canonical))
        {
            return Closed(CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Denied);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        try
        {
            if (!await HasFreshCompanyAccessAsync(canonical, companyId, actorId, cancellationToken))
            {
                return Closed(CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Denied);
            }

            var rows = await ReadRowsAsync(companyId, canonical.PartyIds, cancellationToken);
            if (!RemainsCurrent(canonical, companyId, actorId)
                || !await HasFreshCompanyAccessAsync(canonical, companyId, actorId, cancellationToken))
            {
                return Closed(CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Denied);
            }

            var requested = canonical.PartyIds.ToHashSet();
            var seen = new HashSet<Guid>();
            var items = new List<CounterpartyPurchasingRecordPartyDisplayV1>(rows.Count);
            foreach (var row in rows)
            {
                if (row.PartyId == Guid.Empty
                    || !requested.Contains(row.PartyId)
                    || !seen.Add(row.PartyId)
                    || string.IsNullOrWhiteSpace(row.Code)
                    || string.IsNullOrWhiteSpace(row.LegalName))
                {
                    return Closed(CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Unavailable);
                }

                items.Add(new CounterpartyPurchasingRecordPartyDisplayV1(
                    row.PartyId,
                    row.Code,
                    row.LegalName,
                    row.TradeName));
            }

            return new CounterpartyPurchasingRecordPartyDisplayAnswerV1(
                CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Found,
                items);
        }
        catch (UnauthorizedAccessException)
        {
            return Closed(CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Denied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Closed(CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Unavailable);
        }
    }

    private async Task<bool> HasFreshCompanyAccessAsync(
        CounterpartyPurchasingRecordPartyDisplayRequestV1 request,
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
            throw new InvalidOperationException("Purchasing record party display source requires a relational provider.");
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

    private bool RequestMatchesCurrentContext(CounterpartyPurchasingRecordPartyDisplayRequestV1 request) =>
        currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty
        && currentUser.CanAccessCompany(companyContext.CompanyId)
        && request.CompanyId == companyContext.CompanyId
        && string.Equals(request.ActorId, currentUser.UserId, StringComparison.Ordinal);

    private bool RemainsCurrent(
        CounterpartyPurchasingRecordPartyDisplayRequestV1 request,
        Guid expectedCompanyId,
        string expectedActorId) =>
        RequestMatchesCurrentContext(request)
        && companyContext.CompanyId == expectedCompanyId
        && string.Equals(currentUser.UserId, expectedActorId, StringComparison.Ordinal);

    private static bool TryCanonicalize(
        CounterpartyPurchasingRecordPartyDisplayRequestV1? request,
        out CounterpartyPurchasingRecordPartyDisplayRequestV1 canonical)
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

    private static CounterpartyPurchasingRecordPartyDisplayAnswerV1 Closed(
        CounterpartyPurchasingRecordPartyDisplayOutcomeV1 outcome) => new(outcome, []);

    private sealed record PartyDisplayRow(Guid PartyId, string Code, string LegalName, string? TradeName);
}

internal interface ICounterpartyPurchasingRecordPartyDisplaySqlObserver
{
    void Observe(string commandText);
}

internal sealed class NullCounterpartyPurchasingRecordPartyDisplaySqlObserver
    : ICounterpartyPurchasingRecordPartyDisplaySqlObserver
{
    public void Observe(string commandText)
    {
    }
}
