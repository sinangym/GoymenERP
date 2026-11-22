using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WidenWorkRecordTypesAndNoteReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkTasks_Identity",
                schema: "work",
                table: "Tasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkRecordNotes_Identity",
                schema: "work",
                table: "RecordNotes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkInboxNotifications_Identity",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.CreateTable(
                name: "RecordNoteReads",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReaderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordNoteReads", x => new { x.CompanyId, x.NoteId, x.ReaderUserId });
                    table.CheckConstraint("CK_WorkRecordNoteReads_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [NoteId] <> '00000000-0000-0000-0000-000000000000' AND [ReaderUserId] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_RecordNoteReads_RecordNotes_CompanyId_NoteId",
                        columns: x => new { x.CompanyId, x.NoteId },
                        principalSchema: "work",
                        principalTable: "RecordNotes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkTasks_Identity",
                schema: "work",
                table: "Tasks",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [AssignerUserId] <> '00000000-0000-0000-0000-000000000000' AND [AssigneeUserId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Title]) > 0 AND [Version] >= 1 AND (([RecordType] IS NULL AND [RecordId] IS NULL) OR ([RecordType] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12) AND [RecordId] IS NOT NULL AND [RecordId] <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkRecordNotes_Identity",
                schema: "work",
                table: "RecordNotes",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecordId] <> '00000000-0000-0000-0000-000000000000' AND [AuthorUserId] <> '00000000-0000-0000-0000-000000000000' AND [RecordType] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12) AND LEN([Body]) > 0 AND LEN([CorrelationId]) > 0 AND ([CorrectsNoteId] IS NULL OR ([CorrectsNoteId] <> '00000000-0000-0000-0000-000000000000' AND [CorrectsNoteId] <> [Id]))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkInboxNotifications_Identity",
                schema: "work",
                table: "InboxNotifications",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecipientUserId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CorrelationId]) > 0 AND (([Kind] = 1 AND [Class] = 1 AND [RecordType] IS NOT NULL AND [RecordId] IS NOT NULL AND [ActorUserId] IS NOT NULL) OR ([Kind] = 2 AND [Class] = 2 AND [ActorUserId] IS NOT NULL) OR ([Kind] = 3 AND [Class] = 1 AND [ActorUserId] IS NULL)) AND (([RecordType] IS NULL AND [RecordId] IS NULL) OR ([RecordType] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12) AND [RecordId] IS NOT NULL AND [RecordId] <> '00000000-0000-0000-0000-000000000000'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WP9 forward fix: refuse while a read receipt exists or any note, task or notification is bound to a record kind
            // the narrower checks would not accept (6..12); an empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [work].[RecordNoteReads])
                    OR EXISTS (SELECT 1 FROM [work].[RecordNotes] WHERE [RecordType] NOT IN (1, 2, 3, 4, 5))
                    OR EXISTS (SELECT 1 FROM [work].[Tasks] WHERE [RecordType] IS NOT NULL AND [RecordType] NOT IN (1, 2, 3, 4, 5))
                    OR EXISTS (SELECT 1 FROM [work].[InboxNotifications] WHERE [RecordType] IS NOT NULL AND [RecordType] NOT IN (1, 2, 3, 4, 5))
                BEGIN
                    THROW 52170, 'Work note read receipts or notes, tasks or notifications on the WP9 record kinds exist; WidenWorkRecordTypesAndNoteReads is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "RecordNoteReads",
                schema: "work");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkTasks_Identity",
                schema: "work",
                table: "Tasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkRecordNotes_Identity",
                schema: "work",
                table: "RecordNotes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkInboxNotifications_Identity",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkTasks_Identity",
                schema: "work",
                table: "Tasks",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [AssignerUserId] <> '00000000-0000-0000-0000-000000000000' AND [AssigneeUserId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Title]) > 0 AND [Version] >= 1 AND (([RecordType] IS NULL AND [RecordId] IS NULL) OR ([RecordType] IN (1, 2, 3, 4, 5) AND [RecordId] IS NOT NULL AND [RecordId] <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkRecordNotes_Identity",
                schema: "work",
                table: "RecordNotes",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecordId] <> '00000000-0000-0000-0000-000000000000' AND [AuthorUserId] <> '00000000-0000-0000-0000-000000000000' AND [RecordType] IN (1, 2, 3, 4, 5) AND LEN([Body]) > 0 AND LEN([CorrelationId]) > 0 AND ([CorrectsNoteId] IS NULL OR ([CorrectsNoteId] <> '00000000-0000-0000-0000-000000000000' AND [CorrectsNoteId] <> [Id]))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkInboxNotifications_Identity",
                schema: "work",
                table: "InboxNotifications",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecipientUserId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CorrelationId]) > 0 AND (([Kind] = 1 AND [Class] = 1 AND [RecordType] IS NOT NULL AND [RecordId] IS NOT NULL AND [ActorUserId] IS NOT NULL) OR ([Kind] = 2 AND [Class] = 2 AND [ActorUserId] IS NOT NULL) OR ([Kind] = 3 AND [Class] = 1 AND [ActorUserId] IS NULL)) AND (([RecordType] IS NULL AND [RecordId] IS NULL) OR ([RecordType] IN (1, 2, 3, 4, 5) AND [RecordId] IS NOT NULL AND [RecordId] <> '00000000-0000-0000-0000-000000000000'))");
        }
    }
}
