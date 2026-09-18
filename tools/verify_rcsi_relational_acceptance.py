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
    "GoymenERP.Api.IntegrationTests.ProductGlobalRuntimeSqlServerRelationalAcceptanceTests.ProductGlobalRuntime_SharedSnapshotEmitsEmptyAndPopulatedJsonArrays",
    "GoymenERP.Api.IntegrationTests.ProductGlobalRuntimeSqlServerRelationalAcceptanceTests.ProductGlobalRuntime_ShellCapabilitiesFollowExplicitGlobalAssignments",
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
    "GoymenERP.Api.IntegrationTests.CentralAuthorityRevalidationSqlServerRelationalAcceptanceTests.A_revocation_is_measured_inside_the_request_and_refuses_the_next_one",
    "GoymenERP.Api.IntegrationTests.CentralAuthorityRevalidationSqlServerRelationalAcceptanceTests.A_standing_grant_is_answered_the_same_by_both_ports_and_an_unknown_name_reads_nothing",
    "GoymenERP.Api.IntegrationTests.ProductSalesEligibilityAuthoritySqlServerRelationalAcceptanceTests.A_standing_grant_passes_every_fence_with_one_authority_read_per_fence",
    "GoymenERP.Api.IntegrationTests.ProductSalesEligibilityAuthoritySqlServerRelationalAcceptanceTests.A_revocation_by_another_request_during_the_read_is_seen_at_the_next_fence",
    "GoymenERP.Api.IntegrationTests.ProductSalesEligibilityAuthoritySqlServerRelationalAcceptanceTests.Without_a_grant_the_registered_name_denies_before_any_read",
    "GoymenERP.Api.IntegrationTests.InventorySqlServerRelationalAcceptanceTests.Receipt_issue_block_and_release_are_durable_replayable_audited_and_reconcile",
    "GoymenERP.Api.IntegrationTests.InventorySqlServerRelationalAcceptanceTests.Each_company_sees_only_its_own_labels_and_a_code_is_unique_per_company_only",
    "GoymenERP.Api.IntegrationTests.InventorySqlServerRelationalAcceptanceTests.A_source_causes_one_movement_per_kind_and_a_stock_bearing_label_is_not_received_again",
    "GoymenERP.Api.IntegrationTests.InventorySqlServerRelationalAcceptanceTests.Two_writers_that_read_the_same_piece_cannot_both_commit",
    "GoymenERP.Api.IntegrationTests.InventorySqlServerRelationalAcceptanceTests.A_save_that_fails_in_the_database_leaves_nothing_behind",
    "GoymenERP.Api.IntegrationTests.InventorySqlServerRelationalAcceptanceTests.A_source_is_answered_whole_or_line_by_line_never_both",
    "GoymenERP.Api.IntegrationTests.InventorySqlServerRelationalAcceptanceTests.A_whole_and_a_line_movement_racing_for_one_source_cannot_both_commit",
    "GoymenERP.Api.IntegrationTests.InventorySqlServerRelationalAcceptanceTests.A_label_code_has_one_answer_under_the_code_column_collation",
    "GoymenERP.Api.IntegrationTests.InventorySqlServerRelationalAcceptanceTests.A_known_cost_offered_again_at_another_scale_is_a_replay",
    "GoymenERP.Api.IntegrationTests.AccountingSqlServerRelationalAcceptanceTests.Queue_posting_and_replay_are_durable_audited_and_answer_from_stored_facts",
    "GoymenERP.Api.IntegrationTests.AccountingSqlServerRelationalAcceptanceTests.Each_company_sees_only_its_own_ledger_and_a_message_id_is_unique_per_company_only",
    "GoymenERP.Api.IntegrationTests.AccountingSqlServerRelationalAcceptanceTests.Reversal_and_correction_identities_hold_in_the_store_and_in_the_database",
    "GoymenERP.Api.IntegrationTests.AccountingSqlServerRelationalAcceptanceTests.Two_racing_deliveries_of_one_fact_store_one_original_and_the_loser_retries_to_already_posted",
    "GoymenERP.Api.IntegrationTests.AccountingSqlServerRelationalAcceptanceTests.A_closed_period_takes_no_posting_and_a_close_racing_a_posting_cannot_both_commit",
    "GoymenERP.Api.IntegrationTests.AccountingSqlServerRelationalAcceptanceTests.A_save_that_fails_in_the_database_leaves_nothing_behind",
    "GoymenERP.Api.IntegrationTests.AccountingSqlServerRelationalAcceptanceTests.Two_racing_overlapping_period_openings_store_one_and_the_loser_retries_to_an_overlap",
    "GoymenERP.Api.IntegrationTests.FinanceDraftSqlServerRelationalAcceptanceTests.A_draft_is_saved_reopened_and_saved_again_with_a_fresh_version_and_moves_no_money",
    "GoymenERP.Api.IntegrationTests.FinanceDraftSqlServerRelationalAcceptanceTests.A_stale_version_changes_nothing_and_the_same_key_replays_or_conflicts",
    "GoymenERP.Api.IntegrationTests.FinanceDraftSqlServerRelationalAcceptanceTests.Company_B_can_neither_see_nor_overwrite_company_A_draft",
    "GoymenERP.Api.IntegrationTests.FinanceDraftSqlServerRelationalAcceptanceTests.The_real_unavailable_owners_refuse_every_save_and_store_nothing",
    "GoymenERP.Api.IntegrationTests.FinanceDraftSqlServerRelationalAcceptanceTests.A_save_that_fails_in_the_database_leaves_nothing_behind",
    "GoymenERP.Api.IntegrationTests.ManufacturingSqlServerRelationalAcceptanceTests.Plan_order_and_facts_are_durable_replayable_audited_and_close_from_stored_facts",
    "GoymenERP.Api.IntegrationTests.ManufacturingSqlServerRelationalAcceptanceTests.A_close_from_stored_facts_reconciles_each_roll_on_its_own",
    "GoymenERP.Api.IntegrationTests.ManufacturingSqlServerRelationalAcceptanceTests.Each_company_sees_only_its_own_orders_and_a_fact_id_is_unique_per_company_only",
    "GoymenERP.Api.IntegrationTests.ManufacturingSqlServerRelationalAcceptanceTests.A_fact_id_is_one_fact_in_the_store_and_in_the_database",
    "GoymenERP.Api.IntegrationTests.ManufacturingSqlServerRelationalAcceptanceTests.Two_writers_that_read_the_same_order_cannot_both_commit",
    "GoymenERP.Api.IntegrationTests.ManufacturingSqlServerRelationalAcceptanceTests.A_save_that_fails_in_the_database_leaves_nothing_behind",
    "GoymenERP.Api.IntegrationTests.FinanceBankAccountSqlServerRelationalAcceptanceTests.A_proposal_is_not_usable_until_another_user_approves_it_once_and_moves_no_money",
    "GoymenERP.Api.IntegrationTests.FinanceBankAccountSqlServerRelationalAcceptanceTests.Company_B_can_neither_see_nor_approve_company_A_account",
    "GoymenERP.Api.IntegrationTests.FinanceBankAccountSqlServerRelationalAcceptanceTests.The_same_key_replays_and_another_payload_or_command_conflicts",
    "GoymenERP.Api.IntegrationTests.FinanceBankAccountSqlServerRelationalAcceptanceTests.A_currency_outside_the_settings_candidates_or_an_unavailable_source_stores_nothing",
    "GoymenERP.Api.IntegrationTests.FinanceBankAccountSqlServerRelationalAcceptanceTests.Two_racing_approvals_store_one_and_the_database_refuses_a_self_approval",
    "GoymenERP.Api.IntegrationTests.FinanceBankAccountSqlServerRelationalAcceptanceTests.A_save_that_fails_in_the_database_leaves_nothing_behind",
    "GoymenERP.Api.IntegrationTests.SalesDraftSqlServerRelationalAcceptanceTests.A_draft_with_lines_and_slices_is_stored_replayed_and_audited_in_one_save",
    "GoymenERP.Api.IntegrationTests.SalesDraftSqlServerRelationalAcceptanceTests.The_same_key_with_other_content_is_refused_and_a_racing_same_key_loses_cleanly",
    "GoymenERP.Api.IntegrationTests.SalesDraftSqlServerRelationalAcceptanceTests.Company_B_can_neither_see_nor_replay_company_A_draft_and_the_same_key_is_its_own_attempt",
    "GoymenERP.Api.IntegrationTests.SalesDraftSqlServerRelationalAcceptanceTests.A_stored_draft_is_rebuilt_through_the_domain_and_listed_newest_first_with_a_company_bound_cursor",
    "GoymenERP.Api.IntegrationTests.SalesDraftSqlServerRelationalAcceptanceTests.A_save_that_fails_in_the_database_leaves_nothing_behind",
    "GoymenERP.Api.IntegrationTests.SalesDraftSqlServerRelationalAcceptanceTests.A_key_that_differs_only_by_surrounding_whitespace_is_refused_and_never_replays_the_stored_attempt",
)
BROAD_DISCOVERY_COUNT = 128
BROAD_EXECUTION_COUNT = 135


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
