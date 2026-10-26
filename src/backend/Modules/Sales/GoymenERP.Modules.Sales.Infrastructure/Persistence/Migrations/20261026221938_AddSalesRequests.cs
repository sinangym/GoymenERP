using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// API-20 / API-21 (plan §3.2, S-01; delegation No 160, 161): the sales request. Three NEW tables — the request header
    /// (its decision is the only change), its append-only lines with the owners' snapshots, and the display-number counter
    /// of the non-draft records (series per company) — and ONE nullable column on the existing attempt table, so the
    /// request commands keep the draft's ADR-011 attempt rules in the same table: a stored outcome names exactly one target
    /// (CK_DraftAttempts_Outcome widened), a request target only under a request command (CK_DraftAttempts_RequestTarget),
    /// and a request is born from one create attempt (UX_DraftAttempts_RequestId). No existing row is rewritten.
    /// Rollback: refused once any request or request attempt exists (THROW 51065, the AddDraftRemainderChanges pattern —
    /// forward fix); an installation without one rolls back cleanly.
    /// </summary>
    public partial class AddSalesRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DraftAttempts_Outcome",
                schema: "sales",
                table: "DraftAttempts");

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                schema: "sales",
                table: "DraftAttempts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentCounters",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Series = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Next = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentCounters", x => new { x.CompanyId, x.Series });
                    table.CheckConstraint("CK_DocumentCounters_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Series]) > 0");
                    table.CheckConstraint("CK_DocumentCounters_Next", "[Next] >= 1");
                    table.CheckConstraint("CK_DocumentCounters_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_DocumentCounters_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Requests",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.Id);
                    table.UniqueConstraint("AK_Requests_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_Requests_Decision", "([State] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([State] = 2 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NULL) OR ([State] = 3 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0)");
                    table.CheckConstraint("CK_Requests_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Requests_State", "[State] IN (1, 2, 3)");
                    table.CheckConstraint("CK_Requests_Text", "LEN([DocumentNo]) > 0 AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_Requests_Version", "[Version] >= 1 AND ([State] = 1 OR [Version] >= 2)");
                    table.ForeignKey(
                        name: "FK_Requests_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RequestLines",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProductCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProductEffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                    StockUnitCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockUnitProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockUnitProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SalesUnitCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesUnitProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesUnitProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrderUnitBindingCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderUnitBindingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderUnitBindingProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderUnitBindingProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrderUnitCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderUnitCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OrderUnitDimensionCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OrderUnitProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderUnitProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrderUnitIsKilogram = table.Column<bool>(type: "bit", nullable: false),
                    OrderUnitProfileScale = table.Column<int>(type: "int", nullable: false),
                    OrderUnitScale = table.Column<int>(type: "int", nullable: false),
                    OrderUnitIsActive = table.Column<bool>(type: "bit", nullable: false),
                    OrderUnitEffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    OrderUnitEffectiveThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    QuantityText = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestLines", x => new { x.CompanyId, x.RequestId, x.LineNo });
                    table.CheckConstraint("CK_RequestLines_Identity", "[LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [OrderUnitId] <> '00000000-0000-0000-0000-000000000000' AND [OrderUnitProfileId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_RequestLines_LineNo", "[LineNo] >= 1");
                    table.CheckConstraint("CK_RequestLines_Quantity", "[Quantity] > 0 AND LEN([QuantityText]) > 0");
                    table.CheckConstraint("CK_RequestLines_Scale", "[OrderUnitProfileScale] >= 0 AND [OrderUnitScale] >= 0 AND [OrderUnitScale] <= [OrderUnitProfileScale] AND [OrderUnitScale] <= 3");
                    table.CheckConstraint("CK_RequestLines_Window", "[OrderUnitEffectiveThrough] IS NULL OR [OrderUnitEffectiveThrough] >= [OrderUnitEffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_RequestLines_Requests_CompanyId_RequestId",
                        columns: x => new { x.CompanyId, x.RequestId },
                        principalSchema: "sales",
                        principalTable: "Requests",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_DraftAttempts_RequestId",
                schema: "sales",
                table: "DraftAttempts",
                columns: new[] { "CompanyId", "RequestId" },
                unique: true,
                filter: "[RequestId] IS NOT NULL AND [CommandName] = N'sales.request.create.v1'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DraftAttempts_Outcome",
                schema: "sales",
                table: "DraftAttempts",
                sql: "([Outcome] = 1 AND [RefusalJson] IS NULL AND (([DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [RequestId] IS NULL) OR ([RequestId] IS NOT NULL AND [RequestId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL))) OR ([Outcome] = 2 AND ([DraftId] IS NULL OR [DraftId] <> '00000000-0000-0000-0000-000000000000') AND ([RequestId] IS NULL OR ([RequestId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL)) AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DraftAttempts_RequestTarget",
                schema: "sales",
                table: "DraftAttempts",
                sql: "([RequestId] IS NULL OR [CommandName] IN (N'sales.request.create.v1', N'sales.request.approve.v1', N'sales.request.reject.v1')) AND ([DraftId] IS NULL OR [CommandName] NOT IN (N'sales.request.create.v1', N'sales.request.approve.v1', N'sales.request.reject.v1'))");

            migrationBuilder.CreateIndex(
                name: "UX_RequestLines_LineId",
                schema: "sales",
                table: "RequestLines",
                columns: new[] { "CompanyId", "RequestId", "LineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CompanyId_CreatedAtUtc_Id",
                schema: "sales",
                table: "Requests",
                columns: new[] { "CompanyId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CompanyId_State_CreatedAtUtc_Id",
                schema: "sales",
                table: "Requests",
                columns: new[] { "CompanyId", "State", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Requests_DocumentNo",
                schema: "sales",
                table: "Requests",
                columns: new[] { "CompanyId", "DocumentNo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DraftAttempts_Requests_CompanyId_RequestId",
                schema: "sales",
                table: "DraftAttempts",
                columns: new[] { "CompanyId", "RequestId" },
                principalSchema: "sales",
                principalTable: "Requests",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Requests and their decisions are commercial history. The THROW precedes every destructive operation: once a
            // request (or a request command's attempt) exists the migration is not rolled back (forward fix). 51065 is the
            // next unused Sales code.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[Requests])
                    OR EXISTS (SELECT 1 FROM [sales].[DraftAttempts] WHERE [RequestId] IS NOT NULL
                        OR [CommandName] IN (N'sales.request.create.v1', N'sales.request.approve.v1', N'sales.request.reject.v1'))
                    THROW 51065, 'Sales requests exist; AddSalesRequests is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_DraftAttempts_Requests_CompanyId_RequestId",
                schema: "sales",
                table: "DraftAttempts");

            migrationBuilder.DropTable(
                name: "DocumentCounters",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "RequestLines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "Requests",
                schema: "sales");

            migrationBuilder.DropIndex(
                name: "UX_DraftAttempts_RequestId",
                schema: "sales",
                table: "DraftAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DraftAttempts_Outcome",
                schema: "sales",
                table: "DraftAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DraftAttempts_RequestTarget",
                schema: "sales",
                table: "DraftAttempts");

            migrationBuilder.DropColumn(
                name: "RequestId",
                schema: "sales",
                table: "DraftAttempts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DraftAttempts_Outcome",
                schema: "sales",
                table: "DraftAttempts",
                sql: "([Outcome] = 1 AND [DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [RefusalJson] IS NULL) OR ([Outcome] = 2 AND ([DraftId] IS NULL OR [DraftId] <> '00000000-0000-0000-0000-000000000000') AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
        }
    }
}
