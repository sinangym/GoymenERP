#!/usr/bin/env python3
"""Configure and verify the CI runner's Docker Hub cache mirror."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import stat
import sys
import tempfile
from typing import Any, Sequence


DOCKER_HUB_MIRROR = "https://mirror.gcr.io"


class MirrorConfigurationError(ValueError):
    """Raised when an existing Docker daemon configuration is unsafe to edit."""


def _normalized_mirror(value: str) -> str:
    return value.rstrip("/")


def read_daemon_configuration(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}

    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise MirrorConfigurationError(
            f"Cannot read a valid Docker daemon JSON object from {path}."
        ) from error

    if not isinstance(value, dict):
        raise MirrorConfigurationError(
            f"Docker daemon configuration at {path} must be a JSON object."
        )
    return value


def with_docker_hub_mirror(configuration: dict[str, Any]) -> tuple[dict[str, Any], bool]:
    existing = configuration.get("registry-mirrors", [])
    if not isinstance(existing, list) or any(not isinstance(item, str) for item in existing):
        raise MirrorConfigurationError("registry-mirrors must be an array of strings.")

    if any(_normalized_mirror(item) == DOCKER_HUB_MIRROR for item in existing):
        return dict(configuration), False

    updated = dict(configuration)
    updated["registry-mirrors"] = [DOCKER_HUB_MIRROR, *existing]
    return updated, True


def write_configuration_atomically(path: Path, configuration: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    mode = stat.S_IMODE(path.stat().st_mode) if path.exists() else 0o644
    serialized = json.dumps(configuration, indent=2, sort_keys=True) + "\n"

    temporary_name: str | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w",
            encoding="utf-8",
            newline="\n",
            dir=path.parent,
            prefix=f".{path.name}.",
            suffix=".tmp",
            delete=False,
        ) as temporary:
            temporary_name = temporary.name
            temporary.write(serialized)
            temporary.flush()
            os.fsync(temporary.fileno())
        os.chmod(temporary_name, mode)
        os.replace(temporary_name, path)
        temporary_name = None
    finally:
        if temporary_name is not None:
            Path(temporary_name).unlink(missing_ok=True)


def configure(path: Path) -> bool:
    configuration = read_daemon_configuration(path)
    updated, changed = with_docker_hub_mirror(configuration)
    if changed:
        write_configuration_atomically(path, updated)
    return changed


def verify_docker_info(payload: str) -> None:
    try:
        mirrors = json.loads(payload)
    except json.JSONDecodeError as error:
        raise MirrorConfigurationError("Docker info mirror output must be valid JSON.") from error

    if not isinstance(mirrors, list) or any(not isinstance(item, str) for item in mirrors):
        raise MirrorConfigurationError("Docker info mirror output must be an array of strings.")
    if not any(_normalized_mirror(item) == DOCKER_HUB_MIRROR for item in mirrors):
        raise MirrorConfigurationError("The Docker daemon did not activate mirror.gcr.io.")


def parse_arguments(arguments: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    configure_parser = subparsers.add_parser("configure")
    configure_parser.add_argument("--daemon-config", type=Path, required=True)

    verify_parser = subparsers.add_parser("verify")
    verify_parser.add_argument(
        "--docker-info-json",
        type=argparse.FileType("r", encoding="utf-8"),
        default=sys.stdin,
    )
    return parser.parse_args(arguments)


def main(arguments: Sequence[str] | None = None) -> int:
    options = parse_arguments(arguments if arguments is not None else sys.argv[1:])
    try:
        if options.command == "configure":
            changed = configure(options.daemon_config)
            print(f"mirror={DOCKER_HUB_MIRROR} changed={str(changed).lower()}")
        else:
            verify_docker_info(options.docker_info_json.read())
            print(f"mirror={DOCKER_HUB_MIRROR} active=true")
    except MirrorConfigurationError as error:
        print(f"dockerhub-mirror-error: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
