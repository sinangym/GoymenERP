import contextlib
import importlib.util
import io
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("pre_push_check", REPO_ROOT / "tools" / "pre_push_check.py")
pre_push_check = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(pre_push_check)

PY = pre_push_check.PY


def run_quietly(steps, cwd=None):
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        code = pre_push_check.run(steps) if cwd is None else pre_push_check.run(steps, cwd)
    return code, out.getvalue()


class PrePushCheckTests(unittest.TestCase):
    def test_passing_step_exits_zero(self) -> None:
        code, out = run_quietly((("ok", (*PY, "-c", "pass"), ()),))
        self.assertEqual(code, 0)
        self.assertIn("1 ok, 0 failed, 0 skipped", out)

    def test_failing_step_exits_one_and_is_reported(self) -> None:
        code, out = run_quietly((("bad", (*PY, "-c", "raise SystemExit(3)"), ()),))
        self.assertEqual(code, 1)
        self.assertIn("FAILED   bad (exit 3", out)

    def test_missing_tool_is_skipped_never_counted_as_passed(self) -> None:
        code, out = run_quietly((("needs-tool", (*PY, "-c", "pass"), ("definitely-missing-tool-xyz",)),))
        self.assertEqual(code, 0)
        self.assertIn("SKIPPED  needs-tool", out)
        self.assertIn("0 ok, 0 failed, 1 skipped", out)
        self.assertIn("NOT verified locally", out)

    def test_a_failure_wins_over_skips_and_passes(self) -> None:
        steps = (
            ("ok", (*PY, "-c", "pass"), ()),
            ("skip", (*PY, "-c", "pass"), ("definitely-missing-tool-xyz",)),
            ("bad", (*PY, "-c", "raise SystemExit(1)"), ()),
        )
        code, out = run_quietly(steps)
        self.assertEqual(code, 1)
        self.assertIn("1 ok, 1 failed, 1 skipped", out)

    def test_steps_run_from_the_repository_root_regardless_of_caller_cwd(self) -> None:
        probe = "import pathlib,sys;sys.exit(0 if pathlib.Path('tools/pre_push_check.py').is_file() else 9)"
        step = (("cwd", (*PY, "-c", probe), ()),)
        code, _ = run_quietly(step)
        self.assertEqual(code, 0)
        with tempfile.TemporaryDirectory() as other:
            code, _ = run_quietly(step, Path(other))
            self.assertEqual(code, 1, "an explicit foreign cwd must not see the repository files")

    def test_light_steps_mirror_the_ci_preflight_suites(self) -> None:
        names = {name for name, _, _ in pre_push_check.LIGHT}
        for suite in ("task_manifest_validator", "openapi_contract_drift", "delivery_backlog_validator"):
            self.assertTrue(any(suite in name for name in names), suite)


if __name__ == "__main__":
    unittest.main()
