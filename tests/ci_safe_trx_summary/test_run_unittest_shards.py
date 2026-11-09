import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNNER = ROOT / "tools" / "run_unittest_shards.py"


def run(start_dir: Path, *extra: str) -> subprocess.CompletedProcess:
    return subprocess.run(
        [sys.executable, "-I", "-S", "-B", str(RUNNER), "--start-dir", str(start_dir), *extra],
        capture_output=True, text=True, check=False,
    )


class RunUnittestShardsTests(unittest.TestCase):
    def suite(self, body: str) -> Path:
        directory = Path(tempfile.mkdtemp())
        self.addCleanup(lambda: __import__("shutil").rmtree(directory, ignore_errors=True))
        (directory / "test_sample.py").write_text(textwrap.dedent(body), encoding="utf-8")
        return directory

    def test_shards_form_an_exact_partition_and_all_run(self) -> None:
        directory = self.suite(
            """
            import unittest
            class T(unittest.TestCase):
                def test_a(self): pass
                def test_b(self): pass
                def test_c(self): pass
                def test_d(self): pass
                def test_e(self): pass
            """
        )
        result = run(directory, "--shards", "3")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("PARTITION OK: 5 tests in 3 shards", result.stdout)
        self.assertIn("ALL 3 SHARDS PASSED (5 tests)", result.stdout)

    def test_a_failing_test_fails_the_run(self) -> None:
        directory = self.suite(
            """
            import unittest
            class T(unittest.TestCase):
                def test_ok(self): pass
                def test_bad(self): self.fail("boom")
            """
        )
        self.assertNotEqual(0, run(directory, "--shards", "2").returncode)

    def test_a_discovery_error_fails_closed(self) -> None:
        directory = self.suite("raise RuntimeError('import time failure')\n")
        self.assertNotEqual(0, run(directory, "--shards", "2").returncode)

    def test_an_empty_suite_fails_closed(self) -> None:
        directory = Path(tempfile.mkdtemp())
        self.addCleanup(lambda: __import__("shutil").rmtree(directory, ignore_errors=True))
        self.assertNotEqual(0, run(directory, "--shards", "2").returncode)


if __name__ == "__main__":
    unittest.main()
