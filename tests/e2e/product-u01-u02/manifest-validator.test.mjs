/**
 * Target negatives for the L consumer's validator, against the exact contract in
 * `tools/product-u01-u02/ENVIRONMENT_CONTRACT.md` (7c5d64f).
 *
 * SYNTHETIC, and never anything else: the three files below are hand-built inputs in an
 * OS temp directory that stands in for the fixed acceptance root; nothing is published,
 * no Product request is made, and nothing here is SQL/API/browser acceptance of any
 * kind. The cases measure that a wrong, missing, mismatched, edited or self-approving
 * tuple is REFUSED before anything is claimed, and that the exact tuple binds.
 *
 *   node --test tests/e2e/product-u01-u02/manifest-validator.test.mjs
 */
import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, test } from 'node:test'
import {
  ACCEPTED_FILE, API_BASE_URL, BINDING_ENV, ManifestValidationError, READY_FILE, SOURCE_BINDING_FILE, UI_ORIGIN,
  bindingEnvValue, establishLiveBinding, joinLiveBinding,
} from './manifest-validator.ts'
const join = (env, options) => joinLiveBinding(env, FE_WORKTREE, UI_ORIGIN, options)

const tmp = mkdtempSync(path.join(tmpdir(), 'urun-manifest-validator-'))
after(() => rmSync(tmp, { recursive: true, force: true }))
const sha = (text) => createHash('sha256').update(text).digest('hex')

const SUFFIX = 'a1b2c3d4e5f6'
const RUN_ID = 'product-u0102-' + SUFFIX
const INSTANCE = '7ec92416-5a5d-42ad-9823-3574f27592a8'
const COMPANY = '10000000-0000-0000-0000-000000000001'
const GROUP = 'aaaaaaa1-0000-0000-0000-000000000001'
const ACTOR_ID = '99999999-9999-9999-9999-999999999999'
const UNAUTH_ID = '88888888-8888-8888-8888-888888888888'
const FE_WORKTREE = path.join(tmp, 'frontend-worktree')
const FE_HEAD = '9f5b9bfdef249b9c8e89c6668f66b6aa55c34602'
const H40 = (n) => n.toString(16).padStart(40, '0')
const H64 = (n) => n.toString(16).padStart(64, '0')

function source(over = {}) {
  return {
    worktree: path.join(tmp, 'backend-worktree'),
    acceptanceHead: H40(1), compiledHead: H40(1), backendDependency: H40(2), productDependency: H40(3), backendTree: H40(4),
    openApiSha256: H64(5), migrationSourcesSha256: H64(6),
    frontendWorktree: FE_WORKTREE, frontendHead: FE_HEAD, frontendDiffBase: H40(7), frontendDiffSha256: H64(8),
    fixtureSha256: H64(9), fixturePaths: ['tools/product-u01-u02/run.json', 'tools/product-u01-u02/settings.json'],
    sourcesClean: true,
    diskArtifacts: { 'GoymenERP.Api.dll': H64(10), 'GoymenERP.Modules.Product.Infrastructure.dll': H64(11) },
    ...over,
  }
}
function accepted(base, over = {}) {
  return {
    contractId: 'PRODUCT-U0102-L-v1', taskId: 'U-01-U-02', environmentName: 'Testing',
    runId: RUN_ID, instanceId: INSTANCE, acceptanceRoot: path.join(base, RUN_ID),
    apiBaseUrl: API_BASE_URL, sqlEndpoint: '127.0.0.1,26433', uiOrigin: UI_ORIGIN,
    databaseName: 'GoymenERP_Product_U0102_' + SUFFIX,
    composeProject: RUN_ID, containerName: RUN_ID + '-sql', volumeName: RUN_ID + '-data', networkName: RUN_ID + '-network',
    actor: 'product-global-admin', actorId: ACTOR_ID, unauthorizedActor: 'company-a-user', unauthorizedActorId: UNAUTH_ID,
    companyId: COMPANY, codeGroupId: GROUP, browserPrefix: 'PRODUCT-U0102-' + SUFFIX,
    source: source(),
    erpAccepted: false, productionActivated: false,
    ...over,
  }
}
function binding(a, acceptedSha, over = {}) {
  return {
    contractId: a.contractId, runId: a.runId, instanceId: a.instanceId, acceptedContractSha256: acceptedSha,
    source: a.source,
    ownership: {
      databaseName: a.databaseName, sqlEndpoint: a.sqlEndpoint, apiBaseUrl: a.apiBaseUrl, composeProject: a.composeProject,
      containerId: 'c'.repeat(64), containerName: a.containerName, volumeName: a.volumeName, networkId: 'e'.repeat(64), networkName: a.networkName,
      resourceRunId: a.runId, resourceInstanceId: a.instanceId, hostPid: 4242, hostStartTimeUtc: '2026-09-13T09:31:00Z',
    },
    verifiedAtUtc: '2026-09-13T09:40:00Z',
    ...over,
  }
}
function ready(a, acceptedSha, bindingSha, over = {}) {
  return {
    contractId: a.contractId, taskId: a.taskId, environmentName: a.environmentName, runId: a.runId, instanceId: a.instanceId,
    apiBaseUrl: a.apiBaseUrl, actor: a.actor, unauthorizedActor: a.unauthorizedActor, companyId: a.companyId, codeGroupId: a.codeGroupId, browserPrefix: a.browserPrefix,
    erpAccepted: false, productionActivated: false, readyForBrowser: true,
    acceptedContractSha256: acceptedSha, sourceBindingSha256: bindingSha, verifiedAtUtc: '2026-09-13T09:40:00Z',
    ...over,
  }
}
let n = 0
/** Writes the three synthetic files into `<base>/<runId>/` and returns the env + options a runner would use. */
function tuple({ acceptedOver = {}, bindingOver = {}, readyOver = {}, handoff, frontend } = {}) {
  const base = path.join(tmp, 'root-' + (++n))
  const dir = path.join(base, RUN_ID)
  mkdirSync(dir, { recursive: true })
  const a = accepted(base, acceptedOver)
  const acceptedText = JSON.stringify(a, null, 2) + '\n'
  const acceptedSha = sha(acceptedText)
  const bindingText = JSON.stringify(binding(a, acceptedSha, bindingOver), null, 2) + '\n'
  const bindingSha = sha(bindingText)
  const readyText = JSON.stringify(ready(a, acceptedSha, bindingSha, readyOver), null, 2) + '\n'
  writeFileSync(path.join(dir, ACCEPTED_FILE), acceptedText)
  writeFileSync(path.join(dir, SOURCE_BINDING_FILE), bindingText)
  writeFileSync(path.join(dir, READY_FILE), readyText)
  const env = { URUN_ENVIRONMENT_MANIFEST: path.join(dir, READY_FILE), URUN_ACCEPTED_CONTRACT_SHA256: handoff ?? acceptedSha }
  const options = { acceptanceRootBase: base, observeFrontend: frontend ?? (() => ({ worktree: FE_WORKTREE, head: FE_HEAD, dirtyPaths: [] })) }
  return { env, options, dir, acceptedSha, bindingSha, a }
}
const refused = (fn, code) => assert.throws(fn, (e) => e instanceof ManifestValidationError && e.code === code, 'expected refusal ' + code)

test('the exact tuple binds: three hashes frozen, resource identity and actors from the accepted contract', () => {
  const t = tuple()
  const b = establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options)
  assert.equal(b.acceptedContractSha256, t.acceptedSha)
  assert.equal(b.sourceBindingSha256, t.bindingSha)
  assert.equal(b.manifestSha256.length, 64)
  assert.equal(b.resourceRunId, RUN_ID)
  assert.equal(b.apiBaseUrl, API_BASE_URL)
  assert.equal(b.codeGroupId, GROUP)
  assert.equal(b.actor, 'product-global-admin')
  const joined = join({ ...t.env, [BINDING_ENV]: bindingEnvValue(b) }, t.options)
  assert.deepEqual(joined, b)
})

test('the STARTER always establishes from the files and the handoff hash; an ambient binding never substitutes', () => {
  const t = tuple()
  const good = establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options)
  const forged = bindingEnvValue({ ...good, apiBaseUrl: 'http://127.0.0.1:25080', actor: 'someone-else' })
  // With valid inputs the ambient binding is ignored and the derived one is returned.
  const b = establishLiveBinding({ ...t.env, [BINDING_ENV]: forged }, FE_WORKTREE, UI_ORIGIN, t.options)
  assert.equal(b.apiBaseUrl, API_BASE_URL)
  assert.equal(b.actor, 'product-global-admin')
  // Without the inputs, an ambient binding buys nothing: refused before any claim.
  refused(() => establishLiveBinding({ [BINDING_ENV]: forged }, FE_WORKTREE, UI_ORIGIN, t.options), 'INPUT')
  refused(() => establishLiveBinding({ [BINDING_ENV]: bindingEnvValue(good), URUN_ENVIRONMENT_MANIFEST: t.env.URUN_ENVIRONMENT_MANIFEST }, FE_WORKTREE, UI_ORIGIN, t.options), 'INPUT')
})

for (const [field, value] of [
  ['apiBaseUrl', 'http://127.0.0.1:25080'], ['actor', 'someone-else'], ['unauthorizedActor', 'product-global-admin'], ['uiOrigin', 'http://127.0.0.1:15175'],
  ['resourceRunId', 'product-u0102-000000000000'], ['resourceInstanceId', '11111111-1111-4111-8111-111111111111'], ['codeGroupId', '22222222-2222-4222-8222-222222222222'],
  ['companyId', '33333333-3333-4333-8333-333333333333'], ['browserPrefix', 'PRODUCT-U0102-000000000000'], ['databaseName', 'GoymenCariC006_x'], ['frontendHead', H40(5)],
]) {
  test(`a MEMBER refuses a carried binding whose ${field} was changed while all three file hashes stay correct`, () => {
    const t = tuple()
    const good = establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options)
    const env = { ...t.env, [BINDING_ENV]: bindingEnvValue({ ...good, [field]: value }) }
    refused(() => join(env, t.options), 'TAMPERED')
    // The unchanged binding still joins.
    assert.deepEqual(join({ ...t.env, [BINDING_ENV]: bindingEnvValue(good) }, t.options), good)
  })
}

test('a MEMBER refuses a carried binding with an extra field, and needs the same inputs the starter used', () => {
  const t = tuple()
  const good = establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options)
  refused(() => join({ ...t.env, [BINDING_ENV]: bindingEnvValue({ ...good, proxyOverride: 'x' }) }, t.options), 'TAMPERED')
  refused(() => join({ [BINDING_ENV]: bindingEnvValue(good) }, t.options), 'BINDING')
  refused(() => join({ [BINDING_ENV]: bindingEnvValue(good), URUN_ENVIRONMENT_MANIFEST: t.env.URUN_ENVIRONMENT_MANIFEST }, t.options), 'INPUT')
  const other = tuple()
  // The carried binding names this run's files; the inputs name another run: not this run.
  refused(() => join({ ...other.env, [BINDING_ENV]: bindingEnvValue(good) }, other.options), 'BINDING')
})

test('the handoff hash is required, and a manifest cannot approve itself', () => {
  const t = tuple()
  refused(() => establishLiveBinding({ ...t.env, URUN_ACCEPTED_CONTRACT_SHA256: undefined }, FE_WORKTREE, UI_ORIGIN, t.options), 'INPUT')
  const other = tuple({ handoff: 'f'.repeat(64) })
  refused(() => establishLiveBinding(other.env, FE_WORKTREE, UI_ORIGIN, other.options), 'ACCEPTED_HASH')
})

for (const wrong of ['http://localhost:26080', 'http://127.0.0.1:25080', 'https://127.0.0.1:26080', 'http://10.0.0.5:26080', 'http://127.0.0.1:26080/api', 'http://127.0.0.1:26080/?x=1', 'http://127.0.0.1']) {
  test(`apiBaseUrl ${wrong} is refused — even when every file agrees on it`, () => {
    const t = tuple({ acceptedOver: { apiBaseUrl: wrong } })
    refused(() => establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options), 'ENDPOINT')
  })
}

test('another SQL endpoint, another UI origin, or a run served on another UI port is refused', () => {
  refused(() => { const t = tuple({ acceptedOver: { sqlEndpoint: '127.0.0.1,25433' } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'EXACT')
  refused(() => { const t = tuple({ acceptedOver: { uiOrigin: 'http://127.0.0.1:15175' } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'ENDPOINT')
  refused(() => { const t = tuple(); establishLiveBinding(t.env, FE_WORKTREE, 'http://127.0.0.1:15175', t.options) }, 'ENDPOINT')
})

for (const [name, value, code] of [
  ['erpAccepted', 'false', 'EXACT'], ['erpAccepted', undefined, 'EXACT'], ['productionActivated', 'no', 'EXACT'], ['productionActivated', undefined, 'EXACT'],
  ['contractId', 'CARI-AB-20260905-006-009', 'EXACT'], ['taskId', 'C-DESKTOP-006', 'EXACT'], ['environmentName', 'Development', 'EXACT'],
  ['runId', 'c006-fc721a1b9752', 'FORMAT'], ['instanceId', '00000000-0000-0000-0000-000000000000', 'FORMAT'],
  ['databaseName', 'GoymenCariC006_fc721a1b9752', 'EXACT'], ['containerName', 'goymen-cari-c006-sql', 'EXACT'], ['browserPrefix', 'CARI-JOURNEY-x', 'EXACT'],
  ['unauthorizedActor', 'product-global-admin', 'EXACT'], ['unauthorizedActorId', ACTOR_ID, 'EXACT'], ['codeGroupId', undefined, 'FIELD'],
]) {
  test(`accepted.${name}=${JSON.stringify(value)} is refused (${code}) — C006 constants and stale values never pass as Product values`, () => {
    const t = tuple({ acceptedOver: { [name]: value } })
    refused(() => establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options), code)
  })
}

test('the acceptance root must be the fixed base plus this run id, and the manifest must live inside it', () => {
  const t = tuple({ acceptedOver: { acceptanceRoot: path.join(tmp, 'elsewhere', RUN_ID) } })
  refused(() => establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options), 'EXACT')
  const u = tuple()
  refused(() => establishLiveBinding(u.env, FE_WORKTREE, UI_ORIGIN, { ...u.options, acceptanceRootBase: path.join(tmp, 'other-base') }), 'EXACT')
})

test('the source object: compiledHead ≠ acceptanceHead, unsorted fixture paths, a non-GoymenERP DLL, or sourcesClean not true is refused', () => {
  refused(() => { const t = tuple({ acceptedOver: { source: source({ compiledHead: H40(99) }) } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'EXACT')
  refused(() => { const t = tuple({ acceptedOver: { source: source({ fixturePaths: ['b.json', 'a.json'] }) } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'FORMAT')
  refused(() => { const t = tuple({ acceptedOver: { source: source({ diskArtifacts: { 'System.Text.Json.dll': H64(1) } }) } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'FORMAT')
  refused(() => { const t = tuple({ acceptedOver: { source: source({ sourcesClean: 'true' }) } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'EXACT')
})

test('an observed source that differs from the accepted contract in ANY field, artifact map included, is refused', () => {
  const a = accepted(tmp)
  const t = tuple({ bindingOver: { source: source({ openApiSha256: H64(77) }) } })
  refused(() => establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options), 'SOURCE_MISMATCH')
  const u = tuple({ bindingOver: { source: source({ diskArtifacts: { ...a.source.diskArtifacts, 'GoymenERP.Api.dll': H64(78) } }) } })
  refused(() => establishLiveBinding(u.env, FE_WORKTREE, UI_ORIGIN, u.options), 'SOURCE_MISMATCH')
})

test('the ownership record must name this run’s resources and a real host PID/start; a clean flag alone is nothing', () => {
  const a = accepted(tmp)
  const own = binding(a, 'x').ownership
  refused(() => { const t = tuple({ bindingOver: { ownership: { ...own, resourceRunId: 'product-u0102-000000000000' } } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'EXACT')
  refused(() => { const t = tuple({ bindingOver: { ownership: { ...own, hostPid: 0 } } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'FIELD')
  refused(() => { const t = tuple({ bindingOver: { ownership: { ...own, containerId: 'goymen-sql' } } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'FORMAT')
  refused(() => { const t = tuple({ bindingOver: { ownership: undefined } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'FIELD')
})

test('environment-ready: readyForBrowser alone never passes — sibling hashes, repeated values and the timestamp must agree', () => {
  refused(() => { const t = tuple({ readyOver: { sourceBindingSha256: 'a'.repeat(64) } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'EXACT')
  refused(() => { const t = tuple({ readyOver: { acceptedContractSha256: 'a'.repeat(64) } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'EXACT')
  refused(() => { const t = tuple({ readyOver: { actor: 'someone-else' } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'EXACT')
  refused(() => { const t = tuple({ readyOver: { verifiedAtUtc: '2026-09-13T11:00:00Z' } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'EXACT')
  refused(() => { const t = tuple({ readyOver: { readyForBrowser: 'true' } }); establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options) }, 'EXACT')
})

test('the served frontend must be the approved one: other worktree, other HEAD or a dirty served tree is refused', () => {
  const t = tuple()
  refused(() => establishLiveBinding(t.env, path.join(tmp, 'another-checkout'), UI_ORIGIN, t.options), 'FRONTEND')
  refused(() => establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, { ...t.options, observeFrontend: () => ({ worktree: FE_WORKTREE, head: H40(5), dirtyPaths: [] }) }), 'FRONTEND')
  refused(() => establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, { ...t.options, observeFrontend: () => ({ worktree: FE_WORKTREE, head: FE_HEAD, dirtyPaths: [' M src/frontend/goymen-erp-web/src/product/api.ts'] }) }), 'FRONTEND')
})

test('a file edited after binding is refused by every member that re-reads it', () => {
  const t = tuple()
  const b = establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options)
  writeFileSync(path.join(t.dir, READY_FILE), JSON.stringify({ ...JSON.parse(bindingEnvValue(b)), edited: true }) + '\n')
  refused(() => join({ ...t.env, [BINDING_ENV]: bindingEnvValue(b) }, t.options), 'DRIFT')
  const u = tuple()
  const c = establishLiveBinding(u.env, FE_WORKTREE, UI_ORIGIN, u.options)
  writeFileSync(path.join(u.dir, SOURCE_BINDING_FILE), '{}\n')
  refused(() => join({ ...u.env, [BINDING_ENV]: bindingEnvValue(c) }, u.options), 'DRIFT')
  refused(() => join({}, t.options), 'BINDING')
})

test('a missing sibling, a non-JSON file or a manifest that is not environment-ready.json is refused before anything else', () => {
  const t = tuple()
  rmSync(path.join(t.dir, SOURCE_BINDING_FILE))
  refused(() => establishLiveBinding(t.env, FE_WORKTREE, UI_ORIGIN, t.options), 'INPUT')
  const u = tuple()
  writeFileSync(path.join(u.dir, READY_FILE), 'not json')
  refused(() => establishLiveBinding(u.env, FE_WORKTREE, UI_ORIGIN, u.options), 'INPUT')
  const v = tuple()
  refused(() => establishLiveBinding({ ...v.env, URUN_ENVIRONMENT_MANIFEST: path.join(v.dir, 'environment.json') }, FE_WORKTREE, UI_ORIGIN, v.options), 'INPUT')
})
