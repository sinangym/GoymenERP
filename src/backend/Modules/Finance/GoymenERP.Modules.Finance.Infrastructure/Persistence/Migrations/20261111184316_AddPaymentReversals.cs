using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_OutgoingMoneyEvents_CompanyId_Id",
                schema: "finance",
                table: "OutgoingMoneyEvents",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateTable(
                name: "PaymentReversalProposals",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProposedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReversalEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReversalProposals", x => x.Id);
                    table.UniqueConstraint("AK_PaymentReversalProposals_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PaymentReversalProposals_Decision", "([Status] = 0 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [ReversalEventId] IS NULL) OR ([Status] = 1 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NULL AND [ReversalEventId] IS NOT NULL AND [ProposedBy] <> [DecidedBy]) OR ([Status] = 2 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NOT NULL AND LEN([DecisionReason]) > 0 AND [ReversalEventId] IS NULL AND [ProposedBy] <> [DecidedBy])");
                    table.CheckConstraint("CK_PaymentReversalProposals_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PaymentId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PaymentReversalProposals_Text", "LEN([Reason]) > 0 AND LEN([ProposedBy]) > 0");
                    table.CheckConstraint("CK_PaymentReversalProposals_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_PaymentReversalProposals_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentReversalProposals_ManualBankPayments_CompanyId_PaymentId",
                        columns: x => new { x.CompanyId, x.PaymentId },
                        principalSchema: "finance",
                        principalTable: "ManualBankPayments",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutgoingMoneyEventReversals",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TreasuryAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    ProfileId = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: false),
                    ProfileVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingMoneyEventReversals", x => x.Id);
                    table.CheckConstraint("CK_OutgoingMoneyEventReversals_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PaymentId] <> '00000000-0000-0000-0000-000000000000' AND [OriginalEventId] <> '00000000-0000-0000-0000-000000000000' AND [ProposalId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [TreasuryAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> [OriginalEventId]");
                    table.CheckConstraint("CK_OutgoingMoneyEventReversals_Text", "LEN([AmountText]) > 0 AND LEN([ProfileId]) > 0 AND LEN([ProfileVersion]) > 0 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_OutgoingMoneyEventReversals_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutgoingMoneyEventReversals_ManualBankPayments_CompanyId_PaymentId",
                        columns: x => new { x.CompanyId, x.PaymentId },
                        principalSchema: "finance",
                        principalTable: "ManualBankPayments",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutgoingMoneyEventReversals_OutgoingMoneyEvents_CompanyId_OriginalEventId",
                        columns: x => new { x.CompanyId, x.OriginalEventId },
                        principalSchema: "finance",
                        principalTable: "OutgoingMoneyEvents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutgoingMoneyEventReversals_PaymentReversalProposals_CompanyId_ProposalId",
                        columns: x => new { x.CompanyId, x.ProposalId },
                        principalSchema: "finance",
                        principalTable: "PaymentReversalProposals",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMoneyEventReversals_CompanyId_OriginalEventId",
                schema: "finance",
                table: "OutgoingMoneyEventReversals",
                columns: new[] { "CompanyId", "OriginalEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMoneyEventReversals_CompanyId_PaymentId",
                schema: "finance",
                table: "OutgoingMoneyEventReversals",
                columns: new[] { "CompanyId", "PaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMoneyEventReversals_CompanyId_ProposalId",
                schema: "finance",
                table: "OutgoingMoneyEventReversals",
                columns: new[] { "CompanyId", "ProposalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PaymentReversalProposals_ApprovedPayment",
                schema: "finance",
                table: "PaymentReversalProposals",
                columns: new[] { "CompanyId", "PaymentId" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_PaymentReversalProposals_PendingPayment",
                schema: "finance",
                table: "PaymentReversalProposals",
                columns: new[] { "CompanyId", "PaymentId" },
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[PaymentReversalProposals])
                    OR EXISTS (SELECT 1 FROM [finance].[OutgoingMoneyEventReversals])
                BEGIN
                    THROW 51051, 'Payment reversal proposals or recorded payment reversals exist; AddPaymentReversals is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "OutgoingMoneyEventReversals",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "PaymentReversalProposals",
                schema: "finance");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OutgoingMoneyEvents_CompanyId_Id",
                schema: "finance",
                table: "OutgoingMoneyEvents");
        }
    }
}
