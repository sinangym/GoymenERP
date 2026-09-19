using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;

/// <summary>
/// Purchasing's first schema (PUR-4a; ADR-002, ADR-009, ADR-011; the Sales SALES-P2 / Logistics LOG-4a shape):
/// the purchase request header and lines (the demand), the purchase order header and lines with their share
/// totals and the request binding (the commitment, expected supply — never stock), and the ADR-011 attempt
/// record. All are tenant tables; companies are Organization's, referenced by key only, so this runs after
/// Organization's migrations. The attempt record carries the §7 outcome from the start (a stored request, a
/// stored order or a recorded terminal refusal), and its "one create attempt per stored record" indexes are
/// filtered to the create commands as the Sales lesson taught (the transition commands' attempts share the id).
/// </summary>
[DbContext(typeof(PurchasingDbContext))]
[Migration("20260919170000_InitialPurchasing")]
public sealed class InitialPurchasing : Migration
{
    private const string NotEmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "purchasing");

        migrationBuilder.CreateTable(
            name: "PurchaseRequests",
            schema: "purchasing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                SourcingVersion = table.Column<long>(type: "bigint", nullable: false),
                EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PurchaseRequests", x => x.Id);
                table.UniqueConstraint("AK_PurchaseRequests_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_PurchaseRequests_Identity", "[Id] <> " + NotEmptyGuid + " AND [CompanyId] <> " + NotEmptyGuid);
                table.CheckConstraint("CK_PurchaseRequests_Status", "[Status] IN (1, 2)");
                table.CheckConstraint("CK_PurchaseRequests_Version", "[Version] >= 1 AND [SourcingVersion] >= 0");
                table.CheckConstraint("CK_PurchaseRequests_Text", "LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.ForeignKey(
                    name: "FK_PurchaseRequests_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PurchaseRequestLines",
            schema: "purchasing",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LineNo = table.Column<int>(type: "int", nullable: false),
                LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitIsKilogram = table.Column<bool>(type: "bit", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PurchaseRequestLines", x => new { x.CompanyId, x.RequestId, x.LineNo });
                table.CheckConstraint("CK_PurchaseRequestLines_LineNo", "[LineNo] >= 1");
                table.CheckConstraint("CK_PurchaseRequestLines_Identity",
                    "[LineId] <> " + NotEmptyGuid + " AND [ProductId] <> " + NotEmptyGuid + " AND [UnitId] <> " + NotEmptyGuid + " AND LEN([ProductVersion]) > 0");
                table.CheckConstraint("CK_PurchaseRequestLines_Quantity", "[Quantity] > 0");
                table.ForeignKey(
                    name: "FK_PurchaseRequestLines_PurchaseRequests_CompanyId_RequestId",
                    columns: x => new { x.CompanyId, x.RequestId },
                    principalSchema: "purchasing",
                    principalTable: "PurchaseRequests",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PurchaseOrders",
            schema: "purchasing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SupplierPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                table.UniqueConstraint("AK_PurchaseOrders_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_PurchaseOrders_Identity",
                    "[Id] <> " + NotEmptyGuid + " AND [CompanyId] <> " + NotEmptyGuid + " AND [SupplierPartyId] <> " + NotEmptyGuid
                    + " AND [CompanyAccountId] <> " + NotEmptyGuid + " AND ([SourceRequestId] IS NULL OR [SourceRequestId] <> " + NotEmptyGuid + ")");
                table.CheckConstraint("CK_PurchaseOrders_Status", "[Status] IN (1, 2)");
                table.CheckConstraint("CK_PurchaseOrders_Version", "[Version] >= 1");
                table.CheckConstraint("CK_PurchaseOrders_Text", "LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.ForeignKey(
                    name: "FK_PurchaseOrders_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PurchaseOrders_PurchaseRequests_CompanyId_SourceRequestId",
                    columns: x => new { x.CompanyId, x.SourceRequestId },
                    principalSchema: "purchasing",
                    principalTable: "PurchaseRequests",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PurchaseOrderLines",
            schema: "purchasing",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LineNo = table.Column<int>(type: "int", nullable: false),
                LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                ParentLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                SourceRequestLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitIsKilogram = table.Column<bool>(type: "bit", nullable: false),
                Ordered = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Technical = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Accepted = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PurchaseOrderLines", x => new { x.CompanyId, x.OrderId, x.LineNo });
                table.CheckConstraint("CK_PurchaseOrderLines_LineNo", "[LineNo] >= 1");
                table.CheckConstraint("CK_PurchaseOrderLines_Identity",
                    "[LineId] <> " + NotEmptyGuid + " AND [ProductId] <> " + NotEmptyGuid + " AND [UnitId] <> " + NotEmptyGuid + " AND LEN([ProductVersion]) > 0 "
                    + "AND ([ParentLineId] IS NULL OR [ParentLineId] <> " + NotEmptyGuid + ") AND ([SourceRequestLineId] IS NULL OR [SourceRequestLineId] <> " + NotEmptyGuid + ")");
                // A general line has no parent; a technical line is a slice of one general line (§7.2).
                table.CheckConstraint("CK_PurchaseOrderLines_Kind",
                    "([Kind] = 1 AND [ParentLineId] IS NULL) OR ([Kind] = 2 AND [ParentLineId] IS NOT NULL)");
                // §7.2 / §13: technical slices and the accepted share never exceed the first commitment.
                table.CheckConstraint("CK_PurchaseOrderLines_Shares",
                    "[Ordered] > 0 AND [Technical] >= 0 AND [Accepted] >= 0 AND [Technical] + [Accepted] <= [Ordered]");
                table.ForeignKey(
                    name: "FK_PurchaseOrderLines_PurchaseOrders_CompanyId_OrderId",
                    columns: x => new { x.CompanyId, x.OrderId },
                    principalSchema: "purchasing",
                    principalTable: "PurchaseOrders",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PurchaseAttempts",
            schema: "purchasing",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Outcome = table.Column<int>(type: "int", nullable: false),
                RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PurchaseAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                table.CheckConstraint("CK_PurchaseAttempts_Identity",
                    "[CompanyId] <> " + NotEmptyGuid + " AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                table.CheckConstraint("CK_PurchaseAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                // ADR-011 §7: a stored result (exactly one of request / order) or a recorded terminal refusal, never both.
                table.CheckConstraint("CK_PurchaseAttempts_Outcome",
                    "([Outcome] = 1 AND [RefusalJson] IS NULL AND (([RequestId] IS NOT NULL AND [RequestId] <> " + NotEmptyGuid + " AND [OrderId] IS NULL) "
                    + "OR ([OrderId] IS NOT NULL AND [OrderId] <> " + NotEmptyGuid + " AND [RequestId] IS NULL))) "
                    + "OR ([Outcome] = 2 AND [RequestId] IS NULL AND [OrderId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
                table.ForeignKey(
                    name: "FK_PurchaseAttempts_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PurchaseAttempts_PurchaseOrders_CompanyId_OrderId",
                    columns: x => new { x.CompanyId, x.OrderId },
                    principalSchema: "purchasing",
                    principalTable: "PurchaseOrders",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PurchaseAttempts_PurchaseRequests_CompanyId_RequestId",
                    columns: x => new { x.CompanyId, x.RequestId },
                    principalSchema: "purchasing",
                    principalTable: "PurchaseRequests",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseRequests_CompanyId_CreatedAtUtc_Id",
            schema: "purchasing",
            table: "PurchaseRequests",
            columns: new[] { "CompanyId", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseRequests_CompanyId_Status_CreatedAtUtc_Id",
            schema: "purchasing",
            table: "PurchaseRequests",
            columns: new[] { "CompanyId", "Status", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "UX_PurchaseRequestLines_LineId",
            schema: "purchasing",
            table: "PurchaseRequestLines",
            columns: new[] { "CompanyId", "RequestId", "LineId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseOrders_CompanyId_CreatedAtUtc_Id",
            schema: "purchasing",
            table: "PurchaseOrders",
            columns: new[] { "CompanyId", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseOrders_CompanyId_Status_CreatedAtUtc_Id",
            schema: "purchasing",
            table: "PurchaseOrders",
            columns: new[] { "CompanyId", "Status", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseOrders_CompanyId_SourceRequestId",
            schema: "purchasing",
            table: "PurchaseOrders",
            columns: new[] { "CompanyId", "SourceRequestId" });

        migrationBuilder.CreateIndex(
            name: "UX_PurchaseOrderLines_LineId",
            schema: "purchasing",
            table: "PurchaseOrderLines",
            columns: new[] { "CompanyId", "OrderId", "LineId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseOrderLines_SourceRequestLine",
            schema: "purchasing",
            table: "PurchaseOrderLines",
            columns: new[] { "CompanyId", "SourceRequestLineId" });

        // One create attempt per stored request; the submit command's attempts share the request id.
        migrationBuilder.CreateIndex(
            name: "UX_PurchaseAttempts_RequestId",
            schema: "purchasing",
            table: "PurchaseAttempts",
            columns: new[] { "CompanyId", "RequestId" },
            unique: true,
            filter: "[RequestId] IS NOT NULL AND [CommandName] = N'purchasing.purchase-request.create.v1'");

        // One create attempt per stored order; the approve command's attempts share the order id.
        migrationBuilder.CreateIndex(
            name: "UX_PurchaseAttempts_OrderId",
            schema: "purchasing",
            table: "PurchaseAttempts",
            columns: new[] { "CompanyId", "OrderId" },
            unique: true,
            filter: "[OrderId] IS NOT NULL AND [CommandName] = N'purchasing.purchase-order.create.v1'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PurchaseAttempts", schema: "purchasing");
        migrationBuilder.DropTable(name: "PurchaseOrderLines", schema: "purchasing");
        migrationBuilder.DropTable(name: "PurchaseOrders", schema: "purchasing");
        migrationBuilder.DropTable(name: "PurchaseRequestLines", schema: "purchasing");
        migrationBuilder.DropTable(name: "PurchaseRequests", schema: "purchasing");
    }
}
