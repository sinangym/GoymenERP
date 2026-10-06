from __future__ import annotations

import contextlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = REPOSITORY_ROOT / "tools" / "verify_rcsi_relational_acceptance.py"
SPEC = importlib.util.spec_from_file_location("verify_rcsi_relational_acceptance", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
VERIFIER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFIER)


def broad_trx(executions: dict[str, int], outcome: str = "Passed") -> str:
    """A TRX with one result per execution (theory rows carry their arguments) and exact counters."""
    results = []
    for name, count in executions.items():
        for row in range(count):
            display = name if count == 1 else f'{name}(row: {row})'
            results.append(f'<UnitTestResult testName="{display}" outcome="{outcome}" />')
    total = sum(executions.values())
    return (
        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
        + "".join(results)
        + f'</Results><ResultSummary><Counters total="{total}" executed="{total}" '
        f'passed="{total}" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" '
        'notExecuted="0" /></ResultSummary></TestRun>'
    )


class VerifyRcsiRelationalAcceptanceTests(unittest.TestCase):
    def test_exact_discovery_is_accepted(self) -> None:
        output = "\n".join(f"    {name}" for name in VERIFIER.EXPECTED_TESTS)
        VERIFIER.verify_discovery(output)

    def test_missing_discovery_is_rejected(self) -> None:
        output = "\n".join(VERIFIER.EXPECTED_TESTS[:-1])
        with self.assertRaisesRegex(RuntimeError, "exactly the fourteen"):
            VERIFIER.verify_discovery(output)

    def test_exact_trx_counters_are_accepted(self) -> None:
        self._verify_trx(
            '<TestRun xmlns="urn:test"><ResultSummary><Counters total="14" '
            'executed="14" passed="14" failed="0" error="0" timeout="0" '
            'aborted="0" inconclusive="0" notExecuted="0" />'
            "</ResultSummary></TestRun>",
            14,
        )

    def test_skipped_or_missing_counter_is_rejected(self) -> None:
        with self.assertRaisesRegex(RuntimeError, "skipped=0"):
            self._verify_trx(
                '<TestRun><Counters total="9" executed="8" passed="8" failed="0" '
                'error="0" timeout="0" aborted="0" inconclusive="0" '
                'notExecuted="1" /></TestRun>',
                9,
            )

    def test_broad_discovery_requires_exact_count_and_all_rcsi_tests(self) -> None:
        broad = list(VERIFIER.BROAD_EXPECTED_TESTS)
        VERIFIER.verify_broad_discovery("\n".join(broad))
        with self.assertRaisesRegex(RuntimeError, "Broad SQL Server"):
            VERIFIER.verify_broad_discovery("\n".join(broad[:-1]))

        substituted = broad[1:] + [
            "GoymenERP.Api.IntegrationTests."
            "ReplacementSqlServerRelationalAcceptanceTests.TrivialPass"
        ]
        with self.assertRaisesRegex(RuntimeError, "exact"):
            VERIFIER.verify_broad_discovery("\n".join(substituted))

    def test_optional_roles_regressions_are_mandatory_in_broad_inventory(self) -> None:
        prefix = "GoymenERP.Api.IntegrationTests."
        required = {
            prefix + "CounterpartyCompanyLinkSqlServerRelationalAcceptanceTests."
            "ExistingParty_CanBeLinkedWithNoRole",
            prefix + "CounterpartyWriteSqlServerRelationalAcceptanceTests."
            "CanCreateCounterpartyWithoutRole",
        }
        optional_prefix = prefix + "CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests."
        required.update(optional_prefix + name for name in (
            "ClientContractGate_BlocksUnnegotiatedWritesAndPreservesSameKeyRetry",
            "ClientContractGate_WithholdsRolelessReadsWithoutLeakingForeignCompanyState",
            "MissingAndZeroRoles_CreateAndLinkShareReplayAndRejectInvalidJson",
            "PreviousSchemaUpgradeAndSafeDown_PreserveLegacyRolesAndDurableReplays",
            "RolelessInactiveAccount_StaleVersionAndPermissionGatesRemainEnforced",
            "RolelessRows_BlockDownBeforeSchemaChanges_AndAllowForwardReapply",
            "RolelessReads_RespectReadOnlyAuthorizationAndCompanyIsolationWithoutCreatingAccounts",
            "RolelessDifferentKeyLinkRace_CreatesOneAccountAndOneAudit",
            "RolelessAuditAndIdempotencyFailures_RollBackAndPermitSameKeyRetry",
        ))
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                discovery = "\n".join(
                    name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                )
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery(discovery)

    def test_broad_inventory_requires_cari_journey_and_rejects_its_omission(self) -> None:
        journey = (
            "GoymenERP.Api.IntegrationTests."
            "CounterpartyJourneySqlServerRelationalAcceptanceTests."
            "CreateFindDetailAndLinkSecondCompany_PreserveIdentityVisibilityAndExactlyOnceEffects"
        )
        self.assertIn(journey, VERIFIER.BROAD_EXPECTED_TESTS)
        without_journey = [name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != journey]
        with self.assertRaisesRegex(RuntimeError, "exact"):
            VERIFIER.verify_broad_discovery("\n".join(without_journey))

    def test_organization_lifecycle_is_mandatory_and_rejects_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "OrganizationLocationLifecycleSqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + "ListDetailDeactivateReactivateAndDelayedReplay_ReturnFreshStateAndSingleAuditPerCommand",
            prefix + "ScopedPagingAndRejectedCommands_PreserveLocationAndExposeNoForeignState",
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                discovery = "\n".join(
                    name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                )
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery(discovery)

    def test_product_runtime_sql_cases_are_mandatory_and_reject_omission(self) -> None:
        prefix = "GoymenERP.Api.IntegrationTests.ProductGlobalRuntimeSqlServerRelationalAcceptanceTests."
        required = {
            prefix + name for name in (
                "ProductGlobalRuntime_ReadCreateEditReplayAndCompanyIsolation_AreDurable",
                "ProductGlobalRuntime_EachFlushFailureAndLateRevocation_RollBackAllOwners",
                "ProductGlobalRuntime_ConcurrentDuplicateAndSharedPrefixCommands_HaveSingleEffects",
                "ProductGlobalRuntime_TwoEditsStagedAtVersionOne_ProduceOneLateEfConflict",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaises(RuntimeError):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_central_authority_revalidation_cases_are_mandatory_and_reject_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "CentralAuthorityRevalidationSqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + name for name in (
                "A_revocation_is_measured_inside_the_request_and_refuses_the_next_one",
                "A_standing_grant_is_answered_the_same_by_both_ports_and_an_unknown_name_reads_nothing",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_product_sales_eligibility_authority_cases_are_mandatory_and_reject_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "ProductSalesEligibilityAuthoritySqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + name for name in (
                "A_standing_grant_passes_every_fence_with_one_authority_read_per_fence",
                "A_revocation_by_another_request_during_the_read_is_seen_at_the_next_fence",
                "Without_a_grant_the_registered_name_denies_before_any_read",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_inventory_cases_are_mandatory_and_reject_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "InventorySqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + name for name in (
                "Receipt_issue_block_and_release_are_durable_replayable_audited_and_reconcile",
                "Each_company_sees_only_its_own_labels_and_a_code_is_unique_per_company_only",
                "A_source_causes_one_movement_per_kind_and_a_stock_bearing_label_is_not_received_again",
                "Two_writers_that_read_the_same_piece_cannot_both_commit",
                "A_save_that_fails_in_the_database_leaves_nothing_behind",
                "A_source_is_answered_whole_or_line_by_line_never_both",
                "A_whole_and_a_line_movement_racing_for_one_source_cannot_both_commit",
                "A_label_code_has_one_answer_under_the_code_column_collation",
                "A_known_cost_offered_again_at_another_scale_is_a_replay",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_accounting_cases_are_mandatory_and_reject_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "AccountingSqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + name for name in (
                "Queue_posting_and_replay_are_durable_audited_and_answer_from_stored_facts",
                "Each_company_sees_only_its_own_ledger_and_a_message_id_is_unique_per_company_only",
                "Reversal_and_correction_identities_hold_in_the_store_and_in_the_database",
                "Two_racing_deliveries_of_one_fact_store_one_original_and_the_loser_retries_to_already_posted",
                "A_closed_period_takes_no_posting_and_a_close_racing_a_posting_cannot_both_commit",
                "A_save_that_fails_in_the_database_leaves_nothing_behind",
                "Two_racing_overlapping_period_openings_store_one_and_the_loser_retries_to_an_overlap",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_finance_draft_cases_are_mandatory_and_reject_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "FinanceDraftSqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + name for name in (
                "A_draft_is_saved_reopened_and_saved_again_with_a_fresh_version_and_moves_no_money",
                "A_stale_version_changes_nothing_and_the_same_key_replays_or_conflicts",
                "Company_B_can_neither_see_nor_overwrite_company_A_draft",
                "The_real_unavailable_owners_refuse_every_save_and_store_nothing",
                "A_save_that_fails_in_the_database_leaves_nothing_behind",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_manufacturing_cases_are_mandatory_and_reject_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "ManufacturingSqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + name for name in (
                "Plan_order_and_facts_are_durable_replayable_audited_and_close_from_stored_facts",
                "A_close_from_stored_facts_reconciles_each_roll_on_its_own",
                "Each_company_sees_only_its_own_orders_and_a_fact_id_is_unique_per_company_only",
                "A_fact_id_is_one_fact_in_the_store_and_in_the_database",
                "Two_writers_that_read_the_same_order_cannot_both_commit",
                "A_save_that_fails_in_the_database_leaves_nothing_behind",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_finance_bank_account_cases_are_mandatory_and_reject_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "FinanceBankAccountSqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + name for name in (
                "A_proposal_is_not_usable_until_another_user_approves_it_once_and_moves_no_money",
                "A_legacy_NULL_money_declaration_is_visible_as_undeclared_and_is_refused_on_the_money_path",
                "A_pending_IBAN_never_replaces_the_approved_value_and_only_another_actor_can_approve_it",
                "Company_B_can_neither_see_nor_approve_company_A_account",
                "The_same_key_replays_and_another_payload_or_actor_conflicts",
                "A_currency_outside_the_settings_candidates_or_an_unavailable_source_stores_nothing",
                "Two_racing_approvals_store_one_and_the_database_refuses_a_self_approval",
                "A_save_that_fails_in_the_database_leaves_nothing_behind",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_finance_reversal_cases_are_mandatory_and_reject_omission(self) -> None:
        prefix = "GoymenERP.Api.IntegrationTests."
        required = {
            prefix + "FinanceCommandApiSqlServerRelationalAcceptanceTests."
            "A_reversal_proposal_requires_another_HTTP_user_and_preserves_both_history_links",
            prefix + "FinanceRecordSqlServerRelationalAcceptanceTests."
            "Reversal_requires_another_checker_and_keeps_the_original_with_two_way_history",
            prefix + "FinanceRecordSqlServerRelationalAcceptanceTests."
            "A_closed_reversal_period_refuses_approval_and_writes_no_reversal",
            prefix + "FinanceRecordSqlServerRelationalAcceptanceTests."
            "An_allocated_source_refuses_reversal_approval_and_writes_nothing",
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_inventory_read_api_cases_are_mandatory_and_reject_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "InventoryReadApiSqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + name for name in (
                "A_reader_with_both_grants_sees_the_label_and_its_stock_position_by_position",
                "Each_read_needs_its_own_grant",
                "Another_companys_label_is_not_found_and_the_company_context_is_required",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_sales_draft_cases_are_mandatory_and_reject_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "SalesDraftSqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + name for name in (
                "A_draft_with_lines_and_slices_is_stored_replayed_and_audited_in_one_save",
                "The_same_key_with_other_content_is_refused_and_a_racing_same_key_loses_cleanly",
                "Company_B_can_neither_see_nor_replay_company_A_draft_and_the_same_key_is_its_own_attempt",
                "A_stored_draft_is_rebuilt_through_the_domain_and_listed_newest_first_with_a_company_bound_cursor",
                "A_save_that_fails_in_the_database_leaves_nothing_behind",
                "A_key_that_differs_only_by_surrounding_whitespace_is_refused_and_never_replays_the_stored_attempt",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_product_shell_capability_case_is_mandatory_and_rejects_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "ProductShellCapabilitySqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + name for name in (
                "Product_screens_follow_the_global_Product_authority_through_the_real_provider",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_manufacturing_api_cases_are_mandatory_and_reject_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "ManufacturingApiSqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + name for name in (
                "An_authorized_user_plans_approves_orders_records_facts_and_reads_the_roll_balance",
                "Each_endpoint_needs_its_own_grant_and_a_refused_write_stores_nothing",
                "Another_companys_order_is_not_found_and_the_company_comes_only_from_the_context",
                "A_plan_is_read_back_from_storage_under_its_own_grant_and_only_for_the_context_company",
                "Stale_versions_reused_ids_masses_outside_the_profile_and_foreign_work_centers_are_refused",
                "The_idempotency_key_replays_one_attempt_and_refuses_another_request_or_actor",
                "Two_attempts_racing_on_one_key_store_one_command_and_both_answer_it",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_sales_draft_api_cases_are_mandatory_and_reject_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "SalesDraftApiSqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + name for name in (
                "The_draft_routes_answer_only_with_fresh_sales_grants_and_only_for_the_context_company",
                "A_create_reaches_its_real_owners_in_order_and_answers_the_first_refusal_without_storing_anything",
                "A_stored_draft_is_read_by_its_company_only_and_its_cursor_binds_the_company",
                "A_same_key_race_stores_one_draft_and_every_replay_holds_in_the_real_host",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_finance_draft_list_case_is_mandatory_and_rejects_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "FinanceDraftSqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + name for name in (
                "Drafts_are_listed_newest_first_across_pages_with_a_company_bound_cursor",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_finance_draft_api_cases_are_mandatory_and_reject_omission(self) -> None:
        prefix = (
            "GoymenERP.Api.IntegrationTests."
            "FinanceDraftApiSqlServerRelationalAcceptanceTests."
        )
        required = {
            prefix + name for name in (
                "The_draft_routes_answer_only_with_fresh_finance_grants_and_only_for_the_context_company",
                "A_save_reaches_its_real_owners_and_answers_their_refusal_without_storing_anything",
                "Another_companys_draft_is_not_found_and_its_cursor_reads_nothing",
            )
        }
        self.assertTrue(required.issubset(VERIFIER.BROAD_EXPECTED_TESTS))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_global_product_shell_authority_is_mandatory(self) -> None:
        name = (
            "GoymenERP.Api.IntegrationTests."
            "ProductGlobalRuntimeSqlServerRelationalAcceptanceTests."
            "ProductGlobalRuntime_ShellCapabilitiesFollowExplicitGlobalAssignments"
        )
        self.assertIn(name, VERIFIER.BROAD_EXPECTED_TESTS)
        without_shell = [item for item in VERIFIER.BROAD_EXPECTED_TESTS if item != name]
        with self.assertRaisesRegex(RuntimeError, "exact"):
            VERIFIER.verify_broad_discovery("\n".join(without_shell))

    def test_actual_product_shared_snapshot_is_mandatory(self) -> None:
        name = (
            "GoymenERP.Api.IntegrationTests."
            "ProductGlobalRuntimeSqlServerRelationalAcceptanceTests."
            "ProductGlobalRuntime_SharedSnapshotEmitsEmptyAndPopulatedJsonArrays"
        )
        self.assertIn(name, VERIFIER.BROAD_EXPECTED_TESTS)
        without_snapshot = [item for item in VERIFIER.BROAD_EXPECTED_TESTS if item != name]
        with self.assertRaisesRegex(RuntimeError, "exact"):
            VERIFIER.verify_broad_discovery("\n".join(without_snapshot))

    def test_main_repeats_bootstrap_preflight_before_each_dotnet_boundary(self) -> None:
        calls: list[tuple[str, ...]] = []

        def fake_run_checked(
            arguments: list[str], repository_root: Path
        ) -> subprocess.CompletedProcess[str]:
            del repository_root
            calls.append(tuple(arguments))
            if "--list-tests" in arguments:
                return subprocess.CompletedProcess(
                    arguments,
                    0,
                    stdout="\n".join(VERIFIER.BROAD_EXPECTED_TESTS),
                    stderr="",
                )
            if "--logger" in arguments and any(
                argument.startswith("trx;") for argument in arguments
            ):
                results_directory = Path(arguments[arguments.index("--results-directory") + 1])
                (results_directory / "rcsi.trx").write_text(
                    broad_trx(VERIFIER.BROAD_EXECUTIONS), encoding="utf-8"
                )
            return subprocess.CompletedProcess(arguments, 0, stdout="", stderr="")

        with (
            mock.patch.object(VERIFIER, "run_checked", side_effect=fake_run_checked),
            mock.patch.object(sys, "argv", [str(MODULE_PATH), "--broad"]),
            mock.patch.dict(
                "os.environ",
                {"GOYMEN_ERP_TEST_SQLSERVER_CONNECTION": "test-only"},
                clear=False,
            ),
        ):
            # SELFTEST: the synthetic run's verdict is captured, never written to the CI log, so the
            # log's only SQLSERVER_RELATIONAL_ACCEPTANCE lines are the real run's.
            with contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(0, VERIFIER.main())

        lines = output.getvalue().splitlines()
        self.assertEqual(len(VERIFIER.INVENTORY_MODULES) + 1, len(lines))
        for module, line in zip(VERIFIER.INVENTORY_MODULES, lines):
            tests = VERIFIER.BROAD_INVENTORY[module]
            self.assertEqual(
                f"SQLSERVER_RELATIONAL_ACCEPTANCE module={module} tests={len(tests)} "
                f"discovered={len(tests)} executions={sum(tests.values())} "
                f"executed={sum(tests.values())}",
                line,
            )
        self.assertEqual(
            f"SQLSERVER_RELATIONAL_ACCEPTANCE: PASS total={VERIFIER.BROAD_EXECUTION_COUNT} "
            f"passed={VERIFIER.BROAD_EXECUTION_COUNT} failed=0 skipped=0",
            lines[-1],
        )
        self.assertEqual(4, len(calls))
        self.assertIn("tools/validate_dotnet_bootstrap_integrity.py", calls[0])
        self.assertIn("--list-tests", calls[1])
        self.assertIn("tools/validate_dotnet_bootstrap_integrity.py", calls[2])
        self.assertIn("--results-directory", calls[3])

    @staticmethod
    def _verify_trx(contents: str, expected_total: int) -> None:
        with tempfile.TemporaryDirectory(prefix="goymen-rcsi-verifier-unit-") as directory:
            path = Path(directory) / "results.trx"
            path.write_text(contents, encoding="utf-8")
            VERIFIER.verify_trx(path, expected_total)



class RelationalInventoryLoaderTests(unittest.TestCase):
    """The per-module inventory files: grammar, ownership by class prefix, and fail-closed loading."""

    def setUp(self) -> None:
        self._directory = tempfile.TemporaryDirectory(prefix="goymen-relational-inventory-")
        self.root = Path(self._directory.name)
        for module in VERIFIER.INVENTORY_MODULES:
            name = f"{module}.txt"
            (self.root / name).write_text(
                (VERIFIER.INVENTORY_DIRECTORY / name).read_text(encoding="utf-8"),
                encoding="utf-8",
            )

    def tearDown(self) -> None:
        self._directory.cleanup()

    def _append(self, module: str, line: str) -> None:
        path = self.root / f"{module}.txt"
        path.write_text(path.read_text(encoding="utf-8") + line + "\n", encoding="utf-8")

    def _rejects(self, pattern: str) -> None:
        with self.assertRaisesRegex(RuntimeError, pattern):
            VERIFIER.load_broad_inventory(self.root)

    def test_checked_in_files_load_and_the_totals_are_computed_from_them(self) -> None:
        self.assertIsNone(VERIFIER.BROAD_INVENTORY_ERROR)
        self.assertEqual(VERIFIER.BROAD_INVENTORY, VERIFIER.load_broad_inventory(self.root))
        self.assertEqual(tuple(VERIFIER.INVENTORY_MODULES), tuple(VERIFIER.BROAD_INVENTORY))
        self.assertEqual(len(VERIFIER.BROAD_EXPECTED_TESTS), VERIFIER.BROAD_DISCOVERY_COUNT)
        self.assertEqual(
            sum(sum(tests.values()) for tests in VERIFIER.BROAD_INVENTORY.values()),
            VERIFIER.BROAD_EXECUTION_COUNT,
        )

    def test_every_inventory_test_is_mandatory(self) -> None:
        for omitted in VERIFIER.BROAD_EXPECTED_TESTS:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

    def test_every_multi_execution_line_states_its_reason(self) -> None:
        for module in VERIFIER.INVENTORY_MODULES:
            for line in (VERIFIER.INVENTORY_DIRECTORY / f"{module}.txt").read_text(
                encoding="utf-8"
            ).splitlines():
                if line and not line.startswith("#") and " " in line:
                    with self.subTest(line=line):
                        self.assertRegex(line, r"^\S+  x[2-9][0-9]*  # \S")

    def test_classes_map_to_their_module_and_the_cari_journey_is_counterparty(self) -> None:
        prefix = VERIFIER.TEST_NAMESPACE
        self.assertEqual(
            "counterparty",
            VERIFIER.module_of(prefix + "SqlServerRelationalAcceptanceTests.Journey"),
        )
        self.assertEqual(
            "sales",
            VERIFIER.module_of(prefix + "SalesDraftApiSqlServerRelationalAcceptanceTests.Case"),
        )
        self.assertEqual(
            "logistics",
            VERIFIER.module_of(prefix + "LogisticsInstructionSqlServerRelationalAcceptanceTests.Case"),
        )
        self.assertEqual(
            "purchasing",
            VERIFIER.module_of(prefix + "PurchasingOrderSqlServerRelationalAcceptanceTests.Case"),
        )
        self.assertEqual(
            "work",
            VERIFIER.module_of(prefix + "WorkDesiredStateSqlServerRelationalAcceptanceTests.Case"),
        )
        self.assertEqual(
            "platform",
            VERIFIER.module_of(prefix + "ShellCapabilitySqlServerRelationalAcceptanceTests.Case"),
        )
        for module, tests in VERIFIER.BROAD_INVENTORY.items():
            for name in tests:
                with self.subTest(name=name):
                    self.assertEqual(module, VERIFIER.module_of(name))

    def test_a_test_in_another_modules_file_is_rejected(self) -> None:
        self._append(
            "finance",
            VERIFIER.TEST_NAMESPACE + "SalesZzzSqlServerRelationalAcceptanceTests.Misplaced",
        )
        self._rejects("belongs in sales.txt")

    def test_work_inventory_is_exact_mandatory_and_owned(self) -> None:
        prefix = VERIFIER.TEST_NAMESPACE + "WorkDesiredStateSqlServerRelationalAcceptanceTests."
        required = {
            prefix + "Apply_replay_conflict_older_and_unavailable_watermark_are_atomic",
            prefix + "Audit_failure_rolls_back_snapshot_inbox_outbox_and_write_guard_refuses_foreign_rows",
            prefix + "Digest_collision_is_refused_before_SQL_and_preserves_the_first_identity",
            prefix + "Internal_admission_projects_the_single_store_write_and_snapshot_read_has_no_effect",
            prefix + "Same_and_newer_ordinal_races_have_one_CAS_winner_without_blind_overwrite",
            prefix + "Snapshot_read_refuses_company_drift_and_foreign_scope_without_writing",
            prefix + "Snapshot_read_rejects_row_outbox_disagreement_without_recording_an_effect",
            prefix + "Versionless_and_foreign_observations_write_nothing_and_tenant_filter_hides_other_company",
        }
        self.assertEqual(required, set(VERIFIER.BROAD_INVENTORY["work"]))
        for omitted in required:
            with self.subTest(omitted=omitted):
                with self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_broad_discovery("\n".join(
                        name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != omitted
                    ))

        work_path = self.root / "work.txt"
        work_source = work_path.read_text(encoding="utf-8")
        work_path.unlink()
        self._rejects("missing \\['work.txt'\\]")
        work_path.write_text(work_source, encoding="utf-8")
        self._append(
            "platform",
            VERIFIER.TEST_NAMESPACE + "WorkZzzSqlServerRelationalAcceptanceTests.Misplaced",
        )
        self._rejects("belongs in work.txt")

    def test_a_name_repeated_in_one_file_is_rejected(self) -> None:
        last = sorted(VERIFIER.BROAD_INVENTORY["sales"])[-1]
        self._append("sales", last)
        self._rejects("out of order|repeats")

    def test_an_unsorted_file_is_rejected(self) -> None:
        self._append(
            "sales",
            VERIFIER.TEST_NAMESPACE + "SalesAaaSqlServerRelationalAcceptanceTests.Early",
        )
        self._rejects("out of order")

    def test_malformed_lines_are_rejected(self) -> None:
        name = VERIFIER.TEST_NAMESPACE + "SalesZzzSqlServerRelationalAcceptanceTests.Case"
        for line, pattern in (
            ("not a test name", "malformed"),
            ("GoymenERP.Api.IntegrationTests.SalesZzzOtherTests.Case", "malformed"),
            (name + " x2  # single spaces", "malformed"),
            (name + "  x2", "malformed"),
            (name + "  x2  #", "malformed"),
            (name + "  # a comment without a count", "malformed"),
            (name + "  x1  # one run needs no count", "x1"),
            (name + "  x0  # never runs", "x0"),
            (name + " ", "trailing"),
            ("\t" + name, "tab"),
        ):
            with self.subTest(line=line):
                self.tearDown()
                self.setUp()
                self._append("sales", line)
                self._rejects(pattern)

    def test_a_multi_execution_line_with_its_reason_is_accepted(self) -> None:
        name = VERIFIER.TEST_NAMESPACE + "SalesZzzSqlServerRelationalAcceptanceTests.Case"
        self._append("sales", name + "  x3  # MemberData Rows: 3 rows")
        self.assertEqual(3, VERIFIER.load_broad_inventory(self.root)["sales"][name])

    def test_unknown_missing_and_empty_files_are_rejected(self) -> None:
        (self.root / "shipping.txt").write_text("", encoding="utf-8")
        self._rejects("Unknown \\['shipping.txt'\\]")
        (self.root / "shipping.txt").unlink()
        (self.root / "identity.txt").unlink()
        self._rejects("missing \\['identity.txt'\\]")
        (self.root / "identity.txt").write_text("# comments only\n\n", encoding="utf-8")
        self._rejects("names no test")

    def test_a_broken_checked_in_inventory_fails_the_broad_run_before_any_dotnet_call(self) -> None:
        with (
            mock.patch.object(VERIFIER, "BROAD_INVENTORY_ERROR", RuntimeError("inventory broken")),
            mock.patch.object(VERIFIER, "run_checked") as run_checked,
            mock.patch.object(sys, "argv", [str(MODULE_PATH), "--broad"]),
            mock.patch.dict(
                "os.environ",
                {"GOYMEN_ERP_TEST_SQLSERVER_CONNECTION": "test-only"},
                clear=False,
            ),
        ):
            with self.assertRaisesRegex(RuntimeError, "inventory broken"):
                VERIFIER.main()
        run_checked.assert_not_called()


class BroadTrxResultTests(unittest.TestCase):
    """The TRX must hold exactly the declared executions per test, all passed."""

    def _report(self, contents: str) -> list[str]:
        with tempfile.TemporaryDirectory(prefix="goymen-rcsi-verifier-unit-") as directory:
            path = Path(directory) / "results.trx"
            path.write_text(contents, encoding="utf-8")
            return VERIFIER.verify_broad_trx_results(path, VERIFIER.BROAD_EXPECTED_TESTS)

    def test_exact_executions_report_one_line_per_module(self) -> None:
        report = self._report(broad_trx(VERIFIER.BROAD_EXECUTIONS))
        self.assertEqual(
            [f"SQLSERVER_RELATIONAL_ACCEPTANCE module={module} " for module in VERIFIER.INVENTORY_MODULES],
            [line.split("tests=", 1)[0] for line in report],
        )

    def test_a_missing_or_extra_theory_row_is_rejected(self) -> None:
        multi = next(name for name, count in VERIFIER.BROAD_EXECUTIONS.items() if count > 1)
        for delta in (-1, 1):
            with self.subTest(delta=delta):
                executions = dict(VERIFIER.BROAD_EXECUTIONS)
                executions[multi] += delta
                with self.assertRaisesRegex(RuntimeError, "per test"):
                    self._report(broad_trx(executions))

    def test_a_test_outside_the_inventory_is_rejected(self) -> None:
        executions = dict(VERIFIER.BROAD_EXECUTIONS)
        executions[VERIFIER.TEST_NAMESPACE + "ExtraSqlServerRelationalAcceptanceTests.Case"] = 1
        with self.assertRaisesRegex(RuntimeError, "per test"):
            self._report(broad_trx(executions))

    def test_a_result_that_did_not_pass_is_rejected(self) -> None:
        with self.assertRaisesRegex(RuntimeError, "not Passed"):
            self._report(broad_trx(VERIFIER.BROAD_EXECUTIONS, outcome="NotExecuted"))


class ShardedRelationalProofTests(unittest.TestCase):
    SOURCE = "a" * 40
    TESTED = "b" * 40

    def test_rcsi_trx_requires_the_exact_fourteen_names(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "rcsi.trx"
            expected = {name: 1 for name in VERIFIER.EXPECTED_TESTS}
            path.write_text(broad_trx(expected), encoding="utf-8")
            VERIFIER.verify_rcsi_trx_results(path)
            wrong = dict(expected)
            wrong.pop(next(iter(wrong)))
            wrong[VERIFIER.TEST_NAMESPACE + "ExtraSqlServerRelationalAcceptanceTests.Case"] = 1
            path.write_text(broad_trx(wrong), encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "fourteen reviewed"):
                VERIFIER.verify_rcsi_trx_results(path)

    def test_shard_runner_discovers_the_full_catalog_before_its_exact_subset(self) -> None:
        calls: list[tuple[str, ...]] = []
        selected = VERIFIER.shard_executions("A")
        with tempfile.TemporaryDirectory() as directory:
            proof_path = Path(directory) / "proof-A.json"

            def fake_run(arguments: list[str], repository_root: Path) -> subprocess.CompletedProcess[str]:
                del repository_root
                calls.append(tuple(arguments))
                if "--list-tests" in arguments:
                    return subprocess.CompletedProcess(
                        arguments, 0, stdout="\n".join(VERIFIER.BROAD_EXPECTED_TESTS), stderr=""
                    )
                if "--results-directory" in arguments:
                    results = Path(arguments[arguments.index("--results-directory") + 1])
                    (results / "rcsi.trx").write_text(broad_trx(selected), encoding="utf-8")
                return subprocess.CompletedProcess(arguments, 0, stdout="", stderr="")

            with (
                mock.patch.object(VERIFIER, "run_checked", side_effect=fake_run),
                mock.patch.object(VERIFIER, "resolve_proof_path", return_value=proof_path),
                mock.patch.object(sys, "argv", [
                    str(MODULE_PATH), "--shard", "A", "--proof-output", "proof-A.json",
                    "--source-head", self.SOURCE, "--tested-commit", self.TESTED,
                ]),
                mock.patch.dict("os.environ", {"GOYMEN_ERP_TEST_SQLSERVER_CONNECTION": "test-only"}),
                contextlib.redirect_stdout(io.StringIO()),
            ):
                self.assertEqual(0, VERIFIER.main())
            self.assertEqual(4, len(calls))
            self.assertEqual(VERIFIER.BROAD_FILTER, calls[1][calls[1].index("--filter") + 1])
            self.assertEqual(
                "|".join(f"FullyQualifiedName={name}" for name in sorted(selected)),
                calls[3][calls[3].index("--filter") + 1],
            )
            self.assertEqual(
                VERIFIER.expected_shard_proof("A", self.SOURCE, self.TESTED),
                json.loads(proof_path.read_text(encoding="utf-8")),
            )

    def test_partition_is_disjoint_complete_and_preserves_every_execution(self) -> None:
        covered: dict[str, int] = {}
        for shard in VERIFIER.SHARD_MODULES:
            selected = VERIFIER.shard_executions(shard)
            self.assertTrue(selected)
            self.assertFalse(set(covered) & set(selected))
            covered.update(selected)
        self.assertEqual(VERIFIER.BROAD_EXECUTIONS, covered)
        self.assertEqual(VERIFIER.BROAD_DISCOVERY_COUNT, len(covered))
        self.assertEqual(VERIFIER.BROAD_EXECUTION_COUNT, sum(covered.values()))
        invalid = dict(VERIFIER.SHARD_MODULES)
        invalid["C"] = invalid["C"] + ("inventory",)
        with mock.patch.object(VERIFIER, "SHARD_MODULES", invalid):
            with self.assertRaisesRegex(RuntimeError, "partition"):
                VERIFIER.shard_executions("A")

    def test_shard_trx_refuses_missing_extra_theory_and_nonpassed_results(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "shard.trx"
            expected = VERIFIER.shard_executions("C")
            path.write_text(broad_trx(expected), encoding="utf-8")
            VERIFIER.verify_shard_trx_results(path, "C")
            multi = next(name for name, count in expected.items() if count > 1)
            for delta in (-1, 1):
                broken = dict(expected)
                broken[multi] += delta
                path.write_text(broad_trx(broken), encoding="utf-8")
                with self.subTest(delta=delta), self.assertRaisesRegex(RuntimeError, "exact"):
                    VERIFIER.verify_shard_trx_results(path, "C")
            missing = dict(expected)
            missing.pop(next(iter(missing)))
            path.write_text(broad_trx(missing), encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "exact"):
                VERIFIER.verify_shard_trx_results(path, "C")
            path.write_text(broad_trx(expected, outcome="NotExecuted"), encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "not Passed"):
                VERIFIER.verify_shard_trx_results(path, "C")

    def test_aggregate_requires_exact_proofs_and_successful_workers(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for shard in VERIFIER.SHARD_MODULES:
                artifact = root / f"ci-relational-shard-proof-{shard}"
                artifact.mkdir()
                (artifact / f"proof-{shard}.json").write_text(
                    json.dumps(VERIFIER.expected_shard_proof(shard, self.SOURCE, self.TESTED)),
                    encoding="utf-8",
                )
            def verify() -> None:
                VERIFIER.verify_aggregate_proofs(root, self.SOURCE, self.TESTED, "success", "success")
            verify()
            for result in ("failure", "cancelled", "skipped", ""):
                with self.subTest(result=result), self.assertRaisesRegex(RuntimeError, "succeed"):
                    VERIFIER.verify_aggregate_proofs(root, self.SOURCE, self.TESTED, result, "success")
            with self.assertRaisesRegex(RuntimeError, "succeed"):
                VERIFIER.verify_aggregate_proofs(root, self.SOURCE, self.TESTED, "success", "failure")
            with self.assertRaisesRegex(RuntimeError, "source"):
                VERIFIER.verify_aggregate_proofs(root, "c" * 40, self.TESTED, "success", "success")
            with self.assertRaisesRegex(RuntimeError, "source"):
                VERIFIER.verify_aggregate_proofs(root, self.SOURCE, "c" * 40, "success", "success")
            path = root / "ci-relational-shard-proof-A" / "proof-A.json"
            valid = path.read_text(encoding="utf-8")
            proof = json.loads(valid)
            for field in ("catalogHash", "executionMapHash", "executionCount", "testCount"):
                broken = dict(proof)
                broken[field] = "0" * 64 if field.endswith("Hash") else 0
                path.write_text(json.dumps(broken), encoding="utf-8")
                with self.subTest(field=field), self.assertRaisesRegex(RuntimeError, "inventory"):
                    verify()
            path.write_text(valid, encoding="utf-8")
            (root / "ci-relational-shard-proof-B" / "proof-B.json").unlink()
            with self.assertRaisesRegex(RuntimeError, "missing"):
                verify()
            (root / "ci-relational-shard-proof-B" / "proof-B.json").write_text(
                json.dumps(VERIFIER.expected_shard_proof("B", self.SOURCE, self.TESTED)), encoding="utf-8"
            )
            (root / "ci-relational-shard-proof-extra").mkdir()
            with self.assertRaisesRegex(RuntimeError, "unknown"):
                verify()
            (root / "ci-relational-shard-proof-extra").rmdir()
            path.write_text(valid[:-1] + ',"shard":"A"}', encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "repeats"):
                verify()

if __name__ == "__main__":
    unittest.main()
