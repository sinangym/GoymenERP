import contextlib
import io
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "ci"))
import trx_durations  # noqa: E402

TRX = """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testId="a" duration="00:00:01.500"/>
    <UnitTestResult testId="b" duration="00:01:00"/>
    <UnitTestResult testId="c" duration="bad"/>
  </Results>
  <TestDefinitions>
    <UnitTest id="a"><TestMethod className="GoymenERP.X.Fast" name="m1"/></UnitTest>
    <UnitTest id="b"><TestMethod className="GoymenERP.X.Slow" name="m2"/></UnitTest>
    <UnitTest id="c"><TestMethod className="GoymenERP.X.Fast" name="m3"/></UnitTest>
  </TestDefinitions>
</TestRun>
"""


class TrxDurationsTests(unittest.TestCase):
    def test_parse_duration(self):
        self.assertEqual(trx_durations.parse_duration("00:00:01.500"), 1.5)
        self.assertEqual(trx_durations.parse_duration("01:00:00"), 3600.0)
        self.assertEqual(trx_durations.parse_duration("nope"), 0.0)

    def test_collect_groups_by_class(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "r.trx").write_text(TRX, encoding="utf-8")
            classes = trx_durations.collect(Path(directory))
        self.assertEqual(classes["GoymenERP.X.Fast"], [2, 1.5])
        self.assertEqual(classes["GoymenERP.X.Slow"], [1, 60.0])

    def test_main_prints_slowest_first_and_tolerates_bad_xml(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "r.trx").write_text(TRX, encoding="utf-8")
            out = io.StringIO()
            argv = sys.argv
            sys.argv = ["x", "--trx-directory", directory, "--label", "t"]
            try:
                with contextlib.redirect_stdout(out):
                    self.assertEqual(trx_durations.main(), 0)
                Path(directory, "r.trx").write_text("<broken", encoding="utf-8")
                with contextlib.redirect_stdout(out):
                    self.assertEqual(trx_durations.main(), 0)
            finally:
                sys.argv = argv
        lines = out.getvalue().splitlines()
        self.assertIn("GoymenERP.X.Slow", lines[2])
        self.assertTrue(any("skipped" in line for line in lines))


if __name__ == "__main__":
    unittest.main()
