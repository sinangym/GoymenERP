using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalCredentialSelfServiceAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditEvents_PermissionOrFounderRecovery",
                schema: "audit",
                table: "AuditEvents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditEvents_PermissionOrFounderRecovery",
                schema: "audit",
                table: "AuditEvents",
                sql: "([PermissionCode] IS NOT NULL AND LEN(LTRIM(RTRIM([PermissionCode]))) > 0) OR ([PermissionCode] IS NULL AND [SubjectKind] = N'Company' AND [SchemaVersion] = 1 AND [ActorType] COLLATE Latin1_General_100_BIN2 = N'System' AND [ActorId] COLLATE Latin1_General_100_BIN2 = N'identity.recover-founder' AND [EntityType] COLLATE Latin1_General_100_BIN2 = N'Identity.User' AND TRY_CONVERT(uniqueidentifier,[EntityId]) IS NOT NULL AND TRY_CONVERT(uniqueidentifier,[EntityId]) <> '00000000-0000-0000-0000-000000000000' AND (([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.founder.recovered' AND [Operation] COLLATE Latin1_General_100_BIN2 = N'RecoverFounder') OR ([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.founder.password_reset' AND [Operation] COLLATE Latin1_General_100_BIN2 = N'ResetFounderPassword')) AND [SourceChannel] COLLATE Latin1_General_100_BIN2 = N'System' AND [Outcome] COLLATE Latin1_General_100_BIN2 = N'Succeeded' AND [EntityVersionBefore] IS NOT NULL AND [EntityVersionBefore] >= 1 AND [EntityVersionAfter] IS NOT NULL AND [EntityVersionAfter] >= [EntityVersionBefore] AND [RoleCodesJson] = N'[]' AND [AiInvolvement] = N'None' AND [AiContextJson] IS NULL AND [ClientIpAddress] IS NULL AND [UserAgent] IS NULL AND [ApprovalContextJson] IS NOT NULL AND ISJSON([ApprovalContextJson]) = 1 AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.kind') IS NOT NULL AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.kind') COLLATE Latin1_General_100_BIN2 = N'IdentityFounderRecoveryV1' AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.operatingSystemUser') IS NOT NULL AND LEN(LTRIM(RTRIM(JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.operatingSystemUser')))) BETWEEN 1 AND 256 AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.machineName') IS NOT NULL AND LEN(LTRIM(RTRIM(JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.machineName')))) BETWEEN 1 AND 256) OR ([PermissionCode] IS NULL AND [SubjectKind] = N'Company' AND [SchemaVersion] = 1 AND [ActorType] COLLATE Latin1_General_100_BIN2 = N'User' AND [EntityType] COLLATE Latin1_General_100_BIN2 = N'Identity.LocalCredential' AND TRY_CONVERT(uniqueidentifier,[ActorId]) IS NOT NULL AND TRY_CONVERT(uniqueidentifier,[ActorId]) <> '00000000-0000-0000-0000-000000000000' AND [ActorId] COLLATE Latin1_General_100_BIN2 = [EntityId] COLLATE Latin1_General_100_BIN2 AND (([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.local-credentials.two-factor-enabled.v1' AND [Operation] COLLATE Latin1_General_100_BIN2 = N'EnableTwoFactor') OR ([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.local-credentials.two-factor-disabled.v1' AND [Operation] COLLATE Latin1_General_100_BIN2 = N'DisableTwoFactor') OR ([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.local-credentials.recovery-codes-regenerated.v1' AND [Operation] COLLATE Latin1_General_100_BIN2 = N'RegenerateRecoveryCodes')) AND [EntityVersionBefore] IS NOT NULL AND [EntityVersionBefore] >= 0 AND [EntityVersionAfter] IS NOT NULL AND [EntityVersionAfter] > 0 AND [EntityVersionBefore] = [EntityVersionAfter] - 1 AND [SourceChannel] COLLATE Latin1_General_100_BIN2 = N'Web' AND [Outcome] COLLATE Latin1_General_100_BIN2 = N'Succeeded' AND [RoleCodesJson] = N'[]' AND [AiInvolvement] = N'None' AND [AiContextJson] IS NULL AND [ClientIpAddress] IS NULL AND [UserAgent] IS NULL AND [ApprovalContextJson] IS NOT NULL AND ISJSON([ApprovalContextJson]) = 1 AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.kind') IS NOT NULL AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.kind') COLLATE Latin1_General_100_BIN2 = N'IdentityLocalCredentialSelfServiceV1' AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.method') IS NOT NULL AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.method') COLLATE Latin1_General_100_BIN2 = N'TOTP')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [audit].[AuditEvents]
                    WHERE [PermissionCode] IS NULL AND [EntityType] = N'Identity.LocalCredential')
                BEGIN
                    THROW 51970, 'Self-service credential audit history exists; AddLocalCredentialSelfServiceAudit requires a forward fix.', 1;
                END;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditEvents_PermissionOrFounderRecovery",
                schema: "audit",
                table: "AuditEvents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditEvents_PermissionOrFounderRecovery",
                schema: "audit",
                table: "AuditEvents",
                sql: "([PermissionCode] IS NOT NULL AND LEN(LTRIM(RTRIM([PermissionCode]))) > 0) OR ([PermissionCode] IS NULL AND [SubjectKind] = N'Company' AND [SchemaVersion] = 1 AND [ActorType] COLLATE Latin1_General_100_BIN2 = N'System' AND [ActorId] COLLATE Latin1_General_100_BIN2 = N'identity.recover-founder' AND [EntityType] COLLATE Latin1_General_100_BIN2 = N'Identity.User' AND TRY_CONVERT(uniqueidentifier,[EntityId]) IS NOT NULL AND TRY_CONVERT(uniqueidentifier,[EntityId]) <> '00000000-0000-0000-0000-000000000000' AND (([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.founder.recovered' AND [Operation] COLLATE Latin1_General_100_BIN2 = N'RecoverFounder') OR ([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.founder.password_reset' AND [Operation] COLLATE Latin1_General_100_BIN2 = N'ResetFounderPassword')) AND [SourceChannel] COLLATE Latin1_General_100_BIN2 = N'System' AND [Outcome] COLLATE Latin1_General_100_BIN2 = N'Succeeded' AND [EntityVersionBefore] IS NOT NULL AND [EntityVersionBefore] >= 1 AND [EntityVersionAfter] IS NOT NULL AND [EntityVersionAfter] >= [EntityVersionBefore] AND [RoleCodesJson] = N'[]' AND [AiInvolvement] = N'None' AND [AiContextJson] IS NULL AND [ClientIpAddress] IS NULL AND [UserAgent] IS NULL AND [ApprovalContextJson] IS NOT NULL AND ISJSON([ApprovalContextJson]) = 1 AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.kind') IS NOT NULL AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.kind') COLLATE Latin1_General_100_BIN2 = N'IdentityFounderRecoveryV1' AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.operatingSystemUser') IS NOT NULL AND LEN(LTRIM(RTRIM(JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.operatingSystemUser')))) BETWEEN 1 AND 256 AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.machineName') IS NOT NULL AND LEN(LTRIM(RTRIM(JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.machineName')))) BETWEEN 1 AND 256)");
        }
    }
}
