#!/usr/bin/env python3
"""Run the fixed P03.2 U02/R14 browser proof under local admission.

This module is invoked only by ``local_check.py`` while the Windows and Linux
heavy-work locks and the source-bound launcher lease are held. It does not
dispatch GitHub Actions or upload artifacts.
"""
from __future__ import annotations

import base64
from dataclasses import dataclass, field
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import signal
import stat
import subprocess
import sys
import tempfile
import time
from time import monotonic_ns
from typing import Callable
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET


PROFILE = "p032-u02-r14"
SUITES = ("u02", "r14")
PORTS = {"sql": 11433, "api": 15080, "authorizedWeb": 15173, "unauthorizedWeb": 15174}
SUITE_CONTRACTS = {
    "u02": {
        "capabilityProfile": "normal",
        "composeFile": "infra/docker-compose.browser-normal.yml",
        "overrideFile": "infra/browser-suites/u02.override.yml",
        "fixture": True,
        "script": "browser:u02:ci",
        "scriptValue": "playwright test --config playwright.product-u02.config.ts",
        "config": "tests/e2e/playwright.product-u02.config.ts",
        "testDirectory": "./product-u02-suite",
    },
    "r14": {
        "capabilityProfile": "normal",
        "composeFile": "infra/docker-compose.browser-normal.yml",
        "overrideFile": "infra/browser-suites/r14.override.yml",
        "fixture": True,
        "script": "browser:r14:ci",
        "scriptValue": "playwright test --config playwright.inventory-r14.config.ts",
        "config": "tests/e2e/playwright.inventory-r14.config.ts",
        "testDirectory": "./inventory-r14/specs",
    },
}
MANDATORY_ARTIFACTS = (
    "browser-{suite}-junit.xml",
    "browser-fixture-evidence.json",
    "browser-suite-fixture-evidence.json",
    "browser-suite-seed.json",
    "container-logs/compose-ps.txt",
)
SAFE_SEGMENT = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]*$")
COMMIT = re.compile(r"^[0-9a-f]{40}$")
SENSITIVE = re.compile(
    r"(?i)\b[\w.-]*(?:password|passwd|secret|token|api[_-]?key|access[_-]?key|"
    r"private[_-]?key|authorization|connection[_-]?string)[\w.-]*\s*[=:]"
)
BEARER = re.compile(r"(?i)\bBearer\s+[^\r\n]+")
UNSAFE_XML = re.compile(br"<!\s*(?:DOCTYPE|ENTITY)\b", re.IGNORECASE)
MAX_JUNIT_BYTES = 10_000_000
MAX_JUNIT_ELEMENTS = 20_000
MAX_JUNIT_DEPTH = 16


class AcceptanceError(RuntimeError):
    """A fail-closed local acceptance refusal."""


class CommandFailure(AcceptanceError):
    pass


class SecretOutputError(AcceptanceError):
    pass


def now() -> str:
    return datetime.now(timezone.utc).isoformat()


def clock_fields(prefix: str) -> dict[str, str | int]:
    return {prefix + "Utc": now(), prefix + "MonotonicNs": monotonic_ns()}


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def clean_environment() -> dict[str, str]:
    """Keep the same explicit environment boundary as the admitted local runner."""
    allowed = (
        "PATH",
        "HOME",
        "LANG",
        "LC_ALL",
        "DOTNET_ROOT",
        "DOTNET_CLI_HOME",
        "NUGET_PACKAGES",
        "NPM_CONFIG_CACHE",
        "DOTNET_CLI_TELEMETRY_OPTOUT",
        "DOTNET_PROCESSOR_COUNT",
        "NODE_OPTIONS",
        "CI",
    )
    result = {key: os.environ[key] for key in allowed if key in os.environ}
    result["CI"] = "true"
    return result


def git(root: Path, *arguments: str) -> str:
    return subprocess.check_output(
        ["git", "-C", str(root), *arguments],
        text=True,
        encoding="utf-8",
        env=clean_environment(),
    ).strip()


def source_snapshot(root: Path) -> dict:
    if git(root, "rev-parse", "--show-toplevel") != str(root):
        raise AcceptanceError("A canonical repository root is required")
    return {
        "commit": git(root, "rev-parse", "HEAD"),
        "tree": git(root, "rev-parse", "HEAD^{tree}"),
        "dirty": bool(git(root, "status", "--porcelain=v1")),
    }


def source_binding(root: Path, expected_head: str) -> dict:
    if COMMIT.fullmatch(expected_head) is None:
        raise AcceptanceError("Expected source commit is required")
    snapshot = source_snapshot(root)
    if snapshot["commit"] != expected_head:
        raise AcceptanceError("Source moved from the admitted exact head")
    if snapshot["dirty"]:
        raise AcceptanceError("P03.2 browser acceptance requires a clean exact source")
    return snapshot


class _ReviewedSourceLoader:
    """Minimal loader that compiles the reviewed source bytes and ignores all pyc files."""

    def __init__(self, root: Path, path: Path, revision: str):
        self.root = root.resolve()
        self.path = path.resolve()
        self.revision = revision

    def create_module(self, spec):
        return None

    def exec_module(self, module) -> None:
        if (not self.path.is_relative_to(self.root) or self.path.is_symlink()
                or not stat.S_ISREG(self.path.stat().st_mode)):
            raise AcceptanceError("Reviewed Python source must be a confined regular file")
        relative = self.path.relative_to(self.root).as_posix()
        if git(self.root, "ls-files", "--error-unmatch", relative) != relative:
            raise AcceptanceError("Reviewed Python source must be tracked")
        source_bytes = subprocess.check_output(
            ["git", "-C", str(self.root), "cat-file", "blob", f"{self.revision}:{relative}"],
            env=clean_environment(),
        )
        if self.path.read_bytes() != source_bytes:
            raise AcceptanceError("Reviewed Python source does not match the exact HEAD blob")
        try:
            source = source_bytes.decode("utf-8")
        except UnicodeDecodeError as error:
            raise AcceptanceError("Reviewed Python source must be strict UTF-8") from error
        module.__file__ = str(self.path)
        exec(compile(source, str(self.path), "exec", dont_inherit=True), module.__dict__)


def _reviewed_source_spec(
    root: Path,
    name: str,
    location: str | os.PathLike[str],
    revision: str,
):
    path = Path(location).resolve()
    loader = _ReviewedSourceLoader(root, path, revision)
    return importlib.util.spec_from_loader(name, loader, origin=str(path))


def load_browser_contract(root: Path, revision: str = "HEAD"):
    root = root.resolve()
    path = root / "tools/validate_browser_acceptance.py"
    name = "local_p032_browser_contract"
    spec = _reviewed_source_spec(root, name, path, revision)
    if spec is None or spec.loader is None:
        raise AcceptanceError("Canonical browser validator cannot be loaded")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    original = importlib.util.spec_from_file_location

    def reviewed_spec(module_name, location, *args, **kwargs):
        candidate = Path(location).resolve()
        if candidate.suffix == ".py" and candidate.is_relative_to(root):
            return _reviewed_source_spec(root, module_name, candidate, revision)
        return original(module_name, location, *args, **kwargs)

    importlib.util.spec_from_file_location = reviewed_spec
    try:
        spec.loader.exec_module(module)
    except BaseException:
        if sys.modules.get(name) is module:
            del sys.modules[name]
        raise
    finally:
        importlib.util.spec_from_file_location = original
    return module


def _require_source_pattern(source: str, pattern: str, label: str) -> None:
    if re.search(pattern, source, flags=re.MULTILINE) is None:
        raise AcceptanceError(f"Browser source contract is missing {label}")


def validate_source_contract(root: Path, contract) -> list[dict]:
    """Bind the adapter to the reviewed workflow/validator-owned suite surface."""
    package = json.loads((root / "tests/e2e/package.json").read_text(encoding="utf-8"))
    scripts = package.get("scripts")
    if not isinstance(scripts, dict):
        raise AcceptanceError("Browser package scripts are missing")
    evidence = []
    for suite in SUITES:
        expected = SUITE_CONTRACTS[suite]
        profile = contract.profile_of(suite)
        compose = contract.PROFILE_COMPOSE_FILES[profile]
        override = contract.suite_override_file(suite)
        fixture = contract.has_fixture(suite)
        if (profile, compose, override, fixture, scripts.get(expected["script"])) != (
            expected["capabilityProfile"],
            expected["composeFile"],
            expected["overrideFile"],
            expected["fixture"],
            expected["scriptValue"],
        ):
            raise AcceptanceError(f"Browser source contract drifted for {suite}")
        config = (root / expected["config"]).read_text(encoding="utf-8")
        _require_source_pattern(
            config,
            rf"testDir:\s*['\"]{re.escape(expected['testDirectory'])}['\"]",
            f"{suite} testDir",
        )
        for pattern, label in (
            (r"workers:\s*1\s*,", "workers=1"),
            (r"retries:\s*0\s*,", "retries=0"),
            (r"forbidOnly:\s*true\s*,", "forbidOnly=true"),
            (r"trace:\s*['\"]off['\"]", "trace=off"),
            (r"screenshot:\s*['\"]off['\"]", "screenshot=off"),
            (r"video:\s*['\"]off['\"]", "video=off"),
            (r"BROWSER_E2E_JUNIT_FILE", "JUnit reporter"),
        ):
            _require_source_pattern(config, pattern, f"{suite} {label}")
        evidence.append({"suite": suite, **expected})
    return evidence


@dataclass(frozen=True)
class SuiteBinding:
    suite: str
    run_id: str
    attempt: str
    compose_project: str
    database: str
    sql_password: str = field(repr=False)
    cursor_key: str = field(repr=False)
    connection: str = field(repr=False)

    def public(self) -> dict:
        return {
            "suite": self.suite,
            "runId": self.run_id,
            "attempt": self.attempt,
            "composeProject": self.compose_project,
            "database": self.database,
            "ports": PORTS,
        }

    def secret_values(self) -> tuple[str, str, str]:
        return (self.sql_password, self.cursor_key, self.connection)


def new_binding(suite: str, sequence: int, run_id: str | None = None) -> SuiteBinding:
    if suite not in SUITES:
        raise AcceptanceError("Only the fixed U02/R14 suite pair is admitted")
    numeric = run_id or f"{time.time_ns()}{sequence}"
    if re.fullmatch(r"[1-9][0-9]{5,24}", numeric) is None:
        raise AcceptanceError("Disposable browser run id must be bounded positive decimal")
    attempt = "1"
    project = f"goymen-c01-{numeric}-{attempt}"
    database = f"GoymenErpC01_{numeric}_{attempt}"
    password = "BR_" + secrets.token_hex(20) + "_Aa1!"
    cursor = base64.b64encode(secrets.token_bytes(48)).decode("ascii")
    connection = (
        f"Server=127.0.0.1,{PORTS['sql']};Database={database};User Id=sa;"
        f"Password={password};Encrypt=True;TrustServerCertificate=True"
    )
    return SuiteBinding(suite, numeric, attempt, project, database, password, cursor, connection)


def suite_environment(binding: SuiteBinding, expected_head: str, artifacts: Path) -> dict[str, str]:
    result = clean_environment()
    result.update({
        "BROWSER_SUITE": binding.suite,
        "C01_E2E_SQL_PORT": str(PORTS["sql"]),
        "C01_E2E_API_PORT": str(PORTS["api"]),
        "C01_E2E_AUTHORIZED_WEB_PORT": str(PORTS["authorizedWeb"]),
        "C01_E2E_UNAUTHORIZED_WEB_PORT": str(PORTS["unauthorizedWeb"]),
        "C01_E2E_TESTED_COMMIT": expected_head,
        "C01_E2E_COMPOSE_PROJECT": binding.compose_project,
        "C01_E2E_DATABASE_NAME": binding.database,
        "COMPOSE_PROJECT_NAME": binding.compose_project,
        "MSSQL_SA_PASSWORD": binding.sql_password,
        "C01_E2E_CURSOR_SIGNING_KEY": binding.cursor_key,
        "GOYMEN_ERP_C01_E2E_SQLSERVER_CONNECTION": binding.connection,
        "GOYMEN_ERP_C01_E2E_DISPOSABLE": "CONFIRMED",
        "GOYMEN_ERP_C01_E2E_TESTED_COMMIT": expected_head,
        "GOYMEN_ERP_C01_E2E_RUN_ID": binding.run_id,
        "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT": binding.compose_project,
        "GOYMEN_ERP_C01_E2E_FIXTURE_EVIDENCE": str(artifacts / "browser-fixture-evidence.json"),
        "BROWSER_E2E_JUNIT_FILE": str(artifacts / f"browser-{binding.suite}-junit.xml"),
        "BROWSER_E2E_ARTIFACTS_DIR": str(artifacts),
        "BROWSER_E2E_AUTHORIZED_BASE_URL": f"http://127.0.0.1:{PORTS['authorizedWeb']}",
        "BROWSER_E2E_UNAUTHORIZED_BASE_URL": f"http://127.0.0.1:{PORTS['unauthorizedWeb']}",
        "BROWSER_E2E_API_BASE_URL": f"http://127.0.0.1:{PORTS['api']}",
        "BROWSER_E2E_SQL_CONNECTION": binding.connection,
        "BROWSER_E2E_DATABASE": binding.database,
        "BROWSER_E2E_TESTED_COMMIT": expected_head,
        "BROWSER_E2E_PROFILE": "normal",
    })
    return result


def compose_arguments(root: Path, binding: SuiteBinding) -> list[str]:
    expected = SUITE_CONTRACTS[binding.suite]
    return [
        "docker", "compose", "-p", binding.compose_project,
        "-f", str(root / expected["composeFile"]),
        "-f", str(root / expected["overrideFile"]),
    ]


def _safe_output(
    value: str,
    secret_values: tuple[str, ...],
    forbidden_patterns: tuple[re.Pattern[bytes], ...] = (),
) -> tuple[str, bool]:
    encoded = value.encode("utf-8")
    if (any(secret and secret in value for secret in secret_values)
            or any(pattern.search(encoded) for pattern in forbidden_patterns)
            or SENSITIVE.search(value) or BEARER.search(value)):
        return "[CREDENTIAL_REDACTED]\n", True
    return value, False


def _canonical_forbidden_patterns(contract) -> tuple[re.Pattern[bytes], ...]:
    patterns = getattr(getattr(contract, "c01", None), "ARTIFACT_FORBIDDEN_PATTERNS", None)
    if not isinstance(patterns, tuple) or not patterns:
        raise AcceptanceError("Canonical artifact privacy patterns are unavailable")
    result = []
    for item in patterns:
        if (not isinstance(item, tuple) or len(item) != 2 or not isinstance(item[0], str)
                or not hasattr(item[1], "search")):
            raise AcceptanceError("Canonical artifact privacy patterns are invalid")
        result.append(item[1])
    return tuple(result)


def _scan_log_bytes(
    content: bytes,
    secret_values: tuple[str, ...],
    forbidden_patterns: tuple[re.Pattern[bytes], ...],
) -> tuple[str | None, str | None]:
    try:
        decoded = content.decode("utf-8")
    except UnicodeDecodeError:
        return None, "NON_UTF8_OUTPUT"
    if any(pattern.search(content) for pattern in forbidden_patterns):
        return None, "CANONICAL_PRIVACY_PATTERN"
    if any(secret and secret.encode("utf-8") in content for secret in secret_values):
        return None, "RUNTIME_SECRET"
    if SENSITIVE.search(decoded) or BEARER.search(decoded):
        return None, "CREDENTIAL_SHAPE"
    return decoded, None


class CommandRunner:
    """Run owned local children and persist only bounded, secret-scanned output."""

    def __init__(
        self,
        quarantine_logs: Path,
        evidence_logs: Path,
        forbidden_patterns: tuple[re.Pattern[bytes], ...],
    ):
        self.quarantine_logs = quarantine_logs
        self.evidence_logs = evidence_logs
        self.forbidden_patterns = forbidden_patterns
        self.quarantine_logs.mkdir(parents=True, exist_ok=True)
        self.evidence_logs.mkdir(parents=True, exist_ok=True)
        self.records: list[dict] = []
        self._index = 0

    def _execute(
        self,
        arguments: list[str],
        *,
        cwd: Path,
        environment: dict[str, str],
        label: str,
        timeout: int,
        input_text: str | None = None,
        capture_stdout: bool = False,
        sensitive_stdout: bool = False,
        secret_values: tuple[str, ...] = (),
        check: bool = True,
    ) -> tuple[int, str]:
        self._index += 1
        safe_label = re.sub(r"[^A-Za-z0-9_.-]+", "-", label).strip("-") or "command"
        log_name = f"{self._index:02d}-{safe_label}.log"
        log_path = self.evidence_logs / log_name
        quarantine_prefix = self.quarantine_logs / f"{self._index:02d}-{safe_label}"
        record = {
            "index": self._index,
            "label": label,
            "command": arguments,
            "workingDirectory": str(cwd),
            **clock_fields("start"),
            "status": "RUNNING",
        }
        process = subprocess.Popen(
            arguments,
            cwd=cwd,
            env=environment,
            stdin=subprocess.PIPE if input_text is not None else subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE if capture_stdout else subprocess.STDOUT,
            start_new_session=True,
        )
        timed_out = False
        try:
            try:
                stdin = input_text.encode("utf-8") if input_text is not None else None
                stdout, stderr = process.communicate(input=stdin, timeout=timeout)
            except subprocess.TimeoutExpired:
                timed_out = True
                os.killpg(process.pid, signal.SIGKILL)
                stdout, stderr = process.communicate()
        except BaseException:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            for stream in (process.stdin, process.stdout, process.stderr):
                if stream is not None:
                    stream.close()
            raise
        stdout_bytes = stdout or b""
        stderr_bytes = stderr or b""
        logged_bytes = stderr_bytes if capture_stdout else stdout_bytes
        safe, privacy_failure = _scan_log_bytes(logged_bytes, secret_values, self.forbidden_patterns)
        captured_stdout: str | None = None
        if capture_stdout:
            # Captured stdout is classified in memory before any audit record or
            # quarantine write; intentional sensitive compose data stays memory-only.
            if sensitive_stdout:
                try:
                    captured_stdout = stdout_bytes.decode("utf-8")
                except UnicodeDecodeError:
                    privacy_failure = privacy_failure or "NON_UTF8_OUTPUT"
            else:
                captured_stdout, captured_failure = _scan_log_bytes(
                    stdout_bytes,
                    secret_values,
                    self.forbidden_patterns,
                )
                privacy_failure = privacy_failure or captured_failure
        detected = privacy_failure is not None
        if not capture_stdout:
            quarantine_prefix.with_suffix(".stdout.bin").write_bytes(stdout_bytes)
            quarantine_prefix.with_suffix(".stderr.bin").write_bytes(stderr_bytes)
        elif not detected:
            if not sensitive_stdout:
                quarantine_prefix.with_suffix(".stdout.bin").write_bytes(stdout_bytes)
            quarantine_prefix.with_suffix(".stderr.bin").write_bytes(stderr_bytes)
        if detected:
            receipt = (
                "LOCAL_CHILD_LOG_REJECTED\n"
                f"reason={privacy_failure}\n"
                f"label={safe_label[:64]}\n"
            )
            log_path.write_text(receipt, encoding="utf-8")
        else:
            log_path.write_text(safe or "", encoding="utf-8")
        record.update({
            **clock_fields("finish"),
            "exitCode": process.returncode,
            "timedOut": timed_out,
            "credentialOutputDetected": detected,
            "status": "PASS" if process.returncode == 0 and not timed_out and not detected else "FAIL",
            "log": log_name,
        })
        if privacy_failure is not None:
            record["reason"] = "BLOCKED_SECRET_OUTPUT"
            record["privacyFailure"] = privacy_failure
        self.records.append(record)
        if detected:
            raise SecretOutputError(f"Credential-shaped output detected in {label}")
        if timed_out:
            raise CommandFailure(f"Command timed out: {label}")
        if check and process.returncode != 0:
            raise CommandFailure(f"Command failed ({process.returncode}): {label}")
        if capture_stdout:
            if captured_stdout is None:
                raise AssertionError("Captured stdout was not classified")
            return process.returncode, captured_stdout
        return process.returncode, ""

    def run(self, arguments: list[str], **kwargs) -> None:
        self._execute(arguments, **kwargs)

    def run_unchecked(self, arguments: list[str], **kwargs) -> int:
        code, _ = self._execute(arguments, check=False, **kwargs)
        return code

    def capture(self, arguments: list[str], **kwargs) -> str:
        _, value = self._execute(arguments, capture_stdout=True, **kwargs)
        return value

    def wait_http(self, url: str, attempts: int, interval_seconds: int, label: str) -> None:
        if re.fullmatch(r"http://127\.0\.0\.1:[0-9]{2,5}/(?:health/live)?", url) is None:
            raise AcceptanceError("Only an admitted loopback readiness URL may be polled")
        started = clock_fields("start")
        for _ in range(attempts):
            try:
                with urllib.request.urlopen(url, timeout=2) as response:
                    if 200 <= response.status < 400:
                        self.records.append({
                            "label": label,
                            "command": ["HTTP_GET", url],
                            **started,
                            **clock_fields("finish"),
                            "exitCode": 0,
                            "status": "PASS",
                        })
                        return
            except (urllib.error.URLError, TimeoutError):
                pass
            time.sleep(interval_seconds)
        self.records.append({
            "label": label,
            "command": ["HTTP_GET", url],
            **started,
            **clock_fields("finish"),
            "exitCode": 1,
            "status": "FAIL",
        })
        raise CommandFailure(f"Readiness timed out: {label}")


def _parse_json(path: Path, label: str) -> dict:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise AcceptanceError(f"{label} is unreadable") from error
    if not isinstance(value, dict):
        raise AcceptanceError(f"{label} must be a JSON object")
    return value


def validate_fixture_evidence(
    path: Path,
    *,
    suite: str | None,
    binding: SuiteBinding,
    expected_head: str,
) -> dict:
    value = _parse_json(path, path.name)
    expected_suite_id = "BROWSER-SUITE-FIXTURE-V1" if suite else "C01-E2E-INFRASTRUCTURE-V1"
    expected = {
        "schemaVersion": 1,
        "suiteId": expected_suite_id,
        "testedCommit": expected_head,
        "runId": binding.run_id,
        "composeProject": binding.compose_project,
    }
    if suite:
        expected["suite"] = suite
    for key, item in expected.items():
        if value.get(key) != item:
            raise AcceptanceError(f"{path.name} has a mismatched {key}")
    if suite:
        if not isinstance(value.get("requests"), int) or value["requests"] <= 0:
            raise AcceptanceError("Suite fixture evidence records no real requests")
        for before, after in (
            ("auditEventsBefore", "auditEventsAfter"),
            ("commandExecutionsBefore", "commandExecutionsAfter"),
        ):
            if not isinstance(value.get(before), int) or not isinstance(value.get(after), int):
                raise AcceptanceError("Suite fixture evidence counters are invalid")
    return value


class _BoundedJUnitTreeBuilder(ET.TreeBuilder):
    def __init__(self) -> None:
        super().__init__()
        self.elements = 0
        self.depth = 0

    def start(self, tag: str, attrs: dict[str, str]) -> ET.Element:
        self.elements += 1
        self.depth += 1
        if self.elements > MAX_JUNIT_ELEMENTS or self.depth > MAX_JUNIT_DEPTH:
            raise AcceptanceError("Browser JUnit exceeds the bounded XML shape")
        return super().start(tag, attrs)

    def end(self, tag: str) -> ET.Element:
        element = super().end(tag)
        self.depth -= 1
        return element


def _junit_counter(element: ET.Element, key: str) -> int:
    value = element.attrib.get(key)
    if (not isinstance(value, str) or len(value) > 6
            or re.fullmatch(r"0|[1-9][0-9]*", value) is None
            or int(value) > MAX_JUNIT_ELEMENTS):
        raise AcceptanceError(f"Browser JUnit has an invalid or missing {key} counter")
    return int(value)


def _junit_counts(element: ET.Element) -> dict[str, int]:
    return {key: _junit_counter(element, key) for key in ("tests", "failures", "errors", "skipped")}


def validate_junit(path: Path) -> dict:
    content = path.read_bytes()
    if not content or len(content) > MAX_JUNIT_BYTES:
        raise AcceptanceError("Browser JUnit is empty or exceeds the bounded size")
    if (content.startswith((b"\xef\xbb\xbf", b"\xff\xfe", b"\xfe\xff")) or b"\x00" in content):
        raise AcceptanceError("Browser JUnit must use strict UTF-8 without a BOM")
    if UNSAFE_XML.search(content):
        raise AcceptanceError("Browser JUnit contains an unsafe XML declaration")
    try:
        text = content.decode("utf-8")
    except UnicodeDecodeError as error:
        raise AcceptanceError("Browser JUnit must use strict UTF-8") from error
    declaration = re.match(r"\s*<\?xml\s+[^?]*encoding\s*=\s*['\"]([^'\"]+)['\"]", text, re.IGNORECASE)
    if declaration and declaration.group(1).lower() != "utf-8":
        raise AcceptanceError("Browser JUnit declares an unsupported encoding")
    try:
        builder = _BoundedJUnitTreeBuilder()
        parser = ET.XMLParser(target=builder)
        parser.feed(text)
        root = parser.close()
    except (ET.ParseError, AcceptanceError) as error:
        if isinstance(error, AcceptanceError):
            raise
        raise AcceptanceError("Browser JUnit is malformed") from error
    if root.tag == "testsuite":
        suites = [root]
        root_counts = None
    elif root.tag == "testsuites":
        if not list(root) or any(child.tag != "testsuite" for child in root):
            raise AcceptanceError("Browser JUnit has an unsupported root schema")
        suites = list(root)
        root_counts = _junit_counts(root)
    else:
        raise AcceptanceError("Browser JUnit has an unsupported root schema")

    cases: list[ET.Element] = []
    direct_outcomes: set[int] = set()
    aggregate = {"tests": 0, "failures": 0, "errors": 0, "skipped": 0}
    allowed_suite_children = {"testcase", "properties", "system-out", "system-err"}
    allowed_case_children = {"failure", "error", "skipped", "properties", "system-out", "system-err"}
    for suite in suites:
        if any(child.tag not in allowed_suite_children for child in suite):
            raise AcceptanceError("Browser JUnit has an unsupported suite schema")
        suite_cases = [child for child in suite if child.tag == "testcase"]
        suite_counts = _junit_counts(suite)
        actual = {"tests": len(suite_cases), "failures": 0, "errors": 0, "skipped": 0}
        for case in suite_cases:
            if any(child.tag not in allowed_case_children for child in case):
                raise AcceptanceError("Browser JUnit has an unsupported testcase schema")
            outcomes = [child.tag for child in case if child.tag in {"failure", "error", "skipped"}]
            direct_outcomes.update(id(child) for child in case if child.tag in {"failure", "error", "skipped"})
            if len(outcomes) > 1:
                raise AcceptanceError("Browser JUnit testcase has contradictory outcomes")
            if outcomes:
                key = {"failure": "failures", "error": "errors", "skipped": "skipped"}[outcomes[0]]
                actual[key] += 1
        if suite_counts != actual:
            raise AcceptanceError("Browser JUnit suite counters contradict testcase outcomes")
        for key in aggregate:
            aggregate[key] += actual[key]
        cases.extend(suite_cases)
    if len(cases) != len(list(root.iter("testcase"))):
        raise AcceptanceError("Browser JUnit has a testcase outside the accepted suite schema")
    if any(id(element) not in direct_outcomes for element in root.iter()
           if element.tag in {"failure", "error", "skipped"}):
        raise AcceptanceError("Browser JUnit has a suite-level or nested outcome node")
    if root_counts is not None and root_counts != aggregate:
        raise AcceptanceError("Browser JUnit root counters contradict suite totals")
    if not cases:
        raise AcceptanceError("Browser JUnit contains no test cases")
    failed = aggregate["failures"]
    errors = aggregate["errors"]
    skipped = aggregate["skipped"]
    if failed or errors or skipped:
        raise AcceptanceError("Browser JUnit contains failure, error or skipped cases")
    identities = sorted(
        json.dumps(
            {key: case.attrib.get(key, "") for key in ("classname", "name", "file")},
            sort_keys=True,
            separators=(",", ":"),
        )
        for case in cases
    )
    if len(set(identities)) != len(identities):
        raise AcceptanceError("Browser JUnit contains duplicate/retried test cases")
    encoded = ("\n".join(identities) + "\n").encode("utf-8")
    return {
        "total": len(cases),
        "passed": len(cases),
        "failed": 0,
        "errors": 0,
        "skipped": 0,
        "retries": 0,
        "testcaseManifestSha256": hashlib.sha256(encoded).hexdigest(),
    }


def _regular_file(root: Path, relative: str) -> Path:
    parts = relative.split("/")
    if not parts or any(SAFE_SEGMENT.fullmatch(part) is None or part in {".", ".."} for part in parts):
        raise AcceptanceError(f"Unsafe browser artifact path: {relative}")
    current = root
    for part in parts:
        current = current / part
        try:
            metadata = current.lstat()
        except OSError as error:
            raise AcceptanceError(f"Mandatory browser artifact is missing: {relative}") from error
        if stat.S_ISLNK(metadata.st_mode):
            raise AcceptanceError(f"Browser artifact path contains a symlink: {relative}")
    if not stat.S_ISREG(current.stat().st_mode) or not current.resolve().is_relative_to(root.resolve()):
        raise AcceptanceError(f"Browser artifact is not a confined regular file: {relative}")
    return current


def validate_artifacts(
    root: Path,
    suite: str,
    binding: SuiteBinding,
    expected_head: str,
) -> tuple[list[dict], dict]:
    entries = []
    paths = [item.format(suite=suite) for item in MANDATORY_ARTIFACTS]
    for relative in paths:
        path = _regular_file(root, relative)
        content = path.read_bytes()
        entries.append({"path": relative, "bytes": len(content), "sha256": hashlib.sha256(content).hexdigest()})
    validate_fixture_evidence(
        root / "browser-fixture-evidence.json",
        suite=None,
        binding=binding,
        expected_head=expected_head,
    )
    validate_fixture_evidence(
        root / "browser-suite-fixture-evidence.json",
        suite=suite,
        binding=binding,
        expected_head=expected_head,
    )
    seed = _parse_json(root / "browser-suite-seed.json", "browser-suite-seed.json")
    if not seed:
        raise AcceptanceError("Browser suite seed is empty")
    junit = validate_junit(root / f"browser-{suite}-junit.xml")
    return entries, junit


def publish_artifacts(
    source: Path,
    destination: Path,
    *,
    suite: str,
    binding: SuiteBinding,
    expected_head: str,
    contract,
    compose_model: dict,
) -> tuple[list[dict], dict]:
    """Privacy must pass before any runtime artifact enters closed evidence."""
    contract.validate_privacy(source, suite, binding.secret_values(), compose_model)
    entries, junit = validate_artifacts(source, suite, binding, expected_head)
    for entry in entries:
        source_path = _regular_file(source, entry["path"])
        destination_path = destination / entry["path"]
        destination_path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source_path, destination_path)
        if hashlib.sha256(destination_path.read_bytes()).hexdigest() != entry["sha256"]:
            raise AcceptanceError("Browser artifact hash changed during local transport")
    return entries, junit


def _compose_model(
    runner: CommandRunner,
    root: Path,
    binding: SuiteBinding,
    environment: dict[str, str],
) -> dict:
    raw = runner.capture(
        compose_arguments(root, binding) + ["config", "--format", "json"],
        cwd=root,
        environment=environment,
        label=f"{binding.suite}-compose-model",
        timeout=120,
        sensitive_stdout=True,
        secret_values=binding.secret_values(),
    )
    try:
        value = json.loads(raw)
    except json.JSONDecodeError as error:
        raise AcceptanceError("Disposable compose model is not JSON") from error
    if not isinstance(value, dict):
        raise AcceptanceError("Disposable compose model must be an object")
    return value


def _build_environment(environment: dict[str, str]) -> dict[str, str]:
    return {
        key: value
        for key, value in environment.items()
        if not key.startswith("GOYMEN_ERP_C01_E2E_")
        and not key.startswith("C01E2e__")
        and not key.startswith("BROWSER_E2E_")
    }


def _run_preflight(root: Path, runner: CommandRunner) -> dict:
    environment = clean_environment()
    for directory in ("tests/dockerhub_mirror_contract", "tests/browser_acceptance_contract"):
        runner.run(
            [
                "python3", "-I", "-S", "-B", "-m", "unittest", "discover",
                "-s", directory, "-p", "test_*.py", "-v",
            ],
            cwd=root,
            environment=environment,
            label="contract-" + Path(directory).name,
            timeout=300,
        )
    mirrors = runner.capture(
        ["docker", "info", "--format", "{{json .RegistryConfig.Mirrors}}"],
        cwd=root,
        environment=environment,
        label="docker-mirror-read",
        timeout=60,
    )
    runner.run(
        ["python3", "-I", "-S", "-B", "tools/configure_dockerhub_mirror.py", "verify"],
        cwd=root,
        environment=environment,
        label="docker-mirror-verify",
        timeout=60,
        input_text=mirrors,
    )
    dotnet = runner.capture(
        ["dotnet", "--version"], cwd=root, environment=environment, label="dotnet-version", timeout=30
    ).strip()
    node = runner.capture(
        ["node", "--version"], cwd=root, environment=environment, label="node-version", timeout=30
    ).strip()
    compose = runner.capture(
        ["docker", "compose", "version", "--short"],
        cwd=root,
        environment=environment,
        label="docker-compose-version",
        timeout=30,
    ).strip()
    if dotnet != "10.0.303" or re.fullmatch(r"v24\.\d+\.\d+", node) is None or not compose:
        raise AcceptanceError("Local browser runtime versions do not match the admitted toolchain")
    return {"dotnet": dotnet, "node": node, "dockerCompose": compose}


def _append_error(errors: list[str], error: BaseException) -> None:
    value, _ = _safe_output(f"{type(error).__name__}: {error}", ())
    errors.append(value.strip())


def _run_suite(
    root: Path,
    evidence: Path,
    expected_head: str,
    binding: SuiteBinding,
    runner: CommandRunner,
    contract,
    admission_check: Callable[[], None],
) -> dict:
    suite = binding.suite
    result: dict = {
        **clock_fields("start"),
        "suite": suite,
        "binding": binding.public(),
        "contract": SUITE_CONTRACTS[suite],
        "status": "RUNNING",
        "exitCode": 2,
    }
    errors: list[str] = []
    compose_model: dict | None = None
    privacy_passed = False
    artifact_entries: list[dict] = []
    junit: dict | None = None
    cleanup = {"downExitCode": None, "containers": None, "networks": None, "volumes": None}
    suite_destination = evidence / "suites" / suite / "artifacts"
    with tempfile.TemporaryDirectory(prefix=f"goymen-{PROFILE}-{suite}-") as temporary:
        temporary_root = Path(temporary)
        artifacts = temporary_root / "artifacts"
        (artifacts / "container-logs").mkdir(parents=True)
        environment = suite_environment(binding, expected_head, artifacts)
        compose = compose_arguments(root, binding)
        try:
            admission_check()
            compose_model = _compose_model(runner, root, binding, environment)
            contract.validate_suite_compose(compose_model, suite, expected_head, binding.compose_project)
            api_environment = contract.api_environment(compose_model, suite, expected_head, binding.compose_project)
            api_environment_path = temporary_root / "api-environment.json"
            write_json(api_environment_path, api_environment)
            api_environment_path.chmod(0o600)

            for project, label in (
                ("tests/GoymenERP.C01E2eFixtureTool/GoymenERP.C01E2eFixtureTool.csproj", "canonical-fixture"),
                ("tests/GoymenERP.BrowserSuiteFixtureTool/GoymenERP.BrowserSuiteFixtureTool.csproj", "suite-fixture"),
            ):
                runner.run(
                    ["dotnet", "restore", project, "-noAutoResponse", "--configfile", "NuGet.Config"],
                    cwd=root,
                    environment=environment,
                    label=f"{suite}-restore-{label}",
                    timeout=600,
                    secret_values=binding.secret_values(),
                )

            runner.run(
                compose + ["up", "-d", "--build", "sqlserver", "api"],
                cwd=root,
                environment=environment,
                label=f"{suite}-start-sql-api",
                timeout=900,
                secret_values=binding.secret_values(),
            )
            runner.wait_http(f"http://127.0.0.1:{PORTS['api']}/health/live", 120, 2, f"{suite}-api-live")
            admission_check()

            runner.run(
                [
                    "dotnet", "run", "--project",
                    "tests/GoymenERP.C01E2eFixtureTool/GoymenERP.C01E2eFixtureTool.csproj",
                    "--configuration", "Release", "--no-restore", "--", "materialize",
                ],
                cwd=root,
                environment=environment,
                label=f"{suite}-canonical-fixture",
                timeout=600,
                secret_values=binding.secret_values(),
            )
            runner.run(
                [
                    "dotnet", "build",
                    "tests/GoymenERP.BrowserSuiteFixtureTool/GoymenERP.BrowserSuiteFixtureTool.csproj",
                    "-noAutoResponse", "--configuration", "Release", "--no-restore",
                ],
                cwd=root,
                environment=_build_environment(environment),
                label=f"{suite}-build-suite-fixture",
                timeout=600,
                secret_values=binding.secret_values(),
            )
            fixture_environment = dict(environment)
            fixture_environment.update({
                "BROWSER_E2E_API_ENVIRONMENT": str(api_environment_path),
                "BROWSER_E2E_SUITE_FIXTURE_EVIDENCE": str(artifacts / "browser-suite-fixture-evidence.json"),
                "BROWSER_E2E_SUITE_SEED_FILE": str(artifacts / "browser-suite-seed.json"),
            })
            runner.run(
                [
                    "dotnet", "run", "--project",
                    "tests/GoymenERP.BrowserSuiteFixtureTool/GoymenERP.BrowserSuiteFixtureTool.csproj",
                    "--configuration", "Release", "--no-restore", "--no-build", "--",
                    "materialize", "--suite", suite,
                ],
                cwd=root,
                environment=fixture_environment,
                label=f"{suite}-suite-fixture",
                timeout=600,
                secret_values=binding.secret_values(),
            )
            seed = _parse_json(artifacts / "browser-suite-seed.json", "browser-suite-seed.json")
            export = contract.seed_export(suite, seed)
            key, separator, value = export.partition("=")
            if separator != "=" or key != f"BROWSER_E2E_{suite.upper()}_SEED" or not value:
                raise AcceptanceError("Suite seed export contract is invalid")
            environment[key] = value

            runner.run(
                compose + ["up", "-d", "--build", "web-authorized", "web-unauthorized"],
                cwd=root,
                environment=environment,
                label=f"{suite}-start-web",
                timeout=900,
                secret_values=binding.secret_values(),
            )
            for label, port in (
                ("authorized", PORTS["authorizedWeb"]),
                ("unauthorized", PORTS["unauthorizedWeb"]),
            ):
                runner.wait_http(f"http://127.0.0.1:{port}/", 90, 2, f"{suite}-{label}-web")

            e2e = root / "tests/e2e"
            for arguments, label, timeout in (
                (["npm", "ci"], "npm-ci", 600),
                (["npm", "audit", "--audit-level=high"], "npm-audit", 300),
                (["./node_modules/.bin/playwright", "install", "--with-deps", "chromium"], "chromium-install", 900),
                (["npm", "run", SUITE_CONTRACTS[suite]["script"]], "browser-suite", 1200),
            ):
                runner.run(
                    arguments,
                    cwd=e2e,
                    environment=environment,
                    label=f"{suite}-{label}",
                    timeout=timeout,
                    secret_values=binding.secret_values(),
                )
            junit = validate_junit(artifacts / f"browser-{suite}-junit.xml")
        except Exception as error:  # cleanup and privacy are mandatory even after a child failure
            _append_error(errors, error)
        finally:
            try:
                _, compose_ps = runner._execute(
                    compose + ["ps", "--all"],
                    cwd=root,
                    environment=environment,
                    label=f"{suite}-compose-ps",
                    timeout=60,
                    capture_stdout=True,
                    secret_values=binding.secret_values(),
                    check=False,
                )
                (artifacts / "container-logs/compose-ps.txt").write_text(compose_ps, encoding="utf-8")
            except Exception as error:
                _append_error(errors, error)

            try:
                cleanup["downExitCode"] = runner.run_unchecked(
                    compose + ["down", "-v", "--remove-orphans"],
                    cwd=root,
                    environment=environment,
                    label=f"{suite}-cleanup",
                    timeout=300,
                    secret_values=binding.secret_values(),
                )
                checks = (
                    ("containers", [
                        "docker", "ps", "-aq", "--filter",
                        f"label=com.docker.compose.project={binding.compose_project}",
                    ]),
                    ("networks", [
                        "docker", "network", "ls", "-q", "--filter",
                        f"label=com.docker.compose.project={binding.compose_project}",
                    ]),
                    ("volumes", [
                        "docker", "volume", "ls", "-q", "--filter",
                        f"label=com.docker.compose.project={binding.compose_project}",
                    ]),
                )
                for name, arguments in checks:
                    output = runner.capture(
                        arguments,
                        cwd=root,
                        environment=environment,
                        label=f"{suite}-cleanup-{name}",
                        timeout=60,
                        secret_values=binding.secret_values(),
                    )
                    cleanup[name] = len([line for line in output.splitlines() if line.strip()])
                if cleanup["downExitCode"] != 0 or any(
                    cleanup[name] != 0 for name in ("containers", "networks", "volumes")
                ):
                    raise AcceptanceError("Disposable browser cleanup did not reach zero")
            except Exception as error:
                _append_error(errors, error)

            if compose_model is not None:
                try:
                    artifact_entries, published_junit = publish_artifacts(
                        artifacts,
                        suite_destination,
                        suite=suite,
                        binding=binding,
                        expected_head=expected_head,
                        contract=contract,
                        compose_model=compose_model or {},
                    )
                    privacy_passed = True
                    junit = junit or published_junit
                except Exception as error:
                    _append_error(errors, error)

    result.update({
        **clock_fields("finish"),
        "privacy": {"status": "PASS" if privacy_passed else "FAIL"},
        "cleanup": cleanup,
        "artifacts": artifact_entries,
        "junit": junit,
        "errors": errors,
        "exitCode": 0 if not errors and junit is not None else 1,
        "status": "PASS" if not errors and junit is not None else "FAIL",
    })
    if any("SecretOutputError" in error for error in errors):
        result.update({"status": "BLOCKED_SECRET_OUTPUT", "exitCode": 2})
    write_json(evidence / "suites" / suite / "result.json", result)
    return result


def run_acceptance(
    root: Path,
    evidence: Path,
    expected_head: str,
    admission_check: Callable[[], None],
) -> dict:
    """Run the admitted fixed pair and return one local_check step result."""
    root = root.resolve()
    evidence = evidence.resolve()
    if evidence == root or evidence.is_relative_to(root):
        raise AcceptanceError("Browser evidence must be outside the source checkout")
    evidence.mkdir(mode=0o700)
    started = clock_fields("start")
    initial = source_binding(root, expected_head)
    contract = load_browser_contract(root, expected_head)
    source_contract = validate_source_contract(root, contract)
    log_quarantine = tempfile.TemporaryDirectory(prefix=f"goymen-{PROFILE}-logs-")
    runner = CommandRunner(
        Path(log_quarantine.name),
        evidence / "logs",
        _canonical_forbidden_patterns(contract),
    )
    result: dict = {
        "schemaVersion": 1,
        "suiteId": "LOCAL-P032-U02-R14-V1",
        "profile": PROFILE,
        "plannedSuites": list(SUITES),
        "source": initial,
        "sourceContract": source_contract,
        **started,
        "status": "RUNNING",
        "githubJobStarted": False,
        "githubStorageUsed": False,
        "suites": [],
    }
    code = 2
    try:
        admission_check()
        result["runtime"] = _run_preflight(root, runner)
        bindings = [new_binding(suite, index) for index, suite in enumerate(SUITES, start=1)]
        if len({binding.compose_project for binding in bindings}) != len(SUITES):
            raise AcceptanceError("U02 and R14 must use separate disposable bindings")
        for binding in bindings:
            admission_check()
            suite_result = _run_suite(
                root,
                evidence,
                expected_head,
                binding,
                runner,
                contract,
                admission_check,
            )
            result["suites"].append(suite_result)
            if suite_result["exitCode"] != 0:
                break
        code = 0 if len(result["suites"]) == len(SUITES) and all(
            item["status"] == "PASS" for item in result["suites"]
        ) else 1
        blocked = next(
            (item["status"] for item in result["suites"] if item["status"].startswith("BLOCKED")),
            None,
        )
        result["status"] = blocked or ("PASS" if code == 0 else "FAIL")
    except SecretOutputError as error:
        result.update({"status": "BLOCKED_SECRET_OUTPUT", "reason": str(error)})
        code = 2
    except (AcceptanceError, OSError, subprocess.SubprocessError) as error:
        result.update({"status": "BLOCKED", "reason": f"{type(error).__name__}: {error}"})
        code = 2
    finally:
        final = source_snapshot(root)
        result["sourceFinish"] = final
        if final != initial or final["commit"] != expected_head or final["dirty"]:
            result.update({"status": "BLOCKED_SOURCE_CHANGED", "reason": "Source changed during browser acceptance"})
            code = 2
        total = sum((item.get("junit") or {}).get("total", 0) for item in result["suites"])
        result.update({
            **clock_fields("finish"),
            "exitCode": code,
            "testCounts": {
                "total": total,
                "passed": total if code == 0 else None,
                "failed": 0 if code == 0 else None,
                "errors": 0 if code == 0 else None,
                "skipped": 0 if code == 0 else None,
            },
        })
        write_json(evidence / "commands.json", runner.records)
        write_json(evidence / "result.json", result)
        log_quarantine.cleanup()
    return {
        "name": "P03.2 fixed U02/R14 disposable browser acceptance",
        "command": "source-owned p032-u02-r14 adapter (u02 then r14)",
        "workingDirectory": str(root),
        **started,
        "finishUtc": result["finishUtc"],
        "finishMonotonicNs": result["finishMonotonicNs"],
        "status": result["status"],
        "exitCode": code,
        "counts": result["testCounts"],
        "credentialOutputDetected": result["status"] == "BLOCKED_SECRET_OUTPUT",
        "evidence": evidence.name,
    }
