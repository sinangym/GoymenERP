"""Pure C006 publication-contract tests; never start or publish an environment."""
import copy
import importlib.util
import json
import tempfile
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


class PublisherFrontendReviewTests(unittest.TestCase):
    def setUp(self):
        self.source = {'FrontendHead': 'a' * 40, 'RunId': 'controlled-run', 'InstanceId': 'controlled-instance'}
        self.review = {
            'head': 'a' * 40, 'status': 'approved', 'pullRequest': 235, 'riskClass': 'K2',
            'requiredIndependentReviews': 1, 'reviewerId': '/root/frontend_final_review_one',
            'reviewerKind': 'ai-non-author', 'authorAgent': 'CLAUDE-FRONTEND',
            'authorIsReviewer': False, 'humanApproval': False, 'openP0': 0, 'openP1': 0, 'openP2': 0,
            'reviewUrls': ['https://github.com/sinangym/GoymenERP/pull/235#issuecomment-123'],
        }
        payload = {key: value for key, value in self.review.items() if key != 'reviewUrls'}
        self.evidence = {
            'comment': {'id': 123, 'user': {'id': 456}, 'html_url': self.review['reviewUrls'][0],
                        'issue_url': 'https://api.github.com/repos/sinangym/GoymenERP/issues/235',
                        'author_association': 'OWNER', 'body': '```json\n' + json.dumps(payload) + '\n```'},
            'pullRequest': {'id': 789, 'number': 235, 'head': {'sha': 'a' * 40},
                            'html_url': 'https://github.com/sinangym/GoymenERP/pull/235', 'body': 'Controlled PR prose'},
            'checkRuns': {'check_runs': [
                {'id': index, 'head_sha': 'a' * 40, 'name': name, 'status': 'completed', 'conclusion': 'success'}
                for index, name in enumerate(PUBLISHER.REQUIRED_FRONTEND_CHECKS, start=1)
            ]},
        }

    def validate(self):
        PUBLISHER.validate_frontend_review(self.source, self.review, self.evidence)

    def projection(self):
        responses = {name: json.dumps(value, ensure_ascii=False) for name, value in self.evidence.items()}
        return PUBLISHER.prepare_frontend_review_evidence(self.source, self.review, self.source, responses)

    def test_pr_token_prose_is_preserved_outside_the_unchanged_browser_marker_gate(self):
        self.evidence['pullRequest']['body'] = 'token yine ayr\u0131\u015ft\u0131r\u0131lm\u0131yor'
        before = copy.deepcopy(self.evidence)
        projection, raw_bytes = self.projection()
        PUBLISHER.require_credential_free_envelope({'frontendReviewEvidence': projection})
        self.assertNotIn('body', projection['pullRequest'])
        self.assertNotIn('body', projection['comment'])
        self.assertEqual(before, self.evidence)
        raw = json.loads(raw_bytes)
        self.assertEqual(json.loads(raw['responses']['pullRequest'])['body'], self.evidence['pullRequest']['body'])
        self.assertEqual(projection['sourceArtifact']['sha256'], PUBLISHER.hashlib.sha256(raw_bytes).hexdigest())
        self.assertEqual(projection['sourceArtifact']['bytes'], len(raw_bytes))
        self.assertEqual(projection['pullRequest']['head']['sha'], self.source['FrontendHead'])
        self.assertEqual(projection['comment']['id'], 123)
        self.assertEqual(projection['comment']['authorId'], 456)
        self.assertEqual(projection['reviewerId'], self.review['reviewerId'])
        self.assertEqual(len(projection['checkRuns']['check_runs']), len(PUBLISHER.REQUIRED_FRONTEND_CHECKS))
        for name, value in raw['responses'].items():
            self.assertEqual(projection['sourceResponseSha256'][name], PUBLISHER.hashlib.sha256(value.encode()).hexdigest())

    def test_actual_credential_shaped_envelope_values_are_still_rejected(self):
        for value in [{'password': 'synthetic-only'}, {'accessToken': 'synthetic-only'},
                      {'connection': 'Server=test;Pwd=synthetic-only'}, {'secret': 'synthetic-only'}]:
            with self.subTest(value=value):
                with self.assertRaisesRegex(RuntimeError, 'credential-like material'):
                    PUBLISHER.require_credential_free_envelope(value)

    def test_projection_cannot_hide_substituted_source_identity_or_a_failed_check(self):
        original = copy.deepcopy(self.evidence)
        for mutation in ['comment-id', 'author-id', 'pr-id', 'pr-url', 'check-id', 'check-failed']:
            with self.subTest(mutation=mutation):
                self.evidence = copy.deepcopy(original)
                if mutation == 'comment-id': self.evidence['comment']['id'] = 999
                elif mutation == 'author-id': self.evidence['comment']['user']['id'] = True
                elif mutation == 'pr-id': self.evidence['pullRequest']['id'] = 0
                elif mutation == 'pr-url': self.evidence['pullRequest']['html_url'] += '?access_token=synthetic-only'
                elif mutation == 'check-id': self.evidence['checkRuns']['check_runs'][0]['id'] = True
                else: self.evidence['checkRuns']['check_runs'][0]['conclusion'] = 'failure'
                with self.assertRaises(RuntimeError):
                    self.projection()

    def test_raw_source_is_immutable_and_retains_the_bytes_bound_by_projection(self):
        projection, raw_bytes = self.projection()
        with tempfile.TemporaryDirectory() as directory, patch.object(PUBLISHER, 'EVIDENCE', Path(directory)):
            PUBLISHER.atomic_bytes('frontend-review-source.json', raw_bytes)
            path = Path(directory) / 'frontend-review-source.json'
            self.assertEqual(path.read_bytes(), raw_bytes)
            self.assertEqual(PUBLISHER.digest(path), projection['sourceArtifact']['sha256'])
            with self.assertRaisesRegex(RuntimeError, 'existing evidence is preserved'):
                PUBLISHER.atomic_bytes('frontend-review-source.json', b'different')
            self.assertEqual(path.read_bytes(), raw_bytes)

    def test_projection_requires_the_complete_strict_json_sources_and_same_run(self):
        responses = {name: json.dumps(value) for name, value in self.evidence.items()}
        with self.assertRaises(RuntimeError):
            PUBLISHER.prepare_frontend_review_evidence(self.source, self.review,
                {**self.source, 'RunId': 'different'}, responses)
        incomplete = dict(responses)
        incomplete.pop('pullRequest')
        with self.assertRaises(RuntimeError):
            PUBLISHER.prepare_frontend_review_evidence(self.source, self.review, self.source, incomplete)
        responses['pullRequest'] = '{"number":235,"number":233}'
        with self.assertRaises(RuntimeError):
            PUBLISHER.prepare_frontend_review_evidence(self.source, self.review, self.source, responses)

    def test_exact_non_author_ai_result_and_fresh_checks_accept_only_the_scoped_profile(self):
        self.validate()
        self.assertFalse(self.review['humanApproval'])

    def test_self_human_or_wrong_reviewer_claim_is_rejected(self):
        original = copy.deepcopy(self.review)
        for field, value in [('authorIsReviewer', True), ('humanApproval', True), ('reviewerId', 'CLAUDE-FRONTEND'),
                             ('reviewerKind', 'human'), ('authorAgent', 'someone-else'), ('requiredIndependentReviews', True)]:
            with self.subTest(field=field):
                self.review = {**original, field: value}
                with self.assertRaises(RuntimeError):
                    self.validate()

    def test_wrong_pr_head_risk_or_open_findings_are_rejected(self):
        original = copy.deepcopy(self.review)
        for field, value in [('pullRequest', 233), ('head', 'b' * 40), ('riskClass', 'K3'),
                             ('status', 'changes_required'), ('openP0', 1), ('openP1', 1), ('openP2', 1)]:
            with self.subTest(field=field):
                self.review = {**original, field: value}
                with self.assertRaises(RuntimeError):
                    self.validate()

    def test_comment_url_alone_or_unrelated_source_cannot_approve(self):
        original = copy.deepcopy(self.evidence)
        for field, value in [('body', 'approved'), ('html_url', 'https://example.test/'),
                             ('issue_url', 'https://api.github.com/repos/sinangym/GoymenERP/issues/233'),
                             ('author_association', 'NONE')]:
            with self.subTest(field=field):
                self.evidence = copy.deepcopy(original)
                self.evidence['comment'][field] = value
                with self.assertRaises(RuntimeError):
                    self.validate()

    def test_actual_comment_result_must_match_the_receipt(self):
        self.evidence['comment']['body'] = self.evidence['comment']['body'].replace('"openP2": 0', '"openP2": 12')
        with self.assertRaises(RuntimeError):
            self.validate()

    def test_github_json_booleans_cannot_impersonate_counts_or_numeric_review_requirements(self):
        original = self.evidence['comment']['body']
        for before, after in [('"requiredIndependentReviews": 1', '"requiredIndependentReviews": true'),
                              ('"openP2": 0', '"openP2": false'), ('"openP1": 0', '"openP1": 0.0'),
                              ('"humanApproval": false', '"humanApproval": 0')]:
            with self.subTest(value=after):
                self.evidence['comment']['body'] = original.replace(before, after)
                with self.assertRaises(RuntimeError):
                    self.validate()

    def test_second_conflicting_or_duplicate_json_block_is_ambiguous(self):
        original = self.evidence['comment']['body']
        for extra in ['```json\n{"status":"changes_required"}\n```', original]:
            with self.subTest(extra=extra):
                self.evidence['comment']['body'] = original + '\n' + extra
                with self.assertRaises(RuntimeError):
                    self.validate()

    def test_duplicate_review_members_cannot_hide_a_conflicting_result(self):
        self.evidence['comment']['body'] = self.evidence['comment']['body'].replace(
            '"status": "approved"', '"status": "changes_required", "status": "approved"')
        with self.assertRaises(RuntimeError):
            self.validate()

    def test_contract_hash_is_the_hash_of_the_exact_validated_bytes(self):
        content = b'{"actual":"validated snapshot"}'
        value = PUBLISHER.hashlib.sha256(content).hexdigest()
        self.assertEqual(PUBLISHER.bind_validated_contract({'contractSha256': value}, content), value)

    def test_changed_contract_bytes_are_rejected_before_publication(self):
        value = PUBLISHER.hashlib.sha256(b'{"actual":"validated snapshot"}').hexdigest()
        for changed in [b'{"actual":"unvalidated replacement"}', b'', b'{"actual":"validated snapshot"}\n']:
            with self.subTest(changed=changed):
                with self.assertRaises(RuntimeError):
                    PUBLISHER.bind_validated_contract({'contractSha256': value}, changed)

    def test_missing_validator_hash_cannot_be_replaced_by_a_fresh_unvalidated_hash(self):
        with self.assertRaises(RuntimeError):
            PUBLISHER.bind_validated_contract({}, b'anything')

    def test_pr_advanced_after_review_is_rejected(self):
        self.evidence['pullRequest']['head']['sha'] = 'b' * 40
        with self.assertRaises(RuntimeError):
            self.validate()

    def test_missing_failed_skipped_stale_or_later_failing_checks_reject(self):
        original = copy.deepcopy(self.evidence['checkRuns']['check_runs'])
        for mutation in ['missing', 'failure', 'skipped', 'pending', 'stale', 'later_failure']:
            with self.subTest(mutation=mutation):
                checks = copy.deepcopy(original)
                if mutation == 'missing': checks.pop()
                elif mutation == 'failure': checks[0]['conclusion'] = 'failure'
                elif mutation == 'skipped': checks[0]['conclusion'] = 'skipped'
                elif mutation == 'pending': checks[0]['status'] = 'in_progress'
                elif mutation == 'stale': checks[0]['head_sha'] = 'b' * 40
                else: checks.append({**checks[0], 'id': 999, 'conclusion': 'failure'})
                self.evidence['checkRuns']['check_runs'] = checks
                with self.assertRaises(RuntimeError):
                    self.validate()

    def test_legacy_233_still_requires_two_urls_and_cannot_be_relabelled_as_235(self):
        review = {'head': 'a' * 40, 'status': 'approved',
                  'reviewUrls': ['https://github.com/sinangym/GoymenERP/pull/233#issuecomment-1']}
        with self.assertRaises(RuntimeError):
            PUBLISHER.validate_frontend_review(self.source, review, pull_request=233)
        review['reviewUrls'].append('https://github.com/sinangym/GoymenERP/pull/233#issuecomment-2')
        PUBLISHER.validate_frontend_review(self.source, review, pull_request=233)
        with self.assertRaises(RuntimeError):
            PUBLISHER.validate_frontend_review(self.source, review, self.evidence)

    def test_l04_prose_review_requires_exact_text_head_attribution_and_fresh_checks(self):
        body = 'Controlled independent source-only L04 review; browser and SQL NOT_RUN.'
        review = {**self.review, 'pullRequest': 250, 'head': PUBLISHER.L04_HEAD,
                  'reviewUrls': ['https://github.com/sinangym/GoymenERP/pull/250#issuecomment-5652534455']}
        source = {**self.source, 'FrontendHead': PUBLISHER.L04_HEAD}
        evidence = copy.deepcopy(self.evidence)
        evidence['comment'].update(id=5652534455, body=body, html_url=review['reviewUrls'][0],
                                  issue_url='https://api.github.com/repos/sinangym/GoymenERP/issues/250')
        evidence['pullRequest'].update(number=250, head={'sha': PUBLISHER.L04_HEAD},
                                      html_url='https://github.com/sinangym/GoymenERP/pull/250')
        for check in evidence['checkRuns']['check_runs']:
            check['head_sha'] = PUBLISHER.L04_HEAD
        with patch.object(PUBLISHER, 'L04_REVIEW_BODY_SHA256', PUBLISHER.hashlib.sha256(body.encode()).hexdigest()):
            PUBLISHER.validate_frontend_review(source, review, evidence, pull_request=250)
            original = copy.deepcopy(evidence)
            for mutation in ['body', 'id', 'head', 'failed']:
                with self.subTest(mutation=mutation):
                    changed = copy.deepcopy(original)
                    if mutation == 'body': changed['comment']['body'] += ' changed'
                    elif mutation == 'id': changed['comment']['id'] = 123
                    elif mutation == 'head': changed['pullRequest']['head']['sha'] = 'b' * 40
                    else: changed['checkRuns']['check_runs'][0]['conclusion'] = 'failure'
                    with self.assertRaises(RuntimeError):
                        PUBLISHER.validate_frontend_review(source, review, changed, pull_request=250)
            responses = {name: json.dumps(value) for name, value in evidence.items()}
            projection, raw = PUBLISHER.prepare_frontend_review_evidence(source, review, source, responses)
            self.assertEqual(projection['pullRequest']['number'], 250)
            self.assertEqual(projection['sourceArtifact']['sha256'], PUBLISHER.hashlib.sha256(raw).hexdigest())
            with self.assertRaises(RuntimeError):
                PUBLISHER.validate_frontend_review(source, {**review, 'humanApproval': True}, evidence, pull_request=250)


if __name__ == '__main__':
    unittest.main()
