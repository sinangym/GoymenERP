"""Publish only this C006 run after live source, process, protocol and SQL checks."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
from datetime import datetime, timezone
import urllib.request

ROOT = Path(r'C:\src\GoymenERP-cari-party-card-backend-20260912')
EVIDENCE = ROOT / 'artifacts/cari-acceptance-006/contact-edit-20260912/c006-db61f4b0aac9'
DLL = ROOT / 'tools/GoymenERP.CariAcceptance006/bin/Release/net10.0-windows/GoymenERP.CariAcceptance006.dll'
CONTRACT_HEADER = 'X-ERP-Counterparty-Contract'
CONTRACT_VERSION = 'optional-trade-roles-v1'
TEST_OUTPUT = 'tests/GoymenERP.CariAcceptance006.Tests/bin/Release/net10.0-windows/'
TEST_ASSEMBLY = 'GoymenERP.CariAcceptance006.Tests.dll'
REQUIRED_FRONTEND_CHECKS = {
    'Backend build, test and migration', 'Frontend lint, test and build',
    'SQL Server relational acceptance', 'Container build',
    'UI catalog structure, references and hashes', 'OpenAPI contract drift',
    'Cari C006 backend envelope', 'C-01 real-browser E2E',
}

def require(condition, message):
    if not condition:
        raise RuntimeError('C006 ' + message)

def read(path):
    return strict_json(path.read_text(encoding='utf-8-sig'))

def strict_json(value):
    def unique(pairs):
        result = {}
        for key, item in pairs:
            require(key not in result, 'duplicate JSON member is ambiguous')
            result[key] = item
        return result
    def constant(_):
        raise ValueError('Non-finite JSON number')
    try:
        return json.loads(value, object_pairs_hook=unique, parse_constant=constant)
    except (ValueError, TypeError) as error:
        raise RuntimeError('C006 malformed JSON evidence') from error

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def encode(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')

def atomic(name, value):
    atomic_bytes(name, encode(value))

def atomic_bytes(name, value):
    destination = EVIDENCE / name
    require(not destination.exists(), 'existing evidence is preserved: ' + name)
    temporary = destination.with_name(destination.name + '.tmp')
    with temporary.open('xb') as stream:
        stream.write(value)
        stream.flush()
        os.fsync(stream.fileno())
    temporary.rename(destination)

def command(*arguments):
    result = subprocess.run(arguments, cwd=ROOT, capture_output=True, text=True, encoding='utf-8')
    require(result.returncode == 0, 'verification command failed; no readiness published')
    return result.stdout

def validate_tested_build(source, host, tests):
    require(isinstance(source, dict) and isinstance(host, dict) and isinstance(tests, dict),
            'build evidence records are missing')
    require(tests.get('mode') == 'BackendVerify'
            and tests.get('status') == 'backend_checks_passed_not_ERP_acceptance',
            'backend tests did not pass')
    require(type(tests.get('backend_executed')) is int and tests['backend_executed'] > 0
            and isinstance(tests.get('source_before'), dict) and isinstance(tests.get('source_after'), dict)
            and tests['source_before'].get('head') == source.get('AcceptanceHead')
            and tests['source_after'].get('head') == source.get('AcceptanceHead')
            and isinstance(source.get('AcceptanceHead'), str)
            and re.fullmatch(r'[0-9a-f]{40}', source['AcceptanceHead']), 'test source is stale')
    require(tests.get('publisher_status') == 'passed'
            and type(tests.get('publisher_executed')) is int and tests['publisher_executed'] > 0,
            'publisher contract tests did not pass')
    require(host.get('sourceBinding') == source, 'host source/build binding differs')
    runtime = source.get('DiskArtifacts')
    require(isinstance(runtime, dict) and bool(runtime), 'runtime artifact inventory is missing')
    valid_name = lambda value: isinstance(value, str) and re.fullmatch(r'GoymenERP(?:\.[A-Za-z0-9_]+)+\.dll', value)
    valid_hash = lambda value: isinstance(value, str) and re.fullmatch(r'[0-9a-f]{64}', value)
    require(all(valid_name(name) and valid_hash(value) for name, value in runtime.items())
            and len({name.casefold() for name in runtime}) == len(runtime)
            and TEST_ASSEMBLY not in runtime, 'runtime artifact inventory is malformed')
    artifacts = tests.get('build_artifacts')
    require(isinstance(artifacts, list) and bool(artifacts), 'tested artifact inventory is missing')
    tested = {}
    for artifact in artifacts:
        require(isinstance(artifact, dict), 'tested artifact record is missing')
        path = artifact.get('path')
        require(isinstance(path, str) and path.startswith(TEST_OUTPUT), 'artifact was not loaded from the test output')
        name = path[len(TEST_OUTPUT):]
        require(valid_name(name) and name.casefold() not in {key.casefold() for key in tested},
                'tested artifact path is malformed or duplicated')
        require(valid_hash(artifact.get('sha256')) and type(artifact.get('bytes')) is int and artifact['bytes'] > 0,
                'tested artifact hash or size is missing')
        tested[name] = artifact['sha256']
    require(set(tested) == set(runtime) | {TEST_ASSEMBLY}, 'tested and runtime artifact inventories differ')
    for name, value in runtime.items():
        require(tested[name] == value, 'tested runtime artifact hash differs: ' + name)
    return tested

def validate_frontend_review(source, review, evidence=None, pull_request=235):
    require(isinstance(review, dict) and review.get('head') == source.get('FrontendHead')
            and review.get('status') == 'approved', 'exact-head frontend review receipt is missing')
    urls = review.get('reviewUrls')
    require(isinstance(urls, list) and all(isinstance(url, str) for url in urls), 'review URLs are missing')
    # Historical #233 required two independent reviews. The new K2 rule is scoped
    # to #235 only, never a generic reduction of the old K3 gate.
    if pull_request == 233:
        require(len(set(urls)) >= 2 and all(url.startswith('https://github.com/sinangym/GoymenERP/pull/233#') for url in urls),
                'historical #233 two-review gate is missing')
        return
    require(pull_request == 235 and review.get('pullRequest') == 235
            and review.get('riskClass') == 'K2' and type(review.get('requiredIndependentReviews')) is int
            and review['requiredIndependentReviews'] == 1 and len(urls) == 1
            and re.fullmatch(r'https://github\.com/sinangym/GoymenERP/pull/235#issuecomment-[0-9]+', urls[0]),
            'unexpected #235 review profile or source')
    require(review.get('reviewerId') == '/root/frontend_final_review_one'
            and review.get('reviewerKind') == 'ai-non-author' and review.get('authorAgent') == 'CLAUDE-FRONTEND'
            and review.get('authorIsReviewer') is False and review.get('humanApproval') is False
            and all(type(review.get(name)) is int and review[name] == 0 for name in ['openP0', 'openP1', 'openP2']),
            'independent AI attribution or clean review result is missing')
    require(isinstance(evidence, dict), 'live GitHub review evidence is missing')
    comment, pr = evidence.get('comment', {}), evidence.get('pullRequest', {})
    require(comment.get('html_url') == urls[0]
            and comment.get('issue_url') == 'https://api.github.com/repos/sinangym/GoymenERP/issues/235'
            and comment.get('author_association') in ['OWNER', 'MEMBER', 'COLLABORATOR']
            and isinstance(comment.get('body'), str) and pr.get('number') == 235
            and pr.get('head', {}).get('sha') == source['FrontendHead'],
            'GitHub comment or current PR source differs from the receipt')
    expected = {key: value for key, value in review.items() if key != 'reviewUrls'}
    blocks = re.findall(r'```json\s*\n(.*?)\n```', comment['body'], re.DOTALL)
    require(len(blocks) == 1, 'GitHub source must contain one unambiguous review JSON block')
    payload = strict_json(blocks[0])
    # JSON encoding preserves booleans versus numbers, unlike Python == (True == 1).
    canonical = lambda value: json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False)
    require(canonical(payload) == canonical(expected), 'GitHub source does not attest this exact typed review result')
    checks = evidence.get('checkRuns', {}).get('check_runs', [])
    require(isinstance(checks, list), 'live frontend check evidence is missing')
    latest = {}
    for check in checks:
        if check.get('head_sha') == source['FrontendHead'] and check.get('name') in REQUIRED_FRONTEND_CHECKS:
            name = check['name']
            if name not in latest or check['id'] > latest[name]['id']:
                latest[name] = check
    require(set(latest) == REQUIRED_FRONTEND_CHECKS
            and all(check.get('status') == 'completed' and check.get('conclusion') == 'success' for check in latest.values()),
            'fresh required frontend checks have not all passed')

def require_credential_free_envelope(value):
    # Keep the existing frontend marker gate, without exceptions for review prose.
    require(not re.search(r'password|secret|token|pwd',
                          json.dumps(value, ensure_ascii=False, allow_nan=False), re.IGNORECASE),
            'browser envelope contains credential-like material')

def prepare_frontend_review_evidence(source, review, run, responses):
    require(set(responses) == {'comment', 'pullRequest', 'checkRuns'}
            and all(isinstance(value, str) for value in responses.values()),
            'complete GitHub response texts are required')
    evidence = {name: strict_json(value) for name, value in responses.items()}
    validate_frontend_review(source, review, evidence)
    require(source.get('RunId') == run.get('RunId')
            and source.get('InstanceId') == run.get('InstanceId')
            and isinstance(run.get('RunId'), str) and isinstance(run.get('InstanceId'), str),
            'GitHub evidence run binding differs')
    comment, pr = evidence['comment'], evidence['pullRequest']
    comment_id = review['reviewUrls'][0].rsplit('-', 1)[1]
    require(type(comment.get('id')) is int and comment['id'] > 0
            and str(comment['id']) == comment_id
            and type(pr.get('id')) is int and pr['id'] > 0
            and pr.get('html_url') == 'https://github.com/sinangym/GoymenERP/pull/235'
            and isinstance(comment.get('user'), dict)
            and type(comment['user'].get('id')) is int and comment['user']['id'] > 0,
            'GitHub projected identities are malformed')
    require(pr.get('body') is None or isinstance(pr.get('body'), str), 'GitHub PR body is malformed')
    latest = {}
    for check in evidence['checkRuns']['check_runs']:
        if check.get('head_sha') == source['FrontendHead'] and check.get('name') in REQUIRED_FRONTEND_CHECKS:
            require(type(check.get('id')) is int and check['id'] > 0, 'GitHub check identity is malformed')
            if check['name'] not in latest or check['id'] > latest[check['name']]['id']:
                latest[check['name']] = check
    raw_record = {
        'runId': run['RunId'], 'instanceId': run['InstanceId'], 'frontendHead': source['FrontendHead'],
        'retrievedAtUtc': datetime.now(timezone.utc).isoformat(),
        'meaning': 'Exact decoded gh api stdout texts used by validation; not an HTTP wire capture.',
        'responses': dict(responses),
    }
    raw_bytes = encode(raw_record)
    body_hash = lambda value: hashlib.sha256(value.encode('utf-8')).hexdigest()
    projection = {
        'pullRequest': {'id': pr['id'], 'number': 235, 'html_url': pr['html_url'],
                        'head': {'sha': source['FrontendHead']}, 'bodySha256': body_hash(pr.get('body') or '')},
        'comment': {'id': comment['id'], 'html_url': comment['html_url'], 'issue_url': comment['issue_url'],
                    'author_association': comment['author_association'], 'authorId': comment['user']['id'],
                    'bodySha256': body_hash(comment['body'])},
        'reviewerId': review['reviewerId'], 'reviewerKind': review['reviewerKind'],
        'authorAgent': review['authorAgent'],
        'checkRuns': {'check_runs': [
            {key: check[key] for key in ['id', 'head_sha', 'name', 'status', 'conclusion']}
            for _, check in sorted(latest.items())
        ]},
        'sourceResponseSha256': {name: body_hash(value) for name, value in responses.items()},
        'sourceArtifact': {'path': str(EVIDENCE / 'frontend-review-source.json'),
                           'sha256': hashlib.sha256(raw_bytes).hexdigest(), 'bytes': len(raw_bytes)},
    }
    require_credential_free_envelope(projection)
    return projection, raw_bytes

def bind_validated_contract(validated, observed_bytes):
    value = validated.get('contractSha256') if isinstance(validated, dict) else None
    require(isinstance(value, str) and re.fullmatch(r'[0-9a-f]{64}', value)
            and hashlib.sha256(observed_bytes).hexdigest() == value,
            'external contract changed after byte-bound validation; no readiness published')
    return value

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--tests-summary', type=Path, required=True)
    parser.add_argument('--frontend-review', type=Path, required=True)
    parser.add_argument('--github-cli', default='gh')
    args = parser.parse_args()
    require(Path(__file__).resolve().parent.parent.parent == ROOT, 'wrong publisher worktree')
    require(not (EVIDENCE / 'environment.json').exists() and not (EVIDENCE / 'ENV_READY.json').exists(),
            'this run was already published')
    command('dotnet', str(DLL), 'source-check')
    source = read(EVIDENCE / 'source-binding.json')
    require(source['FrontendHead'] not in ['8f8e241db68b8f071e52d37a33c2176980eed1e8',
                                         '8273530417964ce8a85873f5b95df091535878bb'],
            'frontend candidate has open findings; final corrected source and review are required')
    run = read(ROOT / 'tools/cari-acceptance-006/run.json')
    smoke = read(EVIDENCE / 'smoke.json')
    host = read(EVIDENCE / 'host-started.json')
    resources = read(EVIDENCE / 'resources.json')
    tests_bytes = args.tests_summary.read_bytes()
    tests = json.loads(tests_bytes.decode('utf-8-sig'))
    review = read(args.frontend_review)
    tested_artifacts = validate_tested_build(source, host, tests)
    urls = review.get('reviewUrls', [])
    require(isinstance(urls, list) and len(urls) == 1 and isinstance(urls[0], str)
            and re.fullmatch(r'https://github\.com/sinangym/GoymenERP/pull/235#issuecomment-[0-9]+', urls[0]),
            'exact #235 review comment URL is required')
    comment_id = urls[0].rsplit('-', 1)[1]
    github_responses = {
        'comment': command(args.github_cli, 'api', 'repos/sinangym/GoymenERP/issues/comments/' + comment_id),
        'pullRequest': command(args.github_cli, 'api', 'repos/sinangym/GoymenERP/pulls/235'),
        'checkRuns': command(args.github_cli, 'api', 'repos/sinangym/GoymenERP/commits/' + source['FrontendHead'] + '/check-runs?per_page=100'),
    }
    github_projection, github_raw_bytes = prepare_frontend_review_evidence(source, review, run, github_responses)
    require(smoke['passed'] and smoke['RunId'] == run['RunId'] and smoke['InstanceId'] == run['InstanceId'],
            'smoke belongs to another run')
    require(host['RunId'] == run['RunId'] and host['InstanceId'] == run['InstanceId']
            and host['executionRunId'] == run['RunId'] and resources['instanceId'] == run['InstanceId']
            and resources['runId'] == run['RunId'] and resources['databaseName'] == run['DatabaseName'],
            'resource or host identity differs')
    started = json.loads(command('dotnet', str(DLL), 'start-check', str(host['pid'])))
    require(started['Started'] and started['recordedPid'] == host['pid'], 'recorded host is not live')
    owners = command('powershell.exe', '-NoProfile', '-NonInteractive', '-Command',
        f"@(Get-NetTCPConnection -State Listen -LocalPort {run['ApiPort']} -ErrorAction Stop | "
        f"Where-Object {{ $_.OwningProcess -eq {host['pid']} }}).Count").strip()
    require(owners.isdigit() and int(owners) >= 1, 'API listener is not owned by the recorded process')
    container = json.loads(command('docker', 'inspect', '--format',
        '{"id":{{json .Id}},"labels":{{json .Config.Labels}}}', run['ContainerName']))
    require(container['id'] == resources['container']['id']
            and container['labels']['com.goymen.acceptance.instance'] == run['InstanceId'],
            'SQL container ownership changed')
    current = json.loads(command('dotnet', str(DLL), 'snapshot'))
    require(current == read(EVIDENCE / 'baseline.json'), 'database changed before browser publication')
    require(current['Rcsi'] and current['DatabaseName'] == run['DatabaseName']
            and len(current['Rows']) == 2 and all(type(row['Roles']) is int and row['Roles'] == 0 for row in current['Rows']),
            'fresh roleless SQL baseline is missing')
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    observations = []
    for company in [run['CompanyAId'], run['CompanyBId']]:
        request = urllib.request.Request(run['ApiBaseUrl'] + '/api/v1/counterparties/?includeCount=true',
            headers={CONTRACT_HEADER: CONTRACT_VERSION, 'X-Development-User': run['Actor'], 'X-Company-Id': company})
        with opener.open(request, timeout=15) as response:
            require(response.status == 200 and response.headers.get(CONTRACT_HEADER) == CONTRACT_VERSION,
                    'fresh successful GET acknowledgment is missing')
            body = json.load(response)
            require(type(body.get('items')) is list and len(body['items']) == 1, 'unexpected fresh list')
            for item in body['items']:
                require(type(item.get('companyAccounts')) is list and len(item['companyAccounts']) == 1,
                        'malformed fresh account projection')
                account = item['companyAccounts'][0]
                require(account.get('companyId') == company and type(account.get('roles')) is int
                        and 0 <= account['roles'] <= 15, 'malformed fresh roles or company scope')
            observations.append({'companyId': company, 'status': 200, 'ack': CONTRACT_VERSION,
                                 'observedAtUtc': datetime.now(timezone.utc).isoformat()})
    command('dotnet', str(DLL), 'source-check')
    require(json.loads(command('dotnet', str(DLL), 'snapshot')) == current, 'readiness GET produced SQL effects')
    contract_path = ROOT / 'artifacts/cari-acceptance-006/contact-edit-20260912/contract.json'
    validated_contract = strict_json(command('dotnet', str(DLL), 'contract-check'))
    contract_hash = bind_validated_contract(validated_contract, contract_path.read_bytes())
    manifest = {
        'contractId': run['ContractId'], 'taskId': run['TaskId'], 'readyForBrowser': True,
        'preparedAtUtc': datetime.now(timezone.utc).isoformat(), 'runId': run['RunId'],
        'instanceId': run['InstanceId'], 'apiBaseUrl': run['ApiBaseUrl'], 'databaseName': run['DatabaseName'],
        'worktree': str(ROOT), 'sources': source, 'resources': resources, 'host': host,
        'fixtureHash': source['FixtureSha256'], 'baselineSha256': digest(EVIDENCE / 'baseline.json'),
        'contractSha256': contract_hash,
        'smokeSha256': digest(EVIDENCE / 'smoke.json'),
        'backendTests': {'path': str(args.tests_summary.resolve()), 'sha256': hashlib.sha256(tests_bytes).hexdigest(),
                         'executed': tests['backend_executed'], 'testedArtifacts': tested_artifacts,
                         'publisherExecuted': tests['publisher_executed']},
        'frontendReviewReceipt': review, 'reviewReceiptMeaning': 'Coordinator-supplied receipt; not a new independent review.',
        'frontendReviewEvidence': github_projection,
        'contactEdit': {'logicalUpdates': 6, 'expectedNewParties': 0, 'expectedDurableResults': 7,
                        'readOnlyActor': 'company-a-user', 'browserVerdict': 'NOT_RUN'},
        'freshGetObservations': observations, 'companyAId': run['CompanyAId'], 'companyBId': run['CompanyBId'],
        'actor': run['Actor'], 'browserPrefix': run['BrowserPrefix'],
        'clientContractHeader': CONTRACT_HEADER, 'clientContractVersion': CONTRACT_VERSION,
        'commands': {'control': str(ROOT / 'tools/cari-acceptance-006/control.ps1'),
                     'start': '-Action start', 'verify': '-Action verify -ExpectedNewParties 1',
                     'verifyContactEdit': '-Action verify-contact-edit -BrowserMeasurement <actual-l03-measurement.json>',
                     'stop': '-Action stop', 'releaseCheck': '-Action release-check', 'teardown': '-Action teardown'},
        'browserAcceptance': 'NOT_RUN', 'erpAccepted': False, 'productionActivated': False,
        'topology': 'One loopback API process and one newly owned disposable SQL container. No mixed-node claim.',
    }
    require_credential_free_envelope(manifest)
    # Preserve validated source texts separately; bind the exact written bytes, not a later reread.
    atomic_bytes('frontend-review-source.json', github_raw_bytes)
    atomic('environment.json', manifest)
    atomic('ENV_READY.json', {'runId': run['RunId'], 'instanceId': run['InstanceId'],
        'environmentSha256': digest(EVIDENCE / 'environment.json'), 'readyForBrowser': True, 'erpAccepted': False})
    print(json.dumps({'readyForBrowser': True, 'environmentSha256': digest(EVIDENCE / 'environment.json'),
                      'browserAcceptance': 'NOT_RUN', 'erpAccepted': False}))

if __name__ == '__main__':
    main()
