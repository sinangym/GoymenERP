using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// F3a / API-20 (DEC-FIN-006): a recorded manual bank payment's allocations to payable items and its per-payment
    /// balance. Pure DDL up; the Down refuses (51051) while any payment allocation is stored.
    /// </summary>
    public partial class AddPaymentAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentAllocations",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReleasedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleaseReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAllocations", x => x.Id);
                    table.UniqueConstraint("AK_PaymentAllocations_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PaymentAllocations_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PaymentId] <> '00000000-0000-0000-0000-000000000000' AND [OpenItemId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PaymentAllocations_Status", "([Status] = 1 AND [ReleasedBy] IS NULL AND [ReleasedAtUtc] IS NULL AND [ReleaseReason] IS NULL) OR ([Status] = 2 AND [ReleasedBy] IS NOT NULL AND LEN([ReleasedBy]) > 0 AND [ReleasedAtUtc] IS NOT NULL AND [ReleaseReason] IS NOT NULL AND LEN([ReleaseReason]) > 0)");
                    table.CheckConstraint("CK_PaymentAllocations_Text", "LEN([AmountText]) > 0 AND LEN([Reason]) > 0 AND LEN([CreatedBy]) > 0");
                    table.CheckConstraint("CK_PaymentAllocations_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_ManualBankPayments_CompanyId_PaymentId",
                        columns: x => new { x.CompanyId, x.PaymentId },
                        principalSchema: "finance",
                        principalTable: "ManualBankPayments",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_OpenItems_CompanyId_OpenItemId",
                        columns: x => new { x.CompanyId, x.OpenItemId },
                        principalSchema: "finance",
                        principalTable: "OpenItems",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAllocationStates",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    UnallocatedAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAllocationStates", x => new { x.CompanyId, x.PaymentId });
                    table.CheckConstraint("CK_PaymentAllocationStates_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PaymentId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PaymentAllocationStates_Text", "LEN([OriginalAmountText]) > 0 AND LEN([UnallocatedAmountText]) > 0 AND LEN([UpdatedBy]) > 0");
                    table.CheckConstraint("CK_PaymentAllocationStates_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_PaymentAllocationStates_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAllocationStates_ManualBankPayments_CompanyId_PaymentId",
                        columns: x => new { x.CompanyId, x.PaymentId },
                        principalSchema: "finance",
                        principalTable: "ManualBankPayments",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_CompanyId_OpenItemId_Status",
                schema: "finance",
                table: "PaymentAllocations",
                columns: new[] { "CompanyId", "OpenItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_CompanyId_PaymentId_Status",
                schema: "finance",
                table: "PaymentAllocations",
                columns: new[] { "CompanyId", "PaymentId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Finance's module refusal (51051), forward fix: dropping a stored payment allocation (or the payment balance
            // it moved) would lose which payment settled which payable while the items stay settled. An empty store
            // rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[PaymentAllocations])
                    OR EXISTS (SELECT 1 FROM [finance].[PaymentAllocationStates])
                BEGIN
                    THROW 51051, 'Finance payment allocations are stored; AddPaymentAllocations is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "PaymentAllocations",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "PaymentAllocationStates",
                schema: "finance");
        }
    }
}
