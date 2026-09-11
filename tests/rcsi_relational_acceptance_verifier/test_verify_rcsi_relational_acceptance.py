from __future__ import annotations

import importlib.util
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


class VerifyRcsiRelationalAcceptanceTests(unittest.TestCase):
    def test_exact_discovery_is_accepted(self) -> None:
        output = "\n".join(f"    {name}" for name in VERIFIER.EXPECTED_TESTS)
        VERIFIER.verify_discovery(output)

    def test_missing_discovery_is_rejected(self) -> None:
        output = "\n".join(VERIFIER.EXPECTED_TESTS[:-1])
        with self.assertRaisesRegex(RuntimeError, "exactly the ten"):
            VERIFIER.verify_discovery(output)

    def test_exact_trx_counters_are_accepted(self) -> None:
        self._verify_trx(
            '<TestRun xmlns="urn:test"><ResultSummary><Counters total="10" '
            'executed="10" passed="10" failed="0" error="0" timeout="0" '
            'aborted="0" inconclusive="0" notExecuted="0" />'
            "</ResultSummary></TestRun>",
            10,
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
        self.assertEqual(69, VERIFIER.BROAD_DISCOVERY_COUNT)
        self.assertEqual(69, len(set(VERIFIER.BROAD_EXPECTED_TESTS)))
        self.assertEqual(76, VERIFIER.BROAD_EXECUTION_COUNT)
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
        self.assertEqual(69, VERIFIER.BROAD_DISCOVERY_COUNT)
        self.assertEqual(69, len(set(VERIFIER.BROAD_EXPECTED_TESTS)))
        self.assertEqual(76, VERIFIER.BROAD_EXECUTION_COUNT)
        without_journey = [name for name in VERIFIER.BROAD_EXPECTED_TESTS if name != journey]
        with self.assertRaisesRegex(RuntimeError, "exact"):
            VERIFIER.verify_broad_discovery("\n".join(without_journey))

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
                execution_count = VERIFIER.BROAD_EXECUTION_COUNT
                (results_directory / "rcsi.trx").write_text(
                    f'<TestRun><Counters total="{execution_count}" '
                    f'executed="{execution_count}" passed="{execution_count}" '
                    'failed="0" error="0" timeout="0" aborted="0" '
                    'inconclusive="0" notExecuted="0" /></TestRun>',
                    encoding="utf-8",
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
            self.assertEqual(0, VERIFIER.main())

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


if __name__ == "__main__":
    unittest.main()
