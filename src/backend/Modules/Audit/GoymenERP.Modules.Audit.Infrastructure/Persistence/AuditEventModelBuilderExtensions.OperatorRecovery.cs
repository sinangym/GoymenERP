namespace GoymenERP.Modules.Audit.Infrastructure.Persistence;

public static partial class AuditEventModelBuilderExtensions
{
    internal const string CompanyAndProductSubjectCheck =
        "([SubjectKind] = N'Company' AND [CompanyId] IS NOT NULL AND [OriginCompanyId] IS NULL AND [AuthorityRevision] IS NULL AND [SchemaVersion] = 1) OR "
        + "([SubjectKind] = N'GroupSharedProduct' AND [CompanyId] IS NULL AND [SchemaVersion] = 2 "
        + "AND [OriginCompanyId] IS NOT NULL AND [OriginCompanyId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [AuthorityRevision] IS NOT NULL AND LEN([AuthorityRevision]) > 0 AND [EntityType] = N'Product.Product' AND LEN([EntityId]) = 36 "
        + "AND TRY_CONVERT(uniqueidentifier, [EntityId]) IS NOT NULL AND TRY_CONVERT(uniqueidentifier, [EntityId]) <> '00000000-0000-0000-0000-000000000000')";

    // The additional NULL-permission subject is a closed operator CLI event, never a generic System draft.
    internal const string OperatorRecoveryPermissionCheck =
        "([SubjectKind] COLLATE Latin1_General_100_BIN2 = N'PlatformCredentialRecovery' AND [SchemaVersion] = 3 "
        + "AND [CompanyId] IS NULL AND [OriginCompanyId] IS NULL AND [AuthorityRevision] IS NULL AND [PermissionCode] IS NULL "
        + "AND [ActorType] COLLATE Latin1_General_100_BIN2 = N'System' AND [ActorId] COLLATE Latin1_General_100_BIN2 = N'identity.recover-two-factor' "
        + "AND [EntityType] COLLATE Latin1_General_100_BIN2 = N'Identity.LocalCredential' AND LEN([EntityId]) = 36 "
        + "AND TRY_CONVERT(uniqueidentifier,[EntityId]) IS NOT NULL AND TRY_CONVERT(uniqueidentifier,[EntityId]) <> '00000000-0000-0000-0000-000000000000' "
        + "AND [Operation] COLLATE Latin1_General_100_BIN2 = N'RecoverTwoFactor' AND [SourceChannel] COLLATE Latin1_General_100_BIN2 = N'System' "
        + "AND [EntityVersionBefore] IS NOT NULL AND [EntityVersionBefore] > 0 AND [EntityVersionAfter] IS NOT NULL "
        + "AND (([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.local-credentials.operator-two-factor-recovered.v1' "
        + "AND [Outcome] COLLATE Latin1_General_100_BIN2 = N'Succeeded' AND CONVERT(decimal(20,0),[EntityVersionAfter])-CONVERT(decimal(20,0),[EntityVersionBefore]) = 1) "
        + "OR ([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.local-credentials.operator-two-factor-recovery-refused.v1' "
        + "AND [Outcome] COLLATE Latin1_General_100_BIN2 = N'Refused' AND [EntityVersionAfter] = [EntityVersionBefore])) "
        + "AND [RoleCodesJson] = N'[]' AND [AiInvolvement] = N'None' AND [AiContextJson] IS NULL AND [ClientIpAddress] IS NULL AND [UserAgent] IS NULL "
        + "AND [ApprovalContextJson] IS NOT NULL AND ISJSON([ApprovalContextJson]) = 1 "
        + "AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END,'$.kind') IS NOT NULL "
        + "AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END,'$.kind') COLLATE Latin1_General_100_BIN2 = N'IdentityTwoFactorOperatorRecoveryV1' "
        + "AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END,'$.operator.operatingSystemUser') IS NOT NULL "
        + "AND LEN(LTRIM(RTRIM(JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END,'$.operator.operatingSystemUser')))) BETWEEN 1 AND 256 "
        + "AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END,'$.operator.machineName') IS NOT NULL "
        + "AND LEN(LTRIM(RTRIM(JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END,'$.operator.machineName')))) BETWEEN 1 AND 256)";
}
