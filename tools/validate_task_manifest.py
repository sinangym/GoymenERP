#!/usr/bin/env python3
"""Validate the structural integrity of the ERP task manifest."""

from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from pathlib import Path
from typing import Any, NamedTuple


MANIFEST_RELATIVE_PATH = Path("docs/backlog/task_manifest.json")


class ManifestStatistics(NamedTuple):
    task_count: int
    dependency_count: int


class DuplicateJsonKeyError(ValueError):
    def __init__(self, key: str) -> None:
        super().__init__(f"Duplicate JSON object key: {key!r}")
        self.key = key


class NonFiniteJsonNumberError(ValueError):
    def __init__(self, value: str) -> None:
        super().__init__(f"Non-standard JSON numeric constant: {value}")
        self.value = value


def _reject_duplicate_json_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, item in pairs:
        if key in value:
            raise DuplicateJsonKeyError(key)
        value[key] = item
    return value


def _reject_non_finite_json_number(value: str) -> None:
    raise NonFiniteJsonNumberError(value)


def _load_manifest(path: Path, errors: list[str]) -> dict[str, Any] | None:
    try:
        raw_value = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=_reject_duplicate_json_keys,
            parse_constant=_reject_non_finite_json_number,
        )
    except FileNotFoundError:
        errors.append(f"Missing task manifest: {path}")
        return None
    except (OSError, UnicodeError) as exc:
        errors.append(f"Cannot read task manifest {path}: {exc}")
        return None
    except json.JSONDecodeError as exc:
        errors.append(
            f"Invalid JSON in task manifest {path}:{exc.lineno}:{exc.colno}: "
            f"{exc.msg}"
        )
        return None
    except (DuplicateJsonKeyError, NonFiniteJsonNumberError) as exc:
        errors.append(f"Invalid JSON in task manifest {path}: {exc}")
        return None

    if not isinstance(raw_value, dict):
        errors.append("Task manifest root must be a JSON object")
        return None
    return raw_value


def _task_label(index: int, task_id: str | None = None) -> str:
    if task_id:
        return f"task {task_id!r}"
    return f"task at index {index}"


def _canonical_cycle(nodes: list[str]) -> tuple[str, ...]:
    """Rotate a closed directed cycle to its unique lexical minimum node."""

    ring = nodes[:-1]
    minimum_index = min(range(len(ring)), key=ring.__getitem__)
    canonical_ring = tuple(ring[minimum_index:] + ring[:minimum_index])
    return canonical_ring + (canonical_ring[0],)


def _find_dependency_cycles(graph: dict[str, list[str]]) -> list[str]:
    """Return deterministic cycle descriptions for an otherwise resolved graph.

    The traversal is iterative so a structurally valid, unusually deep manifest cannot
    escape with Python's recursion-limit exception.
    """

    state = {task_id: 0 for task_id in graph}
    active_path: list[str] = []
    active_positions: dict[str, int] = {}
    cycles: set[tuple[str, ...]] = set()

    for root_task_id in graph:
        if state[root_task_id] != 0:
            continue

        state[root_task_id] = 1
        active_positions[root_task_id] = 0
        active_path.append(root_task_id)
        frames: list[tuple[str, int]] = [(root_task_id, 0)]

        while frames:
            task_id, dependency_index = frames[-1]
            dependencies = graph[task_id]

            if dependency_index >= len(dependencies):
                frames.pop()
                finished_task_id = active_path.pop()
                active_positions.pop(finished_task_id)
                state[finished_task_id] = 2
                continue

            dependency_id = dependencies[dependency_index]
            frames[-1] = (task_id, dependency_index + 1)
            if dependency_id not in graph:
                continue

            if state[dependency_id] == 0:
                state[dependency_id] = 1
                active_positions[dependency_id] = len(active_path)
                active_path.append(dependency_id)
                frames.append((dependency_id, 0))
            elif state[dependency_id] == 1:
                cycle_start = active_positions[dependency_id]
                cycles.add(
                    _canonical_cycle(active_path[cycle_start:] + [dependency_id])
                )

    return [" -> ".join(cycle) for cycle in sorted(cycles)]


def _validate_manifest(
    manifest: dict[str, Any], errors: list[str]
) -> ManifestStatistics | None:
    tasks = manifest.get("tasks")
    if not isinstance(tasks, list):
        errors.append("Task manifest field 'tasks' must be a JSON array")
        return None
    if not tasks:
        errors.append("Task manifest field 'tasks' must not be empty")
        return None

    task_ids: list[str] = []
    dependencies_by_index: dict[int, list[str]] = {}

    for index, raw_task in enumerate(tasks):
        if not isinstance(raw_task, dict):
            errors.append(f"Task at index {index} must be a JSON object")
            continue

        raw_task_id = raw_task.get("id")
        task_id: str | None = None
        if (
            not isinstance(raw_task_id, str)
            or not raw_task_id
            or raw_task_id != raw_task_id.strip()
        ):
            errors.append(
                f"Task at index {index} field 'id' must be a non-empty trimmed string"
            )
        else:
            task_id = raw_task_id
            task_ids.append(task_id)

        raw_dependencies = raw_task.get("depends_on")
        if not isinstance(raw_dependencies, list):
            errors.append(
                f"{_task_label(index, task_id)} field 'depends_on' must be a JSON array"
            )
            continue

        dependencies: list[str] = []
        for dependency_index, raw_dependency in enumerate(raw_dependencies):
            if (
                not isinstance(raw_dependency, str)
                or not raw_dependency
                or raw_dependency != raw_dependency.strip()
            ):
                errors.append(
                    f"{_task_label(index, task_id)} dependency at index "
                    f"{dependency_index} must be a non-empty trimmed string"
                )
                continue
            dependencies.append(raw_dependency)

        duplicate_dependencies = sorted(
            dependency
            for dependency, count in Counter(dependencies).items()
            if count > 1
        )
        for dependency in duplicate_dependencies:
            errors.append(
                f"{_task_label(index, task_id)} has duplicate dependency "
                f"{dependency!r}"
            )
        dependencies_by_index[index] = dependencies

    duplicate_task_ids = sorted(
        task_id for task_id, count in Counter(task_ids).items() if count > 1
    )
    for task_id in duplicate_task_ids:
        errors.append(f"Duplicate task id: {task_id!r}")

    known_task_ids = set(task_ids)
    graph: dict[str, list[str]] = {}
    for index, raw_task in enumerate(tasks):
        if not isinstance(raw_task, dict):
            continue
        task_id = raw_task.get("id")
        if not isinstance(task_id, str) or task_id not in known_task_ids:
            continue

        dependencies = dependencies_by_index.get(index, [])
        graph.setdefault(task_id, dependencies)
        for dependency in dependencies:
            if dependency == task_id:
                errors.append(f"Task {task_id!r} cannot depend on itself")
            elif dependency not in known_task_ids:
                errors.append(
                    f"Task {task_id!r} depends on unknown task {dependency!r}"
                )

    if not duplicate_task_ids:
        for cycle in _find_dependency_cycles(graph):
            errors.append(f"Dependency cycle detected: {cycle}")

    if errors:
        return None

    return ManifestStatistics(
        task_count=len(tasks),
        dependency_count=sum(
            len(dependencies) for dependencies in dependencies_by_index.values()
        ),
    )


def validate_repository_with_statistics(
    repo_root: Path,
) -> tuple[list[str], ManifestStatistics | None]:
    """Return structural errors and statistics from the same parsed snapshot."""

    errors: list[str] = []
    manifest = _load_manifest(repo_root / MANIFEST_RELATIVE_PATH, errors)
    if manifest is None:
        return errors, None
    statistics = _validate_manifest(manifest, errors)
    return errors, statistics


def validate_repository(repo_root: Path) -> list[str]:
    """Return structural errors without applying business or status policy."""

    errors, _ = validate_repository_with_statistics(repo_root)
    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
        help="Repository root containing docs/backlog/task_manifest.json",
    )
    args = parser.parse_args(argv)
    repo_root = args.root.resolve()

    errors, statistics = validate_repository_with_statistics(repo_root)
    if errors:
        print("Task manifest validation FAILED", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    if statistics is None:
        print("Task manifest validation FAILED", file=sys.stderr)
        print("- Validation completed without statistics", file=sys.stderr)
        return 1

    print("Task manifest validation PASSED")
    print(f"- tasks: {statistics.task_count}")
    print(f"- dependency edges: {statistics.dependency_count}")
    print("- task ids: unique")
    print("- dependency references: resolved")
    print("- dependency graph: acyclic")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
