#!/usr/bin/env python3
"""Print a bounded per-class duration table from VSTest TRX files (log only).

Measurement aid for shard balancing: it writes to stdout, produces no artifact,
reads no secrets and never fails the build on a malformed file.
"""

from __future__ import annotations

import argparse
from collections import defaultdict
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

MAX_TRX_FILES = 128
MAX_TRX_BYTES = 256 * 1024 * 1024
DURATION = re.compile(r"^(\d+):(\d{2}):(\d{2})(?:\.(\d+))?$")
CLASS_NAME = re.compile(r"^[A-Za-z_][A-Za-z0-9_.+`]*$")


def parse_duration(text: str | None) -> float:
    match = DURATION.match(text or "")
    if not match:
        return 0.0
    hours, minutes, seconds, fraction = match.groups()
    return int(hours) * 3600 + int(minutes) * 60 + int(seconds) + (
        float(f"0.{fraction}") if fraction else 0.0
    )


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def collect(directory: Path) -> dict[str, list[float]]:
    classes: dict[str, list[float]] = defaultdict(lambda: [0.0, 0.0])
    files = sorted(directory.rglob("*.trx"))[:MAX_TRX_FILES]
    for path in files:
        if path.stat().st_size > MAX_TRX_BYTES:
            continue
        owner: dict[str, str] = {}
        durations: list[tuple[str, float]] = []
        for _, element in ET.iterparse(path):
            name = local(element.tag)
            if name == "UnitTest":
                test_id = element.get("id", "")
                for child in element.iter():
                    if local(child.tag) == "TestMethod":
                        owner[test_id] = child.get("className", "")
                element.clear()
            elif name == "UnitTestResult":
                durations.append((element.get("testId", ""), parse_duration(element.get("duration"))))
                element.clear()
        for test_id, seconds in durations:
            class_name = owner.get(test_id, "")
            if not CLASS_NAME.match(class_name):
                class_name = "(unknown)"
            classes[class_name][0] += 1
            classes[class_name][1] += seconds
    return classes


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trx-directory", required=True, type=Path)
    parser.add_argument("--label", required=True)
    parser.add_argument("--top", type=int, default=40)
    args = parser.parse_args()
    try:
        classes = collect(args.trx_directory)
    except (ET.ParseError, OSError) as error:
        print(f"::warning::TRX duration summary skipped: {type(error).__name__}")
        return 0
    total_seconds = sum(value[1] for value in classes.values())
    total_tests = int(sum(value[0] for value in classes.values()))
    print(f"TRX-DURATIONS label={args.label} classes={len(classes)} tests={total_tests} sum_seconds={total_seconds:.1f}")
    print("(sum of per-test durations; tests may overlap in wall-clock time)")
    ranked = sorted(classes.items(), key=lambda item: (-item[1][1], item[0]))
    for class_name, (count, seconds) in ranked[: args.top]:
        print(f"TRX-DURATIONS {args.label} {seconds:9.1f}s {int(count):5d} {class_name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
