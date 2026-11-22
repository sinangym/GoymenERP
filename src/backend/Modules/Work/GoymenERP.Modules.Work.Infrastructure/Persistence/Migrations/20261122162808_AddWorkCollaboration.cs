using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkCollaboration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CollaborationAttempts",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKeySha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    Command = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RefusalCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollaborationAttempts", x => new { x.CompanyId, x.ActorUserId, x.IdempotencyKeySha256 });
                    table.CheckConstraint("CK_WorkCollaborationAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ActorUserId] <> '00000000-0000-0000-0000-000000000000' AND [TargetId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Command]) > 0 AND LEN([CorrelationId]) > 0 AND DATALENGTH([IdempotencyKeySha256]) = 64 AND [IdempotencyKeySha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND DATALENGTH([RequestSha256]) = 64 AND [RequestSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND (([Status] IN (200, 201) AND [RefusalCode] IS NULL) OR ([Status] = 409 AND [RefusalCode] IS NOT NULL AND LEN([RefusalCode]) > 0))");
                    table.ForeignKey(
                        name: "FK_CollaborationAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InboxDigests",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ScheduleVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxDigests", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkInboxDigests_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecipientUserId] <> '00000000-0000-0000-0000-000000000000' AND [ReleasedAtUtc] >= [SlotUtc] AND LEN([ScheduleVersion]) > 0");
                    table.ForeignKey(
                        name: "FK_InboxDigests_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InboxNotifications",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Class = table.Column<int>(type: "int", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordType = table.Column<int>(type: "int", nullable: true),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxNotifications", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkInboxNotifications_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecipientUserId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CorrelationId]) > 0 AND (([Kind] = 1 AND [Class] = 1 AND [RecordType] IS NOT NULL AND [RecordId] IS NOT NULL AND [ActorUserId] IS NOT NULL) OR ([Kind] = 2 AND [Class] = 2 AND [ActorUserId] IS NOT NULL) OR ([Kind] = 3 AND [Class] = 1 AND [ActorUserId] IS NULL)) AND (([RecordType] IS NULL AND [RecordId] IS NULL) OR ([RecordType] IN (1, 2, 3, 4, 5) AND [RecordId] IS NOT NULL AND [RecordId] <> '00000000-0000-0000-0000-000000000000'))");
                    table.ForeignKey(
                        name: "FK_InboxNotifications_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecordNotes",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordType = table.Column<int>(type: "int", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CorrectsNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordNotes", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkRecordNotes_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecordId] <> '00000000-0000-0000-0000-000000000000' AND [AuthorUserId] <> '00000000-0000-0000-0000-000000000000' AND [RecordType] IN (1, 2, 3, 4, 5) AND LEN([Body]) > 0 AND LEN([CorrelationId]) > 0 AND ([CorrectsNoteId] IS NULL OR ([CorrectsNoteId] <> '00000000-0000-0000-0000-000000000000' AND [CorrectsNoteId] <> [Id]))");
                    table.ForeignKey(
                        name: "FK_RecordNotes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecordNotes_RecordNotes_CompanyId_CorrectsNoteId",
                        columns: x => new { x.CompanyId, x.CorrectsNoteId },
                        principalSchema: "work",
                        principalTable: "RecordNotes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordType = table.Column<int>(type: "int", nullable: true),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AssignerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkTasks_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [AssignerUserId] <> '00000000-0000-0000-0000-000000000000' AND [AssigneeUserId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Title]) > 0 AND [Version] >= 1 AND (([RecordType] IS NULL AND [RecordId] IS NULL) OR ([RecordType] IN (1, 2, 3, 4, 5) AND [RecordId] IS NOT NULL AND [RecordId] <> '00000000-0000-0000-0000-000000000000'))");
                    table.CheckConstraint("CK_WorkTasks_State", "([Status] = 1 AND [Version] = 1 AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [CancelReason] IS NULL) OR ([Status] = 2 AND [Version] = 2 AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] = [AssigneeUserId] AND [CancelReason] IS NULL) OR ([Status] = 3 AND [Version] = 2 AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] = [AssignerUserId] AND [CancelReason] IS NOT NULL AND LEN([CancelReason]) > 0)");
                    table.ForeignKey(
                        name: "FK_Tasks_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InboxDigestItems",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DigestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxDigestItems", x => new { x.CompanyId, x.NotificationId });
                    table.ForeignKey(
                        name: "FK_InboxDigestItems_InboxDigests_CompanyId_DigestId",
                        columns: x => new { x.CompanyId, x.DigestId },
                        principalSchema: "work",
                        principalTable: "InboxDigests",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InboxDigestItems_InboxNotifications_CompanyId_NotificationId",
                        columns: x => new { x.CompanyId, x.NotificationId },
                        principalSchema: "work",
                        principalTable: "InboxNotifications",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InboxNotificationReads",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxNotificationReads", x => new { x.CompanyId, x.NotificationId });
                    table.ForeignKey(
                        name: "FK_InboxNotificationReads_InboxNotifications_CompanyId_NotificationId",
                        columns: x => new { x.CompanyId, x.NotificationId },
                        principalSchema: "work",
                        principalTable: "InboxNotifications",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecordNoteMentions",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MentionedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordNoteMentions", x => new { x.CompanyId, x.NoteId, x.MentionedUserId });
                    table.CheckConstraint("CK_WorkRecordNoteMentions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [NoteId] <> '00000000-0000-0000-0000-000000000000' AND [MentionedUserId] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_RecordNoteMentions_RecordNotes_CompanyId_NoteId",
                        columns: x => new { x.CompanyId, x.NoteId },
                        principalSchema: "work",
                        principalTable: "RecordNotes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecordNotePassivations",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PassivatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PassivatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordNotePassivations", x => new { x.CompanyId, x.NoteId });
                    table.CheckConstraint("CK_WorkRecordNotePassivations_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PassivatedByUserId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Reason]) > 0");
                    table.ForeignKey(
                        name: "FK_RecordNotePassivations_RecordNotes_CompanyId_NoteId",
                        columns: x => new { x.CompanyId, x.NoteId },
                        principalSchema: "work",
                        principalTable: "RecordNotes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxDigestItems_CompanyId_DigestId",
                schema: "work",
                table: "InboxDigestItems",
                columns: new[] { "CompanyId", "DigestId" });

            migrationBuilder.CreateIndex(
                name: "UX_WorkInboxDigests_RecipientSlot",
                schema: "work",
                table: "InboxDigests",
                columns: new[] { "CompanyId", "RecipientUserId", "SlotUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxNotifications_CompanyId_RecipientUserId_RecordedAtUtc_Id",
                schema: "work",
                table: "InboxNotifications",
                columns: new[] { "CompanyId", "RecipientUserId", "RecordedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_WorkInboxNotifications_RecipientEvent",
                schema: "work",
                table: "InboxNotifications",
                columns: new[] { "CompanyId", "RecipientUserId", "Kind", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordNotes_CompanyId_CorrectsNoteId",
                schema: "work",
                table: "RecordNotes",
                columns: new[] { "CompanyId", "CorrectsNoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecordNotes_CompanyId_RecordType_RecordId_CreatedAtUtc_Id",
                schema: "work",
                table: "RecordNotes",
                columns: new[] { "CompanyId", "RecordType", "RecordId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CompanyId_AssigneeUserId_Status_DueAtUtc_Id",
                schema: "work",
                table: "Tasks",
                columns: new[] { "CompanyId", "AssigneeUserId", "Status", "DueAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CompanyId_RecordType_RecordId_CreatedAtUtc_Id",
                schema: "work",
                table: "Tasks",
                columns: new[] { "CompanyId", "RecordType", "RecordId", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // W1-C forward fix (YOL-D19): notes, mentions and tasks are kept at least as long as their record (minimum
            // 10 years); notifications, digests, reads and command attempts are evidence. Rolling back with any of them
            // present would destroy them, so Down refuses; an empty store still rolls back.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [work].[RecordNotes])
                    OR EXISTS (SELECT 1 FROM [work].[RecordNoteMentions])
                    OR EXISTS (SELECT 1 FROM [work].[RecordNotePassivations])
                    OR EXISTS (SELECT 1 FROM [work].[Tasks])
                    OR EXISTS (SELECT 1 FROM [work].[InboxNotifications])
                    OR EXISTS (SELECT 1 FROM [work].[InboxNotificationReads])
                    OR EXISTS (SELECT 1 FROM [work].[InboxDigests])
                    OR EXISTS (SELECT 1 FROM [work].[InboxDigestItems])
                    OR EXISTS (SELECT 1 FROM [work].[CollaborationAttempts])
                    THROW 52090, 'Work notes, tasks, collaboration notifications or command attempts exist; AddWorkCollaboration is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropTable(
                name: "CollaborationAttempts",
                schema: "work");

            migrationBuilder.DropTable(
                name: "InboxDigestItems",
                schema: "work");

            migrationBuilder.DropTable(
                name: "InboxNotificationReads",
                schema: "work");

            migrationBuilder.DropTable(
                name: "RecordNoteMentions",
                schema: "work");

            migrationBuilder.DropTable(
                name: "RecordNotePassivations",
                schema: "work");

            migrationBuilder.DropTable(
                name: "Tasks",
                schema: "work");

            migrationBuilder.DropTable(
                name: "InboxDigests",
                schema: "work");

            migrationBuilder.DropTable(
                name: "InboxNotifications",
                schema: "work");

            migrationBuilder.DropTable(
                name: "RecordNotes",
                schema: "work");
        }
    }
}
