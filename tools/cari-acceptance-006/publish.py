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

ROOT = Path(r'C:\src\GoymenERP-cari-acceptance-envelope-20260911')
EVIDENCE = ROOT / 'artifacts/cari-acceptance-006/c006-ac5fc7069528'
DLL = ROOT / 'tools/GoymenERP.CariAcceptance006/bin/Release/net10.0-windows/GoymenERP.CariAcceptance006.dll'
CONTRACT_HEADER = 'X-ERP-Counterparty-Contract'
CONTRACT_VERSION = 'optional-trade-roles-v1'
TEST_OUTPUT = 'tests/GoymenERP.CariAcceptance006.Tests/bin/Release/net10.0-windows/'
TEST_ASSEMBLY = 'GoymenERP.CariAcceptance006.Tests.dll'

def require(condition, message):
    if not condition:
        raise RuntimeError('C006 ' + message)

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def encode(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')

def atomic(name, value):
    destination = EVIDENCE / name
    require(not destination.exists(), 'existing evidence is preserved: ' + name)
    temporary = destination.with_name(destination.name + '.tmp')
    with temporary.open('xb') as stream:
        stream.write(encode(value))
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

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--tests-summary', type=Path, required=True)
    parser.add_argument('--frontend-review', type=Path, required=True)
    args = parser.parse_args()
    require(Path(__file__).resolve().parent.parent.parent == ROOT, 'wrong publisher worktree')
    require(not (EVIDENCE / 'environment.json').exists() and not (EVIDENCE / 'ENV_READY.json').exists(),
            'this run was already published')
    command('dotnet', str(DLL), 'source-check')
    source = read(EVIDENCE / 'source-binding.json')
    require(source['FrontendHead'] != '8f8e241db68b8f071e52d37a33c2176980eed1e8',
            'frontend candidate has open P1 findings; final corrected source and review are required')
    run = read(ROOT / 'tools/cari-acceptance-006/run.json')
    smoke = read(EVIDENCE / 'smoke.json')
    host = read(EVIDENCE / 'host-started.json')
    resources = read(EVIDENCE / 'resources.json')
    tests_bytes = args.tests_summary.read_bytes()
    tests = json.loads(tests_bytes.decode('utf-8-sig'))
    review = read(args.frontend_review)
    tested_artifacts = validate_tested_build(source, host, tests)
    require(review['head'] == source['FrontendHead'] and review['status'] == 'approved'
            and len(set(review['reviewUrls'])) >= 2, 'exact-head frontend review receipt is missing')
    for url in review['reviewUrls']:
        require(url.startswith('https://github.com/sinangym/GoymenERP/pull/233#'), 'unexpected review source')
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
    manifest = {
        'contractId': run['ContractId'], 'taskId': run['TaskId'], 'readyForBrowser': True,
        'preparedAtUtc': datetime.now(timezone.utc).isoformat(), 'runId': run['RunId'],
        'instanceId': run['InstanceId'], 'apiBaseUrl': run['ApiBaseUrl'], 'databaseName': run['DatabaseName'],
        'worktree': str(ROOT), 'sources': source, 'resources': resources, 'host': host,
        'fixtureHash': source['FixtureSha256'], 'baselineSha256': digest(EVIDENCE / 'baseline.json'),
        'smokeSha256': digest(EVIDENCE / 'smoke.json'),
        'backendTests': {'path': str(args.tests_summary.resolve()), 'sha256': hashlib.sha256(tests_bytes).hexdigest(),
                         'executed': tests['backend_executed'], 'testedArtifacts': tested_artifacts,
                         'publisherExecuted': tests['publisher_executed']},
        'frontendReviewReceipt': review, 'reviewReceiptMeaning': 'Coordinator-supplied receipt; not a new independent review.',
        'freshGetObservations': observations, 'companyAId': run['CompanyAId'], 'companyBId': run['CompanyBId'],
        'actor': run['Actor'], 'browserPrefix': run['BrowserPrefix'],
        'clientContractHeader': CONTRACT_HEADER, 'clientContractVersion': CONTRACT_VERSION,
        'commands': {'control': str(ROOT / 'tools/cari-acceptance-006/control.ps1'),
                     'start': '-Action start', 'verify': '-Action verify -ExpectedNewParties 1',
                     'stop': '-Action stop', 'releaseCheck': '-Action release-check', 'teardown': '-Action teardown'},
        'browserAcceptance': 'NOT_RUN', 'erpAccepted': False, 'productionActivated': False,
        'topology': 'One loopback API process and one newly owned disposable SQL container. No mixed-node claim.',
    }
    atomic('environment.json', manifest)
    atomic('ENV_READY.json', {'runId': run['RunId'], 'instanceId': run['InstanceId'],
        'environmentSha256': digest(EVIDENCE / 'environment.json'), 'readyForBrowser': True, 'erpAccepted': False})
    print(json.dumps({'readyForBrowser': True, 'environmentSha256': digest(EVIDENCE / 'environment.json'),
                      'browserAcceptance': 'NOT_RUN', 'erpAccepted': False}))

if __name__ == '__main__':
    main()
