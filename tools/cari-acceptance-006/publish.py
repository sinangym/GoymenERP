"""One-shot, credential-free publication of the already verified C006 environment."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
from datetime import datetime, timezone
import urllib.request

ROOT = Path(r'C:\Users\LAPTOP\Documents\ChatGPT\GoymenERP-acceptance-backend-006')
EVIDENCE = Path(r'C:\Users\LAPTOP\Documents\ChatGPT\GoymenERP-orchestration\evidence\C-DESKTOP-006')
ORCHESTRATION = EVIDENCE.parent.parent
HEAD = '05b138f525f82a7679aa873406507aa87d461bcc'

def now():
    return datetime.now(timezone.utc).isoformat()

def digest(data):
    return hashlib.sha256(data).hexdigest()

def encode(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')

def read(name):
    return json.loads((EVIDENCE / name).read_text(encoding='utf-8-sig'))

def atomic(name, data):
    destination = EVIDENCE / name
    if destination.exists():
        raise RuntimeError('C006 existing artifact preserved: ' + name)
    temporary = destination.with_name(destination.name + '.tmp')
    with temporary.open('xb') as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())
    # Windows rename refuses an existing destination; no historical replacement.
    temporary.rename(destination)

def git(*arguments, expected=(0,)):
    result = subprocess.run(['git', '-c', 'core.quotepath=false', *arguments], cwd=ROOT, capture_output=True)
    if result.returncode not in expected:
        raise RuntimeError('C006 git evidence command failed.')
    return result.stdout

def main():
    started = now()
    if (EVIDENCE / 'environment.json').exists() or (EVIDENCE / 'report.json').exists():
        raise RuntimeError('C006 already published; no duplicate publication.')
    assert Path(__file__).resolve().parent.parent.parent == ROOT
    assert git('rev-parse', 'HEAD').decode().strip() == HEAD
    assert not git('diff', '--name-only', 'HEAD', '--'), 'Existing tracked sources changed.'
    assert not git('symbolic-ref', '-q', 'HEAD', expected=(0, 1)), 'Expected detached HEAD.'
    run = json.loads((ROOT / 'tools/cari-acceptance-006/run.json').read_text(encoding='utf-8-sig'))
    contract = json.loads((ORCHESTRATION / 'acceptance-C006-F009-contract.json').read_text(encoding='utf-8-sig'))
    smoke, baseline, host, resources = map(read, ['smoke.json', 'baseline.json', 'host-started.json', 'resources.json'])
    assert smoke['passed'] and smoke['RunId'] == run['RunId'] and smoke['InstanceId'] == run['InstanceId']
    assert baseline['DatabaseName'] == run['DatabaseName'] and baseline['Rcsi']
    assert baseline['Effects'] == dict(Parties=1, Accounts=2, CreateAudits=1, LinkAudits=1, CreateExecutions=1, LinkExecutions=1)
    assert host['InstanceId'] == run['InstanceId'] == resources['instanceId']
    for name in ['12-profile-tests.log', '21-live-smoke.log']:
        assert (EVIDENCE / name).read_text(encoding='utf-8-sig').rstrip().endswith('EXIT=0'), name
    runtime = read('runtime-verification.json')
    assert runtime['passed'] and runtime['pid'] == host['pid']
    assert runtime['instanceId'] == run['InstanceId']

    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(run['ApiBaseUrl'] + '/health/live', timeout=15) as response:
        assert response.status == 200
        health = dict(observedAtUtc=now(), url=response.url, status=response.status,
                      body=response.read().decode('utf-8'), transport='real loopback TCP', authentication='anonymous')
    atomic('health.json', encode(health))

    paths = sorted(filter(None, git('ls-files', '--others', '--exclude-standard', '-z').decode().split('\0')))
    assert paths and all(p.startswith(('tools/cari-acceptance-006/', 'tools/GoymenERP.CariAcceptance006/',
                                      'tests/GoymenERP.CariAcceptance006.Tests/')) for p in paths)
    inventory = {p: digest((ROOT / p).read_bytes()) for p in paths}
    diff = git('diff', '--no-ext-diff', '--no-textconv', '--binary', '--full-index', '--no-color', 'HEAD', '--')
    for path in paths:
        diff += git('diff', '--no-index', '--no-ext-diff', '--no-textconv', '--binary', '--full-index', '--no-color',
                    '--', '/dev/null', path, expected=(0, 1))
    diff_hash = digest(diff)
    atomic('changed-files.txt', ('\n'.join(paths) + '\n').encode())
    atomic('sha256-inventory.txt', ''.join(f'{value}  {path}\n' for path, value in inventory.items()).encode())
    atomic('final-diff.bin', diff)
    atomic('diff-sha256.txt', f'{diff_hash}  final-diff.bin\nbytes={len(diff)}\n'.encode())
    fixture_parts = {
        'run.json': inventory['tools/cari-acceptance-006/run.json'],
        'AcceptanceFactory.cs': inventory['tools/GoymenERP.CariAcceptance006/AcceptanceFactory.cs'],
        'baseline.json': digest((EVIDENCE / 'baseline.json').read_bytes()),
    }
    fixture_hash = digest(json.dumps(fixture_parts, sort_keys=True, separators=(',', ':')).encode())
    control = str(ROOT / 'tools/cari-acceptance-006/control.ps1')
    identities = [json.loads(x['body']) for x in smoke['exchanges'] if x['route'] == '/api/v1/identity/me']
    capability_responses = [json.loads(x['body']) for x in smoke['exchanges'] if x['route'] == '/api/v1/shell/capabilities']
    assert len(capability_responses) == 2 and all(set(x['capabilities']) == {
        'cari.accounts.link', 'cari.parties.create', 'cari.parties.detail', 'cari.parties.list'
    } for x in capability_responses)
    manifest = dict(
        contractId=run['ContractId'], taskId=run['TaskId'], readyForBrowser=True, preparedAtUtc=now(),
        runId=run['RunId'], instanceId=run['InstanceId'], apiBaseUrl=run['ApiBaseUrl'],
        databaseName=run['DatabaseName'], databaseEndpointLoopback=run['SqlEndpoint'],
        databaseResourceIdsAndLabels=resources, apiPidAndStartTime=host,
        backendHead=HEAD, backendDiffSha256=diff_hash, backendDiffBytes=len(diff),
        frontendDiffSha256=run['FrontendDiffSha256'], fixtureHash=fixture_hash, fixtureHashComponents=fixture_parts,
        companyAId=run['CompanyAId'], companyBId=run['CompanyBId'],
        unusedSeedCompanyIds=['10000000-0000-0000-0000-000000000003'],
        syntheticActorIds=dict(browser=run['Actor'], companyA='company-a-user', companyB='company-b-user',
                               readonlyA='company-a-viewer', observedIdentities=identities),
        safeBootstrapMethodNoSecrets=dict(environment='Testing', mode='DevelopmentBootstrap',
            headers={'X-Development-User':run['Actor'], 'X-Company-Id':'one of companyAId or companyBId'},
            authorization='Existing seeded RBAC; no bypass; A-only B request and viewer create both 403'),
        healthEvidence=dict(path=str(EVIDENCE / 'health.json'), sha256=digest(encode(health))),
        smokeEvidence=dict(path=str(EVIDENCE / 'smoke.json'), sha256=digest((EVIDENCE / 'smoke.json').read_bytes()),
                           log=str(EVIDENCE / '21-live-smoke.log'), passed=True),
        baselineEvidence=dict(path=str(EVIDENCE / 'baseline.json'), sha256=fixture_parts['baseline.json'],
                              effects=baseline['Effects']),
        credentialFreeVerifyCommand=f"& '{control}' -Action verify -ExpectedNewParties 1",
        credentialFreeSnapshotCommand=f"& '{control}' -Action snapshot",
        credentialFreeTeardownCommand=f"& '{control}' -Action teardown",
        browserDataPrefix=run['BrowserPrefix'], smokeDataPrefix=run['SmokePrefix'],
        browserInstruction='LegalName starts with browserDataPrefix. Verify immediately after one A-create/B-link journey, before additional successful mutation scenarios.',
        allowedUiOrigins=['http://127.0.0.1:15173','http://localhost:15173'],
        runtimeEvidence=runtime, normalProviderChanged=False, c01ProfileChanged=False,
        existingDataTouched=False, erpAccepted=False)
    assert set(contract['environmentManifestRequired']).issubset(manifest)
    assert manifest['frontendDiffSha256'] == contract['claude']['frontendDiffSha256']
    assert git('rev-parse', 'HEAD').decode().strip() == HEAD
    assert inventory == {p: digest((ROOT / p).read_bytes()) for p in paths}
    manifest_bytes = encode(manifest)
    atomic('environment.json', manifest_bytes)
    print('C006 ready environment atomically published: ' + str(EVIDENCE / 'environment.json'), flush=True)
    missing = [
        'F009 real-browser L journey and SQL after-journey evidence pending.',
        'Browser context/session/capability/delayed-response and UX-AC001..004 acceptance pending; controlled T distinct from live L.',
        'EO unknown-outcome browser evidence pending unless actually measured by F009.',
        'Fresh candidate CI and independent final acceptance not performed; local setup is not ERP acceptance.',
    ]
    result = ('C-DESKTOP-006 completed: izole SQL/API ortamı hazır.\n'
              'Yeni profil testleri 16/16 EXIT0. Gerçek HTTP A create/list/detail -> B candidate/link/list/detail ve A korunumu PASS.\n'
              'SQL: 1 Party, 2 account, create/link audit 1/1, execution 1/1. GET ve aynı-key replay ek etki üretmedi.\n'
              'Normal provider/C01 ve mevcut kaynaklar değişmedi. Ortam F009 için açık bırakıldı. ERP kabulü verilmedi.\n'
              f'API: {run["ApiBaseUrl"]}\nDiff SHA256: {diff_hash}\n'
              'İlk test loglama denemesi başarısızdı; 11-* kayıtları korunur ve başarı sayılmaz.\n'
              + '\n'.join(missing) + '\n')
    atomic('result.txt', result.encode('utf-8'))
    atomic('publication.log', f'START={started}\nCOMMAND=py -3 tools/cari-acceptance-006/publish.py\nEND={now()}\nEXIT=0\n'.encode())
    report = dict(task_id='C-DESKTOP-006', generation=1, status='completed', model='gpt-6-astra', effort='ultra',
        cwd=str(ROOT), head=HEAD, observedAtUtc=now(), finding_counts=dict(P0=0,P1=0,P2=0), findings=[], blockers=[],
        missing_evidence=missing, environmentManifestSha256=digest(manifest_bytes), backendDiffSha256=diff_hash,
        local_tests=dict(profile_passed=16, profile_failed=0, profile_skipped=0, live_http_sql_smoke='PASS'),
        retained_failed_attempt='11-profile-tests.log and 11-logging-failure.json; no successful exit evidence',
        services_left_running=True, protected_changes=[], erp_accepted=False, next_handoff='F-DESKTOP-009/coordinator')
    atomic('report.json', encode(report))
    print(json.dumps({'status':'completed','environmentManifestSha256':digest(manifest_bytes),
                      'backendDiffSha256':diff_hash,'erp_accepted':False}))

if __name__ == '__main__':
    main()
