using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesApprovalRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalRequests",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestNo = table.Column<int>(type: "int", nullable: false),
                    SubjectRevision = table.Column<int>(type: "int", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Ordered = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Quantity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RequestedTotal = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SourceInstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRequests", x => x.Id);
                    table.UniqueConstraint("AK_ApprovalRequests_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_ApprovalRequests_Decision", "([State] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL) OR ([State] = 2 AND [Kind] = 2 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NULL) OR ([State] = 3 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NOT NULL AND LEN([DecisionReason]) > 0)");
                    table.CheckConstraint("CK_ApprovalRequests_Facts", "([Kind] = 1 AND [LineId] IS NULL AND [Ordered] IS NULL AND [Quantity] IS NULL AND [RequestedTotal] IS NULL AND [SourceInstructionId] IS NULL AND [SourceShipmentId] IS NULL AND ([Note] IS NULL OR LEN([Note]) > 0)) OR ([Kind] = 2 AND [LineId] IS NOT NULL AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [Ordered] IS NOT NULL AND LEN([Ordered]) > 0 AND [Quantity] IS NOT NULL AND LEN([Quantity]) > 0 AND [RequestedTotal] IS NOT NULL AND LEN([RequestedTotal]) > 0 AND [SourceInstructionId] IS NOT NULL AND [SourceInstructionId] <> '00000000-0000-0000-0000-000000000000' AND [SourceShipmentId] IS NOT NULL AND [SourceShipmentId] <> '00000000-0000-0000-0000-000000000000' AND [Note] IS NULL)");
                    table.CheckConstraint("CK_ApprovalRequests_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SubjectId] <> '00000000-0000-0000-0000-000000000000' AND [Kind] IN (1, 2) AND [RequestNo] >= 1 AND [SubjectRevision] >= 0 AND [Version] >= 1");
                    table.CheckConstraint("CK_ApprovalRequests_MakerChecker", "[Kind] <> 2 OR [DecidedBy] IS NULL OR [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [RequestedBy] COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_ApprovalRequests_State", "[State] IN (1, 2, 3)");
                    table.CheckConstraint("CK_ApprovalRequests_Text", "LEN([RequestedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_ApprovalRequests_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalRequestAttempts",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRequestAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_ApprovalRequestAttempts_Command", "[CommandName] IN (N'sales.approval-request.request-quote-conversion.v1', N'sales.approval-request.reject-quote-conversion.v1', N'sales.approval-request.approve-over-delivery.v1', N'sales.approval-request.reject-over-delivery.v1')");
                    table.CheckConstraint("CK_ApprovalRequestAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_ApprovalRequestAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SubjectId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                    table.CheckConstraint("CK_ApprovalRequestAttempts_Outcome", "([Outcome] = 1 AND [RequestId] IS NOT NULL AND [RefusalJson] IS NULL) OR ([Outcome] = 2 AND [RequestId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_ApprovalRequestAttempts_ApprovalRequests_CompanyId_RequestId",
                        columns: x => new { x.CompanyId, x.RequestId },
                        principalSchema: "sales",
                        principalTable: "ApprovalRequests",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalRequestAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequestAttempts_CompanyId_RequestId",
                schema: "sales",
                table: "ApprovalRequestAttempts",
                columns: new[] { "CompanyId", "RequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_Pending",
                schema: "sales",
                table: "ApprovalRequests",
                columns: new[] { "CompanyId", "Kind", "State", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ApprovalRequests_No",
                schema: "sales",
                table: "ApprovalRequests",
                columns: new[] { "CompanyId", "Kind", "SubjectId", "RequestNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ApprovalRequests_PendingOrderLine",
                schema: "sales",
                table: "ApprovalRequests",
                columns: new[] { "CompanyId", "SubjectId", "LineId" },
                unique: true,
                filter: "[Kind] = 2 AND [State] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_ApprovalRequests_PendingQuoteRevision",
                schema: "sales",
                table: "ApprovalRequests",
                columns: new[] { "CompanyId", "SubjectId", "SubjectRevision" },
                unique: true,
                filter: "[Kind] = 1 AND [State] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WP4 forward fix: an approval request (SAT-D475 / SAT-D476) or its attempt is a recorded business fact; a rollback
            // that drops it would lose who asked, who decided and when.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[ApprovalRequests]) OR EXISTS (SELECT 1 FROM [sales].[ApprovalRequestAttempts])
                BEGIN
                    THROW 52130, 'Sales approval requests or their attempts exist; AddSalesApprovalRequests is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "ApprovalRequestAttempts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "ApprovalRequests",
                schema: "sales");
        }
    }
}
