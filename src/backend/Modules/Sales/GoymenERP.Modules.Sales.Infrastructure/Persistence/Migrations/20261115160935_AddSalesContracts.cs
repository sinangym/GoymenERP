using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Contracts",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractNo = table.Column<int>(type: "int", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MoneyProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MoneyProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MoneyScale = table.Column<int>(type: "int", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    InstallmentsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DiscountRulesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Clauses = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DiscountLimitPercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: true),
                    DiscountApprovalRequired = table.Column<bool>(type: "bit", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.Id);
                    table.UniqueConstraint("AK_Contracts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_Contracts_DiscountLimit", "[DiscountLimitPercent] IS NULL OR ([DiscountLimitPercent] >= 0 AND [DiscountLimitPercent] <= 100)");
                    table.CheckConstraint("CK_Contracts_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [MoneyProfileId] <> '00000000-0000-0000-0000-000000000000' AND [ContractNo] >= 1");
                    table.CheckConstraint("CK_Contracts_Json", "ISJSON([InstallmentsJson]) = 1 AND ISJSON([DiscountRulesJson]) = 1");
                    table.CheckConstraint("CK_Contracts_MakerChecker", "[DecidedBy] IS NULL OR [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [CreatedBy] COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_Contracts_Money", "[MoneyScale] >= 0 AND [MoneyScale] <= 8 AND LEN([MoneyProfileVersion]) > 0");
                    table.CheckConstraint("CK_Contracts_State", "([DiscountApprovalRequired] = 0 AND [State] = 2 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [Version] >= 1) OR ([DiscountApprovalRequired] = 1 AND [State] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [Version] >= 1) OR ([DiscountApprovalRequired] = 1 AND [State] = 2 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NULL AND [Version] >= 2) OR ([DiscountApprovalRequired] = 1 AND [State] = 3 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0 AND [Version] >= 2)");
                    table.CheckConstraint("CK_Contracts_StateAxis", "[State] IN (1, 2, 3)");
                    table.CheckConstraint("CK_Contracts_Text", "LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0 AND ([Clauses] IS NULL OR LEN([Clauses]) > 0)");
                    table.CheckConstraint("CK_Contracts_Validity", "[ValidThrough] IS NULL OR [ValidThrough] >= [ValidFrom]");
                    table.ForeignKey(
                        name: "FK_Contracts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractAttempts",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_ContractAttempts_Command", "[CommandName] IN (N'sales.contract.create.v1', N'sales.contract.discount-approve.v1', N'sales.contract.discount-reject.v1')");
                    table.CheckConstraint("CK_ContractAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_ContractAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0 AND ([ContractId] IS NULL OR [ContractId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_ContractAttempts_Outcome", "([Outcome] = 1 AND [ContractId] IS NOT NULL AND [RefusalJson] IS NULL) OR ([Outcome] = 2 AND [ContractId] IS NOT NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1 AND [CommandName] <> N'sales.contract.create.v1')");
                    table.ForeignKey(
                        name: "FK_ContractAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractAttempts_Contracts_CompanyId_ContractId",
                        columns: x => new { x.CompanyId, x.ContractId },
                        principalSchema: "sales",
                        principalTable: "Contracts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractAttempts_CompanyId_ContractId",
                schema: "sales",
                table: "ContractAttempts",
                columns: new[] { "CompanyId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_Party",
                schema: "sales",
                table: "Contracts",
                columns: new[] { "CompanyId", "PartyId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "UX_Contracts_No",
                schema: "sales",
                table: "Contracts",
                columns: new[] { "CompanyId", "ContractNo" },
                unique: true);

            // S-11 (NS-13): the contract facts, the payment plan, the discount rules and the limit snapshot never change; the only
            // change is the one discount decision, PendingDiscountApproval (1) → Active (2) | DiscountRejected (3), with the version
            // moving by exactly one. No row is deleted.
            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_Contracts_Guard]
                ON [sales].[Contracts]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM deleted AS d
                        LEFT JOIN inserted AS i ON i.[Id] = d.[Id]
                        WHERE i.[Id] IS NULL
                           OR d.[State] <> 1
                           OR i.[State] NOT IN (2, 3)
                           OR i.[Version] <> d.[Version] + 1
                           OR EXISTS (
                               SELECT d.[CompanyId], d.[ContractNo], d.[PartyId], d.[CompanyAccountId], d.[CurrencyId], d.[MoneyProfileId],
                                      d.[MoneyProfileVersion] COLLATE Latin1_General_100_BIN2, d.[MoneyScale], d.[ValidFrom], d.[ValidThrough],
                                      d.[InstallmentsJson] COLLATE Latin1_General_100_BIN2, d.[DiscountRulesJson] COLLATE Latin1_General_100_BIN2,
                                      d.[Clauses] COLLATE Latin1_General_100_BIN2, d.[DiscountLimitPercent], d.[DiscountApprovalRequired],
                                      d.[CreatedBy] COLLATE Latin1_General_100_BIN2, d.[CreatedAtUtc], d.[CorrelationId] COLLATE Latin1_General_100_BIN2
                               EXCEPT
                               SELECT i.[CompanyId], i.[ContractNo], i.[PartyId], i.[CompanyAccountId], i.[CurrencyId], i.[MoneyProfileId],
                                      i.[MoneyProfileVersion] COLLATE Latin1_General_100_BIN2, i.[MoneyScale], i.[ValidFrom], i.[ValidThrough],
                                      i.[InstallmentsJson] COLLATE Latin1_General_100_BIN2, i.[DiscountRulesJson] COLLATE Latin1_General_100_BIN2,
                                      i.[Clauses] COLLATE Latin1_General_100_BIN2, i.[DiscountLimitPercent], i.[DiscountApprovalRequired],
                                      i.[CreatedBy] COLLATE Latin1_General_100_BIN2, i.[CreatedAtUtc], i.[CorrelationId] COLLATE Latin1_General_100_BIN2))
                    BEGIN
                        THROW 51474, 'A sales contract keeps its facts, payment plan and discount rules and takes its one discount decision only.', 1;
                    END;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_ContractAttempts_Guard]
                ON [sales].[ContractAttempts]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51472, 'Sales contract attempts are append-only.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a recorded sales contract (or a recorded attempt) is evidence that the previous model cannot hold. The guard
            // refuses (51473) before anything is dropped; dropping a table drops its trigger.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[Contracts])
                   OR EXISTS (SELECT 1 FROM [sales].[ContractAttempts])
                BEGIN
                    THROW 51473, 'Sales contracts were recorded; AddSalesContracts is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "ContractAttempts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "Contracts",
                schema: "sales");
        }
    }
}
