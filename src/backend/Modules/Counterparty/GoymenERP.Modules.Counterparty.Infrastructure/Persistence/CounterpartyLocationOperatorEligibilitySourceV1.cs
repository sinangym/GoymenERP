using System.Data;
using System.Data.Common;
using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// Internal exact source for the accepted external-location supplier rule. The consuming
/// Organization command owns authorization; this runtime only fences the current company/actor
/// and validates Cari-owned party/account facts.
/// </summary>
internal sealed class CounterpartyLocationOperatorEligibilitySourceV1(
    CounterpartyDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICounterpartyLocationOperatorEligibilitySqlObserver sqlObserver)
    : ICounterpartyLocationOperatorEligibilitySourceV1
{
    public async Task<CounterpartyLocationOperatorEligibilityAnswerV1> ReadForLocationOperatorAsync(
        CounterpartyLocationOperatorEligibilityRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValid(request) || !RequestMatchesCurrentContext(request))
        {
            return Closed(CounterpartyLocationOperatorEligibilityOutcomeV1.Denied);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        try
        {
            var row = await ReadRowAsync(companyId, request.PartyId, cancellationToken);
            if (!RemainsCurrent(request, companyId, actorId))
            {
                return Closed(CounterpartyLocationOperatorEligibilityOutcomeV1.Denied);
            }

            if (row is null)
            {
                return Closed(CounterpartyLocationOperatorEligibilityOutcomeV1.NotFound);
            }

            if (!row.PartyIsActive
                || !row.AccountIsActive
                || (row.Roles & PartyRole.Supplier) != PartyRole.Supplier)
            {
                return Closed(CounterpartyLocationOperatorEligibilityOutcomeV1.Ineligible);
            }

            if (row.CompanyAccountId == Guid.Empty
                || row.PartyId != request.PartyId
                || string.IsNullOrWhiteSpace(row.Code)
                || string.IsNullOrWhiteSpace(row.LegalName)
                || row.PartyVersion <= 0
                || row.AccountVersion <= 0)
            {
                return Closed(CounterpartyLocationOperatorEligibilityOutcomeV1.Unavailable);
            }

            return new CounterpartyLocationOperatorEligibilityAnswerV1(
                CounterpartyLocationOperatorEligibilityOutcomeV1.Eligible,
                new CounterpartyLocationOperatorEligibilityV1(
                    row.PartyId,
                    row.CompanyAccountId,
                    row.Code,
                    row.LegalName,
                    row.TradeName,
                    row.PartyVersion.ToString(CultureInfo.InvariantCulture),
                    row.AccountVersion.ToString(CultureInfo.InvariantCulture)));
        }
        catch (UnauthorizedAccessException)
        {
            return Closed(CounterpartyLocationOperatorEligibilityOutcomeV1.Denied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Closed(CounterpartyLocationOperatorEligibilityOutcomeV1.Unavailable);
        }
    }

    private async Task<LocationOperatorRow?> ReadRowAsync(
        Guid companyId,
        Guid partyId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            throw new InvalidOperationException(
                "Location-operator eligibility requires a relational provider.");
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
                    a.[Id],
                    a.[PartyId],
                    a.[Roles],
                    a.[IsActive],
                    a.[Version],
                    p.[Code],
                    p.[LegalName],
                    p.[TradeName],
                    p.[IsActive],
                    p.[Version]
                FROM [counterparty].[CompanyPartyAccounts] AS a
                INNER JOIN [counterparty].[Parties] AS p
                    ON p.[Id] = a.[PartyId]
                WHERE a.[CompanyId] = @CompanyId
                  AND a.[PartyId] = @PartyId;
                """;
            AddParameter(command, "@CompanyId", companyId, DbType.Guid);
            AddParameter(command, "@PartyId", partyId, DbType.Guid);
            sqlObserver.Observe(command.CommandText);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var row = new LocationOperatorRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                (PartyRole)reader.GetInt32(2),
                reader.GetBoolean(3),
                reader.GetInt64(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetBoolean(8),
                reader.GetInt64(9));
            return await reader.ReadAsync(cancellationToken)
                ? throw new InvalidOperationException(
                    "A company may have only one account for the proposed location operator.")
                : row;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private bool RequestMatchesCurrentContext(
        CounterpartyLocationOperatorEligibilityRequestV1 request) =>
        currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty
        && currentUser.CanAccessCompany(companyContext.CompanyId)
        && request.CompanyId == companyContext.CompanyId
        && string.Equals(request.ActorId, currentUser.UserId, StringComparison.Ordinal);

    private bool RemainsCurrent(
        CounterpartyLocationOperatorEligibilityRequestV1 request,
        Guid expectedCompanyId,
        string expectedActorId) =>
        RequestMatchesCurrentContext(request)
        && companyContext.CompanyId == expectedCompanyId
        && string.Equals(currentUser.UserId, expectedActorId, StringComparison.Ordinal);

    private static bool IsValid(CounterpartyLocationOperatorEligibilityRequestV1? request) =>
        request is not null
        && request.CompanyId != Guid.Empty
        && request.PartyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(request.ActorId)
        && string.Equals(request.ActorId, request.ActorId.Trim(), StringComparison.Ordinal);

    private static void AddParameter(DbCommand command, string name, object value, DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static CounterpartyLocationOperatorEligibilityAnswerV1 Closed(
        CounterpartyLocationOperatorEligibilityOutcomeV1 outcome) => new(outcome, null);

    private sealed record LocationOperatorRow(
        Guid CompanyAccountId,
        Guid PartyId,
        PartyRole Roles,
        bool AccountIsActive,
        long AccountVersion,
        string Code,
        string LegalName,
        string? TradeName,
        bool PartyIsActive,
        long PartyVersion);
}

internal interface ICounterpartyLocationOperatorEligibilitySqlObserver
{
    void Observe(string commandText);
}

internal sealed class NullCounterpartyLocationOperatorEligibilitySqlObserver
    : ICounterpartyLocationOperatorEligibilitySqlObserver
{
    public void Observe(string commandText)
    {
    }
}
