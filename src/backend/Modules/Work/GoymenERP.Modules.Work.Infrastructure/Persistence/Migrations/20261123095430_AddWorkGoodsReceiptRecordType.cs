using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkGoodsReceiptRecordType : Migration
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkTasks_Identity",
                schema: "work",
                table: "Tasks",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [AssignerUserId] <> '00000000-0000-0000-0000-000000000000' AND [AssigneeUserId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Title]) > 0 AND [Version] >= 1 AND (([RecordType] IS NULL AND [RecordId] IS NULL) OR ([RecordType] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13) AND [RecordId] IS NOT NULL AND [RecordId] <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkRecordNotes_Identity",
                schema: "work",
                table: "RecordNotes",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecordId] <> '00000000-0000-0000-0000-000000000000' AND [AuthorUserId] <> '00000000-0000-0000-0000-000000000000' AND [RecordType] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13) AND LEN([Body]) > 0 AND LEN([CorrelationId]) > 0 AND ([CorrectsNoteId] IS NULL OR ([CorrectsNoteId] <> '00000000-0000-0000-0000-000000000000' AND [CorrectsNoteId] <> [Id]))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkInboxNotifications_Identity",
                schema: "work",
                table: "InboxNotifications",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecipientUserId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CorrelationId]) > 0 AND (([Kind] = 1 AND [Class] = 1 AND [RecordType] IS NOT NULL AND [RecordId] IS NOT NULL AND [ActorUserId] IS NOT NULL) OR ([Kind] = 2 AND [Class] = 2 AND [ActorUserId] IS NOT NULL) OR ([Kind] = 3 AND [Class] = 1 AND [ActorUserId] IS NULL)) AND (([RecordType] IS NULL AND [RecordId] IS NULL) OR ([RecordType] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13) AND [RecordId] IS NOT NULL AND [RecordId] <> '00000000-0000-0000-0000-000000000000'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // YOL-D42 forward fix: refuse while any note, task or notification is bound to a purchase goods receipt (13);
            // an empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [work].[RecordNotes] WHERE [RecordType] = 13)
                    OR EXISTS (SELECT 1 FROM [work].[Tasks] WHERE [RecordType] = 13)
                    OR EXISTS (SELECT 1 FROM [work].[InboxNotifications] WHERE [RecordType] = 13)
                BEGIN
                    THROW 52171, 'Work notes, tasks or notifications on purchase goods receipts exist; AddWorkGoodsReceiptRecordType is not rolled back (forward fix).', 1;
                END;
                """);

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
    }
}
