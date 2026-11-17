using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTypedFounderRecoverySystemAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PermissionCode",
                schema: "audit",
                table: "AuditEvents",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(160)",
                oldMaxLength: 160);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditEvents_PermissionOrFounderRecovery",
                schema: "audit",
                table: "AuditEvents",
                sql: "([PermissionCode] IS NOT NULL AND LEN(LTRIM(RTRIM([PermissionCode]))) > 0) OR ([PermissionCode] IS NULL AND [SubjectKind] = N'Company' AND [SchemaVersion] = 1 AND [ActorType] COLLATE Latin1_General_100_BIN2 = N'System' AND [ActorId] COLLATE Latin1_General_100_BIN2 = N'identity.recover-founder' AND [EntityType] COLLATE Latin1_General_100_BIN2 = N'Identity.User' AND TRY_CONVERT(uniqueidentifier,[EntityId]) IS NOT NULL AND TRY_CONVERT(uniqueidentifier,[EntityId]) <> '00000000-0000-0000-0000-000000000000' AND (([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.founder.recovered' AND [Operation] COLLATE Latin1_General_100_BIN2 = N'RecoverFounder') OR ([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.founder.password_reset' AND [Operation] COLLATE Latin1_General_100_BIN2 = N'ResetFounderPassword')) AND [SourceChannel] COLLATE Latin1_General_100_BIN2 = N'System' AND [Outcome] COLLATE Latin1_General_100_BIN2 = N'Succeeded' AND [EntityVersionBefore] IS NOT NULL AND [EntityVersionBefore] >= 1 AND [EntityVersionAfter] IS NOT NULL AND [EntityVersionAfter] >= [EntityVersionBefore] AND [RoleCodesJson] = N'[]' AND [AiInvolvement] = N'None' AND [AiContextJson] IS NULL AND [ClientIpAddress] IS NULL AND [UserAgent] IS NULL AND [ApprovalContextJson] IS NOT NULL AND ISJSON([ApprovalContextJson]) = 1 AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.kind') IS NOT NULL AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.kind') COLLATE Latin1_General_100_BIN2 = N'IdentityFounderRecoveryV1' AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.operatingSystemUser') IS NOT NULL AND LEN(LTRIM(RTRIM(JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.operatingSystemUser')))) BETWEEN 1 AND 256 AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.machineName') IS NOT NULL AND LEN(LTRIM(RTRIM(JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.machineName')))) BETWEEN 1 AND 256)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [audit].[AuditEvents] WHERE [PermissionCode] IS NULL)
                BEGIN
                    THROW 51952, 'Typed System audit history exists; AddTypedFounderRecoverySystemAudit is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditEvents_PermissionOrFounderRecovery",
                schema: "audit",
                table: "AuditEvents");

            migrationBuilder.AlterColumn<string>(
                name: "PermissionCode",
                schema: "audit",
                table: "AuditEvents",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(160)",
                oldMaxLength: 160,
                oldNullable: true);
        }
    }
}
