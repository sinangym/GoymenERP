#!/usr/bin/env python3
"""Run one unittest suite as N parallel local shards with an exact coverage proof.

The suite is discovered once, flattened and ordered by test id; shard K runs the
tests whose position modulo N is K.  Every shard prints its test ids, and the
parent proves that the shards are disjoint and together equal the discovered
set before it reports success.  A failing shard fails the run.
"""
from __future__ import annotations

import argparse
import subprocess
import sys
import unittest
from pathlib import Path


def _flatten(suite: unittest.TestSuite):
    for item in suite:
        if isinstance(item, unittest.TestSuite):
            yield from _flatten(item)
        else:
            yield item


def discover(start_dir: str, pattern: str) -> list[unittest.TestCase]:
    suite = unittest.defaultTestLoader.discover(
        start_dir, pattern=pattern, top_level_dir=start_dir
    )
    tests = sorted(_flatten(suite), key=lambda test: test.id())
    failed = [t for t in tests if t.__class__.__name__ == "_FailedTest"]
    if failed:
        raise SystemExit("Test discovery failed: " + ", ".join(t.id() for t in failed))
    ids = [t.id() for t in tests]
    if not ids or len(ids) != len(set(ids)):
        raise SystemExit("Test discovery is empty or has duplicate ids.")
    return tests


def select(tests: list[unittest.TestCase], shard: int, count: int) -> unittest.TestSuite:
    return unittest.TestSuite(t for i, t in enumerate(tests) if i % count == shard)


def run_shard(start_dir: str, pattern: str, shard: int, count: int) -> int:
    tests = discover(start_dir, pattern)
    chosen = select(tests, shard, count)
    ids = [t.id() for t in _flatten(chosen)]
    print(f"SHARD {shard + 1}/{count}: {len(ids)} of {len(tests)} tests", flush=True)
    result = unittest.TextTestRunner(verbosity=2).run(chosen)
    if result.testsRun != len(ids):
        print("Shard ran an unexpected number of tests.", file=sys.stderr)
        return 1
    return 0 if result.wasSuccessful() else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--start-dir", required=True)
    parser.add_argument("--pattern", default="test_*.py")
    parser.add_argument("--shards", type=int, default=4)
    parser.add_argument("--shard", type=int)
    args = parser.parse_args()
    if args.shards < 1:
        raise SystemExit("--shards must be positive.")
    start_dir = str(Path(args.start_dir).resolve())
    if args.shard is not None:
        return run_shard(start_dir, args.pattern, args.shard, args.shards)

    tests = discover(start_dir, args.pattern)
    expected = [t.id() for t in tests]
    covered: list[str] = []
    for shard in range(args.shards):
        covered.extend(t.id() for t in _flatten(select(tests, shard, args.shards)))
    if sorted(covered) != expected:
        raise SystemExit("Shards are not an exact partition of the discovered tests.")
    print(f"PARTITION OK: {len(expected)} tests in {args.shards} shards", flush=True)

    processes = [
        subprocess.Popen(
            [sys.executable, "-I", "-S", "-B", __file__, "--start-dir", start_dir,
             "--pattern", args.pattern, "--shards", str(args.shards), "--shard", str(k)]
        )
        for k in range(args.shards)
    ]
    codes = [p.wait() for p in processes]
    if any(codes):
        print(f"Shard exit codes: {codes}", file=sys.stderr)
        return 1
    print(f"ALL {args.shards} SHARDS PASSED ({len(expected)} tests)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
