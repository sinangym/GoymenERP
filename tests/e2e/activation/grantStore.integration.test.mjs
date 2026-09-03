/**
 * Fault-injected paths for revoke/restore/verify (#199 P1-3, Codex review of #203).
 *
 * The binding tests next door are all pre-connect, and Codex was right that this
 * is not evidence for the paths #199 actually named: a half-applied delete, a
 * restore after the test process was killed, a restore pointed at the wrong
 * target, and a repeated restore. Those need the SQL layer to misbehave on
 * demand, so the connector is injected and made to fail where it matters.
 *
 * "The happy path restored twice" is not kill-path evidence, which is why these
 * exist separately.
 */

import { test, beforeEach } from 'node:test'
import assert from 'node:assert/strict'
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import {
  revokeReadGrant,
  restoreFromPreimage,
  verifyAgainstPreimage,
  PREIMAGE_FILENAME,
  READ_PERMISSION,
} from './grantStore.mjs'

const ROLE = '11111111-2222-3333-4444-555555555555'
const TARGET = {
  connectionString:
    'Server=127.0.0.1,11433;Database=GoymenErpC01_42_1;User Id=sa;Password=x;Encrypt=True',
  database: 'GoymenErpC01_42_1',
  actorId: 'group-bootstrap-admin',
}

function env({ disposable = 'CONFIRMED', runId = '42', project = 'goymen-c01-42-1' } = {}) {
  process.env.GOYMEN_ERP_C01_E2E_DISPOSABLE = disposable
  process.env.GOYMEN_ERP_C01_E2E_RUN_ID = runId
  process.env.GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT = project
  process.env.C01_E2E_SQL_PORT = '11433'
}

function preimage(overrides = {}) {
  return {
    runId: '42',
    attempt: '1',
    composeProject: 'goymen-c01-42-1',
    database: TARGET.database,
    actorId: TARGET.actorId,
    revokedAt: new Date().toISOString(),
    rows: [{ RoleId: ROLE, PermissionCode: READ_PERMISSION }],
    ...overrides,
  }
}

/**
 * A pool that records what was asked of it, and can be told to throw on the Nth
 * write so a half-applied mutation becomes reproducible.
 */
function fakeDeps({
  rows = [{ RoleId: ROLE, PermissionCode: READ_PERMISSION }],
  throwOnWrite = 0,
  actorRoleIds = [ROLE],
} = {}) {
  const log = { pools: 0, queries: [], commits: 0, rollbacks: 0, closed: 0 }
  let writes = 0
  const chain = (sink) => {
    const request = {
      input: () => request,
      query: async (text) => {
        sink.push(text)
        if (/DELETE|INSERT/.test(text)) {
          writes += 1
          if (throwOnWrite !== 0 && writes === throwOnWrite) {
            throw new Error('injected write failure')
          }
        }
        if (/FROM \[identity\]\.\[RoleAssignments\]/.test(text) && !/RolePermissions/.test(text)) {
          return { recordset: actorRoleIds.map((RoleId) => ({ RoleId })) }
        }
        return { recordset: /SELECT/.test(text) ? rows : [] }
      },
    }
    return request
  }
  const deps = {
    createPool: async (connectionString) => {
      log.pools += 1
      log.connectionString = connectionString
      return {
        request: () => chain(log.queries),
        close: async () => {
          log.closed += 1
        },
      }
    },
    Transaction: class {
      async begin() {}
      async commit() {
        log.commits += 1
      }
      async rollback() {
        log.rollbacks += 1
      }
    },
    Request: class {
      constructor() {
        return chain(log.queries)
      }
    },
    log,
  }
  return deps
}

let dir
beforeEach(() => {
  env()
  dir = mkdtempSync(join(tmpdir(), 'activation-'))
})

function writePreimage(value) {
  writeFileSync(join(dir, PREIMAGE_FILENAME), JSON.stringify(value), 'utf8')
}

test('a half-applied revoke rolls back rather than leaving the grant torn', async () => {
  const deps = fakeDeps({
    rows: [
      { RoleId: ROLE, PermissionCode: READ_PERMISSION },
      { RoleId: '99999999-2222-3333-4444-555555555555', PermissionCode: READ_PERMISSION },
    ],
    throwOnWrite: 2,
  })
  await assert.rejects(
    () => revokeReadGrant({ ...TARGET, artifactsDir: dir }, deps),
    /injected write failure/,
  )
  assert.equal(deps.log.rollbacks, 1, 'the transaction was not rolled back')
  assert.equal(deps.log.commits, 0, 'a torn delete was committed')
  rmSync(dir, { recursive: true, force: true })
})

test('restore works from the preimage alone, as it must after a killed process', async () => {
  // No in-memory state exists here at all: this is a fresh process reading a
  // file a dead one left behind, which is the whole point of the durable copy.
  writePreimage(preimage())
  const deps = fakeDeps()
  const result = await restoreFromPreimage(TARGET, dir, deps)
  assert.equal(result.restored, 1)
  assert.equal(deps.log.commits, 1)
  assert.match(deps.log.queries.join(' '), /INSERT INTO \[identity\]\.\[RolePermissions\]/)
  rmSync(dir, { recursive: true, force: true })
})

test('a repeated restore is a no-op, not a second conflicting write', async () => {
  writePreimage(preimage())
  const first = fakeDeps()
  const second = fakeDeps()
  await restoreFromPreimage(TARGET, dir, first)
  const again = await restoreFromPreimage(TARGET, dir, second)
  assert.equal(again.restored, 1)
  // The guard is in the SQL: every write is IF NOT EXISTS, so a second run
  // cannot duplicate a row.
  assert.match(second.log.queries.join(' '), /IF NOT EXISTS/)
  rmSync(dir, { recursive: true, force: true })
})

test('restore refuses a non-loopback connection BEFORE connecting', async () => {
  writePreimage(preimage())
  const deps = fakeDeps()
  await assert.rejects(
    () =>
      restoreFromPreimage(
        { ...TARGET, connectionString: TARGET.connectionString.replace('127.0.0.1', 'db.prod') },
        dir,
        deps,
      ),
    /non-loopback server/,
  )
  assert.equal(deps.log.pools, 0, 'it connected before refusing')
  rmSync(dir, { recursive: true, force: true })
})

test('restore refuses a missing confirmation token BEFORE connecting', async () => {
  writePreimage(preimage())
  env({ disposable: 'yes' })
  const deps = fakeDeps()
  await assert.rejects(() => restoreFromPreimage(TARGET, dir, deps), /DISPOSABLE is yes/)
  assert.equal(deps.log.pools, 0, 'it connected before refusing')
  rmSync(dir, { recursive: true, force: true })
})

test('restore refuses a preimage taken from another run', async () => {
  writePreimage(preimage({ runId: '99', composeProject: 'goymen-c01-99-1' }))
  const deps = fakeDeps()
  await assert.rejects(() => restoreFromPreimage(TARGET, dir, deps), /different target: runId/)
  assert.equal(deps.log.pools, 0)
  rmSync(dir, { recursive: true, force: true })
})

test('restore refuses a preimage for another database', async () => {
  writePreimage(preimage({ database: 'GoymenErpC01_99_1' }))
  const deps = fakeDeps()
  await assert.rejects(() => restoreFromPreimage(TARGET, dir, deps), /different target: database/)
  assert.equal(deps.log.pools, 0)
  rmSync(dir, { recursive: true, force: true })
})

test('restore refuses a preimage for another actor', async () => {
  writePreimage(preimage({ actorId: 'company-a-user' }))
  const deps = fakeDeps()
  await assert.rejects(() => restoreFromPreimage(TARGET, dir, deps), /different target: actorId/)
  assert.equal(deps.log.pools, 0)
  rmSync(dir, { recursive: true, force: true })
})

test('restore refuses a preimage carrying a permission this tool does not own', async () => {
  // A file is durable input from a previous process, not truth. Widening the
  // blast radius by trusting whatever it names is how a cleanup becomes a
  // privilege-granting tool.
  writePreimage(preimage({ rows: [{ RoleId: ROLE, PermissionCode: 'identity.roles.admin' }] }))
  const deps = fakeDeps()
  await assert.rejects(() => restoreFromPreimage(TARGET, dir, deps), /this tool only handles/)
  assert.equal(deps.log.pools, 0)
  rmSync(dir, { recursive: true, force: true })
})

test('restore refuses a preimage row whose role id is not a guid', async () => {
  writePreimage(preimage({ rows: [{ RoleId: "'; DROP TABLE x--", PermissionCode: READ_PERMISSION }] }))
  const deps = fakeDeps()
  await assert.rejects(() => restoreFromPreimage(TARGET, dir, deps), /role id/)
  assert.equal(deps.log.pools, 0)
  rmSync(dir, { recursive: true, force: true })
})

test('verify enforces the same identities, also before connecting', async () => {
  writePreimage(preimage({ database: 'GoymenErpC01_99_1' }))
  const deps = fakeDeps()
  await assert.rejects(() => verifyAgainstPreimage(TARGET, dir, deps), /different target: database/)
  assert.equal(deps.log.pools, 0, 'verify connected before refusing')
  rmSync(dir, { recursive: true, force: true })
})

test('verify fails when a row did not come back', async () => {
  writePreimage(preimage())
  const deps = fakeDeps({ rows: [] })
  await assert.rejects(() => verifyAgainstPreimage(TARGET, dir, deps), /1 missing/)
  rmSync(dir, { recursive: true, force: true })
})

test('verify fails when a row that was never revoked is present', async () => {
  writePreimage(preimage())
  const deps = fakeDeps({
    rows: [
      { RoleId: ROLE, PermissionCode: READ_PERMISSION },
      { RoleId: '77777777-2222-3333-4444-555555555555', PermissionCode: READ_PERMISSION },
    ],
  })
  await assert.rejects(() => verifyAgainstPreimage(TARGET, dir, deps), /1 unexpected/)
  rmSync(dir, { recursive: true, force: true })
})

test('with no preimage both paths are quiet no-ops', async () => {
  const deps = fakeDeps()
  assert.equal((await restoreFromPreimage(TARGET, dir, deps)).restored, 0)
  assert.equal((await verifyAgainstPreimage(TARGET, dir, deps)).verified, 0)
  assert.equal(deps.log.pools, 0)
  rmSync(dir, { recursive: true, force: true })
})

test('restore refuses a preimage naming a role this actor does not hold', async () => {
  // A syntactically valid GUID is not authority. Without this the restore would
  // hand counterparty.parties.read to whatever role a tampered file named.
  writePreimage(preimage())
  const deps = fakeDeps({ actorRoleIds: ['00000000-2222-3333-4444-555555555555'] })
  await assert.rejects(
    () => restoreFromPreimage(TARGET, dir, deps),
    /is not assigned to group-bootstrap-admin/,
  )
  assert.equal(deps.log.commits, 0, 'it granted before proving membership')
  rmSync(dir, { recursive: true, force: true })
})

test('a killed process still leaves a restorable, correctly ordered preimage', async () => {
  // #203 review P1-4. Everything above runs in one process; this one does not.
  const { spawnSync } = await import('node:child_process')
  const { readFileSync, existsSync } = await import('node:fs')

  writeFileSync(
    join(dir, 'fake-sql-state.json'),
    JSON.stringify({
      connectionString: TARGET.connectionString,
      actorId: TARGET.actorId,
      rows: [{ RoleId: ROLE, PermissionCode: READ_PERMISSION }],
      actorRoles: [ROLE],
    }),
    'utf8',
  )

  const child = spawnSync(
    process.execPath,
    [join('activation', 'killHarness.mjs'), dir, 'kill'],
    { env: { ...process.env }, encoding: 'utf8' },
  )

  // It must have died, not returned. A clean exit would mean the kill never
  // happened and every assertion below would be measuring the wrong thing.
  assert.notEqual(child.status, 0, `the harness exited cleanly: ${child.stderr}`)

  const order = JSON.parse(readFileSync(join(dir, 'fake-sql-order.json'), 'utf8'))
  assert.equal(order.deletes.length, 1, 'the child never issued the delete')
  assert.equal(
    order.deletes[0].preimageExisted,
    true,
    'the DELETE ran before the preimage was durable -- a real kill here would lose it',
  )
  assert.equal(order.commits, 1, 'the delete was not committed before the kill')

  // The dead process restored nothing; the file is what survives.
  assert.equal(existsSync(join(dir, PREIMAGE_FILENAME)), true, 'no durable preimage survived')

  // An INDEPENDENT process (this one) restores from it, and a second restore is
  // a no-op rather than a conflicting write.
  const cleanup = fakeDeps()
  const first = await restoreFromPreimage(TARGET, dir, cleanup)
  assert.equal(first.restored, 1)
  const again = await restoreFromPreimage(TARGET, dir, fakeDeps())
  assert.equal(again.restored, 1)
  assert.match(cleanup.log.queries.join(' '), /IF NOT EXISTS/)

  rmSync(dir, { recursive: true, force: true })
})
