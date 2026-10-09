from __future__ import annotations

import importlib.util
import json
import os
from pathlib import Path
import stat
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools" / "configure_dockerhub_mirror.py"
SPEC = importlib.util.spec_from_file_location("configure_dockerhub_mirror", TOOL)
assert SPEC is not None and SPEC.loader is not None
mirror_tool = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(mirror_tool)


class DockerHubMirrorConfigurationTests(unittest.TestCase):
    def test_missing_configuration_is_created_with_only_the_reviewed_mirror(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "daemon.json"

            self.assertTrue(mirror_tool.configure(path))

            self.assertEqual(
                json.loads(path.read_text(encoding="utf-8")),
                {"registry-mirrors": [mirror_tool.DOCKER_HUB_MIRROR]},
            )
            if os.name != "nt":
                self.assertEqual(stat.S_IMODE(path.stat().st_mode), 0o644)

    def test_existing_keys_and_mirrors_are_preserved(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "daemon.json"
            path.write_text(
                json.dumps(
                    {
                        "features": {"containerd-snapshotter": True},
                        "registry-mirrors": ["https://existing.example.test"],
                    }
                ),
                encoding="utf-8",
            )

            self.assertTrue(mirror_tool.configure(path))

            self.assertEqual(
                json.loads(path.read_text(encoding="utf-8")),
                {
                    "features": {"containerd-snapshotter": True},
                    "registry-mirrors": [
                        mirror_tool.DOCKER_HUB_MIRROR,
                        "https://existing.example.test",
                    ],
                },
            )

    def test_configuration_is_idempotent_and_accepts_daemon_trailing_slash(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "daemon.json"
            original = '{"registry-mirrors":["https://mirror.gcr.io/"]}\n'
            path.write_text(original, encoding="utf-8")

            self.assertFalse(mirror_tool.configure(path))

            self.assertEqual(path.read_text(encoding="utf-8"), original)

    def test_invalid_existing_json_fails_closed_without_replacing_the_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "daemon.json"
            original = "{not-json\n"
            path.write_text(original, encoding="utf-8")

            with self.assertRaises(mirror_tool.MirrorConfigurationError):
                mirror_tool.configure(path)

            self.assertEqual(path.read_text(encoding="utf-8"), original)

    def test_invalid_registry_mirror_shape_fails_closed(self) -> None:
        with self.assertRaises(mirror_tool.MirrorConfigurationError):
            mirror_tool.with_docker_hub_mirror({"registry-mirrors": "not-an-array"})
        with self.assertRaises(mirror_tool.MirrorConfigurationError):
            mirror_tool.with_docker_hub_mirror({"registry-mirrors": [7]})

    def test_verify_accepts_the_docker_normalized_mirror(self) -> None:
        mirror_tool.verify_docker_info('["https://mirror.gcr.io/"]')

    def test_verify_refuses_missing_or_malformed_mirror_output(self) -> None:
        for payload in ('["https://different.example.test/"]', "{}", "not-json"):
            with self.subTest(payload=payload):
                with self.assertRaises(mirror_tool.MirrorConfigurationError):
                    mirror_tool.verify_docker_info(payload)


if __name__ == "__main__":
    unittest.main()
