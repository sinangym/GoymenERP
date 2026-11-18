from __future__ import annotations

import contextlib
import importlib.util
import io
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "relational_live_progress", ROOT / "tools/verify_rcsi_relational_acceptance.py"
)
assert SPEC is not None and SPEC.loader is not None
VERIFIER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFIER)
FQN = VERIFIER.EXPECTED_TESTS[0]
SECRET = "PRIVATE_CONNECTION_VALUE_DO_NOT_PRINT"


class LiveProgressTests(unittest.TestCase):
    def project(self, *chunks: bytes, final: bool = True) -> str:
        progress = VERIFIER.TestProgress()
        with contextlib.redirect_stdout(io.StringIO()) as output:
            for chunk in chunks:
                progress.feed(chunk)
                self.assertLessEqual(len(progress.pending), VERIFIER.MAX_PROGRESS_LINE_BYTES)
            if final:
                progress.feed(b"", final=True)
        return output.getvalue()

    def command(self, code: str) -> list[str]:
        return [sys.executable, "-I", "-S", "-B", "-u", "-c", code]

    def test_native_results_and_long_running_messages_have_only_safe_fields(self) -> None:
        text = self.project(
            (f"  Passed {FQN}(connection: '{SECRET}') [1 m 2 s]\r\n"
             f"  Failed {FQN} [< 1 ms]\n"
             f"  Skipped {FQN} [2 ms]\n"
             f"[xUnit.net 00:01:01.10] GoymenERP.Api.IntegrationTests: "
             f"[Long Running Test] '{FQN}(token: '{SECRET}')', Elapsed: 00:01:01\n").encode()
        )
        self.assertNotIn(SECRET, text)
        self.assertEqual(4, len(text.splitlines()))
        self.assertIn(f"fqn={FQN} outcome=Passed duration_seconds=62.000", text)
        self.assertIn("outcome=Failed duration_seconds=0.001", text)
        self.assertIn("outcome=Skipped duration_seconds=0.002", text)
        self.assertIn("outcome=long-running duration_seconds=61.000", text)

    def test_unknown_names_private_logs_and_malformed_fields_are_not_replayed(self) -> None:
        text = self.project(
            (f"Server=private;Password={SECRET}\n"
             f"  Passed Unknown.Namespace.Method [1 ms]\n"
             f"  Passed {FQN}Extra [1 ms]\n"
             f"  Passed {FQN} [1 ms] {SECRET}\n"
             f"  Passed {FQN} [{SECRET} ms]\n"
             f"[Long Running Test] '{FQN}', Elapsed: 00:99:01\n"
             f"SQL-DIAGNOSTIC phase={SECRET}\n").encode()
        )
        self.assertEqual("", text)

    def test_split_utf8_crlf_and_final_line_without_newline_are_preserved_safely(self) -> None:
        raw = f"  Passed {FQN}(name: '\u015f{SECRET}') [1 s]\r\n  Failed {FQN} [2 s]".encode()
        split = raw.index(b"\xc5") + 1
        text = self.project(raw[:split], raw[split:split + 3], raw[split + 3:])
        self.assertEqual(2, len(text.splitlines()))
        self.assertNotIn(SECRET, text)

    def test_oversized_line_is_discarded_whole_then_next_valid_line_survives(self) -> None:
        text = self.project(
            b"x" * VERIFIER.MAX_PROGRESS_LINE_BYTES,
            (f"  Passed {FQN} [1 s]\n  Passed {FQN} [2 s]\n").encode(),
        )
        self.assertEqual(1, len(text.splitlines()))
        self.assertIn("duration_seconds=2.000", text)

    def test_event_budget_and_nonfinite_durations_do_not_expand_output(self) -> None:
        with mock.patch.object(VERIFIER, "MAX_PROGRESS_EVENTS", 3):
            text = self.project((f"  Passed {FQN} [1 ms]\n" * 1000).encode())
        self.assertEqual(3, len(text.splitlines()))
        self.assertEqual("", self.project(f"  Passed {FQN} [{'9' * 400} s]\n".encode()))

    def test_default_execution_deadline_remains_2400_seconds(self) -> None:
        self.assertEqual(2400, VERIFIER.TEST_EXECUTION_TIMEOUT_SECONDS)

    def test_native_success_drains_large_stdout_and_stderr_without_raw_replay(self) -> None:
        code = (
            "import sys\n"
            f"sys.stdout.write('{SECRET}' * 100000 + '\\n'); sys.stdout.flush()\n"
            f"sys.stderr.write('{SECRET}' * 100000 + '\\n'); sys.stderr.flush()\n"
            f"print('  Passed {FQN} [1 ms]', flush=True)\n"
        )
        with (
            mock.patch.object(VERIFIER, "TEST_EXECUTION_TIMEOUT_SECONDS", 10),
            contextlib.redirect_stdout(io.StringIO()) as output,
        ):
            result = VERIFIER.run_test_checked(self.command(code), ROOT)
        self.assertEqual(0, result.returncode)
        self.assertEqual(("", ""), (result.stdout, result.stderr))
        self.assertIn(f"fqn={FQN}", output.getvalue())
        self.assertNotIn(SECRET, output.getvalue())
        self.assertLess(len(output.getvalue()), 1000)

    def test_progress_is_emitted_before_native_process_finishes(self) -> None:
        observed = threading.Event()
        errors: list[BaseException] = []

        class Output(io.StringIO):
            def write(self, value: str) -> int:
                if f"fqn={FQN}" in value:
                    observed.set()
                return super().write(value)

        with tempfile.TemporaryDirectory() as directory:
            release = Path(directory) / "release"
            code = (
                "import pathlib, time\n"
                f"print('  Passed {FQN} [1 ms]', flush=True)\n"
                f"p = pathlib.Path({str(release)!r})\n"
                "while not p.exists(): time.sleep(.02)\n"
            )

            def run() -> None:
                try:
                    VERIFIER.run_test_checked(self.command(code), ROOT)
                except BaseException as error:
                    errors.append(error)

            with (
                mock.patch.object(VERIFIER, "TEST_EXECUTION_TIMEOUT_SECONDS", 10),
                contextlib.redirect_stdout(Output()) as output,
            ):
                worker = threading.Thread(target=run)
                worker.start()
                try:
                    self.assertTrue(observed.wait(5), "No live progress before child exit")
                    self.assertTrue(worker.is_alive())
                    self.assertNotIn("outcome=completed", output.getvalue())
                finally:
                    release.touch()
                    worker.join(35)
            self.assertFalse(worker.is_alive())
            self.assertEqual([], errors)

    def test_native_failure_retains_exit_code_and_safe_partial_output(self) -> None:
        code = (
            "import sys\n"
            f"print('{SECRET}', file=sys.stderr, flush=True)\n"
            f"print('  Failed {FQN} [3 s]', file=sys.stderr, flush=True)\n"
            "sys.exit(7)\n"
        )
        with (
            mock.patch.object(VERIFIER, "TEST_EXECUTION_TIMEOUT_SECONDS", 10),
            contextlib.redirect_stdout(io.StringIO()) as output,
        ):
            with self.assertRaisesRegex(RuntimeError, "native code 7"):
                VERIFIER.run_test_checked(self.command(code), ROOT)
        self.assertIn(f"fqn={FQN} outcome=Failed", output.getvalue())
        self.assertNotIn(SECRET, output.getvalue())
        self.assertNotIn("outcome=completed", output.getvalue())

    def test_timeout_preserves_partial_output_and_kills_and_waits_for_real_child(self) -> None:
        children: list[subprocess.Popen[bytes]] = []
        real_popen = subprocess.Popen

        def start(*args: object, **kwargs: object) -> subprocess.Popen[bytes]:
            process = real_popen(*args, **kwargs)
            children.append(process)
            return process

        code = (
            "import sys, time\n"
            f"print('{SECRET}', flush=True)\n"
            f"sys.stdout.write(\"[Long Running Test] '{FQN}', Elapsed: 00:01:00\"); sys.stdout.flush()\n"
            "time.sleep(30)\n"
        )
        with (
            mock.patch.object(VERIFIER, "TEST_EXECUTION_TIMEOUT_SECONDS", .8),
            mock.patch.object(VERIFIER.subprocess, "Popen", side_effect=start),
            contextlib.redirect_stdout(io.StringIO()) as output,
        ):
            with self.assertRaisesRegex(RuntimeError, "timed out") as failure:
                VERIFIER.run_test_checked(self.command(code), ROOT)
        self.assertIsInstance(failure.exception.__cause__, subprocess.TimeoutExpired)
        self.assertIsNotNone(children[0].poll())
        self.assertTrue(children[0].stdout.closed)
        self.assertIn(f"fqn={FQN} outcome=long-running", output.getvalue())
        self.assertIn("outcome=timeout", output.getvalue())
        self.assertNotIn(SECRET, output.getvalue())

    def test_repeated_live_progress_never_renews_execution_deadline(self) -> None:
        code = (
            "import time\n"
            "while True:\n"
            f" print('  Passed {FQN} [1 ms]', flush=True)\n"
            " time.sleep(.02)\n"
        )
        started = time.monotonic()
        with (
            mock.patch.object(VERIFIER, "TEST_EXECUTION_TIMEOUT_SECONDS", .5),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            with self.assertRaisesRegex(RuntimeError, "timed out"):
                VERIFIER.run_test_checked(self.command(code), ROOT)
        self.assertLess(time.monotonic() - started, 5)

    def test_native_failure_is_not_replaced_by_lingering_pipe_timeout(self) -> None:
        process = mock.Mock(pid=123, stdout=io.BytesIO())
        process.wait.return_value = 9
        reader = mock.Mock()
        reader.is_alive.return_value = True
        with (
            mock.patch.object(VERIFIER.subprocess, "Popen", return_value=process),
            mock.patch.object(VERIFIER.threading, "Thread", return_value=reader),
            mock.patch.object(VERIFIER, "stop_test_process") as stop,
            contextlib.redirect_stdout(io.StringIO()) as output,
        ):
            with self.assertRaisesRegex(RuntimeError, "native code 9"):
                VERIFIER.run_test_checked(["synthetic"], ROOT)
        stop.assert_called_once_with(process)
        reader.join.assert_called_once_with(timeout=10)
        self.assertNotIn("outcome=timeout", output.getvalue())

    def test_parent_exit_with_open_pipe_uses_remaining_deadline_and_fails_closed(self) -> None:
        process = mock.Mock(pid=123, stdout=io.BytesIO())
        process.wait.return_value = 0
        reader = mock.Mock()
        reader.is_alive.return_value = True
        with (
            mock.patch.object(VERIFIER.subprocess, "Popen", return_value=process),
            mock.patch.object(VERIFIER.threading, "Thread", return_value=reader),
            mock.patch.object(VERIFIER.time, "monotonic", side_effect=[10, 11, 12, 13]),
            mock.patch.object(VERIFIER, "stop_test_process") as stop,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            with self.assertRaisesRegex(RuntimeError, "timed out"):
                VERIFIER.run_test_checked(["synthetic"], ROOT)
        process.wait.assert_called_once_with(timeout=2399)
        self.assertEqual(mock.call(timeout=2398), reader.join.call_args_list[0])
        stop.assert_called_once_with(process)

    def test_cleanup_failure_cannot_mask_primary_timeout(self) -> None:
        process = mock.Mock(pid=123, stdout=io.BytesIO())
        process.wait.side_effect = subprocess.TimeoutExpired(["private"], 2400)
        with (
            mock.patch.object(VERIFIER.subprocess, "Popen", return_value=process),
            mock.patch.object(VERIFIER, "stop_test_process", side_effect=OSError(SECRET)),
            contextlib.redirect_stdout(io.StringIO()) as output,
        ):
            with self.assertRaisesRegex(RuntimeError, "timed out") as failure:
                VERIFIER.run_test_checked(["synthetic"], ROOT)
        self.assertNotIn(SECRET, str(failure.exception) + output.getvalue())

    def test_tree_cleanup_and_reaping_are_explicit_on_both_platforms(self) -> None:
        for platform in ("nt", "posix"):
            with self.subTest(platform=platform):
                process = mock.Mock(pid=123)
                with (
                    mock.patch.object(VERIFIER.os, "name", platform),
                    mock.patch.object(VERIFIER.signal, "SIGKILL", 9, create=True),
                    mock.patch.object(VERIFIER.os, "killpg", create=True) as killpg,
                    mock.patch.object(VERIFIER.subprocess, "run") as run,
                ):
                    VERIFIER.stop_test_process(process)
                if platform == "nt":
                    self.assertEqual(["taskkill", "/PID", "123", "/T", "/F"], run.call_args.args[0])
                    self.assertEqual(subprocess.DEVNULL, run.call_args.kwargs["stdout"])
                    self.assertEqual(subprocess.DEVNULL, run.call_args.kwargs["stderr"])
                else:
                    killpg.assert_called_once_with(123, 9)
                process.kill.assert_called_once_with()
                process.wait.assert_called_once_with(timeout=10)

    def test_reader_start_failure_still_stops_and_reaps_the_child(self) -> None:
        process = mock.Mock(pid=123, stdout=io.BytesIO())
        reader = mock.Mock()
        reader.start.side_effect = RuntimeError("reader unavailable")
        reader.is_alive.return_value = False
        with (
            mock.patch.object(VERIFIER.subprocess, "Popen", return_value=process),
            mock.patch.object(VERIFIER.threading, "Thread", return_value=reader),
            mock.patch.object(VERIFIER, "stop_test_process") as stop,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            with self.assertRaisesRegex(RuntimeError, "reader unavailable"):
                VERIFIER.run_test_checked(["synthetic"], ROOT)
        stop.assert_called_once_with(process)
        reader.join.assert_not_called()
        self.assertTrue(process.stdout.closed)

    def test_tree_kill_error_still_kills_and_waits_for_direct_child(self) -> None:
        process = mock.Mock(pid=123)
        with (
            mock.patch.object(VERIFIER.os, "name", "nt"),
            mock.patch.object(VERIFIER.subprocess, "run", side_effect=OSError(SECRET)),
        ):
            VERIFIER.stop_test_process(process)
        process.kill.assert_called_once_with()
        process.wait.assert_called_once_with(timeout=10)

    def test_discovery_still_uses_native_capture_and_original_timeout(self) -> None:
        arguments = ["dotnet", "test", "--list-tests"]
        expected = subprocess.CompletedProcess(arguments, 0, stdout=FQN, stderr="")
        with (
            mock.patch.object(VERIFIER.subprocess, "run", return_value=expected) as run,
            mock.patch.object(VERIFIER, "run_test_checked") as execution,
        ):
            self.assertIs(expected, VERIFIER.run_checked(arguments, ROOT))
        execution.assert_not_called()
        self.assertEqual(2400, run.call_args.kwargs["timeout"])
        self.assertTrue(run.call_args.kwargs["capture_output"])

    def test_only_execution_boundary_uses_live_projection(self) -> None:
        arguments = ["dotnet", "test", "--no-build", "--results-directory", "synthetic"]
        with mock.patch.object(VERIFIER, "run_test_checked") as execution:
            VERIFIER.run_checked(arguments, ROOT)
        execution.assert_called_once_with(arguments, ROOT)

    def test_main_adds_diagnostics_but_never_proves_acceptance_without_trx(self) -> None:
        calls: list[list[str]] = []

        def run(arguments: list[str], root: Path, **_kwargs: object) -> subprocess.CompletedProcess[str]:
            calls.append(arguments)
            return subprocess.CompletedProcess(
                arguments, 0,
                stdout="\n".join(VERIFIER.BROAD_EXPECTED_TESTS) if "--list-tests" in arguments else "",
                stderr="",
            )

        with tempfile.TemporaryDirectory(dir=ROOT) as directory:
            trx = Path(directory) / "result.trx"
            proof = Path(directory) / "proof.json"
            proof.write_text("stale proof", encoding="utf-8")
            with (
                mock.patch.object(VERIFIER, "run_checked", side_effect=run),
                mock.patch.dict(VERIFIER.os.environ, {"GOYMEN_ERP_TEST_SQLSERVER_CONNECTION": SECRET}),
                mock.patch.object(sys, "argv", [
                    "verifier", "--shard", "A", "--trx-output", str(trx), "--proof-output", str(proof),
                    "--source-head", "a" * 40, "--tested-commit", "b" * 40,
                ]),
                contextlib.redirect_stdout(io.StringIO()),
            ):
                with self.assertRaisesRegex(RuntimeError, "normal output file"):
                    VERIFIER.main()
            self.assertFalse(proof.exists())
            self.assertFalse(trx.exists())
        execution = next(call for call in calls if "--no-build" in call)
        self.assertIn("console;verbosity=detailed", execution)
        self.assertEqual(["xUnit.DiagnosticMessages=true", "xUnit.LongRunningTestSeconds=60"], execution[-2:])
        self.assertEqual("--", execution[-3])
        self.assertEqual(
            "|".join(f"FullyQualifiedName={name}" for name in VERIFIER.plan_sql_batches(VERIFIER.shard_executions("A"))[0]),
            execution[execution.index("--filter") + 1],
        )


if __name__ == "__main__":
    unittest.main()
