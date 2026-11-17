using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancePartyBalanceTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AllocationAccountId",
                schema: "finance",
                table: "PaymentAllocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AllocationPartyId",
                schema: "finance",
                table: "PaymentAllocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AllocationAccountId",
                schema: "finance",
                table: "Allocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AllocationPartyId",
                schema: "finance",
                table: "Allocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PartyBalanceTransferCommands",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyBalanceTransferCommands", x => new { x.CompanyId, x.KeyHash });
                    table.CheckConstraint("CK_PartyBalanceTransferCommands_Evidence", "LEN([KeyHash]) = 64 AND LEN([PayloadHash]) = 64 AND LEN([ActorId]) > 0 AND ISJSON([ResultJson]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "PartyBalanceTransfers",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PredecessorPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PredecessorAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuccessorPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuccessorAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerProofJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CurrenciesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssignmentSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SnapshotVersion = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyBalanceTransfers", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PartyBalanceTransfers_Evidence", "ISJSON([OwnerProofJson]) = 1 AND ISJSON([EvidenceJson]) = 1 AND ISJSON([CurrenciesJson]) = 1 AND ISJSON([AssignmentSnapshotJson]) = 1 AND LEN([SnapshotVersion]) = 64 AND LEN([Reason]) > 0 AND LEN([RecordedBy]) > 0");
                    table.CheckConstraint("CK_PartyBalanceTransfers_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PredecessorPartyId] <> [SuccessorPartyId] AND [PredecessorAccountId] <> [SuccessorAccountId] AND [PredecessorPartyId] <> '00000000-0000-0000-0000-000000000000' AND [SuccessorPartyId] <> '00000000-0000-0000-0000-000000000000' AND [PredecessorAccountId] <> '00000000-0000-0000-0000-000000000000' AND [SuccessorAccountId] <> '00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateTable(
                name: "PartyBalanceAssignments",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AvailableAmountText = table.Column<string>(type: "varchar(max)", nullable: false),
                    LastTransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyBalanceAssignments", x => new { x.CompanyId, x.Kind, x.SourceId, x.CurrentAccountId });
                    table.CheckConstraint("CK_PartyBalanceAssignments_Identity", "[Kind] IN (1,2,3) AND [Version] >= 1 AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND [OriginalPartyId] <> '00000000-0000-0000-0000-000000000000' AND [OriginalAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrentPartyId] <> '00000000-0000-0000-0000-000000000000' AND [CurrentAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrentAccountId] <> [OriginalAccountId] AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([AvailableAmountText]) > 0 AND [AvailableAmountText] NOT LIKE '%[^0-9.]%' AND [AvailableAmountText] NOT LIKE '%.%.%' AND [AvailableAmountText] NOT LIKE '.%' AND [AvailableAmountText] NOT LIKE '%.'");
                    table.ForeignKey(
                        name: "FK_PartyBalanceAssignments_PartyBalanceTransfers_CompanyId_LastTransferId",
                        columns: x => new { x.CompanyId, x.LastTransferId },
                        principalSchema: "finance",
                        principalTable: "PartyBalanceTransfers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentAllocations_Beneficiary",
                schema: "finance",
                table: "PaymentAllocations",
                sql: "([AllocationPartyId] IS NULL AND [AllocationAccountId] IS NULL) OR ([AllocationPartyId] IS NOT NULL AND [AllocationAccountId] IS NOT NULL AND [AllocationPartyId] <> '00000000-0000-0000-0000-000000000000' AND [AllocationAccountId] <> '00000000-0000-0000-0000-000000000000')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocations_Beneficiary",
                schema: "finance",
                table: "Allocations",
                sql: "([AllocationPartyId] IS NULL AND [AllocationAccountId] IS NULL) OR ([AllocationPartyId] IS NOT NULL AND [AllocationAccountId] IS NOT NULL AND [AllocationPartyId] <> '00000000-0000-0000-0000-000000000000' AND [AllocationAccountId] <> '00000000-0000-0000-0000-000000000000')");

            migrationBuilder.CreateIndex(
                name: "IX_PartyBalanceAssignments_CompanyId_CurrentAccountId_Kind",
                schema: "finance",
                table: "PartyBalanceAssignments",
                columns: new[] { "CompanyId", "CurrentAccountId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_PartyBalanceAssignments_CompanyId_LastTransferId",
                schema: "finance",
                table: "PartyBalanceAssignments",
                columns: new[] { "CompanyId", "LastTransferId" });

            migrationBuilder.CreateIndex(
                name: "IX_PartyBalanceTransfers_CompanyId_PredecessorAccountId_OccurredOn",
                schema: "finance",
                table: "PartyBalanceTransfers",
                columns: new[] { "CompanyId", "PredecessorAccountId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_PartyBalanceTransfers_CompanyId_SuccessorAccountId_OccurredOn",
                schema: "finance",
                table: "PartyBalanceTransfers",
                columns: new[] { "CompanyId", "SuccessorAccountId", "OccurredOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[PartyBalanceTransfers])
                   OR EXISTS (SELECT 1 FROM [finance].[PartyBalanceAssignments])
                   OR EXISTS (SELECT 1 FROM [finance].[PartyBalanceTransferCommands])
                   OR EXISTS (SELECT 1 FROM [finance].[Allocations] WHERE [AllocationPartyId] IS NOT NULL OR [AllocationAccountId] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [finance].[PaymentAllocations] WHERE [AllocationPartyId] IS NOT NULL OR [AllocationAccountId] IS NOT NULL)
                BEGIN
                    THROW 51984, 'Party balance transfer or allocation beneficiary evidence exists; AddFinancePartyBalanceTransfers is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "PartyBalanceAssignments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "PartyBalanceTransferCommands",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "PartyBalanceTransfers",
                schema: "finance");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentAllocations_Beneficiary",
                schema: "finance",
                table: "PaymentAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocations_Beneficiary",
                schema: "finance",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "AllocationAccountId",
                schema: "finance",
                table: "PaymentAllocations");

            migrationBuilder.DropColumn(
                name: "AllocationPartyId",
                schema: "finance",
                table: "PaymentAllocations");

            migrationBuilder.DropColumn(
                name: "AllocationAccountId",
                schema: "finance",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "AllocationPartyId",
                schema: "finance",
                table: "Allocations");
        }
    }
}
