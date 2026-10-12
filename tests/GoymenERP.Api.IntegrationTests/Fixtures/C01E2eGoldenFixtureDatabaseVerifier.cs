using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests.Fixtures;

internal static class C01E2eGoldenFixtureDatabaseVerifier
{
    private static readonly Guid GdcCompanyId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid GspCompanyId =
        Guid.Parse("10000000-0000-0000-0000-000000000002");

    internal static async Task<C01E2eGoldenFixtureDatabaseEvidence> VerifyAsync(
        SqlConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var evidence = new C01E2eGoldenFixtureDatabaseEvidence(
            PartyCount: await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [counterparty].[Parties];",
                cancellationToken),
            AccountCount: await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [counterparty].[CompanyPartyAccounts];",
                cancellationToken),
            GdcAccountCount: await ScalarAsync<long>(
                connection,
                """
                SELECT COUNT_BIG(*)
                FROM [counterparty].[CompanyPartyAccounts]
                WHERE [CompanyId] = @CompanyId;
                """,
                cancellationToken,
                new SqlParameter("@CompanyId", GdcCompanyId)),
            GdcGlobalActiveVisibleCount: await ScalarAsync<long>(
                connection,
                """
                SELECT COUNT_BIG(*)
                FROM [counterparty].[Parties] AS [p]
                INNER JOIN [counterparty].[CompanyPartyAccounts] AS [a]
                    ON [a].[PartyId] = [p].[Id]
                WHERE [a].[CompanyId] = @CompanyId
                  AND [p].[IsActive] = 1;
                """,
                cancellationToken,
                new SqlParameter("@CompanyId", GdcCompanyId)),
            GdcGlobalInactiveCount: await ScalarAsync<long>(
                connection,
                """
                SELECT COUNT_BIG(*)
                FROM [counterparty].[Parties] AS [p]
                INNER JOIN [counterparty].[CompanyPartyAccounts] AS [a]
                    ON [a].[PartyId] = [p].[Id]
                WHERE [a].[CompanyId] = @CompanyId
                  AND [p].[IsActive] = 0;
                """,
                cancellationToken,
                new SqlParameter("@CompanyId", GdcCompanyId)),
            GspGlobalActiveVisibleCount: await ScalarAsync<long>(
                connection,
                """
                SELECT COUNT_BIG(*)
                FROM [counterparty].[Parties] AS [p]
                INNER JOIN [counterparty].[CompanyPartyAccounts] AS [a]
                    ON [a].[PartyId] = [p].[Id]
                WHERE [a].[CompanyId] = @CompanyId
                  AND [p].[IsActive] = 1;
                """,
                cancellationToken,
                new SqlParameter("@CompanyId", GspCompanyId)),
            AuditEventCount: await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [audit].[AuditEvents];",
                cancellationToken),
            CommandExecutionCount: await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [idempotency].[CommandExecutions];",
                cancellationToken),
            Sequence: await ReadSequenceAsync(connection, cancellationToken));

        Require(evidence.PartyCount == 54, "Party toplamı 54 olmalıdır.");
        Require(evidence.AccountCount == 55, "CompanyPartyAccount toplamı 55 olmalıdır.");
        Require(evidence.GdcAccountCount == 53, "GDC hesap toplamı 53 olmalıdır.");
        Require(
            evidence.GdcGlobalActiveVisibleCount == 52,
            "GDC global-aktif görünür kayıt toplamı 52 olmalıdır.");
        Require(
            evidence.GdcGlobalInactiveCount == 1,
            "GDC global-pasif kayıt toplamı 1 olmalıdır.");
        Require(
            evidence.GspGlobalActiveVisibleCount == 2,
            "GSP global-aktif görünür kayıt toplamı 2 olmalıdır.");
        Require(
            evidence.Sequence.CurrentValue == 55 && evidence.Sequence.LastUsedValue is null,
            "PartyCodeSequence sonraki değeri 55 olmalıdır.");

        await RequireScalarAsync(
            connection,
            """
            SELECT CONCAT(
                [p].[LegalName], N'|', [p].[TradeName], N'|', [a].[Roles], N'|',
                CONVERT(int, [p].[IsActive]), N'|', CONVERT(int, [a].[IsActive]))
            FROM [counterparty].[Parties] AS [p]
            INNER JOIN [counterparty].[CompanyPartyAccounts] AS [a]
                ON [a].[PartyId] = [p].[Id]
            WHERE [p].[Code] = N'CAR-000001' AND [a].[CompanyId] = @CompanyId;
            """,
            "Örnek Metal Sanayi ve Ticaret A.Ş.|Örnek Metal|3|1|1",
            cancellationToken,
            new SqlParameter("@CompanyId", GdcCompanyId));
        await RequireScalarAsync(
            connection,
            """
            SELECT CONCAT(
                [p].[LegalName], N'|', [a].[Roles], N'|',
                CONVERT(int, [p].[IsActive]), N'|', CONVERT(int, [a].[IsActive]))
            FROM [counterparty].[Parties] AS [p]
            INNER JOIN [counterparty].[CompanyPartyAccounts] AS [a]
                ON [a].[PartyId] = [p].[Id]
            WHERE [p].[Code] = N'CAR-000002' AND [a].[CompanyId] = @CompanyId;
            """,
            "Pasif Hesap Metal A.Ş.|1|1|0",
            cancellationToken,
            new SqlParameter("@CompanyId", GdcCompanyId));
        await RequireScalarAsync(
            connection,
            """
            SELECT CONCAT(
                [p].[LegalName], N'|',
                MAX(CASE WHEN [a].[CompanyId] = @Gdc THEN [a].[Roles] END), N'|',
                MAX(CASE WHEN [a].[CompanyId] = @Gsp THEN [a].[Roles] END))
            FROM [counterparty].[Parties] AS [p]
            INNER JOIN [counterparty].[CompanyPartyAccounts] AS [a]
                ON [a].[PartyId] = [p].[Id]
            WHERE [p].[Code] = N'CAR-000003'
            GROUP BY [p].[LegalName];
            """,
            "Ayşe Örnek|8|2",
            cancellationToken,
            new SqlParameter("@Gdc", GdcCompanyId),
            new SqlParameter("@Gsp", GspCompanyId));
        await RequireScalarAsync(
            connection,
            """
            SELECT CONCAT(
                [p].[LegalName], N'|', CONVERT(int, [p].[IsActive]), N'|',
                CONVERT(int, [a].[IsActive]), N'|', [a].[Roles])
            FROM [counterparty].[Parties] AS [p]
            INNER JOIN [counterparty].[CompanyPartyAccounts] AS [a]
                ON [a].[PartyId] = [p].[Id]
            WHERE [p].[Code] = N'CAR-000053' AND [a].[CompanyId] = @CompanyId;
            """,
            "Tarihsel Cari Sanayi A.Ş.|0|1|4",
            cancellationToken,
            new SqlParameter("@CompanyId", GdcCompanyId));
        await RequireScalarAsync(
            connection,
            """
            SELECT CONCAT([p].[LegalName], N'|', [a].[Roles])
            FROM [counterparty].[Parties] AS [p]
            INNER JOIN [counterparty].[CompanyPartyAccounts] AS [a]
                ON [a].[PartyId] = [p].[Id]
            WHERE [p].[Code] = N'CAR-000054' AND [a].[CompanyId] = @CompanyId;
            """,
            "Yalnız GSP Metal A.Ş.|2",
            cancellationToken,
            new SqlParameter("@CompanyId", GspCompanyId));

        Require(
            await ScalarAsync<long>(
                connection,
                "SELECT COUNT_BIG(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [ContactInformationJson] IS NOT NULL;",
                cancellationToken) == 4,
            "C-01 adres filtresi için tam dört şirket hesabı hazırlanmalıdır.");
        Require(
            await AddressMatchCountAsync(connection, GdcCompanyId, "delivery", "İstanbul", "Çankaya", cancellationToken) == 2,
            "GDC adres filtresi ilk sayfa içi ve dışı tam iki eşleşme üretmelidir.");
        Require(
            await AddressMatchCountAsync(connection, GspCompanyId, "delivery", "İstanbul", "Çankaya", cancellationToken) == 1,
            "GSP adres filtresi şirket sınırındaki tek eşleşmeyi üretmelidir.");
        Require(
            await AddressMatchCountAsync(connection, GdcCompanyId, "billing", "İstanbul", "Çankaya", cancellationToken) == 0,
            "Şehir, ilçe ve adres türü aynı adres satırında eşleşmelidir.");

        await RequireScalarAsync(
            connection,
            """
            SELECT CONCAT([LegalName], N'|', [TradeName])
            FROM [counterparty].[Parties]
            WHERE [Code] = N'CAR-000004';
            """,
            "Işık Profil Sanayi A.Ş.|Işık Profil",
            cancellationToken);
        await RequireScalarAsync(
            connection,
            """
            SELECT CONCAT([LegalName], N'|', [TradeName])
            FROM [counterparty].[Parties]
            WHERE [Code] = N'CAR-000005';
            """,
            "İpek Çelik Sanayi A.Ş.|İpek Çelik",
            cancellationToken);

        return evidence;
    }

    private static Task<long> AddressMatchCountAsync(
        SqlConnection connection,
        Guid companyId,
        string purpose,
        string city,
        string district,
        CancellationToken cancellationToken) =>
        ScalarAsync<long>(
            connection,
            """
            SELECT COUNT_BIG(DISTINCT [a].[Id])
            FROM [counterparty].[CompanyPartyAccounts] AS [a]
            CROSS APPLY OPENJSON([a].[ContactInformationJson], '$.information.addresses')
                WITH
                (
                    [purpose] nvarchar(120) '$.purpose',
                    [city] nvarchar(100) '$.city',
                    [district] nvarchar(100) '$.district'
                ) AS [address]
            WHERE [a].[CompanyId] = @CompanyId
              AND [address].[purpose] = @Purpose
              AND [address].[city] COLLATE Turkish_100_CI_AS = @City
              AND [address].[district] COLLATE Turkish_100_CI_AS = @District;
            """,
            cancellationToken,
            new SqlParameter("@CompanyId", companyId),
            new SqlParameter("@Purpose", purpose),
            new SqlParameter("@City", city),
            new SqlParameter("@District", district));

    private static async Task<C01E2eSequenceEvidence> ReadSequenceAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT CONVERT(bigint, [current_value]), CONVERT(bigint, [last_used_value])
            FROM sys.sequences
            WHERE [name] = N'PartyCodeSequence'
              AND [schema_id] = SCHEMA_ID(N'counterparty');
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "counterparty.PartyCodeSequence exact tek kayıt olarak bulunmalıdır.");
        }

        var evidence = new C01E2eSequenceEvidence(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetInt64(1));
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "counterparty.PartyCodeSequence exact tek kayıt olarak bulunmalıdır.");
        }

        return evidence;
    }

    private static async Task RequireScalarAsync(
        SqlConnection connection,
        string commandText,
        string expected,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters)
    {
        var actual = await ScalarAsync<string>(
            connection,
            commandText,
            cancellationToken,
            parameters);
        Require(
            string.Equals(actual, expected, StringComparison.Ordinal),
            $"Golden özel satır uyuşmuyor. Beklenen: '{expected}', gerçek: '{actual}'.");
    }

    private static async Task<T> ScalarAsync<T>(
        SqlConnection connection,
        string commandText,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not T typed)
        {
            throw new InvalidOperationException(
                $"Golden SQL doğrulaması {typeof(T).Name} yerine "
                + $"{value?.GetType().Name ?? "null"} döndürdü.");
        }

        return typed;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal sealed record C01E2eGoldenFixtureDatabaseEvidence(
    long PartyCount,
    long AccountCount,
    long GdcAccountCount,
    long GdcGlobalActiveVisibleCount,
    long GdcGlobalInactiveCount,
    long GspGlobalActiveVisibleCount,
    long AuditEventCount,
    long CommandExecutionCount,
    C01E2eSequenceEvidence Sequence);

internal sealed record C01E2eSequenceEvidence(long CurrentValue, long? LastUsedValue);
