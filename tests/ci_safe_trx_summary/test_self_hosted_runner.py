from __future__ import annotations
import copy
import re
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
        self.assertEqual((workflows / 'local-ci-bootstrap.yml.disabled').read_text(encoding='utf-8'), BOOTSTRAP_HEADER + '\n'.join(bodies))

    def test_unaccepted_migration_templates_cannot_register_as_workflows(self):
        workflows = ROOT.parents[1] / '.github' / 'workflows'
        for name in ('local-ci-bootstrap.yml', 'local-runner-smoke.yml'):
            self.assertFalse((workflows / name).exists())
            self.assertTrue((workflows / (name + '.disabled')).is_file())

    def test_all_workflow_jobs_use_github_hosted_runners(self):
        # SINAN_KARARLARI_2026-10-26: the repository is public and CI runs only on
        # GitHub-hosted runners. A self-hosted label here would let a fork's pull
        # request run code on the owner's computer, so none is allowed.
        workflows = ROOT.parents[1] / '.github' / 'workflows'
        allowed = {'runs-on: ubuntu-latest', 'runs-on: windows-latest'}
        count = 0
        # Only active workflows can run; *.disabled templates never register.
        active = sorted([*workflows.glob('*.yml'), *workflows.glob('*.yaml')])
        for path in active:
            for line in path.read_text(encoding='utf-8').splitlines():
                if line.lstrip().startswith('runs-on:'):
                    count += 1
                    self.assertIn(line.strip(), allowed, str(path))
        self.assertGreater(count, 0)


    def test_active_workflows_open_no_cache_secret_environment_or_target_surface(self):
        # SINAN_KARARLARI_2026-10-26_PUBLIC_CI: artifacts carry only short-lived,
        # secret-free CI evidence; cache, secrets, environments and
        # pull_request_target stay closed on the public repository.
        workflows = ROOT.parents[1] / '.github' / 'workflows'
        forbidden = ('actions/cache', 'secrets.', 'environment:', 'pull_request_target')
        active = sorted([*workflows.glob('*.yml'), *workflows.glob('*.yaml')])
        self.assertTrue(active)
        for path in active:
            text = path.read_text(encoding='utf-8')
            for marker in forbidden:
                self.assertNotIn(marker, text, f'{path.name}: {marker}')
            for days in re.findall(r'retention-days:\s*(\d+)', text):
                self.assertLessEqual(int(days), 7, path.name)

if __name__ == '__main__':
    unittest.main()
