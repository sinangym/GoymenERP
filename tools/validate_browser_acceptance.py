#!/usr/bin/env python3
"""Contract of the selectable browser workflow (.github/workflows/browser-acceptance.yml).

The workflow reuses the C-01 disposable stack. Its artifacts are NOT C-01's, so they get their own
upload allowlist here; the privacy rules themselves are C-01's, loaded from
tools/validate_c01_e2e_contract.py unchanged (by path: the workflow runs tools with `python3 -I`,
which keeps the script directory off sys.path).

Stage 2: each suite runs under one capability profile, each with its own compose file:
- "c01-list-only": the canonical C-01 stack, validated by the canonical validator unchanged;
- "cari-staged": the same disposable envelope plus C01E2e__Profile=cari-list-detail;
- "normal": the same stack without the envelope, so the API serves its normal shell authority.
A suite compose model is mapped back to the canonical shape by exactly its profile's documented
difference and then checked by the canonical validator, so every canonical rule (Testing,
loopback-only ports, per-run disposable database, image digest, identities, labels) applies to
every profile; on top of that the API environment key set must be exact.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import pathlib
import re
import sys

_C01_PATH = pathlib.Path(__file__).resolve().with_name("validate_c01_e2e_contract.py")
_spec = importlib.util.spec_from_file_location("validate_c01_e2e_contract", _C01_PATH)
assert _spec is not None and _spec.loader is not None
c01 = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = c01
_spec.loader.exec_module(c01)

ContractError = c01.ContractError
require = c01.require

SUITES = ("smoke", "c03", "org03", "u02", "s03")
READ_ONLY_SUITES = ("smoke", "c03", "s03")
_SUITE_PATTERN = re.compile(r"^[a-z0-9]+$")

PROFILES = ("c01-list-only", "cari-staged", "normal")
SUITE_PROFILES = {
    "smoke": "c01-list-only",
    "c03": "cari-staged",
    "org03": "normal",
    "u02": "normal",
    # S-03 v1 (#321): fail-closed evidence on the normal host (staged Sales screens, no grant).
    "s03": "normal",
}
PROFILE_COMPOSE_FILES = {
    "c01-list-only": "infra/docker-compose.c01-e2e.yml",
    "cari-staged": "infra/docker-compose.browser-cari-staged.yml",
    "normal": "infra/docker-compose.browser-normal.yml",
}
PROFILE_SUITE_LABELS = {
    "c01-list-only": "C01-REAL-BROWSER-E2E-V1",
    "cari-staged": "BROWSER-SUITE-CARI-STAGED-V1",
    "normal": "BROWSER-SUITE-NORMAL-V1",
}
CANONICAL_SUITE_LABEL = PROFILE_SUITE_LABELS["c01-list-only"]
CARI_STAGED_PROFILE_KEY = "C01E2e__Profile"
CARI_STAGED_PROFILE_VALUE = "cari-list-detail"
# The disposable C-01 capability envelope, as the canonical compose file sets it on the API.
ENVELOPE_KEYS = (
    "C01E2e__EnableListCapability",
    "GOYMEN_ERP_C01_E2E_DISPOSABLE",
    "GOYMEN_ERP_C01_E2E_TESTED_COMMIT",
    "GOYMEN_ERP_C01_E2E_RUN_ID",
    "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT",
)
CANONICAL_API_ENVIRONMENT_KEYS = frozenset({
    "ASPNETCORE_ENVIRONMENT",
    "DB_PROVIDER",
    "DB_CONNECTION_STRING",
    "Counterparty__CursorSigningKey",
    *ENVELOPE_KEYS,
    "BootstrapIdentity__HeaderName",
    "BootstrapIdentity__Users__0__UserId",
    "BootstrapIdentity__Users__0__DisplayName",
    "BootstrapIdentity__Users__1__UserId",
    "BootstrapIdentity__Users__1__DisplayName",
})


# Stage 2 (a2): the only API environment keys a suite may add on top of its profile, through its own
# compose override file (infra/browser-suites/<suite>.override.yml). Closed lists: an unlisted key, or a
# listed key with a value outside its shape, fails; a suite without a list may not have an override.
# The VALUES are the suite owner's (their seed's deterministic identities, their synthetic actors).
SUITE_OVERRIDE_DIRECTORY = "infra/browser-suites"
_GUID = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")
_ACTOR_KEY = re.compile(r"^BootstrapIdentity__Users__([0-9]+)__(UserId|DisplayName)$")
MAX_SUITE_ACTORS = 8
SUITE_API_EXTENSIONS = {
    # U-02: the Product runtime's issuer binding, matching the suite seed's local user and
    # external identity (ÜRÜN, #319 P3).
    "u02": {"issuer": True, "actors": False},
    # ORG-03: extra synthetic development actors after the canonical two (ORG, #319 P3-1).
    "org03": {"issuer": False, "actors": True},
}
ISSUER_KEYS = ("ProductRuntime__IssuerLocalUserId", "ProductRuntime__IssuerExternalIdentityId")


def suite_override_file(suite: str) -> str | None:
    """The suite's compose override file, if it has one; only suites with a closed extension list may."""
    profile_of(suite)
    relative = f"{SUITE_OVERRIDE_DIRECTORY}/{suite}.override.yml"
    exists = (c01.REPOSITORY_ROOT / relative).is_file()
    require(not exists or suite in SUITE_API_EXTENSIONS,
            f"suite {suite} has an override file but no closed extension list")
    return relative if exists else None


def validate_suite_extensions(suite: str, extra: dict) -> None:
    """The keys a suite adds are exactly allowed by its closed list, with values of the right shape."""
    if not extra:
        return
    allowed = SUITE_API_EXTENSIONS.get(suite)
    require(allowed is not None, f"suite {suite} may not extend the API environment: {sorted(extra)}")
    actors: dict[int, dict[str, str]] = {}
    for key, value in extra.items():
        require(isinstance(value, str), f"suite extension {key} must be a string")
        if key in ISSUER_KEYS:
            require(allowed["issuer"], f"suite {suite} may not set {key}")
            require(_GUID.fullmatch(value) is not None, f"{key} must be a lowercase non-nil GUID")
            continue
        match = _ACTOR_KEY.fullmatch(key)
        require(match is not None, f"suite {suite} extension key is not allowed: {key}")
        require(allowed["actors"], f"suite {suite} may not add development actors")
        actors.setdefault(int(match.group(1)), {})[match.group(2)] = value
    if allowed["issuer"]:
        present = [key for key in ISSUER_KEYS if key in extra]
        require(len(present) in (0, len(ISSUER_KEYS)), "the Product issuer binding needs both keys or neither")
        if present:
            require(extra[ISSUER_KEYS[0]] != extra[ISSUER_KEYS[1]], "the issuer's local and external ids must differ")
    if actors:
        indices = sorted(actors)
        require(indices == list(range(2, 2 + len(indices))),
                "suite actors must continue the canonical two contiguously from index 2")
        require(len(indices) <= MAX_SUITE_ACTORS, f"at most {MAX_SUITE_ACTORS} suite actors")
        ids = set()
        for index in indices:
            actor = actors[index]
            require(set(actor) == {"UserId", "DisplayName"}, f"suite actor {index} needs UserId and DisplayName")
            require(re.fullmatch(rf"{suite}-[a-z0-9-]{{1,48}}", actor["UserId"]) is not None,
                    f"suite actor {index} id must be '{suite}-' followed by lowercase letters, digits or '-'")
            require(actor["DisplayName"].startswith("SENTETIK "), f"suite actor {index} must be labelled SENTETIK")
            require(actor["UserId"] not in ids, f"suite actor id repeated: {actor['UserId']}")
            ids.add(actor["UserId"])


def profile_of(suite: str) -> str:
    require(isinstance(suite, str) and _SUITE_PATTERN.fullmatch(suite) is not None, "suite name is not safe")
    require(suite in SUITES, f"unknown browser suite: {suite}")
    profile = SUITE_PROFILES.get(suite)
    require(profile in PROFILES, f"browser suite {suite} has no known capability profile")
    return profile


def _relabel(value: object, source: str, target: str) -> object:
    if isinstance(value, dict):
        return {key: _relabel(item, source, target) for key, item in value.items()}
    if isinstance(value, list):
        return [_relabel(item, source, target) for item in value]
    if value == source:
        return target
    return value


def validate_suite_compose(model: dict, suite: str, expected_commit: str, expected_project: str) -> None:
    """The suite's compose model is its profile's exact difference from the canonical C-01 model."""
    profile = profile_of(suite)
    require(isinstance(model, dict), "compose model must be an object")
    label = PROFILE_SUITE_LABELS[profile]
    services = model.get("services")
    require(isinstance(services, dict) and "api" in services, "compose model has no api service")
    for name, service in services.items():
        require(isinstance(service, dict), f"{name} service must be an object")
        require(service.get("labels", {}).get("com.goymen.e2e.suite") == label, f"{name} suite label is not the {profile} label")
    environment = services["api"].get("environment")
    require(isinstance(environment, dict), "API environment must be an object")
    canonical_environment = dict(environment)
    if profile == "c01-list-only":
        expected_keys = CANONICAL_API_ENVIRONMENT_KEYS
    elif profile == "cari-staged":
        require(environment.get(CARI_STAGED_PROFILE_KEY) == CARI_STAGED_PROFILE_VALUE,
                "cari-staged API must carry the cari-list-detail profile")
        expected_keys = CANONICAL_API_ENVIRONMENT_KEYS | {CARI_STAGED_PROFILE_KEY}
        del canonical_environment[CARI_STAGED_PROFILE_KEY]
    else:
        for key in environment:
            require(not key.startswith("C01E2e__") and not key.startswith("GOYMEN_ERP_C01_E2E_"),
                    f"normal profile API must not carry the disposable capability envelope ({key})")
        expected_keys = CANONICAL_API_ENVIRONMENT_KEYS - set(ENVELOPE_KEYS)
        # Only to let the canonical rules run: the envelope values they expect, never sent to the API.
        canonical_environment.update({
            "C01E2e__EnableListCapability": "true",
            "GOYMEN_ERP_C01_E2E_DISPOSABLE": "CONFIRMED",
            "GOYMEN_ERP_C01_E2E_TESTED_COMMIT": expected_commit,
            "GOYMEN_ERP_C01_E2E_RUN_ID": expected_project.split("-")[-2] if "-" in expected_project else "",
            "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT": expected_project,
        })
    extra = {key: environment[key] for key in set(environment) - set(expected_keys)}
    missing = sorted(set(expected_keys) - set(environment))
    unlisted = sorted(key for key in extra
                      if key not in ISSUER_KEYS and _ACTOR_KEY.fullmatch(key) is None)
    require(not missing and not unlisted,
            f"{profile} API environment key set is not exact: missing {missing}, extra {unlisted}")
    validate_suite_extensions(suite, extra)
    for key in extra:
        canonical_environment.pop(key, None)
    canonical = _relabel(model, label, CANONICAL_SUITE_LABEL)
    canonical["services"]["api"]["environment"] = canonical_environment
    c01.validate_compose(canonical, expected_commit, expected_project)


def upload_allowlist(suite: str) -> tuple[str, ...]:
    """The only files the workflow may upload for one suite, relative to the artifact root."""
    require(isinstance(suite, str) and _SUITE_PATTERN.fullmatch(suite) is not None, "suite name is not safe")
    require(suite in SUITES, f"unknown browser suite: {suite}")
    return (
        f"browser-{suite}-junit.xml",
        "browser-fixture-evidence.json",
        "browser-post-browser-sql-evidence.json",
        "container-logs/compose-ps.txt",
    )


def upload_paths(root: str | pathlib.Path, suite: str) -> list[pathlib.Path]:
    artifact_root = pathlib.Path(root).resolve()
    require(artifact_root.is_dir(), "artifact root is missing")
    selected: list[pathlib.Path] = []
    for relative in upload_allowlist(suite):
        candidate = artifact_root / relative
        if not candidate.exists():
            continue
        require(candidate.is_file(), f"artifact allowlist entry is not a file: {relative}")
        require(not candidate.is_symlink(), f"artifact allowlist entry is a symlink: {relative}")
        require(candidate.resolve().is_relative_to(artifact_root), f"artifact allowlist entry escapes root: {relative}")
        selected.append(candidate)
    require(selected, "artifact upload allowlist selected no files")
    return selected


def validate_privacy(root: str | pathlib.Path, suite: str, forbidden_values: list[str] | tuple[str, ...] = ()) -> None:
    """Every file that would be uploaded is free of C-01's forbidden patterns and of the run's secrets."""
    artifact_root = pathlib.Path(root).resolve()
    selected = upload_paths(artifact_root, suite)
    needles: list[bytes] = []
    for index, value in enumerate(forbidden_values):
        require(isinstance(value, str) and len(value.encode("utf-8")) >= 8,
                f"runtime forbidden value {index} is missing or too short")
        needles.append(value.encode("utf-8"))
    for path in selected:
        content = path.read_bytes()
        relative = path.relative_to(artifact_root).as_posix()
        for label, pattern in c01.ARTIFACT_FORBIDDEN_PATTERNS:
            require(pattern.search(content) is None, f"artifact privacy violation ({label}): {relative}")
        for index, needle in enumerate(needles):
            require(needle not in content, f"artifact privacy violation (runtime secret {index}): {relative}")


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser()
    commands = result.add_subparsers(dest="command", required=True)
    privacy = commands.add_parser("privacy")
    privacy.add_argument("--root", required=True)
    privacy.add_argument("--suite", required=True)
    privacy.add_argument("--forbidden-value", action="append", default=[])
    profile = commands.add_parser("profile")
    profile.add_argument("--suite", required=True)
    compose = commands.add_parser("compose")
    compose.add_argument("--suite", required=True)
    compose.add_argument("--expected-commit", required=True)
    compose.add_argument("--expected-project", required=True)
    return result


def main(argv: list[str] | None = None) -> int:
    arguments = parser().parse_args(argv)
    try:
        if arguments.command == "privacy":
            validate_privacy(arguments.root, arguments.suite, arguments.forbidden_value)
        elif arguments.command == "profile":
            selected = profile_of(arguments.suite)
            # GITHUB_OUTPUT lines for the workflow.
            print(f"profile={selected}")
            print(f"compose_file={PROFILE_COMPOSE_FILES[selected]}")
            print(f"override_file={suite_override_file(arguments.suite) or ''}")
            return 0
        elif arguments.command == "compose":
            try:
                model = json.load(sys.stdin)
            except json.JSONDecodeError as error:
                raise ContractError("compose model is not JSON") from error
            validate_suite_compose(model, arguments.suite, arguments.expected_commit, arguments.expected_project)
    except ContractError as error:
        print(f"Browser acceptance contract validation failed: {error}", file=sys.stderr)
        return 1
    print("Browser acceptance contract validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
