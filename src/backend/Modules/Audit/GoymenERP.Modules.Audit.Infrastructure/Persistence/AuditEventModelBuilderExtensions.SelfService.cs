namespace GoymenERP.Modules.Audit.Infrastructure.Persistence;

public static partial class AuditEventModelBuilderExtensions
{
    // A self-service credential transition carries no administrator permission or assigned role.
    // The typed Identity port proves fresh session/membership; the Audit owner additionally fixes actor == target.
    internal const string LocalCredentialSelfServicePermissionCheck =
        "([PermissionCode] IS NULL AND [SubjectKind] = N'Company' AND [SchemaVersion] = 1 "
        + "AND [ActorType] COLLATE Latin1_General_100_BIN2 = N'User' "
        + "AND [EntityType] COLLATE Latin1_General_100_BIN2 = N'Identity.LocalCredential' "
        + "AND TRY_CONVERT(uniqueidentifier,[ActorId]) IS NOT NULL AND TRY_CONVERT(uniqueidentifier,[ActorId]) <> '00000000-0000-0000-0000-000000000000' "
        + "AND [ActorId] COLLATE Latin1_General_100_BIN2 = [EntityId] COLLATE Latin1_General_100_BIN2 "
        + "AND (([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.local-credentials.two-factor-enabled.v1' AND [Operation] COLLATE Latin1_General_100_BIN2 = N'EnableTwoFactor') "
        + "OR ([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.local-credentials.two-factor-disabled.v1' AND [Operation] COLLATE Latin1_General_100_BIN2 = N'DisableTwoFactor') "
        + "OR ([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.local-credentials.recovery-codes-regenerated.v1' AND [Operation] COLLATE Latin1_General_100_BIN2 = N'RegenerateRecoveryCodes')) "
        + "AND [EntityVersionBefore] IS NOT NULL AND [EntityVersionBefore] >= 0 AND [EntityVersionAfter] IS NOT NULL "
        + "AND [EntityVersionAfter] > 0 AND [EntityVersionBefore] = [EntityVersionAfter] - 1 "
        + "AND [SourceChannel] COLLATE Latin1_General_100_BIN2 = N'Web' AND [Outcome] COLLATE Latin1_General_100_BIN2 = N'Succeeded' "
        + "AND [RoleCodesJson] = N'[]' AND [AiInvolvement] = N'None' AND [AiContextJson] IS NULL AND [ClientIpAddress] IS NULL AND [UserAgent] IS NULL "
        + "AND [ApprovalContextJson] IS NOT NULL AND ISJSON([ApprovalContextJson]) = 1 "
        + "AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.kind') IS NOT NULL "
        + "AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.kind') COLLATE Latin1_General_100_BIN2 = N'IdentityLocalCredentialSelfServiceV1' "
        + "AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.method') IS NOT NULL "
        + "AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.method') COLLATE Latin1_General_100_BIN2 = N'TOTP')";

    internal const string TypedPermissionCheck = FounderRecoveryPermissionCheck + " OR " + LocalCredentialSelfServicePermissionCheck;
}
