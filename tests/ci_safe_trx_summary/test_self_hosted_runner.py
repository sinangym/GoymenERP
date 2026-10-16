from __future__ import annotations
import copy
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / "trusted_job.py"
if not SOURCE.exists():
    SOURCE = ROOT.parents[1] / "tools" / "ci" / "self_hosted_admission.py"
spec = importlib.util.spec_from_file_location("admission", SOURCE)
admission = importlib.util.module_from_spec(spec)
spec.loader.exec_module(admission)


class AdmissionTests(unittest.TestCase):
    def setUp(self):
        self.env = {"GITHUB_REPOSITORY": "sinangym/GoymenERP", "GITHUB_ACTOR": "sinangym",
                    "GITHUB_EVENT_NAME": "push", "GITHUB_REF": "refs/heads/main"}
        self.event = {"repository": {"full_name": "sinangym/GoymenERP", "private": True}}

    def reject(self):
        with self.assertRaises(ValueError):
            admission.validate(self.env, self.event)

    def test_internal_push(self):
        admission.validate(self.env, self.event)

    def test_manual_dispatch(self):
        self.env["GITHUB_EVENT_NAME"] = "workflow_dispatch"
        admission.validate(self.env, self.event)

    def test_internal_pr(self):
        self.env["GITHUB_EVENT_NAME"] = "pull_request"
        side = {"repo": {"full_name": "sinangym/GoymenERP"}}
        self.event["pull_request"] = {"head": copy.deepcopy(side), "base": copy.deepcopy(side)}
        admission.validate(self.env, self.event)

    def test_other_repository(self):
        self.env["GITHUB_REPOSITORY"] = "other/repo"
        self.reject()

    def test_other_actor(self):
        self.env["GITHUB_ACTOR"] = "outsider"
        self.reject()

    def test_public_repository(self):
        self.event["repository"]["private"] = False
        self.reject()

    def test_missing_private_flag(self):
        del self.event["repository"]["private"]
        self.reject()

    def test_mismatched_event_repository(self):
        self.event["repository"]["full_name"] = "other/repo"
        self.reject()

    def test_fork_pr(self):
        self.env["GITHUB_EVENT_NAME"] = "pull_request"
        self.event["pull_request"] = {"head": {"repo": {"full_name": "fork/repo"}},
                                      "base": {"repo": {"full_name": "sinangym/GoymenERP"}}}
        self.reject()

    def test_missing_pr_source(self):
        self.env["GITHUB_EVENT_NAME"] = "pull_request"
        self.reject()

    def test_pull_request_target_is_rejected(self):
        self.env["GITHUB_EVENT_NAME"] = "pull_request_target"
        self.reject()

    def test_tags_are_rejected(self):
        self.env["GITHUB_REF"] = "refs/tags/v1"
        self.reject()


BOOTSTRAP_HEADER = 'name: Local CI migration acceptance\n\non:\n  push:\n    branches:\n      - chatgpt/local-ci-validate-20261016\n\npermissions:\n  contents: read\n\nconcurrency:\n  group: local-ci-bootstrap-${{ github.ref }}\n  cancel-in-progress: true\n\njobs:\n'


class MigrationRoutingTests(unittest.TestCase):
    def test_bootstrap_runs_exact_canonical_job_bodies(self):
        repo = ROOT.parents[1]
        workflows = repo / '.github' / 'workflows'
        names = ['ci.yml', 'ui-catalog-validation.yml', 'cari-c02-link-acceptance.yml']
        bodies = [(workflows / name).read_text(encoding='utf-8').split('\njobs:\n', 1)[1].rstrip() + '\n' for name in names]
        self.assertEqual((workflows / 'local-ci-bootstrap.yml').read_text(encoding='utf-8'), BOOTSTRAP_HEADER + '\n'.join(bodies))

    def test_all_workflow_jobs_use_explicit_local_runners(self):
        workflows = ROOT.parents[1] / '.github' / 'workflows'
        allowed = {'runs-on: [self-hosted, Linux, X64, goymen-ci-isolated]',
                   'runs-on: [self-hosted, Windows, X64, goymen-ci-windows]'}
        count = 0
        for path in workflows.glob('*.yml'):
            for line in path.read_text(encoding='utf-8').splitlines():
                if line.lstrip().startswith('runs-on:'):
                    count += 1
                    self.assertIn(line.strip(), allowed, str(path))
        self.assertGreater(count, 0)


if __name__ == '__main__':
    unittest.main()
