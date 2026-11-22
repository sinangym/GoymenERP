using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderSignedApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderSignedApprovalAttempts",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderSignedApprovalAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_OrderSignedApprovalAttempts_Command", "[CommandName] IN (N'sales.order-signed-approval.record.v1', N'sales.order-signed-approval.deactivate.v1')");
                    table.CheckConstraint("CK_OrderSignedApprovalAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_OrderSignedApprovalAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                    table.CheckConstraint("CK_OrderSignedApprovalAttempts_Outcome", "([Outcome] = 1 AND [RefusalJson] IS NULL) OR ([Outcome] = 2 AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_OrderSignedApprovalAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderSignedApprovalAttempts_Drafts_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "sales",
                        principalTable: "Drafts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderSignedApprovals",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderRevision = table.Column<int>(type: "int", nullable: false),
                    OrderVersion = table.Column<long>(type: "bigint", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentVersionNo = table.Column<int>(type: "int", nullable: false),
                    DocumentSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    State = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeactivationKind = table.Column<int>(type: "int", nullable: true),
                    DeactivatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DeactivatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeactivationReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    SupersededById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderSignedApprovals", x => x.Id);
                    table.UniqueConstraint("AK_OrderSignedApprovals_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_OrderSignedApprovals_Document", "[DocumentId] <> '00000000-0000-0000-0000-000000000000' AND [DocumentVersionNo] >= 1 AND LEN([DocumentSha256]) = 64 AND [DocumentSha256] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_OrderSignedApprovals_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [OrderRevision] >= 0 AND [OrderVersion] >= 1");
                    table.CheckConstraint("CK_OrderSignedApprovals_State", "([State] = 1 AND [Version] = 1 AND [DeactivationKind] IS NULL AND [DeactivatedBy] IS NULL AND [DeactivatedAtUtc] IS NULL AND [DeactivationReason] IS NULL AND [SupersededById] IS NULL) OR ([State] = 2 AND [Version] = 2 AND [DeactivatedBy] IS NOT NULL AND LEN([DeactivatedBy]) > 0 AND [DeactivatedAtUtc] IS NOT NULL AND (([DeactivationKind] = 1 AND [DeactivationReason] IS NOT NULL AND LEN([DeactivationReason]) > 0 AND [SupersededById] IS NULL) OR ([DeactivationKind] = 2 AND [DeactivationReason] IS NULL AND [SupersededById] IS NOT NULL AND [SupersededById] <> [Id])))");
                    table.CheckConstraint("CK_OrderSignedApprovals_Text", "LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_OrderSignedApprovals_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderSignedApprovals_Drafts_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "sales",
                        principalTable: "Drafts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderSignedApprovalAttempts_CompanyId_OrderId",
                schema: "sales",
                table: "OrderSignedApprovalAttempts",
                columns: new[] { "CompanyId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderSignedApprovals_Order",
                schema: "sales",
                table: "OrderSignedApprovals",
                columns: new[] { "CompanyId", "OrderId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_OrderSignedApprovals_Active",
                schema: "sales",
                table: "OrderSignedApprovals",
                columns: new[] { "CompanyId", "OrderId" },
                unique: true,
                filter: "[State] = 1");

            // WP13 (YOL-D11): the record's facts never change; the only change is Active (1) → Passive (2) once, the version moving
            // 1 → 2. No row is deleted (the file stays in the Document Vault; a wrong record is withdrawn, never removed).
            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_OrderSignedApprovals_Guard]
                ON [sales].[OrderSignedApprovals]
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
                           OR i.[State] <> 2
                           OR i.[Version] <> d.[Version] + 1
                           OR EXISTS (
                               SELECT d.[CompanyId], d.[OrderId], d.[OrderRevision], d.[OrderVersion], d.[DocumentId], d.[DocumentVersionNo],
                                      d.[DocumentSha256] COLLATE Latin1_General_100_BIN2, d.[RecordedBy] COLLATE Latin1_General_100_BIN2,
                                      d.[RecordedAtUtc], d.[CorrelationId] COLLATE Latin1_General_100_BIN2
                               EXCEPT
                               SELECT i.[CompanyId], i.[OrderId], i.[OrderRevision], i.[OrderVersion], i.[DocumentId], i.[DocumentVersionNo],
                                      i.[DocumentSha256] COLLATE Latin1_General_100_BIN2, i.[RecordedBy] COLLATE Latin1_General_100_BIN2,
                                      i.[RecordedAtUtc], i.[CorrelationId] COLLATE Latin1_General_100_BIN2))
                    BEGIN
                        THROW 52261, 'A sales order signed approval keeps its facts and only becomes passive once.', 1;
                    END;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_OrderSignedApprovalAttempts_Guard]
                ON [sales].[OrderSignedApprovalAttempts]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 52262, 'Sales order signed approval attempts are append-only.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a recorded signed approval (or attempt) is evidence the previous model cannot hold. The guard refuses (52260)
            // before anything is dropped; dropping a table drops its trigger. An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[OrderSignedApprovals])
                   OR EXISTS (SELECT 1 FROM [sales].[OrderSignedApprovalAttempts])
                BEGIN
                    THROW 52260, 'Sales order signed approvals were recorded; AddSalesOrderSignedApprovals is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "OrderSignedApprovalAttempts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "OrderSignedApprovals",
                schema: "sales");
        }
    }
}
