using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;

public partial class AddWorkExternalEventInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ExternalEventHighWater",
            schema: "work",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ConsumerName = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                EventType = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                AggregateType = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InitialSequence = table.Column<long>(type: "bigint", nullable: false),
                HighestCompletedSequence = table.Column<long>(type: "bigint", nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExternalEventHighWater", x => new
                {
                    x.CompanyId,
                    x.ConsumerName,
                    x.EventType,
                    x.AggregateType,
                    x.AggregateId
                });
                table.CheckConstraint("CK_WorkExternalHighWater_Identity",
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [AggregateId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [ConsumerName] = 'work.shipment-evidence.v1' "
                    + "AND [EventType] = 'inventory.definite-shipment.recorded.v1' "
                    + "AND [AggregateType] = 'Inventory.StockAllocation'");
                table.CheckConstraint("CK_WorkExternalHighWater_Sequence",
                    "[InitialSequence] >= 1 AND ([HighestCompletedSequence] IS NULL "
                    + "OR [HighestCompletedSequence] >= [InitialSequence]) AND [Version] >= 1");
                table.ForeignKey(
                    name: "FK_ExternalEventHighWater_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ExternalEventInbox",
            schema: "work",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ConsumerName = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EventType = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                SchemaVersion = table.Column<int>(type: "int", nullable: false),
                SourceOperation = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                AggregateType = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AggregateVersion = table.Column<long>(type: "bigint", nullable: false),
                Sequence = table.Column<long>(type: "bigint", nullable: false),
                PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                PayloadSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExternalEventInbox", x => new
                {
                    x.CompanyId,
                    x.ConsumerName,
                    x.MessageId
                });
                table.CheckConstraint("CK_WorkExternalInbox_Identity",
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [MessageId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [AggregateId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [ConsumerName] = 'work.shipment-evidence.v1' "
                    + "AND [EventType] = 'inventory.definite-shipment.recorded.v1' "
                    + "AND [SchemaVersion] = 1 "
                    + "AND [SourceOperation] = 'inventory.definite-shipment.record.v1' "
                    + "AND [AggregateType] = 'Inventory.StockAllocation'");
                table.CheckConstraint("CK_WorkExternalInbox_Payload",
                    "ISJSON([PayloadJson]) = 1 AND DATALENGTH([PayloadSha256]) = 64 "
                    + "AND [PayloadSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                table.CheckConstraint("CK_WorkExternalInbox_Sequence",
                    "[AggregateVersion] >= 1 AND [Sequence] = [AggregateVersion]");
                table.CheckConstraint("CK_WorkExternalInbox_Time",
                    "[CompletedAtUtc] IS NULL OR [CompletedAtUtc] >= [ReceivedAtUtc]");
                table.ForeignKey(
                    name: "FK_ExternalEventInbox_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ExternalEventInbox_ExternalEventHighWater",
                    columns: x => new
                    {
                        x.CompanyId,
                        x.ConsumerName,
                        x.EventType,
                        x.AggregateType,
                        x.AggregateId
                    },
                    principalSchema: "work",
                    principalTable: "ExternalEventHighWater",
                    principalColumns: new[]
                    {
                        "CompanyId",
                        "ConsumerName",
                        "EventType",
                        "AggregateType",
                        "AggregateId"
                    },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "UX_WorkExternalInbox_AggregateSequence",
            schema: "work",
            table: "ExternalEventInbox",
            columns: new[]
            {
                "CompanyId",
                "ConsumerName",
                "EventType",
                "AggregateType",
                "AggregateId",
                "Sequence"
            },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS (SELECT 1 FROM [work].[ExternalEventInbox])
                OR EXISTS (SELECT 1 FROM [work].[ExternalEventHighWater])
                THROW 51082, 'work_external_event_storage_not_empty', 1;
            """);

        migrationBuilder.DropTable(name: "ExternalEventInbox", schema: "work");
        migrationBuilder.DropTable(name: "ExternalEventHighWater", schema: "work");
    }
}
