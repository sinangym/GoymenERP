using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCounterpartyIdentityHistoryAndSuccession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerEDocumentStatusRevisions",
                schema: "counterparty",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    RecordedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RecordedInCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerEDocumentStatusRevisions", x => x.Id);
                    table.CheckConstraint("CK_CustomerEDocumentStatusRevisions_Kind", "[Kind] IN (1, 2)");
                    table.CheckConstraint("CK_CustomerEDocumentStatusRevisions_Recorded", "[Kind] = 1 OR ([RecordedByUserId] IS NOT NULL AND LEN([RecordedByUserId]) > 0 AND [RecordedInCompanyId] IS NOT NULL)");
                    table.CheckConstraint("CK_CustomerEDocumentStatusRevisions_Sequence", "[Sequence] >= 1");
                    table.CheckConstraint("CK_CustomerEDocumentStatusRevisions_Status", "[Status] IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_CustomerEDocumentStatusRevisions_Companies_RecordedInCompanyId",
                        column: x => x.RecordedInCompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerEDocumentStatusRevisions_CustomerEDocumentProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalSchema: "counterparty",
                        principalTable: "CustomerEDocumentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartyIdentityRevisions",
                schema: "counterparty",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Attribute = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SourceChangeRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RecordedInCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyIdentityRevisions", x => x.Id);
                    table.CheckConstraint("CK_PartyIdentityRevisions_Attribute", "[Attribute] IN (1, 2)");
                    table.CheckConstraint("CK_PartyIdentityRevisions_Correction", "([Kind] = 3 AND [Attribute] = 1 AND [SourceChangeRequestId] IS NOT NULL AND [Reason] IS NOT NULL AND LEN([Reason]) > 0) OR ([Kind] <> 3 AND [SourceChangeRequestId] IS NULL)");
                    table.CheckConstraint("CK_PartyIdentityRevisions_EffectiveFrom", "([Kind] IN (1, 2) AND [EffectiveFrom] IS NULL) OR ([Kind] IN (3, 4) AND [EffectiveFrom] IS NOT NULL)");
                    table.CheckConstraint("CK_PartyIdentityRevisions_Kind", "[Kind] IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_PartyIdentityRevisions_Recorded", "[Kind] = 1 OR ([RecordedByUserId] IS NOT NULL AND LEN([RecordedByUserId]) > 0 AND [RecordedInCompanyId] IS NOT NULL)");
                    table.CheckConstraint("CK_PartyIdentityRevisions_Sequence", "[Sequence] >= 1");
                    table.CheckConstraint("CK_PartyIdentityRevisions_Value", "LEN([Value]) > 0 AND ([Attribute] <> 1 OR (LEN([Value]) IN (10, 11) AND DATALENGTH([Value]) = LEN([Value]) AND [Value] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%'))");
                    table.ForeignKey(
                        name: "FK_PartyIdentityRevisions_Companies_RecordedInCompanyId",
                        column: x => x.RecordedInCompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartyIdentityRevisions_Parties_PartyId",
                        column: x => x.PartyId,
                        principalSchema: "counterparty",
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartyIdentityRevisions_PartyTaxIdentityChangeRequests_SourceChangeRequestId",
                        column: x => x.SourceChangeRequestId,
                        principalSchema: "counterparty",
                        principalTable: "PartyTaxIdentityChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartySuccessions",
                schema: "counterparty",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PredecessorPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuccessorPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    LegalBasis = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartySuccessions", x => x.Id);
                    table.CheckConstraint("CK_PartySuccessions_Kind", "[Kind] IN (1, 2, 3)");
                    table.CheckConstraint("CK_PartySuccessions_Parties", "[PredecessorPartyId] <> [SuccessorPartyId]");
                    table.CheckConstraint("CK_PartySuccessions_Text", "LEN([LegalBasis]) > 0 AND LEN([RecordedByUserId]) > 0");
                    table.ForeignKey(
                        name: "FK_PartySuccessions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartySuccessions_Parties_PredecessorPartyId",
                        column: x => x.PredecessorPartyId,
                        principalSchema: "counterparty",
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartySuccessions_Parties_SuccessorPartyId",
                        column: x => x.SuccessorPartyId,
                        principalSchema: "counterparty",
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerEDocumentStatusRevisions_RecordedInCompanyId",
                schema: "counterparty",
                table: "CustomerEDocumentStatusRevisions",
                column: "RecordedInCompanyId");

            migrationBuilder.CreateIndex(
                name: "UX_CustomerEDocumentStatusRevisions_ProfileSequence",
                schema: "counterparty",
                table: "CustomerEDocumentStatusRevisions",
                columns: new[] { "ProfileId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartyIdentityRevisions_RecordedInCompanyId",
                schema: "counterparty",
                table: "PartyIdentityRevisions",
                column: "RecordedInCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyIdentityRevisions_SourceChangeRequestId",
                schema: "counterparty",
                table: "PartyIdentityRevisions",
                column: "SourceChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "UX_PartyIdentityRevisions_PartySequence",
                schema: "counterparty",
                table: "PartyIdentityRevisions",
                columns: new[] { "PartyId", "Attribute", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartySuccessions_CompanyId_SuccessorPartyId",
                schema: "counterparty",
                table: "PartySuccessions",
                columns: new[] { "CompanyId", "SuccessorPartyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PartySuccessions_PredecessorPartyId",
                schema: "counterparty",
                table: "PartySuccessions",
                column: "PredecessorPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_PartySuccessions_SuccessorPartyId",
                schema: "counterparty",
                table: "PartySuccessions",
                column: "SuccessorPartyId");

            migrationBuilder.CreateIndex(
                name: "UX_PartySuccessions_Predecessor",
                schema: "counterparty",
                table: "PartySuccessions",
                columns: new[] { "CompanyId", "PredecessorPartyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [counterparty].[PartyIdentityRevisions])
                    OR EXISTS (SELECT 1 FROM [counterparty].[CustomerEDocumentStatusRevisions])
                    OR EXISTS (SELECT 1 FROM [counterparty].[PartySuccessions])
                BEGIN
                    THROW 51530, 'Counterparty identity history or party succession rows exist; AddCounterpartyIdentityHistoryAndSuccession is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CustomerEDocumentStatusRevisions",
                schema: "counterparty");

            migrationBuilder.DropTable(
                name: "PartyIdentityRevisions",
                schema: "counterparty");

            migrationBuilder.DropTable(
                name: "PartySuccessions",
                schema: "counterparty");
        }
    }
}
