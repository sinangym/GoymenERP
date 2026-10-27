using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// API-22 / API-23 / API-24 (plan §3.2, S-01; delegation No 160, 162, 163): the sales quote. Five NEW tables — the quote
    /// header (its lifecycle is the only change; CK_Quotes_Lifecycle), its append-only revisions and their lines, the requests
    /// it covers (UX_QuoteRequests_ActiveRequest: a request is covered by at most one live quote) and the quote commands' OWN
    /// attempt table (ADR-SAL-001 §2: create / revise / lose, the draft attempt's shape, UX_QuoteAttempts_QuoteId, with the
    /// SQL trigger TR_QuoteAttempts_RejectMutation refusing every UPDATE/DELETE — THROW 51068, ADR-SAL-001 §6) — and two
    /// nullable columns on the order (SourceQuoteId / SourceQuoteRevision, written once; UX_Drafts_SourceQuoteId: ONE order
    /// per quote, company-leading FK to the quote). The conversion's attempt is the ORDER's (ADR-SAL-001 §3) in the existing
    /// sales.DraftAttempts under its own command name; that table's shape is not touched. No existing row is rewritten.
    /// Rollback: refused once any quote, quote attempt, converted order or conversion attempt exists (THROW 51066 — forward
    /// fix); an installation without one rolls back cleanly (dropping the attempt table drops its trigger).
    /// </summary>
    public partial class AddSalesQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceQuoteId",
                schema: "sales",
                table: "Drafts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceQuoteRevision",
                schema: "sales",
                table: "Drafts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Quotes",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MoneyOperation = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MoneyProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MoneyProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MoneyEffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                    MoneyIsActive = table.Column<bool>(type: "bit", nullable: false),
                    MoneyEffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    MoneyEffectiveThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    MoneyScale = table.Column<int>(type: "int", nullable: false),
                    LossReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LostBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LostAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConvertedDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConvertedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ConvertedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                    table.UniqueConstraint("AK_Quotes_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_Quotes_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [MoneyProfileId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Quotes_Lifecycle", "([State] = 1 AND [LossReason] IS NULL AND [LostBy] IS NULL AND [LostAtUtc] IS NULL AND [ConvertedDraftId] IS NULL AND [ConvertedBy] IS NULL AND [ConvertedAtUtc] IS NULL) OR ([State] = 3 AND [LossReason] IS NOT NULL AND LEN([LossReason]) > 0 AND [LostBy] IS NOT NULL AND LEN([LostBy]) > 0 AND [LostAtUtc] IS NOT NULL AND [ConvertedDraftId] IS NULL AND [ConvertedBy] IS NULL AND [ConvertedAtUtc] IS NULL) OR ([State] = 2 AND [ConvertedDraftId] IS NOT NULL AND [ConvertedDraftId] <> '00000000-0000-0000-0000-000000000000' AND [ConvertedBy] IS NOT NULL AND LEN([ConvertedBy]) > 0 AND [ConvertedAtUtc] IS NOT NULL AND [LossReason] IS NULL AND [LostBy] IS NULL AND [LostAtUtc] IS NULL)");
                    table.CheckConstraint("CK_Quotes_Money", "[MoneyScale] >= 0 AND ([MoneyEffectiveThrough] IS NULL OR [MoneyEffectiveThrough] >= [MoneyEffectiveFrom])");
                    table.CheckConstraint("CK_Quotes_State", "[State] IN (1, 2, 3) AND [Revision] >= 0");
                    table.CheckConstraint("CK_Quotes_Text", "LEN([DocumentNo]) > 0 AND LEN([MoneyOperation]) > 0 AND LEN([MoneyProfileVersion]) > 0 AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_Quotes_Version", "[Version] >= [Revision] + 1");
                    table.ForeignKey(
                        name: "FK_Quotes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuoteAttempts",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_QuoteAttempts_Command", "[CommandName] IN (N'sales.quote.create.v1', N'sales.quote.revise.v1', N'sales.quote.lose.v1')");
                    table.CheckConstraint("CK_QuoteAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_QuoteAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                    table.CheckConstraint("CK_QuoteAttempts_Outcome", "([Outcome] = 1 AND [QuoteId] IS NOT NULL AND [QuoteId] <> '00000000-0000-0000-0000-000000000000' AND [RefusalJson] IS NULL) OR ([Outcome] = 2 AND ([QuoteId] IS NULL OR [QuoteId] <> '00000000-0000-0000-0000-000000000000') AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
                    table.CheckConstraint("CK_QuoteAttempts_Result", "[ResultJson] IS NULL");
                    table.ForeignKey(
                        name: "FK_QuoteAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuoteAttempts_Quotes_CompanyId_QuoteId",
                        columns: x => new { x.CompanyId, x.QuoteId },
                        principalSchema: "sales",
                        principalTable: "Quotes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuoteRequests",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteRequests", x => new { x.CompanyId, x.QuoteId, x.RequestId });
                    table.CheckConstraint("CK_QuoteRequests_Identity", "[QuoteId] <> '00000000-0000-0000-0000-000000000000' AND [RequestId] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_QuoteRequests_Quotes_CompanyId_QuoteId",
                        columns: x => new { x.CompanyId, x.QuoteId },
                        principalSchema: "sales",
                        principalTable: "Quotes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuoteRequests_Requests_CompanyId_RequestId",
                        columns: x => new { x.CompanyId, x.RequestId },
                        principalSchema: "sales",
                        principalTable: "Requests",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuoteRevisions",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    RequestedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RevisedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RevisedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteRevisions", x => new { x.CompanyId, x.QuoteId, x.Revision });
                    table.CheckConstraint("CK_QuoteRevisions_Identity", "[QuoteId] <> '00000000-0000-0000-0000-000000000000' AND [Revision] >= 0 AND LEN([RevisedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_QuoteRevisions_Quotes_CompanyId_QuoteId",
                        columns: x => new { x.CompanyId, x.QuoteId },
                        principalSchema: "sales",
                        principalTable: "Quotes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuoteLines",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
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
                    UnitPrice = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PriceUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DiscountPercent = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RequestedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteLines", x => new { x.CompanyId, x.QuoteId, x.Revision, x.LineNo });
                    table.CheckConstraint("CK_QuoteLines_Identity", "[LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [OrderUnitId] <> '00000000-0000-0000-0000-000000000000' AND [OrderUnitProfileId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_QuoteLines_LineNo", "[LineNo] >= 1");
                    table.CheckConstraint("CK_QuoteLines_Price", "(([UnitPrice] IS NULL AND [PriceUnitId] IS NULL) OR ([UnitPrice] IS NOT NULL AND [PriceUnitId] IS NOT NULL)) AND ([DiscountPercent] IS NULL OR [UnitPrice] IS NOT NULL)");
                    table.CheckConstraint("CK_QuoteLines_Quantity", "[Quantity] > 0 AND LEN([QuantityText]) > 0");
                    table.CheckConstraint("CK_QuoteLines_Scale", "[OrderUnitProfileScale] >= 0 AND [OrderUnitScale] >= 0 AND [OrderUnitScale] <= [OrderUnitProfileScale] AND [OrderUnitScale] <= 3");
                    table.CheckConstraint("CK_QuoteLines_Window", "[OrderUnitEffectiveThrough] IS NULL OR [OrderUnitEffectiveThrough] >= [OrderUnitEffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_QuoteLines_QuoteRevisions_CompanyId_QuoteId_Revision",
                        columns: x => new { x.CompanyId, x.QuoteId, x.Revision },
                        principalSchema: "sales",
                        principalTable: "QuoteRevisions",
                        principalColumns: new[] { "CompanyId", "QuoteId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Drafts_SourceQuoteId",
                schema: "sales",
                table: "Drafts",
                columns: new[] { "CompanyId", "SourceQuoteId" },
                unique: true,
                filter: "[SourceQuoteId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Drafts_SourceQuote",
                schema: "sales",
                table: "Drafts",
                sql: "([SourceQuoteId] IS NULL AND [SourceQuoteRevision] IS NULL) OR ([SourceQuoteId] IS NOT NULL AND [SourceQuoteId] <> '00000000-0000-0000-0000-000000000000' AND [SourceQuoteRevision] IS NOT NULL AND [SourceQuoteRevision] >= 0)");

            migrationBuilder.CreateIndex(
                name: "UX_QuoteAttempts_QuoteId",
                schema: "sales",
                table: "QuoteAttempts",
                columns: new[] { "CompanyId", "QuoteId" },
                unique: true,
                filter: "[QuoteId] IS NOT NULL AND [CommandName] = N'sales.quote.create.v1'");

            migrationBuilder.CreateIndex(
                name: "UX_QuoteLines_LineId",
                schema: "sales",
                table: "QuoteLines",
                columns: new[] { "CompanyId", "QuoteId", "Revision", "LineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_QuoteRequests_ActiveRequest",
                schema: "sales",
                table: "QuoteRequests",
                columns: new[] { "CompanyId", "RequestId" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_CompanyId_CreatedAtUtc_Id",
                schema: "sales",
                table: "Quotes",
                columns: new[] { "CompanyId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_CompanyId_State_CreatedAtUtc_Id",
                schema: "sales",
                table: "Quotes",
                columns: new[] { "CompanyId", "State", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Quotes_DocumentNo",
                schema: "sales",
                table: "Quotes",
                columns: new[] { "CompanyId", "DocumentNo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Drafts_Quotes_CompanyId_SourceQuoteId",
                schema: "sales",
                table: "Drafts",
                columns: new[] { "CompanyId", "SourceQuoteId" },
                principalSchema: "sales",
                principalTable: "Quotes",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // ADR-SAL-001 §6: the quote attempt is append-only in SQL too, beside the EF write guard (after the platform's
            // TR_CommandExecutions_RejectMutation / 51010; 51068 is the next unused code).
            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_QuoteAttempts_RejectMutation]
                ON [sales].[QuoteAttempts]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51068, 'Sales quote attempt records are immutable.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Quotes, their conversions and the orders converted from them are commercial history. The THROW precedes every
            // destructive operation: once one exists the migration is not rolled back (forward fix). 51066 is this
            // migration's own refusal code.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[Quotes])
                    OR EXISTS (SELECT 1 FROM [sales].[QuoteAttempts])
                    OR EXISTS (SELECT 1 FROM [sales].[Drafts] WHERE [SourceQuoteId] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.quote.convert.v1')
                    THROW 51066, 'Sales quotes exist; AddSalesQuotes is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Drafts_Quotes_CompanyId_SourceQuoteId",
                schema: "sales",
                table: "Drafts");

            migrationBuilder.DropTable(
                name: "QuoteAttempts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "QuoteLines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "QuoteRequests",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "QuoteRevisions",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "Quotes",
                schema: "sales");

            migrationBuilder.DropIndex(
                name: "UX_Drafts_SourceQuoteId",
                schema: "sales",
                table: "Drafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Drafts_SourceQuote",
                schema: "sales",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "SourceQuoteId",
                schema: "sales",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "SourceQuoteRevision",
                schema: "sales",
                table: "Drafts");
        }
    }
}
