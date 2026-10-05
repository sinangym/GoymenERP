using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;

public partial class InitialWorkDesiredStateStorage : Migration
{
    private const string KeyBytesCheck =
        "DATALENGTH([SourceKeyUtf16Be]) > 0 AND DATALENGTH([SourceKeyUtf16Be]) % 2 = 0 "
        + "AND DATALENGTH([ScopeKeyUtf16Be]) > 0 AND DATALENGTH([ScopeKeyUtf16Be]) % 2 = 0 "
        + "AND [SourceKeyDigest] = CONVERT(binary(32), HASHBYTES('SHA2_256', [SourceKeyUtf16Be])) "
        + "AND [ScopeKeyDigest] = CONVERT(binary(32), HASHBYTES('SHA2_256', [ScopeKeyUtf16Be]))";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "work");

        migrationBuilder.CreateTable(
            name: "DesiredStateSnapshots",
            schema: "work",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceKeyDigest = table.Column<byte[]>(type: "binary(32)", nullable: false),
                ScopeKeyDigest = table.Column<byte[]>(type: "binary(32)", nullable: false),
                SourceKeyUtf16Be = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                ScopeKeyUtf16Be = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                HighestObservationOrdinal = table.Column<long>(type: "bigint", nullable: false),
                HighestAvailability = table.Column<int>(type: "int", nullable: false),
                HighestDesiredState = table.Column<int>(type: "int", nullable: true),
                HighestEvidenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                HighestPayloadSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                EffectiveObservationOrdinal = table.Column<long>(type: "bigint", nullable: true),
                EffectiveDesiredState = table.Column<int>(type: "int", nullable: true),
                EffectiveEvidenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                EffectivePayloadSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DesiredStateSnapshots", x => new { x.CompanyId, x.SourceKeyDigest, x.ScopeKeyDigest });
                table.CheckConstraint("CK_WorkSnapshots_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_WorkSnapshots_KeyBytes", KeyBytesCheck);
                table.CheckConstraint("CK_WorkSnapshots_HighestHash", "DATALENGTH([HighestPayloadSha256]) = 64 AND [HighestPayloadSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                table.CheckConstraint("CK_WorkSnapshots_EffectiveHash", "[EffectivePayloadSha256] IS NULL OR (DATALENGTH([EffectivePayloadSha256]) = 64 AND [EffectivePayloadSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')");
                table.CheckConstraint("CK_WorkSnapshots_State",
                    "[HighestObservationOrdinal] >= 1 AND [HighestEvidenceId] <> '00000000-0000-0000-0000-000000000000' AND "
                    + "(([HighestAvailability] = 1 AND [HighestDesiredState] IN (1,2) AND [EffectiveObservationOrdinal] = [HighestObservationOrdinal] "
                    + "AND [EffectiveDesiredState] = [HighestDesiredState] AND [EffectiveEvidenceId] = [HighestEvidenceId] "
                    + "AND [EffectivePayloadSha256] = [HighestPayloadSha256]) OR ([HighestAvailability] = 2 AND [HighestDesiredState] IS NULL AND "
                    + "(([EffectiveObservationOrdinal] IS NULL AND [EffectiveDesiredState] IS NULL AND [EffectiveEvidenceId] IS NULL AND [EffectivePayloadSha256] IS NULL) OR "
                    + "([EffectiveObservationOrdinal] >= 1 AND [EffectiveObservationOrdinal] < [HighestObservationOrdinal] AND [EffectiveDesiredState] IN (1,2) "
                    + "AND [EffectiveEvidenceId] IS NOT NULL AND [EffectiveEvidenceId] <> '00000000-0000-0000-0000-000000000000' AND [EffectivePayloadSha256] IS NOT NULL))))");
                table.CheckConstraint("CK_WorkSnapshots_Version", "[Version] >= 1");
                table.ForeignKey(
                    name: "FK_DesiredStateSnapshots_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ObservationInbox",
            schema: "work",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceKeyDigest = table.Column<byte[]>(type: "binary(32)", nullable: false),
                ScopeKeyDigest = table.Column<byte[]>(type: "binary(32)", nullable: false),
                SourceKeyUtf16Be = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                ScopeKeyUtf16Be = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                ObservationOrdinal = table.Column<long>(type: "bigint", nullable: false),
                Availability = table.Column<int>(type: "int", nullable: false),
                DesiredState = table.Column<int>(type: "int", nullable: true),
                EvidenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PayloadSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ObservationInbox", x => new { x.CompanyId, x.SourceKeyDigest, x.ScopeKeyDigest, x.ObservationOrdinal });
                table.CheckConstraint("CK_WorkInbox_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_WorkInbox_KeyBytes", KeyBytesCheck);
                table.CheckConstraint("CK_WorkInbox_Hash", "DATALENGTH([PayloadSha256]) = 64 AND [PayloadSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                table.CheckConstraint("CK_WorkInbox_State", "[ObservationOrdinal] >= 1 AND [EvidenceId] <> '00000000-0000-0000-0000-000000000000' AND (([Availability] = 1 AND [DesiredState] IN (1,2)) OR ([Availability] = 2 AND [DesiredState] IS NULL))");
                table.ForeignKey(
                    name: "FK_ObservationInbox_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ObservationInbox_DesiredStateSnapshots",
                    columns: x => new { x.CompanyId, x.SourceKeyDigest, x.ScopeKeyDigest },
                    principalSchema: "work",
                    principalTable: "DesiredStateSnapshots",
                    principalColumns: new[] { "CompanyId", "SourceKeyDigest", "ScopeKeyDigest" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "OutboxMessages",
            schema: "work",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceKeyDigest = table.Column<byte[]>(type: "binary(32)", nullable: false),
                ScopeKeyDigest = table.Column<byte[]>(type: "binary(32)", nullable: false),
                SourceKeyUtf16Be = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                ScopeKeyUtf16Be = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                ObservationOrdinal = table.Column<long>(type: "bigint", nullable: false),
                AggregateVersion = table.Column<long>(type: "bigint", nullable: false),
                EventType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                SchemaVersion = table.Column<int>(type: "int", nullable: false),
                PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                table.CheckConstraint("CK_WorkOutbox_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ObservationOrdinal] >= 1 AND [AggregateVersion] >= 1 AND [SchemaVersion] = 2 AND LEN([EventType]) > 0 AND LEN([CorrelationId]) > 0");
                table.CheckConstraint("CK_WorkOutbox_KeyBytes", KeyBytesCheck);
                table.CheckConstraint("CK_WorkOutbox_Payload", "ISJSON([PayloadJson]) = 1");
                table.ForeignKey(
                    name: "FK_OutboxMessages_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_OutboxMessages_DesiredStateSnapshots",
                    columns: x => new { x.CompanyId, x.SourceKeyDigest, x.ScopeKeyDigest },
                    principalSchema: "work",
                    principalTable: "DesiredStateSnapshots",
                    principalColumns: new[] { "CompanyId", "SourceKeyDigest", "ScopeKeyDigest" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ObservationInbox_CompanyId",
            schema: "work",
            table: "ObservationInbox",
            column: "CompanyId");

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_CompanyId",
            schema: "work",
            table: "OutboxMessages",
            column: "CompanyId");

        migrationBuilder.CreateIndex(
            name: "UX_WorkOutbox_ScopedOrdinal",
            schema: "work",
            table: "OutboxMessages",
            columns: new[] { "CompanyId", "SourceKeyDigest", "ScopeKeyDigest", "ObservationOrdinal" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ObservationInbox", schema: "work");
        migrationBuilder.DropTable(name: "OutboxMessages", schema: "work");
        migrationBuilder.DropTable(name: "DesiredStateSnapshots", schema: "work");
    }
}
