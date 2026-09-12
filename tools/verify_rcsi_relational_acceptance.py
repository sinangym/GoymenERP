#!/usr/bin/env python3
"""Fail-closed discovery and TRX gate for the Issue #134 RCSI acceptance slice."""

from __future__ import annotations

import os
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET


PROJECT = "tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj"
BROAD_FILTER = "FullyQualifiedName~SqlServerRelationalAcceptanceTests"
RCSI_FILTER = (
    "FullyQualifiedName~"
    "GoymenERP.Api.IntegrationTests."
    "SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests"
)
PREFIX = (
    "GoymenERP.Api.IntegrationTests."
    "SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests."
)
EXPECTED_TESTS = tuple(
    sorted(
        PREFIX + name
        for name in (
            "CliProcess_OverallTimeoutKillsAndAwaitsTheHungChildWithoutEvidence",
            "CliProcess_ProducesFreshAtomicEvidenceForReadyOffMismatchAndStalePaths",
            "ExpectedDatabaseMismatch_IsRejectedBeforeTheConnectionIsOpened",
            "PowerShellWrapper_MultipleEndpointsReadyAndOffUseBoundApphostAndPropagateExit",
            "PowerShellWrapper_OverallTimeoutKillsChildWithoutEvidenceOrSecretEcho",
            "PowerShellWrapper_InFlightScriptDriftFailsClosedAndDiscardsEvidence",
            "RcsiOffStartupFailsThenExplicitTestAdminEnableAllowsStartupAndProbe",
            "RcsiOff_ProbeFailsClosedAndDoesNotChangeTheDatabase",
            "RcsiOn_ProbeAndIdentityStartupUseTheSameReadyPolicy",
            "UnapprovedLogicalEndpoint_IsRejectedWithoutChangingDatabase",
        )
    )
)
BROAD_EXPECTED_TESTS = (
    "GoymenERP.Api.IntegrationTests.ProductGlobalRuntimeSqlServerRelationalAcceptanceTests.ProductGlobalRuntime_ReadCreateEditReplayAndCompanyIsolation_AreDurable",
    "GoymenERP.Api.IntegrationTests.ProductGlobalRuntimeSqlServerRelationalAcceptanceTests.ProductGlobalRuntime_EachFlushFailureAndLateRevocation_RollBackAllOwners",
    "GoymenERP.Api.IntegrationTests.ProductGlobalRuntimeSqlServerRelationalAcceptanceTests.ProductGlobalRuntime_ConcurrentDuplicateAndSharedPrefixCommands_HaveSingleEffects",
    "GoymenERP.Api.IntegrationTests.ProductGlobalRuntimeSqlServerRelationalAcceptanceTests.ProductGlobalRuntime_TwoEditsStagedAtVersionOne_ProduceOneLateEfConflict",
    "GoymenERP.Api.IntegrationTests.OrganizationLocationLifecycleSqlServerRelationalAcceptanceTests.ListDetailDeactivateReactivateAndDelayedReplay_ReturnFreshStateAndSingleAuditPerCommand",
    "GoymenERP.Api.IntegrationTests.OrganizationLocationLifecycleSqlServerRelationalAcceptanceTests.ScopedPagingAndRejectedCommands_PreserveLocationAndExposeNoForeignState",
    "GoymenERP.Api.IntegrationTests.CounterpartyContactInformationSqlServerRelationalAcceptanceTests.ContactCard_NoOpKeepsLogicalVersionAndAuditWhilePersistingOneReplayResult",
    "GoymenERP.Api.IntegrationTests.CounterpartyContactInformationSqlServerRelationalAcceptanceTests.AccountContactCard_RoundTripsExplicitChildrenWithoutChangingPartyOrOtherCompany",
    "GoymenERP.Api.IntegrationTests.CounterpartyContactInformationSqlServerRelationalAcceptanceTests.ContactCard_AuthorizationNoDefaultGrantAndMissingRelationAreNonDisclosing",
    "GoymenERP.Api.IntegrationTests.CounterpartyContactInformationSqlServerRelationalAcceptanceTests.ContactCard_RejectsMalformedChangesAndPreservesExactInt64Versions",
    "GoymenERP.Api.IntegrationTests.CounterpartyContactInformationSqlServerRelationalAcceptanceTests.ContactCard_ConcurrentCommandsHaveOneEffectAndDurableTerminalReplay",
    "GoymenERP.Api.IntegrationTests.CounterpartyContactInformationSqlServerRelationalAcceptanceTests.ContactCard_AuditAndIdempotencyFailuresRollBackAndAllowSameKeyRetry",
    "GoymenERP.Api.IntegrationTests.CounterpartyContactInformationSqlServerRelationalAcceptanceTests.ContactCard_MigrationChecksJsonAndBlocksLossyDownWithoutChangingExistingIdentity",
    "GoymenERP.Api.IntegrationTests.CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.RolelessInactiveAccount_StaleVersionAndPermissionGatesRemainEnforced",
    "GoymenERP.Api.IntegrationTests.CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.ClientContractGate_BlocksUnnegotiatedWritesAndPreservesSameKeyRetry",
    "GoymenERP.Api.IntegrationTests.CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.ClientContractGate_WithholdsRolelessReadsWithoutLeakingForeignCompanyState",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyLinkSqlServerRelationalAcceptanceTests.ExistingParty_CanBeLinkedWithNoRole",
    "GoymenERP.Api.IntegrationTests.CounterpartyWriteSqlServerRelationalAcceptanceTests.CanCreateCounterpartyWithoutRole",
    "GoymenERP.Api.IntegrationTests.CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.MissingAndZeroRoles_CreateAndLinkShareReplayAndRejectInvalidJson",
    "GoymenERP.Api.IntegrationTests.CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.PreviousSchemaUpgradeAndSafeDown_PreserveLegacyRolesAndDurableReplays",
    "GoymenERP.Api.IntegrationTests.CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.RolelessRows_BlockDownBeforeSchemaChanges_AndAllowForwardReapply",
    "GoymenERP.Api.IntegrationTests.CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.RolelessReads_RespectReadOnlyAuthorizationAndCompanyIsolationWithoutCreatingAccounts",
    "GoymenERP.Api.IntegrationTests.CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.RolelessDifferentKeyLinkRace_CreatesOneAccountAndOneAudit",
    "GoymenERP.Api.IntegrationTests.CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.RolelessAuditAndIdempotencyFailures_RollBackAndPermitSameKeyRetry",
    "GoymenERP.Api.IntegrationTests.C01E2eGoldenFixtureSqlServerRelationalAcceptanceTests.Fixture_MaterializesOnceAndProvesC01HttpSqlAndSequenceContract",
    "GoymenERP.Api.IntegrationTests.C01E2eGoldenFixtureSqlServerRelationalAcceptanceTests.FixtureArtifact_IsTheExactCanonicalContract",
    "GoymenERP.Api.IntegrationTests.CompanyDirectorySqlServerRelationalAcceptanceTests.Directory_EnforcesAssignmentIsolationActiveFilteringAndReadOnlyBehavior",
    "GoymenERP.Api.IntegrationTests.CounterpartyAuditEventTenantGuardSqlServerRelationalAcceptanceTests.AuditSidecar_IsTenantFilteredAndAllWritePathsFailClosed",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyLinkInactivePartySqlServerRelationalAcceptanceTests.GloballyInactiveParty_IsNonDisclosingAndDurablyReplayedWithoutEffects",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyLinkProblemCodeSqlServerRelationalAcceptanceTests.ConflictResponses_ExposeStableCodes_OnInitialReplayAndRacePaths",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyLinkProblemCodeSqlServerRelationalAcceptanceTests.GloballyActiveParty_WithInactiveSourceAccount_CanBeLinkedToTargetCompany",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyLinkReviewGapSqlServerRelationalAcceptanceTests.CompanyInputsCannotOverrideActiveContext_AndWrongScopeIsForbidden",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyLinkReviewGapSqlServerRelationalAcceptanceTests.HiddenPartyNotFound_IsDurablyReplayedWithoutDisclosureOrEffects",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyLinkReviewGapSqlServerRelationalAcceptanceTests.SameAndDifferentKeyRaces_ProduceExactlyOneCreatedAuditPerParty",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyLinkReviewGapSqlServerRelationalAcceptanceTests.SameKeyDifferentActor_ReturnsConflictWithoutSecondEffect",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyLinkSecurityBoundarySqlServerRelationalAcceptanceTests.PermissionInputAndHeaderBoundaries_AreFailClosedAndDoNotPoisonValidRetry",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyLinkSqlServerRelationalAcceptanceTests.AuditOrIdempotencyFailure_RollsBackOnlyNewCompanyLink",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyLinkSqlServerRelationalAcceptanceTests.DurableCreatedReplay_FailsClosedForUnknownSchemaOrForeignAccountRelationship",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyLinkSqlServerRelationalAcceptanceTests.ExistingParty_CanBeLinkedToSecondAuthorizedCompany_WithoutDuplicatingParty",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyLinkSqlServerRelationalAcceptanceTests.SameAndDifferentKeyLinkRaces_ProduceOneAccountAndNoServerError",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyLinkSqlServerRelationalAcceptanceTests.TargetAuthorizedActor_CannotLinkPartyThatIsNotVisibleThroughAnyAllowedRelationship",
    "GoymenERP.Api.IntegrationTests.CounterpartyCompanyScopedIdempotencySqlServerRelationalAcceptanceTests.SameIdempotencyKey_InDifferentCompanies_IsIndependent",
    "GoymenERP.Api.IntegrationTests.CounterpartyIdempotencyHeaderBoundarySqlServerRelationalAcceptanceTests.ExactMaximumLengthPaddedKey_IsAcceptedAndTrimmedReplayAddsNoEffects",
    "GoymenERP.Api.IntegrationTests.CounterpartyIdempotencyHeaderBoundarySqlServerRelationalAcceptanceTests.InvalidIdempotencyHeader_IsRejectedBeforeCreateOrStatusPersistence",
    "GoymenERP.Api.IntegrationTests.CounterpartyJourneySqlServerRelationalAcceptanceTests.CreateFindDetailAndLinkSecondCompany_PreserveIdentityVisibilityAndExactlyOnceEffects",
    "GoymenERP.Api.IntegrationTests.CounterpartyLinkCandidateSqlServerRelationalAcceptanceTests.CandidateRead_EnforcesVisibilityAuthorityCursorAndBoundedReadOnlySql",
    "GoymenERP.Api.IntegrationTests.CounterpartyLinkCandidateSqlServerRelationalAcceptanceTests.NarrowSourceScope_OmitsCandidateAndKnownIdLinkHasNoEffect_ThenSameKeySucceedsAfterCompanyWidePromotion",
    "GoymenERP.Api.IntegrationTests.CounterpartyNoOpConcurrencySqlServerRelationalAcceptanceTests.Different_key_no_op_status_commands_are_serialized",
    "GoymenERP.Api.IntegrationTests.CounterpartyReadPageSqlServerRelationalAcceptanceTests.C01ReadPage_EnforcesAuthorizationFiltersTurkishSearchKeysetAndRedaction",
    "GoymenERP.Api.IntegrationTests.CounterpartyReadPageSqlServerRelationalAcceptanceTests.CompanyDeactivationBetweenAuthorizationAndDataRead_FailsClosed",
    "GoymenERP.Api.IntegrationTests.CounterpartySqlServerRelationalAcceptanceTests.CounterpartyMigrationConstraintsAndGroupRead_AreEnforced",
    "GoymenERP.Api.IntegrationTests.CounterpartyWriteSafetySqlServerRelationalAcceptanceTests.AuditAndIdempotencyInsertFailures_RollBackCounterpartyCreate",
    "GoymenERP.Api.IntegrationTests.CounterpartyWriteSafetySqlServerRelationalAcceptanceTests.ConcurrentCreatesAndDifferentKeyStatusWrites_AreSerialized",
    "GoymenERP.Api.IntegrationTests.CounterpartyWriteSafetySqlServerRelationalAcceptanceTests.CounterpartyWrites_RequireAuthorizedCompanyWideContext",
    "GoymenERP.Api.IntegrationTests.CounterpartyWriteSqlServerRelationalAcceptanceTests.WriteIdempotencyAndTerminalReplay_AreDurable",
    "GoymenERP.Api.IntegrationTests.IdentityPermissionReconciliationSqlServerRelationalAcceptanceTests.ExistingInstall_ReconcilesOptionAFailClosedAndDuplicateSafe",
    "GoymenERP.Api.IntegrationTests.IdentityPermissionReconciliationSqlServerRelationalAcceptanceTests.FreshDatabase_SeedsOnlyCanonicalAdministratorGrant",
    "GoymenERP.Api.IntegrationTests.OrganizationAuditEventTenantGuardSqlServerRelationalAcceptanceTests.AuditSidecar_IsTenantFilteredAndAllWritePathsFailClosed",
    "GoymenERP.Api.IntegrationTests.OrganizationRuntimeSeedSqlServerRelationalAcceptanceTests.FixedGinIdAndCode_WithLegacySemanticDrift_IsNormalizedByFiscalMigration",
    "GoymenERP.Api.IntegrationTests.OrganizationRuntimeSeedSqlServerRelationalAcceptanceTests.FixedGinIdWithUnexpectedCode_RollsBackFiscalMigrationAtomically",
    "GoymenERP.Api.IntegrationTests.OrganizationRuntimeSeedSqlServerRelationalAcceptanceTests.FreshRuntimeSeed_PersistsApprovedFiscalRowsAndProductionTopologyWithoutDirectoryLeak",
    "GoymenERP.Api.IntegrationTests.OrganizationRuntimeSeedSqlServerRelationalAcceptanceTests.GinCodeOwnedByUnexpectedCompany_RollsBackFiscalMigrationAtomically",
    "GoymenERP.Api.IntegrationTests.OrganizationRuntimeSeedSqlServerRelationalAcceptanceTests.LegacyAliasCollision_RollsBackFiscalMigrationAtomically",
    "GoymenERP.Api.IntegrationTests.OrganizationRuntimeSeedSqlServerRelationalAcceptanceTests.LegacyPlaceholderUpgrade_PreservesCompanyIdsAndOrganizationForeignKeys",
    "GoymenERP.Api.IntegrationTests.PlatformRequestMetadataProblemCodesSqlServerRelationalAcceptanceTests.RejectedMetadata_IsStableNonDisclosingAndLeavesNoPersistentEffect",
    "GoymenERP.Api.IntegrationTests.ShellCapabilitySqlServerRelationalAcceptanceTests.AuthorizationSnapshot_NeverPublishesSyntheticMultiPermissionGrant",
    "GoymenERP.Api.IntegrationTests.ShellCapabilitySqlServerRelationalAcceptanceTests.AuthorizationSnapshotStartup_FailsClosedWhenRcsiIsDisabled",
    "GoymenERP.Api.IntegrationTests.ShellCapabilitySqlServerRelationalAcceptanceTests.ExactCompanySnapshot_TracksPersistedAuthorizationAndHasNoWriteSideEffects",
    "GoymenERP.Api.IntegrationTests.ShellCapabilitySqlServerRelationalAcceptanceTests.HeaderlessGroupCapability_UsesPerCompanyPermissionsAndTracksRevocation",
    "GoymenERP.Api.IntegrationTests.ShellCapabilitySqlServerRelationalAcceptanceTests.SelectedCompanySnapshot_FailsClosedWhenCompanyIsDeactivatedDuringAuthorization",
    "GoymenERP.Api.IntegrationTests.ShellCapabilitySqlServerRelationalAcceptanceTests.StagedCounterpartyCapability_RemainsHiddenAndTracksPermissionRevision",
    "GoymenERP.Api.IntegrationTests.SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests.CliProcess_OverallTimeoutKillsAndAwaitsTheHungChildWithoutEvidence",
    "GoymenERP.Api.IntegrationTests.SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests.CliProcess_ProducesFreshAtomicEvidenceForReadyOffMismatchAndStalePaths",
    "GoymenERP.Api.IntegrationTests.SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests.ExpectedDatabaseMismatch_IsRejectedBeforeTheConnectionIsOpened",
    "GoymenERP.Api.IntegrationTests.SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests.PowerShellWrapper_MultipleEndpointsReadyAndOffUseBoundApphostAndPropagateExit",
    "GoymenERP.Api.IntegrationTests.SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests.PowerShellWrapper_OverallTimeoutKillsChildWithoutEvidenceOrSecretEcho",
    "GoymenERP.Api.IntegrationTests.SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests.PowerShellWrapper_InFlightScriptDriftFailsClosedAndDiscardsEvidence",
    "GoymenERP.Api.IntegrationTests.SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests.RcsiOff_ProbeFailsClosedAndDoesNotChangeTheDatabase",
    "GoymenERP.Api.IntegrationTests.SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests.RcsiOffStartupFailsThenExplicitTestAdminEnableAllowsStartupAndProbe",
    "GoymenERP.Api.IntegrationTests.SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests.RcsiOn_ProbeAndIdentityStartupUseTheSameReadyPolicy",
    "GoymenERP.Api.IntegrationTests.SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests.UnapprovedLogicalEndpoint_IsRejectedWithoutChangingDatabase",
    "GoymenERP.Api.IntegrationTests.SqlServerRelationalAcceptanceTests.MigrationSeedWriteGuardCompositeForeignKeyAndHttpIsolation_AreEnforced",
)
BROAD_DISCOVERY_COUNT = 82
BROAD_EXECUTION_COUNT = 89


def verify_discovery(standard_output: str) -> None:
    discovered = tuple(
        sorted(
            line.strip()
            for line in standard_output.splitlines()
            if line.strip().startswith(PREFIX)
        )
    )
    if discovered != EXPECTED_TESTS:
        raise RuntimeError(
            "RCSI discovery must contain exactly the ten reviewed tests. "
            f"Expected {EXPECTED_TESTS!r}, found {discovered!r}."
        )


def verify_broad_discovery(standard_output: str) -> None:
    discovered = tuple(
        sorted(
            {
                line.strip()
                .split("(", 1)[0]
                for line in standard_output.splitlines()
                if line.strip().startswith("GoymenERP.Api.IntegrationTests.")
                and "SqlServerRelationalAcceptanceTests." in line
            }
        )
    )
    if discovered != tuple(sorted(BROAD_EXPECTED_TESTS)):
        raise RuntimeError(
            "Broad SQL Server relational discovery must retain the exact "
            f"{BROAD_DISCOVERY_COUNT}-test reviewed inventory. "
            f"Expected {tuple(sorted(BROAD_EXPECTED_TESTS))!r}, found {discovered!r}."
        )


def verify_trx(path: Path, expected_total: int) -> None:
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as exception:
        raise RuntimeError(f"RCSI TRX could not be parsed: {exception}") from exception

    counters = [
        element
        for element in root.iter()
        if element.tag.rsplit("}", 1)[-1] == "Counters"
    ]
    if len(counters) != 1:
        raise RuntimeError(
            f"RCSI TRX must contain exactly one Counters element; found {len(counters)}."
        )

    expected = {
        "total": expected_total,
        "executed": expected_total,
        "passed": expected_total,
        "failed": 0,
        "error": 0,
        "timeout": 0,
        "aborted": 0,
        "inconclusive": 0,
        "notExecuted": 0,
    }
    actual: dict[str, int] = {}
    for name, expected_value in expected.items():
        raw_value = counters[0].attrib.get(name)
        try:
            actual[name] = int(raw_value) if raw_value is not None else -1
        except ValueError as exception:
            raise RuntimeError(
                f"RCSI TRX counter {name!r} is not an integer: {raw_value!r}."
            ) from exception
        if actual[name] != expected_value:
            raise RuntimeError(
                "SQL Server relational TRX is not an exact all-passed, skipped=0 run. "
                f"Expected {expected!r}, found {actual!r}."
            )


def run_checked(arguments: list[str], repository_root: Path) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        arguments,
        cwd=repository_root,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=1200,
    )
    if result.returncode != 0:
        sys.stdout.write(result.stdout)
        sys.stderr.write(result.stderr)
        raise RuntimeError(
            f"RCSI acceptance command exited with native code {result.returncode}."
        )
    return result


def run_bootstrap_preflight(repository_root: Path) -> None:
    run_checked(
        [
            sys.executable,
            "-I",
            "-S",
            "-B",
            "tools/validate_dotnet_bootstrap_integrity.py",
        ],
        repository_root,
    )


def main() -> int:
    repository_root = Path(__file__).resolve().parent.parent
    if not (repository_root / "GoymenERP.slnx").is_file():
        raise RuntimeError("Göymen ERP repository root could not be resolved.")
    if not os.environ.get("GOYMEN_ERP_TEST_SQLSERVER_CONNECTION", "").strip():
        raise RuntimeError(
            "GOYMEN_ERP_TEST_SQLSERVER_CONNECTION must be injected by the relational runner."
        )

    if sys.argv[1:] == ["--broad"]:
        filter_value = BROAD_FILTER
        expected_total = BROAD_EXECUTION_COUNT
        verify_discovered = verify_broad_discovery
        gate_name = "SQLSERVER_RELATIONAL_ACCEPTANCE"
    elif sys.argv[1:] == ["--rcsi"]:
        filter_value = RCSI_FILTER
        expected_total = len(EXPECTED_TESTS)
        verify_discovered = verify_discovery
        gate_name = "RCSI_RELATIONAL_ACCEPTANCE"
    else:
        raise RuntimeError("Exactly one mode is required: --broad or --rcsi.")

    run_bootstrap_preflight(repository_root)
    list_result = run_checked(
        [
            "dotnet",
            "test",
            PROJECT,
            "-noAutoResponse",
            "--configuration",
            "Release",
            "--no-restore",
            "--list-tests",
            "--filter",
            filter_value,
            "--logger",
            "console;verbosity=quiet",
        ],
        repository_root,
    )
    verify_discovered(list_result.stdout)

    with tempfile.TemporaryDirectory(prefix="goymen-rcsi-trx-") as temporary_directory:
        trx_path = Path(temporary_directory) / "rcsi.trx"
        run_bootstrap_preflight(repository_root)
        run_result = run_checked(
            [
                "dotnet",
                "test",
                PROJECT,
                "-noAutoResponse",
                "--configuration",
                "Release",
                "--no-build",
                "--no-restore",
                "--filter",
                filter_value,
                "--results-directory",
                temporary_directory,
                "--logger",
                "trx;LogFileName=rcsi.trx",
            ],
            repository_root,
        )
        sys.stdout.write(run_result.stdout)
        sys.stderr.write(run_result.stderr)
        verify_trx(trx_path, expected_total)

    print(
        f"{gate_name}: PASS total={expected_total} passed={expected_total} "
        "failed=0 skipped=0"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.TimeoutExpired) as exception:
        print(f"RCSI_RELATIONAL_ACCEPTANCE: FAIL {exception}", file=sys.stderr)
        raise SystemExit(1) from exception
