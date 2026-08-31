using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests.Fixtures;

internal static class C01E2eGoldenFixture
{
    internal const string FixtureId = "C01-E2E-GOLDEN-V1";
    internal const int ExpectedByteLength = 29_817;
    internal const string ExpectedSha256 =
        "f23551944a65fe29681d768422b181dd73d68b1bdf8216e42bc643713e4d79d7";

    private const string ResourceName =
        "GoymenERP.Api.IntegrationTests.Fixtures.C01-E2E-GOLDEN-V1.json";

    internal static C01E2eGoldenFixtureArtifact Load()
    {
        using var stream = typeof(C01E2eGoldenFixture).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Test fixture resource '{ResourceName}' was not embedded.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        if (bytes.Length != ExpectedByteLength)
        {
            throw new InvalidOperationException(
                $"{FixtureId} byte length is {bytes.Length}; expected {ExpectedByteLength}.");
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(sha256, ExpectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{FixtureId} SHA-256 is {sha256}; expected {ExpectedSha256}.");
        }

        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            throw new InvalidOperationException($"{FixtureId} must not contain a UTF-8 BOM.");
        }

        var document = JsonDocument.Parse(bytes);
        ValidateEnvelope(document.RootElement);
        ValidateCanonicalSerialization(document.RootElement, bytes);
        return new C01E2eGoldenFixtureArtifact(bytes, sha256, document);
    }

    internal static async Task ApplyAsync(
        SqlConnection connection,
        C01E2eGoldenFixtureArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(artifact);

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var partiesJson = artifact.Root.GetProperty("parties").GetRawText();
        var accountsJson = artifact.Root.GetProperty("companyPartyAccounts").GetRawText();
        await using var transaction = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (SqlTransaction)transaction;
                command.CommandText =
                    """
                    DECLARE @LockResult int;
                    EXEC @LockResult = sys.sp_getapplock
                        @Resource = N'C01-E2E-GOLDEN-V1',
                        @LockMode = N'Exclusive',
                        @LockOwner = N'Transaction',
                        @LockTimeout = 0;

                    IF @LockResult < 0
                        THROW 51020, 'c01_fixture_lock_failed', 1;

                    DECLARE @PartyMatches bigint =
                    (
                        SELECT COUNT_BIG(*)
                        FROM [counterparty].[Parties] AS [p]
                        WHERE [p].[Id] IN
                            (
                                SELECT [Id]
                                FROM OPENJSON(@Parties)
                                WITH ([Id] uniqueidentifier '$.id')
                            )
                           OR [p].[Code] IN
                            (
                                SELECT [Code]
                                FROM OPENJSON(@Parties)
                                WITH ([Code] nvarchar(32) '$.code')
                            )
                    );
                    DECLARE @AccountMatches bigint =
                    (
                        SELECT COUNT_BIG(*)
                        FROM [counterparty].[CompanyPartyAccounts] AS [a]
                        WHERE [a].[Id] IN
                            (
                                SELECT [Id]
                                FROM OPENJSON(@Accounts)
                                WITH ([Id] uniqueidentifier '$.id')
                        )
                    );
                    DECLARE @TotalParties bigint =
                        (SELECT COUNT_BIG(*) FROM [counterparty].[Parties]);
                    DECLARE @TotalAccounts bigint =
                        (SELECT COUNT_BIG(*) FROM [counterparty].[CompanyPartyAccounts]);
                    DECLARE @SequenceCurrent bigint =
                    (
                        SELECT CONVERT(bigint, [current_value])
                        FROM sys.sequences
                        WHERE [name] = N'PartyCodeSequence'
                          AND [schema_id] = SCHEMA_ID(N'counterparty')
                    );
                    DECLARE @SequenceLastUsed bigint =
                    (
                        SELECT CONVERT(bigint, [last_used_value])
                        FROM sys.sequences
                        WHERE [name] = N'PartyCodeSequence'
                          AND [schema_id] = SCHEMA_ID(N'counterparty')
                    );

                    IF @PartyMatches = 54 AND @AccountMatches = 55
                    BEGIN
                        DECLARE @ExactPartyMatches bigint =
                        (
                            SELECT COUNT_BIG(*)
                            FROM OPENJSON(@Parties)
                            WITH
                            (
                                [Id] uniqueidentifier '$.id',
                                [Code] nvarchar(32) '$.code',
                                [Kind] nvarchar(32) '$.kind',
                                [LegalName] nvarchar(250) '$.legalName',
                                [TradeName] nvarchar(200) '$.tradeName',
                                [IsActive] bit '$.isActive',
                                [Version] bigint '$.version',
                                [CreatedAtUtc] datetimeoffset '$.createdAtUtc',
                                [UpdatedAtUtc] datetimeoffset '$.updatedAtUtc'
                            ) AS [expected]
                            INNER JOIN [counterparty].[Parties] AS [actual]
                                ON [actual].[Id] = [expected].[Id]
                               AND [actual].[Code] = [expected].[Code]
                               AND [actual].[Kind] = [expected].[Kind]
                               AND [actual].[LegalName] = [expected].[LegalName]
                               AND
                               (
                                   [actual].[TradeName] = [expected].[TradeName]
                                   OR ([actual].[TradeName] IS NULL AND [expected].[TradeName] IS NULL)
                               )
                               AND [actual].[IsActive] = [expected].[IsActive]
                               AND [actual].[Version] = [expected].[Version]
                               AND [actual].[CreatedAtUtc] = [expected].[CreatedAtUtc]
                               AND [actual].[UpdatedAtUtc] = [expected].[UpdatedAtUtc]
                        );
                        DECLARE @ExactAccountMatches bigint =
                        (
                            SELECT COUNT_BIG(*)
                            FROM OPENJSON(@Accounts)
                            WITH
                            (
                                [Id] uniqueidentifier '$.id',
                                [CompanyId] uniqueidentifier '$.companyId',
                                [PartyId] uniqueidentifier '$.partyId',
                                [Roles] int '$.roles',
                                [ExternalIntegrationCode] nvarchar(128) '$.externalIntegrationCode',
                                [IsActive] bit '$.isActive',
                                [Version] bigint '$.version',
                                [CreatedAtUtc] datetimeoffset '$.createdAtUtc',
                                [UpdatedAtUtc] datetimeoffset '$.updatedAtUtc'
                            ) AS [expected]
                            INNER JOIN [counterparty].[CompanyPartyAccounts] AS [actual]
                                ON [actual].[Id] = [expected].[Id]
                               AND [actual].[CompanyId] = [expected].[CompanyId]
                               AND [actual].[PartyId] = [expected].[PartyId]
                               AND [actual].[Roles] = [expected].[Roles]
                               AND
                               (
                                   [actual].[ExternalIntegrationCode] = [expected].[ExternalIntegrationCode]
                                   OR
                                   (
                                       [actual].[ExternalIntegrationCode] IS NULL
                                       AND [expected].[ExternalIntegrationCode] IS NULL
                                   )
                               )
                               AND [actual].[IsActive] = [expected].[IsActive]
                               AND [actual].[Version] = [expected].[Version]
                               AND [actual].[CreatedAtUtc] = [expected].[CreatedAtUtc]
                               AND [actual].[UpdatedAtUtc] = [expected].[UpdatedAtUtc]
                        );

                        IF @ExactPartyMatches = 54
                           AND @ExactAccountMatches = 55
                           AND @TotalParties = 54
                           AND @TotalAccounts = 55
                           AND @SequenceCurrent = 55
                           AND @SequenceLastUsed IS NULL
                            THROW 51021, 'fixture_already_applied', 1;

                        THROW 51022, 'c01_fixture_conflict', 1;
                    END;

                    IF @PartyMatches <> 0 OR @AccountMatches <> 0
                        THROW 51022, 'c01_fixture_conflict', 1;

                    IF EXISTS (SELECT 1 FROM [counterparty].[Parties])
                       OR EXISTS (SELECT 1 FROM [counterparty].[CompanyPartyAccounts])
                        THROW 51025, 'c01_fixture_requires_empty_counterparty_store', 1;

                    INSERT INTO [counterparty].[Parties]
                        ([Id], [Code], [Kind], [LegalName], [TradeName], [IsActive],
                         [Version], [CreatedAtUtc], [UpdatedAtUtc])
                    SELECT
                        [Id], [Code], [Kind], [LegalName], [TradeName], [IsActive],
                        [Version], [CreatedAtUtc], [UpdatedAtUtc]
                    FROM OPENJSON(@Parties)
                    WITH
                    (
                        [Id] uniqueidentifier '$.id',
                        [Code] nvarchar(32) '$.code',
                        [Kind] nvarchar(32) '$.kind',
                        [LegalName] nvarchar(250) '$.legalName',
                        [TradeName] nvarchar(200) '$.tradeName',
                        [IsActive] bit '$.isActive',
                        [Version] bigint '$.version',
                        [CreatedAtUtc] datetimeoffset '$.createdAtUtc',
                        [UpdatedAtUtc] datetimeoffset '$.updatedAtUtc'
                    );

                    INSERT INTO [counterparty].[CompanyPartyAccounts]
                        ([Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode],
                         [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
                    SELECT
                        [Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode],
                        [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc]
                    FROM OPENJSON(@Accounts)
                    WITH
                    (
                        [Id] uniqueidentifier '$.id',
                        [CompanyId] uniqueidentifier '$.companyId',
                        [PartyId] uniqueidentifier '$.partyId',
                        [Roles] int '$.roles',
                        [ExternalIntegrationCode] nvarchar(128) '$.externalIntegrationCode',
                        [IsActive] bit '$.isActive',
                        [Version] bigint '$.version',
                        [CreatedAtUtc] datetimeoffset '$.createdAtUtc',
                        [UpdatedAtUtc] datetimeoffset '$.updatedAtUtc'
                    );

                    ALTER SEQUENCE [counterparty].[PartyCodeSequence] RESTART WITH 55;

                    IF (SELECT COUNT_BIG(*) FROM [counterparty].[Parties]
                        WHERE [Id] IN
                        (
                            SELECT [Id]
                            FROM OPENJSON(@Parties)
                            WITH ([Id] uniqueidentifier '$.id')
                        )) <> 54
                        THROW 51023, 'c01_fixture_party_count_mismatch', 1;

                    IF (SELECT COUNT_BIG(*) FROM [counterparty].[CompanyPartyAccounts]
                        WHERE [Id] IN
                        (
                            SELECT [Id]
                            FROM OPENJSON(@Accounts)
                            WITH ([Id] uniqueidentifier '$.id')
                        )) <> 55
                        THROW 51024, 'c01_fixture_account_count_mismatch', 1;
                    """;
                command.Parameters.AddWithValue("@Parties", partiesJson);
                command.Parameters.AddWithValue("@Accounts", accountsJson);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void ValidateEnvelope(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || root.GetProperty("fixtureId").GetString() != FixtureId
            || root.GetProperty("schemaVersion").GetInt32() != 1
            || root.GetProperty("sequenceNextValue").GetInt32() != 55
            || root.GetProperty("parties").GetArrayLength() != 54
            || root.GetProperty("companyPartyAccounts").GetArrayLength() != 55)
        {
            throw new InvalidOperationException($"{FixtureId} envelope is invalid.");
        }

        var actualProperties = root.EnumerateObject().Select(property => property.Name).ToArray();
        string[] expectedProperties =
        [
            "companyPartyAccounts",
            "fixtureId",
            "parties",
            "schemaVersion",
            "sequenceNextValue",
        ];
        if (!actualProperties.SequenceEqual(expectedProperties, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"{FixtureId} envelope properties are not canonical.");
        }
    }

    private static void ValidateCanonicalSerialization(JsonElement root, byte[] source)
    {
        using var buffer = new MemoryStream(source.Length);
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = false,
            }))
        {
            WriteCanonical(writer, root);
        }

        if (!source.AsSpan().SequenceEqual(buffer.ToArray()))
        {
            throw new InvalidOperationException(
                $"{FixtureId} is not recursively key-sorted compact canonical JSON.");
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported JSON token {element.ValueKind} in {FixtureId}.");
        }
    }
}

internal sealed class C01E2eGoldenFixtureArtifact(
    byte[] bytes,
    string sha256,
    JsonDocument document) : IDisposable
{
    internal byte[] Bytes { get; } = bytes;

    internal string Sha256 { get; } = sha256;

    internal JsonElement Root => document.RootElement;

    public void Dispose() => document.Dispose();
}
