using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferDepartures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransferDepartureStates",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Association = table.Column<int>(type: "int", nullable: false),
                    OfficialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InitialOfficialDocumentVersion = table.Column<long>(type: "bigint", nullable: true),
                    OfficialLinkedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    ExitWeighingAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VehicleExitAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NoFurtherExitVersion = table.Column<long>(type: "bigint", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferDepartureStates", x => new { x.CompanyId, x.TransferId });
                    table.CheckConstraint("CK_TransferDepartureStates_Association", "[Association] IN (0,1,2)");
                    table.CheckConstraint("CK_TransferDepartureStates_Fence", "[NoFurtherExitVersion] IS NULL OR ([NoFurtherExitVersion] > 0 AND [NoFurtherExitVersion] <= [Version] AND [ExitWeighingAtUtc] IS NULL AND [VehicleExitAtUtc] IS NULL)");
                    table.CheckConstraint("CK_TransferDepartureStates_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [TransferId] <> '00000000-0000-0000-0000-000000000000' AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' AND [SourceLocationId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_TransferDepartureStates_OfficialBinding", "([Association] = 2 AND [OfficialDocumentId] IS NOT NULL AND [OfficialDocumentId] <> '00000000-0000-0000-0000-000000000000' AND [InitialOfficialDocumentVersion] IS NOT NULL AND [InitialOfficialDocumentVersion] > 0 AND [OfficialLinkedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET,[OfficialLinkedAtUtc]) = 0) OR ([Association] <> 2 AND [OfficialDocumentId] IS NULL AND [InitialOfficialDocumentVersion] IS NULL AND [OfficialLinkedAtUtc] IS NULL)");
                    table.CheckConstraint("CK_TransferDepartureStates_Utc", "DATEPART(TZOFFSET,[CreatedAtUtc]) = 0 AND ([ExitWeighingAtUtc] IS NULL OR DATEPART(TZOFFSET,[ExitWeighingAtUtc]) = 0) AND ([VehicleExitAtUtc] IS NULL OR DATEPART(TZOFFSET,[VehicleExitAtUtc]) = 0)");
                    table.ForeignKey(
                        name: "FK_TransferDepartureStates_TransferOutgoingDocuments_CompanyId_DocumentId",
                        columns: x => new { x.CompanyId, x.DocumentId },
                        principalSchema: "logistics",
                        principalTable: "TransferOutgoingDocuments",
                        principalColumns: new[] { "CompanyId", "DocumentId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferDepartureAttempts",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "varchar(96)", unicode: false, maxLength: 96, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferDepartureAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_TransferDepartureAttempts_Command", "[CommandName] IN ('logistics.transfer-departure.record.v1','logistics.transfer-departure.confirm-no-exit.v1')");
                    table.CheckConstraint("CK_TransferDepartureAttempts_Json", "ISJSON([ResultJson]) = 1");
                    table.ForeignKey(
                        name: "FK_TransferDepartureAttempts_TransferDepartureStates_CompanyId_TransferId",
                        columns: x => new { x.CompanyId, x.TransferId },
                        principalSchema: "logistics",
                        principalTable: "TransferDepartureStates",
                        principalColumns: new[] { "CompanyId", "TransferId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferDepartureEvents",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WeighingDirection = table.Column<int>(type: "int", nullable: true),
                    FirstWeighingKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    SecondWeighingKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    NetWeighingKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferDepartureEvents", x => new { x.CompanyId, x.TransferId, x.Version });
                    table.CheckConstraint("CK_TransferDepartureEvents_Audit", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0 AND DATEPART(TZOFFSET,[OccurredAtUtc]) = 0");
                    table.CheckConstraint("CK_TransferDepartureEvents_Identity", "[Version] > 0 AND [Kind] IN ('AssociationRecorded','ExitWeighing','VehicleExit','NoFurtherExit')");
                    table.CheckConstraint("CK_TransferDepartureEvents_Weighing", "([Kind] = 'ExitWeighing' AND [WeighingDirection] IS NOT NULL AND [FirstWeighingKg] IS NOT NULL AND [SecondWeighingKg] IS NOT NULL AND [NetWeighingKg] IS NOT NULL AND [WeighingDirection] IN (1,2) AND [FirstWeighingKg] > 0 AND [SecondWeighingKg] > 0 AND [NetWeighingKg] > 0 AND (([WeighingDirection] = 1 AND [SecondWeighingKg] > [FirstWeighingKg]) OR ([WeighingDirection] = 2 AND [FirstWeighingKg] > [SecondWeighingKg])) AND [NetWeighingKg] = ABS([SecondWeighingKg] - [FirstWeighingKg])) OR ([Kind] <> 'ExitWeighing' AND [WeighingDirection] IS NULL AND [FirstWeighingKg] IS NULL AND [SecondWeighingKg] IS NULL AND [NetWeighingKg] IS NULL)");
                    table.ForeignKey(
                        name: "FK_TransferDepartureEvents_TransferDepartureStates_CompanyId_TransferId",
                        columns: x => new { x.CompanyId, x.TransferId },
                        principalSchema: "logistics",
                        principalTable: "TransferDepartureStates",
                        principalColumns: new[] { "CompanyId", "TransferId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransferDepartureAttempts_CompanyId_TransferId",
                schema: "logistics",
                table: "TransferDepartureAttempts",
                columns: new[] { "CompanyId", "TransferId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferDepartureStates_CompanyId_DocumentId",
                schema: "logistics",
                table: "TransferDepartureStates",
                columns: new[] { "CompanyId", "DocumentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [logistics].[TransferDepartureStates])
                    OR EXISTS (SELECT 1 FROM [logistics].[TransferDepartureEvents])
                    OR EXISTS (SELECT 1 FROM [logistics].[TransferDepartureAttempts])
                BEGIN
                    THROW 51991, 'Transfer departure, association or no-exit history exists; AddTransferDepartures is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "TransferDepartureAttempts",
                schema: "logistics");

            migrationBuilder.DropTable(
                name: "TransferDepartureEvents",
                schema: "logistics");

            migrationBuilder.DropTable(
                name: "TransferDepartureStates",
                schema: "logistics");
        }
    }
}
