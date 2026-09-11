"""Pure C006 publication-contract tests; never start or publish an environment."""
import copy
import importlib.util
import json
from pathlib import Path
import unittest
from unittest.mock import patch

PUBLISHER_PATH = Path(__file__).resolve().parents[2] / 'tools/cari-acceptance-006/publish.py'
SPEC = importlib.util.spec_from_file_location('c006_publisher', PUBLISHER_PATH)
PUBLISHER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PUBLISHER)


class PublisherBuildEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.source = {
            'AcceptanceHead': 'a' * 40,
            'FrontendHead': 'b' * 40,
            'DiskArtifacts': {
                'GoymenERP.Api.dll': '1' * 64,
                'GoymenERP.BuildingBlocks.dll': '2' * 64,
                'GoymenERP.CariAcceptance006.dll': '3' * 64,
            },
        }
        self.host = {'sourceBinding': copy.deepcopy(self.source)}
        self.tests = {
            'mode': 'BackendVerify', 'status': 'backend_checks_passed_not_ERP_acceptance',
            'backend_executed': 114, 'publisher_status': 'passed', 'publisher_executed': 1,
            'source_before': {'head': self.source['AcceptanceHead']},
            'source_after': {'head': self.source['AcceptanceHead']},
            'build_artifacts': [
                {'path': PUBLISHER.TEST_OUTPUT + name, 'sha256': value, 'bytes': 123}
                for name, value in {**self.source['DiskArtifacts'], PUBLISHER.TEST_ASSEMBLY: '4' * 64}.items()
            ],
        }

    def validate(self):
        return PUBLISHER.validate_tested_build(self.source, self.host, self.tests)

    def test_exact_test_output_and_host_build_are_accepted_without_mutating_evidence(self):
        before = copy.deepcopy((self.source, self.host, self.tests))
        self.assertEqual(self.validate(), {**self.source['DiskArtifacts'], PUBLISHER.TEST_ASSEMBLY: '4' * 64})
        self.assertEqual(before, (self.source, self.host, self.tests))

    def test_same_head_with_different_api_build_is_rejected(self):
        self.tests['build_artifacts'][0]['sha256'] = '5' * 64
        with self.assertRaisesRegex(RuntimeError, 'tested runtime artifact hash differs'):
            self.validate()

    def test_same_head_with_different_tool_build_is_rejected(self):
        self.tests['build_artifacts'][2]['sha256'] = '5' * 64
        with self.assertRaises(RuntimeError):
            self.validate()

    def test_missing_runtime_dependency_is_rejected(self):
        self.tests['build_artifacts'].pop(1)
        with self.assertRaises(RuntimeError):
            self.validate()

    def test_missing_test_assembly_is_rejected(self):
        self.tests['build_artifacts'].pop()
        with self.assertRaises(RuntimeError):
            self.validate()

    def test_duplicate_test_artifact_is_rejected(self):
        self.tests['build_artifacts'].append(copy.deepcopy(self.tests['build_artifacts'][0]))
        with self.assertRaises(RuntimeError):
            self.validate()

    def test_case_colliding_artifact_is_rejected(self):
        duplicate = copy.deepcopy(self.tests['build_artifacts'][0])
        duplicate['path'] = PUBLISHER.TEST_OUTPUT + 'GoymenERP.API.dll'
        self.tests['build_artifacts'].append(duplicate)
        with self.assertRaises(RuntimeError):
            self.validate()

    def test_tool_output_or_foreign_path_cannot_claim_the_executed_test_assembly(self):
        original = self.tests['build_artifacts'][0]['path']
        for path in ['tools/GoymenERP.CariAcceptance006/bin/Release/net10.0-windows/GoymenERP.Api.dll',
                     PUBLISHER.TEST_OUTPUT + '../GoymenERP.Api.dll',
                     'C:/another-build/GoymenERP.Api.dll']:
            with self.subTest(path=path):
                self.tests['build_artifacts'][0]['path'] = path
                with self.assertRaises(RuntimeError):
                    self.validate()
        self.tests['build_artifacts'][0]['path'] = original

    def test_unrelated_build_artifact_is_rejected(self):
        self.tests['build_artifacts'].append({
            'path': PUBLISHER.TEST_OUTPUT + 'GoymenERP.Foreign.dll', 'sha256': '6' * 64, 'bytes': 123,
        })
        with self.assertRaises(RuntimeError):
            self.validate()

    def test_missing_or_empty_artifact_records_are_rejected(self):
        for artifacts in [None, [], [None], [{}]]:
            with self.subTest(artifacts=artifacts):
                self.tests['build_artifacts'] = artifacts
                with self.assertRaises(RuntimeError):
                    self.validate()

    def test_invalid_hash_or_size_is_rejected(self):
        original = copy.deepcopy(self.tests['build_artifacts'][0])
        for field, value in [('sha256', None), ('sha256', 'a' * 63), ('sha256', 'A' * 64),
                             ('bytes', 0), ('bytes', True), ('bytes', '123')]:
            with self.subTest(field=field, value=value):
                self.tests['build_artifacts'][0] = {**original, field: value}
                with self.assertRaises(RuntimeError):
                    self.validate()

    def test_missing_or_malformed_runtime_inventory_is_rejected(self):
        for runtime in [None, {}, {'GoymenERP.Api.dll': None}, {'../GoymenERP.Api.dll': '1' * 64}]:
            with self.subTest(runtime=runtime):
                self.source['DiskArtifacts'] = runtime
                self.host['sourceBinding'] = copy.deepcopy(self.source)
                with self.assertRaises(RuntimeError):
                    self.validate()

    def test_host_started_with_another_build_is_rejected(self):
        self.host['sourceBinding']['DiskArtifacts']['GoymenERP.Api.dll'] = '7' * 64
        with self.assertRaisesRegex(RuntimeError, 'host source/build binding differs'):
            self.validate()

    def test_missing_host_binding_is_rejected(self):
        self.host.clear()
        with self.assertRaises(RuntimeError):
            self.validate()

    def test_summary_from_another_head_is_rejected(self):
        for key in ['source_before', 'source_after']:
            with self.subTest(key=key):
                self.tests[key]['head'] = 'c' * 40
                with self.assertRaises(RuntimeError):
                    self.validate()
                self.tests[key]['head'] = self.source['AcceptanceHead']

    def test_failed_or_empty_test_summary_is_rejected(self):
        original = copy.deepcopy(self.tests)
        for field, value in [('status', 'failed'), ('mode', 'Verify'), ('backend_executed', 0),
                             ('backend_executed', True), ('publisher_status', 'skipped'), ('publisher_executed', 0)]:
            with self.subTest(field=field, value=value):
                self.tests = {**original, field: value}
                with self.assertRaises(RuntimeError):
                    self.validate()

    def test_legacy_two_artifact_receipt_is_not_relabelled_as_a_tested_runtime_build(self):
        self.tests['build_artifacts'] = self.tests['build_artifacts'][2:]
        self.tests['build_artifacts'][0]['path'] = (
            'tools/GoymenERP.CariAcceptance006/bin/Release/net10.0-windows/GoymenERP.CariAcceptance006.dll')
        with self.assertRaises(RuntimeError):
            self.validate()

    def test_build_mismatch_stops_main_before_process_sql_http_or_publication(self):
        self.tests['build_artifacts'][0]['sha256'] = '5' * 64
        documents = {'source-binding.json': self.source, 'host-started.json': self.host,
                     'run.json': {}, 'smoke.json': {}, 'resources.json': {}, 'review.json': {}}
        with patch('sys.argv', ['publish.py', '--tests-summary', 'summary.json', '--frontend-review', 'review.json']), \
             patch.object(PUBLISHER, 'ROOT', PUBLISHER_PATH.parent.parent.parent), \
             patch.object(Path, 'exists', return_value=False), \
             patch.object(Path, 'read_bytes', return_value=json.dumps(self.tests).encode()), \
             patch.object(PUBLISHER, 'read', side_effect=lambda path: documents[path.name]), \
             patch.object(PUBLISHER, 'command', return_value='') as command, \
             patch.object(PUBLISHER, 'atomic') as atomic, \
             patch.object(PUBLISHER.urllib.request, 'build_opener') as http:
            with self.assertRaisesRegex(RuntimeError, 'tested runtime artifact hash differs'):
                PUBLISHER.main()
            command.assert_called_once_with('dotnet', str(PUBLISHER.DLL), 'source-check')
            atomic.assert_not_called()
            http.assert_not_called()


if __name__ == '__main__':
    unittest.main()
