using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkApprovalDelegations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalDelegations",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ApproverPermission = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DelegatorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DelegateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RevokedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevocationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDelegations", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkApprovalDelegations_Content", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [DelegatorUserId] <> '00000000-0000-0000-0000-000000000000' AND [DelegateUserId] <> '00000000-0000-0000-0000-000000000000' AND [DelegatorUserId] <> [DelegateUserId] AND [RecordedByUserId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Kind]) > 0 AND LEN([ApproverPermission]) > 0 AND LEN([Reason]) > 0 AND [StartsAtUtc] >= [RecordedAtUtc] AND [EndsAtUtc] > [StartsAtUtc] AND (([Version] = 1 AND [RevokedByUserId] IS NULL AND [RevokedAtUtc] IS NULL AND [RevocationReason] IS NULL) OR ([Version] = 2 AND [RevokedByUserId] IS NOT NULL AND [RevokedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [RevokedAtUtc] IS NOT NULL AND [RevokedAtUtc] >= [RecordedAtUtc] AND [RevocationReason] IS NOT NULL AND LEN([RevocationReason]) > 0))");
                    table.ForeignKey(
                        name: "FK_ApprovalDelegations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDelegations_CompanyId_ApproverPermission_StartsAtUtc_EndsAtUtc",
                schema: "work",
                table: "ApprovalDelegations",
                columns: new[] { "CompanyId", "ApproverPermission", "StartsAtUtc", "EndsAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [work].[ApprovalDelegations] WITH (UPDLOCK, HOLDLOCK))
                    OR EXISTS (SELECT 1 FROM [work].[CollaborationAttempts] WITH (UPDLOCK, HOLDLOCK)
                        WHERE [Command] IN ('work.delegation.create.v1', 'work.delegation.revoke.v1'))
                BEGIN
                    THROW 52120, 'Delegation state or command history exists; AddWorkApprovalDelegations is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "ApprovalDelegations",
                schema: "work");
        }
    }
}
