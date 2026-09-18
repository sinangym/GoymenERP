"""BROWSER-CI stage 2: suite capability profiles and their compose contracts.

Each profile's compose file is the canonical C-01 file plus exactly its documented difference, and its
validated model must still satisfy every canonical rule (the canonical validator runs unchanged on the
mapped-back model). Negative cases: non-disposable database, public bind, non-Testing environment,
envelope present in "normal", envelope or profile key missing in "cari-staged", extra keys, unknown
suite or profile.
"""
import copy
import importlib.util
import pathlib
import sys
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))

import validate_browser_acceptance as browser  # noqa: E402

_spec = importlib.util.spec_from_file_location(
    "c01_contract_tests", ROOT / "tests/c01_e2e_contract/test_validate_c01_e2e_contract.py")
c01_tests = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(c01_tests)

COMMIT = c01_tests.COMMIT
PROJECT = c01_tests.PROJECT
ENVELOPE = browser.ENVELOPE_KEYS


def model(profile):
    result = browser._relabel(c01_tests.valid_compose(), browser.CANONICAL_SUITE_LABEL,
                              browser.PROFILE_SUITE_LABELS[profile])
    environment = result["services"]["api"]["environment"]
    if profile in browser.STAGED_PROFILE_VALUES:
        environment[browser.CARI_STAGED_PROFILE_KEY] = browser.STAGED_PROFILE_VALUES[profile]
    elif profile == "normal":
        for key in ENVELOPE:
            del environment[key]
    return result


SUITE_FOR = {"c01-list-only": "smoke", "cari-staged": "c03", "normal": "org03", "fin-staged": "fin01"}


class ProfileRegistryTests(unittest.TestCase):
    def test_every_suite_has_exactly_one_known_profile(self):
        self.assertEqual(set(browser.SUITES), set(browser.SUITE_PROFILES))
        for suite in browser.SUITES:
            self.assertIn(browser.profile_of(suite), browser.PROFILES)
        self.assertEqual("c01-list-only", browser.profile_of("smoke"))
        self.assertEqual("cari-staged", browser.profile_of("c03"))
        self.assertEqual("normal", browser.profile_of("org03"))
        self.assertEqual("normal", browser.profile_of("u02"))

    def test_unknown_or_unsafe_suite_fails_closed(self):
        for suite in ("unknown", "C03", "c03;rm", "", "../x"):
            with self.subTest(suite=suite), self.assertRaises(browser.ContractError):
                browser.profile_of(suite)

    def test_profile_cli_prints_the_compose_file(self):
        import contextlib
        import io
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            self.assertEqual(0, browser.main(["profile", "--suite", "c03"]))
        self.assertEqual("profile=cari-staged\ncompose_file=infra/docker-compose.browser-cari-staged.yml\n"
                         "override_file=\nfixture=false\n", out.getvalue())


class ComposeFileDerivationTests(unittest.TestCase):
    """The two stage 2 files differ from the canonical file only by their documented transforms."""

    @staticmethod
    def _body(text):
        return "".join(line for line in text.splitlines(keepends=True) if not line.startswith("#"))

    def setUp(self):
        self.canonical = (ROOT / browser.PROFILE_COMPOSE_FILES["c01-list-only"]).read_text(encoding="utf-8")

    def test_normal_is_the_canonical_file_without_the_envelope(self):
        normal = self._body((ROOT / browser.PROFILE_COMPOSE_FILES["normal"]).read_text(encoding="utf-8"))
        expected = "".join(
            line for line in self.canonical.splitlines(keepends=True)
            if not any(line.strip().startswith(key + ":") for key in ENVELOPE)
        ).replace(browser.CANONICAL_SUITE_LABEL, browser.PROFILE_SUITE_LABELS["normal"])
        self.assertEqual(expected, normal)
        self.assertEqual(len(ENVELOPE), sum(
            1 for line in self.canonical.splitlines() if any(line.strip().startswith(k + ":") for k in ENVELOPE)))

    def test_cari_staged_is_the_canonical_file_plus_the_profile_line(self):
        staged = self._body((ROOT / browser.PROFILE_COMPOSE_FILES["cari-staged"]).read_text(encoding="utf-8"))
        anchor = '      C01E2e__EnableListCapability: "true"\n'
        expected = self.canonical.replace(
            anchor, anchor + f"      {browser.CARI_STAGED_PROFILE_KEY}: {browser.CARI_STAGED_PROFILE_VALUE}\n"
        ).replace(browser.CANONICAL_SUITE_LABEL, browser.PROFILE_SUITE_LABELS["cari-staged"])
        self.assertEqual(expected, staged)

    def test_fin_staged_is_the_canonical_file_plus_its_profile_line(self):
        # FIN-UI-1b (#332): the same two transforms, with the finance-bank-accounts-and-drafts profile.
        staged = self._body((ROOT / browser.PROFILE_COMPOSE_FILES["fin-staged"]).read_text(encoding="utf-8"))
        anchor = '      C01E2e__EnableListCapability: "true"\n'
        expected = self.canonical.replace(
            anchor, anchor + f"      {browser.CARI_STAGED_PROFILE_KEY}: {browser.STAGED_PROFILE_VALUES['fin-staged']}\n"
        ).replace(browser.CANONICAL_SUITE_LABEL, browser.PROFILE_SUITE_LABELS["fin-staged"])
        self.assertEqual(expected, staged)

    def test_each_staged_profile_value_is_an_activation_profile(self):
        activation = (ROOT / "src/backend/GoymenERP.Api/Shell/C01E2eShellCapabilityActivation.cs").read_text(
            encoding="utf-8")
        for profile, value in browser.STAGED_PROFILE_VALUES.items():
            with self.subTest(profile=profile):
                self.assertIn(f'Profile = "{value}";', activation)

    def test_a_staged_profile_refuses_the_other_profile_value(self):
        for profile, suite, wrong in (("fin-staged", "fin01", "cari-list-detail"),
                                      ("cari-staged", "c03", "finance-bank-accounts-and-drafts")):
            with self.subTest(profile=profile):
                wrong_model = model(profile)
                wrong_model["services"]["api"]["environment"][browser.CARI_STAGED_PROFILE_KEY] = wrong
                with self.assertRaises(browser.ContractError):
                    browser.validate_suite_compose(wrong_model, suite, COMMIT, PROJECT)


class SuiteComposeContractTests(unittest.TestCase):
    def test_each_profile_valid_model_passes(self):
        for profile, suite in SUITE_FOR.items():
            with self.subTest(profile=profile):
                browser.validate_suite_compose(model(profile), suite, COMMIT, PROJECT)

    def test_canonical_rules_apply_to_every_profile(self):
        mutations = (
            ("non-disposable database", lambda m: m["services"]["api"]["environment"].__setitem__(
                "DB_CONNECTION_STRING", "Database=GoymenErp"), "disposable DB"),
            ("public bind", lambda m: m["services"]["api"]["ports"][0].__setitem__("host_ip", "0.0.0.0"),
             "loopback"),
            ("non-Testing", lambda m: m["services"]["api"]["environment"].__setitem__(
                "ASPNETCORE_ENVIRONMENT", "Production"), "environment"),
            ("mutable SQL image", lambda m: m["services"]["sqlserver"].__setitem__(
                "image", "mcr.microsoft.com/mssql/server:2022-latest"), "SQL image digest"),
            ("identity drift", lambda m: m["services"]["web-authorized"]["build"]["args"].__setitem__(
                "VITE_DEV_USER", "company-a-user"), "identity"),
        )
        for profile, suite in SUITE_FOR.items():
            for name, mutate, message in mutations:
                with self.subTest(profile=profile, mutation=name):
                    broken = model(profile)
                    mutate(broken)
                    with self.assertRaisesRegex(browser.ContractError, message):
                        browser.validate_suite_compose(broken, suite, COMMIT, PROJECT)

    def test_normal_refuses_any_part_of_the_envelope(self):
        for key in (*ENVELOPE, "C01E2e__Profile", "C01E2e__Anything"):
            with self.subTest(key=key):
                broken = model("normal")
                broken["services"]["api"]["environment"][key] = "true"
                with self.assertRaisesRegex(browser.ContractError, "envelope"):
                    browser.validate_suite_compose(broken, "org03", COMMIT, PROJECT)

    def test_cari_staged_needs_the_envelope_and_the_exact_profile(self):
        missing = model("cari-staged")
        del missing["services"]["api"]["environment"]["GOYMEN_ERP_C01_E2E_DISPOSABLE"]
        with self.assertRaises(browser.ContractError):
            browser.validate_suite_compose(missing, "c03", COMMIT, PROJECT)
        for value in (None, "cari-list", "CARI-LIST-DETAIL"):
            with self.subTest(value=value):
                broken = model("cari-staged")
                if value is None:
                    del broken["services"]["api"]["environment"][browser.CARI_STAGED_PROFILE_KEY]
                else:
                    broken["services"]["api"]["environment"][browser.CARI_STAGED_PROFILE_KEY] = value
                with self.assertRaisesRegex(browser.ContractError, "cari-list-detail"):
                    browser.validate_suite_compose(broken, "c03", COMMIT, PROJECT)

    def test_extra_api_keys_are_refused_in_every_profile(self):
        for profile, suite in SUITE_FOR.items():
            with self.subTest(profile=profile):
                broken = model(profile)
                broken["services"]["api"]["environment"]["Authentication__Mode"] = "DevelopmentBootstrap"
                with self.assertRaisesRegex(browser.ContractError, "key set is not exact"):
                    browser.validate_suite_compose(broken, suite, COMMIT, PROJECT)

    def test_a_model_is_only_valid_for_its_own_profile(self):
        # The label pins the profile: the staged model is not a normal model and vice versa.
        with self.assertRaisesRegex(browser.ContractError, "label"):
            browser.validate_suite_compose(model("cari-staged"), "org03", COMMIT, PROJECT)
        with self.assertRaisesRegex(browser.ContractError, "label"):
            browser.validate_suite_compose(model("normal"), "c03", COMMIT, PROJECT)
        with self.assertRaisesRegex(browser.ContractError, "label"):
            browser.validate_suite_compose(model("c01-list-only"), "c03", COMMIT, PROJECT)

    def test_the_input_model_is_not_mutated(self):
        original = model("normal")
        snapshot = copy.deepcopy(original)
        browser.validate_suite_compose(original, "org03", COMMIT, PROJECT)
        self.assertEqual(snapshot, original)


if __name__ == "__main__":
    unittest.main()


ISSUER = {
    "ProductRuntime__IssuerLocalUserId": "5d0f7c2a-4b9e-4a53-8a2e-6c1d2f3a4b01",
    "ProductRuntime__IssuerExternalIdentityId": "5d0f7c2a-4b9e-4a53-8a2e-6c1d2f3a4b02",
}


def actors(suite, count, start=2):
    result = {}
    for offset in range(count):
        index = start + offset
        result[f"BootstrapIdentity__Users__{index}__UserId"] = f"{suite}-actor-{offset}"
        result[f"BootstrapIdentity__Users__{index}__DisplayName"] = f"SENTETIK {suite} actor {offset}"
    return result


def extended(suite, extra):
    result = model("normal")
    result["services"]["api"]["environment"].update(extra)
    return result


class SuiteExtensionTests(unittest.TestCase):
    """Stage 2 (a2): closed, per-suite API environment extensions through an override file."""

    def test_u02_may_bind_the_product_issuer_and_org03_may_add_actors(self):
        browser.validate_suite_compose(extended("u02", ISSUER), "u02", COMMIT, PROJECT)
        browser.validate_suite_compose(extended("org03", actors("org03", 6)), "org03", COMMIT, PROJECT)

    def test_an_extension_is_refused_for_a_suite_whose_list_does_not_allow_it(self):
        cases = (
            ("org03", ISSUER, "may not set"),
            ("u02", actors("u02", 1), "may not add development actors"),
        )
        for suite, extra, message in cases:
            with self.subTest(suite=suite):
                with self.assertRaisesRegex(browser.ContractError, message):
                    browser.validate_suite_compose(extended(suite, extra), suite, COMMIT, PROJECT)

    def test_unlisted_keys_stay_refused_even_for_suites_with_a_list(self):
        for suite in ("u02", "org03"):
            with self.subTest(suite=suite):
                broken = extended(suite, {"ProductRuntime__CursorSigningKeyBase64": "x" * 44})
                with self.assertRaisesRegex(browser.ContractError, "key set is not exact"):
                    browser.validate_suite_compose(broken, suite, COMMIT, PROJECT)

    def test_issuer_values_have_a_strict_shape(self):
        cases = (
            ({ISSUER_KEY: value for ISSUER_KEY, value in ISSUER.items() if ISSUER_KEY.endswith("LocalUserId")},
             "both keys or neither"),
            (dict(ISSUER, ProductRuntime__IssuerLocalUserId="00000000-0000-0000-0000-000000000000"), "GUID"),
            (dict(ISSUER, ProductRuntime__IssuerLocalUserId="5D0F7C2A-4B9E-4A53-8A2E-6C1D2F3A4B01"), "GUID"),
            (dict(ISSUER, ProductRuntime__IssuerExternalIdentityId=ISSUER["ProductRuntime__IssuerLocalUserId"]),
             "must differ"),
        )
        for extra, message in cases:
            with self.subTest(message=message):
                with self.assertRaisesRegex(browser.ContractError, message):
                    browser.validate_suite_compose(extended("u02", extra), "u02", COMMIT, PROJECT)

    def test_actor_shape_is_strict(self):
        cases = (
            (actors("org03", 2, start=3), "contiguously"),
            (actors("org03", browser.MAX_SUITE_ACTORS + 1), "at most"),
            ({"BootstrapIdentity__Users__2__UserId": "org03-a"}, "UserId and DisplayName"),
            (dict(actors("org03", 1), BootstrapIdentity__Users__2__UserId="group-bootstrap-admin"), "'org03-'"),
            (dict(actors("org03", 1), BootstrapIdentity__Users__2__DisplayName="Real Person"), "SENTETIK"),
            (dict(actors("org03", 2), BootstrapIdentity__Users__3__UserId="org03-actor-0"), "repeated"),
        )
        for extra, message in cases:
            with self.subTest(message=message):
                with self.assertRaisesRegex(browser.ContractError, message):
                    browser.validate_suite_compose(extended("org03", extra), "org03", COMMIT, PROJECT)

    def test_a_zero_padded_actor_index_is_refused(self):
        # .NET binds Users__02 and Users__2 as different list items (#322 ORG P3).
        for index in ("02", "002", "03"):
            with self.subTest(index=index):
                extra = dict(actors("org03", 1))
                extra[f"BootstrapIdentity__Users__{index}__UserId"] = "org03-padded"
                extra[f"BootstrapIdentity__Users__{index}__DisplayName"] = "SENTETIK padded"
                with self.assertRaisesRegex(browser.ContractError, "key set is not exact"):
                    browser.validate_suite_compose(extended("org03", extra), "org03", COMMIT, PROJECT)

    def test_issuer_ids_must_carry_an_rfc_4122_version_and_variant(self):
        for value in ("5d0f7c2a-4b9e-0a53-8a2e-6c1d2f3a4b01", "5d0f7c2a-4b9e-4a53-ca2e-6c1d2f3a4b01"):
            with self.subTest(value=value):
                extra = dict(ISSUER, ProductRuntime__IssuerLocalUserId=value)
                with self.assertRaisesRegex(browser.ContractError, "GUID"):
                    browser.validate_suite_compose(extended("u02", extra), "u02", COMMIT, PROJECT)

    def test_the_canonical_two_actors_cannot_be_replaced_through_an_override(self):
        broken = extended("org03", {"BootstrapIdentity__Users__0__UserId": "org03-admin"})
        with self.assertRaisesRegex(browser.ContractError, "identity"):
            browser.validate_suite_compose(broken, "org03", COMMIT, PROJECT)

    def test_an_override_file_needs_a_closed_list(self):
        original = browser.SUITE_API_EXTENSIONS
        directory = ROOT / browser.SUITE_OVERRIDE_DIRECTORY
        created = not directory.exists()
        directory.mkdir(parents=True, exist_ok=True)
        path = directory / "c03.override.yml"
        try:
            path.write_text("services: {}\n", encoding="utf-8")
            with self.assertRaisesRegex(browser.ContractError, "no closed extension list"):
                browser.suite_override_file("c03")
        finally:
            path.unlink()
            if created:
                directory.rmdir()
            browser.SUITE_API_EXTENSIONS = original


class WorkflowProfileWiringTests(unittest.TestCase):
    def setUp(self):
        self.workflow = (ROOT / ".github/workflows/browser-acceptance.yml").read_text(encoding="utf-8")

    def test_the_profile_is_selected_by_the_validator_before_any_compose_use(self):
        select = self.workflow.index("      - name: Select the suite's capability profile")
        self.assertLess(self.workflow.index("      - name: Validate browser workflow contract tooling"), select)
        self.assertLess(select, self.workflow.index("      - name: Validate loopback-only compose model"))
        self.assertLess(select, self.workflow.index("      - name: Build and start disposable SQL/API"))
        self.assertIn('tools/validate_browser_acceptance.py profile --suite "$BROWSER_SUITE"', self.workflow)
        self.assertIn('echo "C01_E2E_COMPOSE_FILE=$compose_file"', self.workflow)

    def test_canonical_profile_keeps_the_canonical_validator_and_others_use_the_suite_validator(self):
        step = self.workflow.split("      - name: Validate loopback-only compose model", 1)[1].split("      - name:", 1)[0]
        canonical, other = step.split("          else", 1)
        self.assertIn('if [ "$BROWSER_E2E_PROFILE" = "c01-list-only" ]; then', canonical)
        self.assertIn("tools/validate_c01_e2e_contract.py compose", canonical)
        self.assertIn('tools/validate_browser_acceptance.py compose', other)
        self.assertIn('--suite "$BROWSER_SUITE"', other)

    def test_the_suite_receives_its_profile(self):
        run = self.workflow.split("      - name: Run the selected suite", 1)[1].split("      - name:", 1)[0]
        self.assertIn("BROWSER_E2E_PROFILE: ${{ env.BROWSER_E2E_PROFILE }}", run)


class WorkflowOverrideWiringTests(unittest.TestCase):
    def test_every_compose_invocation_carries_the_optional_suite_override(self):
        workflow = (ROOT / ".github/workflows/browser-acceptance.yml").read_text(encoding="utf-8")
        plain = workflow.count('-f "$C01_E2E_COMPOSE_FILE"')
        with_override = workflow.count(
            '-f "$C01_E2E_COMPOSE_FILE" ${BROWSER_E2E_COMPOSE_OVERRIDE:+-f "$BROWSER_E2E_COMPOSE_OVERRIDE"}')
        self.assertGreater(plain, 0)
        self.assertEqual(plain, with_override)
        self.assertIn('echo "BROWSER_E2E_COMPOSE_OVERRIDE=$override_file"', workflow)
