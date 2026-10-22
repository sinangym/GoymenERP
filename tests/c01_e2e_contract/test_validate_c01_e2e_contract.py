import copy
import hashlib
import pathlib
import tempfile
import unittest
import unicodedata

import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))

import validate_c01_e2e_contract as validator  # noqa: E402


COMMIT = "a" * 40
PROJECT = "goymen-c01-100-1"


def valid_compose():
    port = lambda target, published: {
        "host_ip": "127.0.0.1",
        "target": target,
        "published": str(published),
        "protocol": "tcp",
    }
    labels = {
        "com.goymen.e2e.suite": "C01-REAL-BROWSER-E2E-V1",
        "com.goymen.e2e.tested-commit": COMMIT,
    }
    web = lambda identity, published: {
        "build": {
            "context": str(ROOT),
            "dockerfile": "src/frontend/goymen-erp-web/Dockerfile",
            "args": {
                "VITE_BOOTSTRAP_IDENTITY_ENABLED": "true",
                "VITE_DEV_USER": identity,
            },
            "labels": {"org.opencontainers.image.revision": COMMIT},
        },
        "ports": [port(80, published)],
        "labels": labels,
    }
    return {
        "name": PROJECT,
        "services": {
            "sqlserver": {
                "image": validator.SQLSERVER_IMAGE,
                "healthcheck": {"test": ["CMD-SHELL", "SELECT 1"]},
                "ports": [port(1433, 11433)],
                "labels": labels,
            },
            "api": {
                "hostname": f"{PROJECT}-api",
                "build": {
                    "context": str(ROOT),
                    "dockerfile": "src/backend/GoymenERP.Api/Dockerfile",
                    "labels": {"org.opencontainers.image.revision": COMMIT}
                },
                "environment": {
                    "ASPNETCORE_ENVIRONMENT": "Testing",
                    "DB_PROVIDER": "SqlServer",
                    "DB_CONNECTION_STRING": "Database=GoymenErpC01_100_1",
                    "Counterparty__CursorSigningKey": "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
                    "C01E2e__EnableListCapability": "true",
                    "GOYMEN_ERP_C01_E2E_DISPOSABLE": "CONFIRMED",
                    "GOYMEN_ERP_C01_E2E_TESTED_COMMIT": COMMIT,
                    "GOYMEN_ERP_C01_E2E_RUN_ID": "100",
                    "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT": PROJECT,
                    "GOYMEN_ERP_C01_E2E_DATABASE_NAME": "GoymenErpC01_100_1",
                    "GOYMEN_ERP_C01_E2E_REQUEST_EVIDENCE_PATH": (
                        "/evidence/c01-activation-api-requests.jsonl"
                    ),
                    "GOYMEN_ERP_C01_E2E_EVIDENCE_ISSUER": "goymenerp-api-c01-testing-v1",
                    "GOYMEN_ERP_C01_E2E_CONTAINER_IDENTITY": f"{PROJECT}-api",
                    "GOYMEN_ERP_C01_E2E_PROCESS_ID": "1",
                    "GOYMEN_ERP_C01_E2E_PROCESS_START_IDENTITY": (
                        f"{PROJECT}-api-process-1"
                    ),
                    "BootstrapIdentity__HeaderName": "X-Development-User",
                    "BootstrapIdentity__Users__0__UserId": "group-bootstrap-admin",
                    "BootstrapIdentity__Users__0__DisplayName": "C01 E2E Group Administrator",
                    "BootstrapIdentity__Users__1__UserId": "company-a-user",
                    "BootstrapIdentity__Users__1__DisplayName": "C01 E2E Unauthorized User",
                },
                "ports": [port(8080, 15080)],
                "volumes": [{
                    "type": "bind",
                    "source": str(ROOT / "artifacts" / "c01-server-evidence"),
                    "target": "/evidence",
                    "bind": {},
                }],
                "labels": labels,
            },
            "web-authorized": web("group-bootstrap-admin", 15173),
            "web-unauthorized": web("company-a-user", 15174),
        },
        "volumes": {"volume": {"labels": {"com.goymen.e2e.tested-commit": COMMIT}}},
    }


class ComposeContractTests(unittest.TestCase):
    def test_valid_model_passes(self):
        validator.validate_compose(valid_compose(), COMMIT, PROJECT)

    def test_c006_local_preservation_is_manual_and_fail_closed(self):
        workflow = (ROOT / ".github" / "workflows" / "cari-c02-link-acceptance.yml").read_text(
            encoding="utf-8"
        )
        trigger = workflow.split("on:\n", 1)[1].split("\npermissions:", 1)[0]
        self.assertEqual("  workflow_dispatch:\n", trigger)

        required_fragments = (
            "shell: '\"C:\\Program Files\\WindowsApps\\Microsoft.PowerShell_7.6.7.0_x64__8wekyb3d8bbwe\\pwsh.exe\" -NoLogo -NoProfile -NonInteractive -File {0}'",
            "$actualHead = (& git rev-parse HEAD).Trim().ToLowerInvariant()",
            "$runId -notmatch '^[1-9][0-9]*$'",
            "Assert-NoReparsePathChain -Path $evidenceRootCandidate -RequireLeaf $false",
            "Assert-NoReparsePathChain -Path $evidenceRootCandidate -RequireLeaf $true",
            "$readbackFiles = @($manifestReadback.files)",
            "C006 evidence manifest does not contain the exact copied file set",
            "$readbackFile.path -cne $expectedFile.path",
            "C006 evidence copied bytes do not match the manifest",
            "[IO.Directory]::Move($temporary, $destination)",
            "C006 temporary evidence directory still exists after publication",
            "C006 published evidence is not a non-reparse directory",
            "C006 published bytes do not match the manifest",
            "C006 published file set does not exactly match the manifest",
            "if (-not $published -and (Test-Path -LiteralPath $temporary))",
        )
        positions = [workflow.index(fragment) for fragment in required_fragments]
        self.assertEqual(sorted(positions), positions)
        self.assertEqual(
            2,
            workflow.count(
                "Get-ChildItem -LiteralPath $temporary -File -Recurse -Force"
            ),
        )
        self.assertEqual(
            1,
            workflow.count(
                "Get-ChildItem -LiteralPath $destination -File -Recurse -Force"
            ),
        )
        self.assertNotIn("shell: pwsh", workflow)
        self.assertNotIn("Move-Item -LiteralPath $temporary", workflow)

    def test_public_port_fails(self):
        model = valid_compose()
        model["services"]["api"]["ports"][0]["host_ip"] = "0.0.0.0"
        with self.assertRaisesRegex(validator.ContractError, "loopback"):
            validator.validate_compose(model, COMMIT, PROJECT)

    def test_mutable_sql_image_fails(self):
        model = valid_compose()
        model["services"]["sqlserver"]["image"] = (
            "mcr.microsoft.com/mssql/server:2022-latest"
        )
        with self.assertRaisesRegex(validator.ContractError, "SQL image digest"):
            validator.validate_compose(model, COMMIT, PROJECT)

    def test_identity_drift_fails(self):
        model = valid_compose()
        model["services"]["web-authorized"]["build"]["args"]["VITE_DEV_USER"] = "company-a-user"
        with self.assertRaisesRegex(validator.ContractError, "identity"):
            validator.validate_compose(model, COMMIT, PROJECT)

    def test_build_provenance_drift_fails(self):
        mutations = (
            ("api", "context", str(ROOT / "stale-tree"), "API build context"),
            ("api", "dockerfile", "stale/Dockerfile", "API Dockerfile"),
            (
                "web-authorized",
                "context",
                str(ROOT / "stale-tree"),
                "web-authorized build context",
            ),
            (
                "web-unauthorized",
                "dockerfile",
                "stale/Dockerfile",
                "web-unauthorized Dockerfile",
            ),
        )
        for service, key, value, message in mutations:
            with self.subTest(service=service, key=key):
                model = valid_compose()
                model["services"][service]["build"][key] = value
                with self.assertRaisesRegex(validator.ContractError, message):
                    validator.validate_compose(model, COMMIT, PROJECT)

    def test_development_environment_fails(self):
        model = valid_compose()
        model["services"]["api"]["environment"]["ASPNETCORE_ENVIRONMENT"] = "Development"
        with self.assertRaisesRegex(validator.ContractError, "environment"):
            validator.validate_compose(model, COMMIT, PROJECT)

    def test_missing_disposable_flag_fails(self):
        model = valid_compose()
        del model["services"]["api"]["environment"]["C01E2e__EnableListCapability"]
        with self.assertRaisesRegex(validator.ContractError, "capability flag"):
            validator.validate_compose(model, COMMIT, PROJECT)

    def test_wrong_disposable_attestation_fails(self):
        model = valid_compose()
        model["services"]["api"]["environment"]["GOYMEN_ERP_C01_E2E_DISPOSABLE"] = "confirmed"
        with self.assertRaisesRegex(validator.ContractError, "attestation"):
            validator.validate_compose(model, COMMIT, PROJECT)

    def test_source_sha_and_compose_binding_drift_fail(self):
        for key, value, message in (
            ("GOYMEN_ERP_C01_E2E_TESTED_COMMIT", "b" * 40, "source SHA"),
            ("GOYMEN_ERP_C01_E2E_RUN_ID", "101", "run id"),
            ("GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT", "goymen-c01-101-1", "compose project"),
        ):
            with self.subTest(key=key):
                model = valid_compose()
                model["services"]["api"]["environment"][key] = value
                with self.assertRaisesRegex(validator.ContractError, message):
                    validator.validate_compose(model, COMMIT, PROJECT)

    def test_request_evidence_database_path_and_mount_drift_fail(self):
        mutations = (
            ("environment", "GOYMEN_ERP_C01_E2E_DATABASE_NAME", "GoymenErpC01_100_2", "database"),
            ("environment", "GOYMEN_ERP_C01_E2E_REQUEST_EVIDENCE_PATH", "/tmp/requests", "path"),
            ("service", "volumes", ["../artifacts:/evidence"], "normalized"),
            ("service", "volumes", [{
                "type": "bind", "source": str(ROOT / "other"), "target": "/evidence",
            }], "source"),
            ("service", "volumes", [{
                "type": "bind", "source": str(ROOT / "artifacts"), "target": "/evidence",
            }], "source"),
            ("service", "volumes", [{
                "type": "bind",
                "source": str(ROOT / "artifacts" / "c01-server-evidence"),
                "target": "/other",
            }], "target"),
            ("service", "volumes", [{
                "type": "bind",
                "source": str(ROOT / "artifacts" / "c01-server-evidence"),
                "target": "/evidence",
                "read_only": True,
            }], "writable"),
        )
        for kind, key, value, message in mutations:
            with self.subTest(key=key):
                model = valid_compose()
                if kind == "environment":
                    model["services"]["api"]["environment"][key] = value
                else:
                    model["services"]["api"][key] = value
                with self.assertRaisesRegex(validator.ContractError, message):
                    validator.validate_compose(model, COMMIT, PROJECT)

    def test_request_evidence_provenance_envelope_drift_fails(self):
        mutations = (
            ("service", "hostname", "other-api", "hostname"),
            ("environment", "GOYMEN_ERP_C01_E2E_EVIDENCE_ISSUER", "other", "issuer"),
            ("environment", "GOYMEN_ERP_C01_E2E_CONTAINER_IDENTITY", "other", "container"),
            ("environment", "GOYMEN_ERP_C01_E2E_PROCESS_ID", "2", "process id"),
            ("environment", "GOYMEN_ERP_C01_E2E_PROCESS_START_IDENTITY", "other", "process start"),
        )
        for kind, key, value, message in mutations:
            with self.subTest(key=key):
                model = valid_compose()
                if kind == "environment":
                    model["services"]["api"]["environment"][key] = value
                else:
                    model["services"]["api"][key] = value
                with self.assertRaisesRegex(validator.ContractError, message):
                    validator.validate_compose(model, COMMIT, PROJECT)

    def test_non_run_scoped_database_fails(self):
        model = valid_compose()
        model["services"]["api"]["environment"]["DB_CONNECTION_STRING"] = (
            "Database=GoymenErp"
        )
        with self.assertRaisesRegex(validator.ContractError, "disposable DB"):
            validator.validate_compose(model, COMMIT, PROJECT)

    def test_disposable_database_diagnostic_is_closed_and_secret_free(self):
        expected = "GoymenErpC01_100_1"
        actual = "SENTETIK-Catalog-Actual"
        password = "SENTETIK-Password-Must-Not-Appear"
        model = valid_compose()
        model["services"]["api"]["environment"]["DB_CONNECTION_STRING"] = (
            f"Server=sqlserver;Initial Catalog={actual};User Id=sa;Password={password}"
        )

        def digest(value):
            normalized = unicodedata.normalize("NFC", value.strip()).casefold()
            return hashlib.sha256(normalized.encode("utf-8")).hexdigest()

        with self.assertRaises(validator.ContractError) as caught:
            validator.validate_compose(model, COMMIT, PROJECT)

        self.assertEqual(
            "API disposable DB drift "
            "[rule=api_disposable_database;service=api;binding=DB_CONNECTION_STRING;"
            f"expectedCatalogSha256={digest(expected)};actualCatalogSha256={digest(actual)}]",
            str(caught.exception),
        )
        for forbidden in (expected, actual, password, "Server=sqlserver", "Initial Catalog="):
            self.assertNotIn(forbidden, str(caught.exception))
        self.assertTrue(str(caught.exception).isascii())

    def test_disposable_database_diagnostic_closes_missing_and_ambiguous_catalogs(self):
        cases = (
            ("Server=sqlserver;User Id=sa;Password=SENTETIK-missing", "missing"),
            ("Database=one;Initial Catalog=two;Password=SENTETIK-ambiguous", "ambiguous"),
        )
        for connection_string, sentinel in cases:
            with self.subTest(sentinel=sentinel):
                model = valid_compose()
                model["services"]["api"]["environment"]["DB_CONNECTION_STRING"] = connection_string
                with self.assertRaises(validator.ContractError) as caught:
                    validator.validate_compose(model, COMMIT, PROJECT)
                message = str(caught.exception)
                self.assertIn(f"actualCatalogSha256={sentinel}]", message)
                self.assertNotIn(connection_string, message)
                self.assertNotIn("SENTETIK-", message)

    def test_disposable_database_diagnostic_ignores_catalog_text_inside_quoted_password(self):
        password = 'SENTETIK-prefix;Database=SENTETIK-private-fragment'
        cases = (
            (
                f'Server=sqlserver;Password="{password}";User Id=sa',
                "missing",
            ),
            (
                f'Database=GoymenErpC01_wrong;Password="{password}";User Id=sa',
                hashlib.sha256(b"goymenerpc01_wrong").hexdigest(),
            ),
        )
        password_fragment_hash = hashlib.sha256(
            'SENTETIK-private-fragment"'.casefold().encode("utf-8")
        ).hexdigest()

        for connection_string, actual in cases:
            with self.subTest(actual=actual):
                model = valid_compose()
                model["services"]["api"]["environment"]["DB_CONNECTION_STRING"] = connection_string
                with self.assertRaises(validator.ContractError) as caught:
                    validator.validate_compose(model, COMMIT, PROJECT)
                message = str(caught.exception)
                self.assertIn(f"actualCatalogSha256={actual}]", message)
                self.assertNotIn(password_fragment_hash, message)
                self.assertNotIn(password, message)

    def test_missing_or_reordered_bootstrap_identity_fails(self):
        for key, value, message in (
            ("BootstrapIdentity__HeaderName", "X-Other", "bootstrap header"),
            ("BootstrapIdentity__Users__0__UserId", "company-a-user", "authorized bootstrap"),
            ("BootstrapIdentity__Users__1__UserId", "group-bootstrap-admin", "unauthorized bootstrap"),
        ):
            with self.subTest(key=key):
                model = valid_compose()
                model["services"]["api"]["environment"][key] = value
                with self.assertRaisesRegex(validator.ContractError, message):
                    validator.validate_compose(model, COMMIT, PROJECT)

    def test_workflow_keeps_exact_head_and_cleanup_boundaries(self):
        workflow = (ROOT / ".github" / "workflows" / "c01-real-browser-e2e.yml").read_text(
            encoding="utf-8"
        )
        for required in (
            "name: C-01 real-browser E2E",
            "github.event.pull_request.head.sha || github.sha",
            "git merge-base --is-ancestor",
            "C01_BROWSER_SUITE_MISSING",
            "down -v --remove-orphans",
            "C01_E2E_BASELINE_EVIDENCE",
            "C01_E2E_DATABASE_NAME",
            "GOYMEN_ERP_C01_E2E_DISPOSABLE=CONFIRMED",
            "name: Preserve exact-head evidence locally",
            "C01_EVIDENCE_ROOT: /home/ci/local-ci-evidence",
            "C01 manifest SHA256:",
            "id: artifact_privacy",
            "steps.artifact_privacy.outcome == 'success'",
            "tools/validate_sab_e2e_contract.py preflight",
            "npm run sab:ci -- --reporter=list,junit,html",
            "tools/validate_sab_e2e_contract.py evidence",
            "PLAYWRIGHT_HTML_OUTPUT_DIR",
            '-s tests/dockerhub_mirror_contract',
            "sudo python3 -I -S -B tools/configure_dockerhub_mirror.py configure",
            "--daemon-config /etc/docker/daemon.json",
            "sudo systemctl restart docker",
            "docker info --format '{{json .RegistryConfig.Mirrors}}'",
            "python3 -I -S -B tools/configure_dockerhub_mirror.py verify",
        ):
            self.assertIn(required, workflow)
        trigger = workflow.split("on:\n", 1)[1].split("\npermissions:", 1)[0]
        self.assertEqual(trigger.strip(), "workflow_dispatch:")
        self.assertNotIn("actions/upload-artifact", workflow)
        self.assertNotIn("pull_request_target", workflow)
        self.assertNotIn("secrets.", workflow)
        self.assertNotIn("compose.log", workflow)
        self.assertNotIn("path: artifacts/", workflow)
        tooling = workflow.index("      - name: Validate E2E contract tooling")
        mirror = workflow.index("      - name: Configure Docker Hub cache mirror")
        compose = workflow.index("      - name: Validate loopback-only compose model")
        build = workflow.index("      - name: Build and start disposable SQL/API")
        self.assertLess(tooling, mirror)
        self.assertLess(mirror, compose)
        self.assertLess(compose, build)
        mirror_body = workflow.split(
            "      - name: Configure Docker Hub cache mirror", 1
        )[1].split("      - name:", 1)[0]
        self.assertNotIn("continue-on-error", mirror_body)
        self.assertNotIn("secrets.", mirror_body)
        sab_run = workflow.index("npm run sab:ci -- --reporter=list,junit,html")
        self.assertLess(workflow.index("npm ci"), sab_run)
        self.assertLess(
            workflow.index("playwright install --with-deps chromium"), sab_run
        )
        self.assertLess(
            workflow.index("tools/validate_sab_e2e_contract.py preflight"), sab_run
        )
        self.assertLess(
            sab_run, workflow.index("tools/validate_sab_e2e_contract.py evidence")
        )
        preservation = workflow.split(
            "      - name: Preserve exact-head evidence locally", 1
        )[1]

        def shell_array(name):
            block = preservation.split(f"          {name}=(\n", 1)[1].split(
                "          )", 1
            )[0]
            return tuple(
                line.strip().strip('"')
                for line in block.splitlines()
                if line.strip()
            )

        self.assertEqual(shell_array("c01_files"), validator.C01_ARTIFACT_UPLOAD_ALLOWLIST)
        self.assertEqual(
            shell_array("activation_files"),
            validator.ACTIVATION_ARTIFACT_UPLOAD_ALLOWLIST,
        )
        self.assertEqual(shell_array("sab_files"), validator.SAB_ARTIFACT_UPLOAD_ALLOWLIST)
        for required in (
            "steps.artifact_privacy.outcome == 'success'",
            "steps.activation_gate.outputs.enabled",
            "steps.sab_gate.outputs.enabled",
            "GITHUB_RUN_ATTEMPT",
            "gh-${workflow}-${run_id}-${run_attempt}-${sha7}",
            "C01_EVIDENCE_DESTINATION_EXISTS",
            "C01_EVIDENCE_ROOT_INVALID",
            "C01_EVIDENCE_ROOT_SYMLINK",
            "C01_EVIDENCE_SELECTED_FILE_ESCAPE",
            "mv -T --no-clobber",
            "C01_EVIDENCE_PUBLICATION_REFUSED",
            "C01_EVIDENCE_PUBLICATION_INVALID",
            '"schemaVersion": 1',
            '"files": files',
            "C01_EVIDENCE_MANIFEST_IDENTITY_MISMATCH",
            "C01_EVIDENCE_MANIFEST_PUBLICATION_DRIFT",
        ):
            self.assertIn(required, preservation)
        publish = preservation.index('mv -T --no-clobber -- "$temporary" "$destination"')
        temp_gone = preservation.index('[[ ! -e "$temporary" && ! -L "$temporary" ]]')
        final_directory = preservation.index('[[ -d "$destination" && ! -L "$destination" ]]')
        final_hash = preservation.index('[[ "$final_manifest_hash" == "$manifest_hash" ]]')
        clear_trap = preservation.index("trap - EXIT")
        self.assertLess(publish, temp_gone)
        self.assertLess(temp_gone, final_directory)
        self.assertLess(final_directory, final_hash)
        self.assertLess(final_hash, clear_trap)

    def test_api_start_failure_diagnostics_are_masked_job_log_only(self):
        workflow = (ROOT / ".github" / "workflows" / "c01-real-browser-e2e.yml").read_text(
            encoding="utf-8"
        )
        start = workflow.split("      - name: Build and start disposable SQL/API", 1)[1].split(
            "      - name:", 1
        )[0]

        self.assertIn("ps --all", start)
        self.assertIn("logs --no-color --tail 80 api || true", start)
        self.assertNotIn("artifacts/", start)
        self.assertNotIn("secrets.", start)
        self.assertNotIn("env", start)
        self.assertEqual(
            validator.C01_ARTIFACT_UPLOAD_ALLOWLIST,
            (
                "c01-e2e-api-preflight.json",
                "c01-e2e-evidence.json",
                "c01-e2e-fixture-evidence.json",
                "c01-e2e-junit.xml",
                "c01-e2e-network-summary.json",
                "c01-e2e-post-browser-sql-evidence.json",
                "c01-e2e-html-report/index.html",
                "container-logs/compose-ps.txt",
            ),
        )


class ArtifactPrivacyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.temp.name)
        self.safe = self.root / "container-logs" / "compose-ps.txt"
        self.safe.parent.mkdir(parents=True)
        self.safe.write_text("NAME STATUS\napi Up\n", encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def test_safe_allowlisted_artifact_passes(self):
        validator.validate_artifact_privacy(self.root)

    def test_sab_artifacts_are_allowlisted_and_privacy_scanned(self):
        junit = self.root / "sab-junit.xml"
        html = self.root / "sab-html-report" / "index.html"
        html.parent.mkdir(parents=True)
        junit.write_text("<testsuites />", encoding="utf-8")
        html.write_text("<html>SAB</html>", encoding="utf-8")
        selected = {
            path.relative_to(self.root).as_posix()
            for path in validator.artifact_upload_paths(self.root)
        }
        self.assertIn("sab-junit.xml", selected)
        self.assertIn("sab-html-report/index.html", selected)
        validator.validate_artifact_privacy(self.root)

        html.write_text("<html>?cursor=SIGNED-CURSOR</html>", encoding="utf-8")
        with self.assertRaisesRegex(validator.ContractError, "cursor query"):
            validator.validate_artifact_privacy(self.root)

    def test_activation_server_evidence_has_a_dedicated_pending_upload_boundary(self):
        relative = validator.ACTIVATION_SERVER_EVIDENCE_RELATIVE_PATH
        self.assertNotIn(relative, validator.ARTIFACT_UPLOAD_ALLOWLIST)
        workflow = (ROOT / ".github" / "workflows" / "c01-real-browser-e2e.yml").read_text(
            encoding="utf-8"
        )
        self.assertNotIn(f"artifacts/{relative}", workflow)

        evidence = self.root / relative
        evidence.parent.mkdir(parents=True)
        evidence.write_text(
            '{"correlationDigest":"' + ("a" * 64) + '","path":"/api/v1/counterparties"}\n',
            encoding="utf-8",
        )
        validator.validate_activation_server_evidence_privacy(self.root)

        evidence.write_text("Authorization: Bearer header.payload.signature", encoding="utf-8")
        with self.assertRaisesRegex(validator.ContractError, "authorization credential"):
            validator.validate_activation_server_evidence_privacy(self.root)

    def test_raw_compose_log_is_not_uploadable(self):
        raw_log = self.root / "container-logs" / "compose.log"
        raw_log.write_text("GET /api/v1/counterparties?cursor=SIGNED", encoding="utf-8")
        selected = {
            path.relative_to(self.root).as_posix()
            for path in validator.artifact_upload_paths(self.root)
        }
        self.assertNotIn("container-logs/compose.log", selected)

    def test_cursor_in_html_report_fails_closed(self):
        html = self.root / "c01-e2e-html-report" / "index.html"
        html.parent.mkdir(parents=True)
        html.write_text(
            "<html>GET /api/v1/counterparties?cursor=SIGNED-CURSOR</html>",
            encoding="utf-8",
        )
        with self.assertRaisesRegex(validator.ContractError, "cursor query"):
            validator.validate_artifact_privacy(self.root)

    def test_secret_matrix_fails_closed(self):
        mutations = (
            "Authorization: Bearer header.payload.signature",
            "Idempotency-Key: secret-key",
            "C01_0123456789abcdef0123456789abcdef_Aa1!",
            "Server=sqlserver;Database=db;User Id=sa;Password=secret",
            "C01_E2E_CURSOR_SIGNING_KEY=base64-secret",
        )
        for index, mutation in enumerate(mutations):
            with self.subTest(mutation=mutation):
                path = self.root / "c01-e2e-api-preflight.json"
                path.write_text(mutation, encoding="utf-8")
                with self.assertRaises(validator.ContractError):
                    validator.validate_artifact_privacy(self.root)
                path.unlink()

    def test_runtime_secret_values_fail_closed(self):
        path = self.root / "c01-e2e-api-preflight.json"
        secret = "otherwise-undetectable-runtime-secret"
        path.write_text(f"prefix:{secret}:suffix", encoding="utf-8")
        with self.assertRaisesRegex(validator.ContractError, "runtime secret"):
            validator.validate_artifact_privacy(self.root, [secret])

    def test_too_short_runtime_secret_fails_closed(self):
        with self.assertRaisesRegex(validator.ContractError, "too short"):
            validator.validate_artifact_privacy(self.root, ["short"])


def valid_evidence():
    return {
        "schemaVersion": 1,
        "suiteId": "C01-REAL-BROWSER-E2E-V1",
        "repository": "sinangym/GoymenERP",
        "testedCommit": COMMIT,
        "runId": "100",
        "composeProject": PROJECT,
        "fixture": {
            "id": validator.FIXTURE_ID,
            "schemaVersion": 1,
            "byteLength": 29817,
            "sha256": validator.FIXTURE_SHA256,
            "partyCount": 54,
            "accountCount": 55,
            "sequenceNextValue": 55,
        },
        "browser": {"name": "chromium", "version": "140.0.1"},
        "viewports": validator.EXPECTED_VIEWPORTS,
        "cases": [{"id": case_id, "status": "passed"} for case_id in validator.EXPECTED_CASES],
        "skipped": 0,
        "startedAtUtc": "2026-09-01T00:00:00Z",
        "completedAtUtc": "2026-09-01T00:01:00Z",
    }


def valid_network():
    requests = [
        {"method": "GET", "path": path, "ordinal": index + 1, "outcome": "real-200", "retryOrdinal": 0}
        for index, path in enumerate(sorted(validator.REQUIRED_REAL_PATHS))
    ]
    for outcome in sorted(validator.REQUIRED_FAULT_OUTCOMES):
        requests.append(
            {
                "method": "GET",
                "path": "/api/v1/counterparties",
                "ordinal": len(requests) + 1,
                "outcome": outcome,
                "retryOrdinal": 1 if outcome == "retry-200" else 0,
            }
        )
    return {
        "schemaVersion": 1,
        "testedCommit": COMMIT,
        "authorizedCompanyId": validator.GDC_COMPANY_ID,
        "foreignRecordAbsent": True,
        "redacted": True,
        "requests": requests,
    }


class EvidenceContractTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        root = pathlib.Path(self.temp.name)
        self.junit = root / "junit.xml"
        self.html = root / "index.html"
        cases = "".join(
            f'<testcase name="{case_id}" classname="c01" />'
            for case_id in validator.EXPECTED_CASES
        )
        self.junit.write_text(f'<testsuites><testsuite>{cases}</testsuite></testsuites>', encoding="utf-8")
        self.html.write_text("<html>C-01</html>", encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def validate(self, evidence=None, network=None):
        validator.validate_evidence(
            evidence or valid_evidence(),
            network or valid_network(),
            str(self.junit),
            str(self.html),
            COMMIT,
            "100",
            PROJECT,
        )

    def test_valid_evidence_passes(self):
        self.validate()

    def test_skip_fails(self):
        evidence = copy.deepcopy(valid_evidence())
        evidence["skipped"] = 1
        with self.assertRaisesRegex(validator.ContractError, "skipped"):
            self.validate(evidence=evidence)

    def test_cursor_query_leak_fails(self):
        network = copy.deepcopy(valid_network())
        network["requests"][0]["path"] = "/api/v1/counterparties?cursor=secret"
        with self.assertRaisesRegex(validator.ContractError, "redacted"):
            self.validate(network=network)

    def test_extra_raw_url_field_fails(self):
        network = copy.deepcopy(valid_network())
        network["requests"][0]["rawUrl"] = "?cursor=RAW-SECRET"
        with self.assertRaisesRegex(validator.ContractError, "keys mismatch"):
            self.validate(network=network)

    def test_fault_outcome_on_identity_path_fails(self):
        network = copy.deepcopy(valid_network())
        fault = next(
            request for request in network["requests"]
            if request["outcome"] == "injected-offline"
        )
        fault["path"] = "/api/v1/identity/me"
        with self.assertRaisesRegex(validator.ContractError, "Counterparty read endpoint"):
            self.validate(network=network)

    def test_retry_200_without_retry_ordinal_fails(self):
        network = copy.deepcopy(valid_network())
        retry = next(
            request for request in network["requests"]
            if request["outcome"] == "retry-200"
        )
        retry["retryOrdinal"] = 0
        with self.assertRaisesRegex(validator.ContractError, "retryOrdinal > 0"):
            self.validate(network=network)

    def test_duplicate_network_ordinal_fails(self):
        network = copy.deepcopy(valid_network())
        network["requests"][-1]["ordinal"] = network["requests"][-2]["ordinal"]
        with self.assertRaisesRegex(validator.ContractError, "unique and contiguous"):
            self.validate(network=network)

    def test_one_junit_case_concatenating_all_ids_fails(self):
        joined = " ".join(validator.EXPECTED_CASES)
        self.junit.write_text(
            "<testsuites><testsuite>"
            f'<testcase name="{joined}" classname="c01" />'
            "</testsuite></testsuites>",
            encoding="utf-8",
        )
        with self.assertRaisesRegex(validator.ContractError, "exactly ten"):
            self.validate()

    def test_junit_duplicate_case_id_fails(self):
        cases = "".join(
            '<testcase name="B-C01-001 duplicate" classname="c01" />'
            for _ in validator.EXPECTED_CASES
        )
        self.junit.write_text(
            f"<testsuites><testsuite>{cases}</testsuite></testsuites>",
            encoding="utf-8",
        )
        with self.assertRaisesRegex(validator.ContractError, "exact and unique"):
            self.validate()


if __name__ == "__main__":
    unittest.main()
