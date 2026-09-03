/**
 * Negative cases for the mutation bindings (#199 P1-3).
 *
 * These never touch SQL. Every case here is refused BEFORE a connection is
 * opened, which is the property worth testing: a binding that only fails after
 * connecting has already told an attacker the server is reachable, and a binding
 * that fails after the first DELETE is not a binding at all.
 */

import { test, beforeEach } from 'node:test'
import assert from 'node:assert/strict'
import { assertDisposable, runIdentity } from './grantStore.mjs'

const GOOD = {
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

beforeEach(() => env())

test('the disposable run is authorized', () => {
  const identity = assertDisposable(GOOD)
  assert.equal(identity.runId, '42')
  assert.equal(identity.attempt, '1')
})

test('a missing confirmation token is refused', () => {
  env({ disposable: 'yes' })
  assert.throws(() => assertDisposable(GOOD), /GOYMEN_ERP_C01_E2E_DISPOSABLE is yes/)
})

test('a non-loopback server is refused', () => {
  const target = { ...GOOD, connectionString: GOOD.connectionString.replace('127.0.0.1', 'db.prod') }
  assert.throws(() => assertDisposable(target), /non-loopback server/)
})

test('a caller-supplied database that disagrees with the run is refused', () => {
  // The database is DERIVED from run/attempt now; a caller may only agree with
  // it. Before, two attacker-controlled inputs were checked against each other.
  assert.throws(
    () => assertDisposable({ ...GOOD, database: 'GoymenErpC01_99_1' }),
    /caller declares database GoymenErpC01_99_1 but run 42\/1 derives/,
  )
})

test('a connection string aliasing the target elsewhere is refused', () => {
  // The mssql driver honours `Data Source` and `Initial Catalog`; the old hand
  // parser read only `Server`/`Database`, so this string looked local and
  // connected remote.
  assert.throws(
    () =>
      assertDisposable({
        ...GOOD,
        connectionString:
          `${GOOD.connectionString};Data Source=prod.example.com;Initial Catalog=master`,
      }),
    /declares server twice|declares database twice/,
  )
})

test('a repeated key is refused rather than resolved', () => {
  assert.throws(
    () => assertDisposable({ ...GOOD, connectionString: `${GOOD.connectionString};Database=master` }),
    /repeats database/,
  )
})

test('a connection on the wrong port is refused', () => {
  assert.throws(
    () =>
      assertDisposable({
        ...GOOD,
        connectionString: GOOD.connectionString.replace('11433', '1433'),
      }),
    /publishes 11433/,
  )
})

test('a compose project belonging to another run is refused', () => {
  env({ runId: '42', project: 'goymen-c01-99-1' })
  assert.throws(() => assertDisposable(GOOD), /is not bound to run 42/)
})

test('a compose project with no attempt is refused', () => {
  env({ project: 'goymen-c01-42-' })
  assert.throws(() => assertDisposable(GOOD), /no numeric attempt/)
})

test('a non-numeric run id is refused', () => {
  env({ runId: 'main', project: 'goymen-c01-main-1' })
  assert.throws(() => assertDisposable(GOOD), /is not numeric/)
})

test('an absent run identity is refused rather than defaulted', () => {
  delete process.env.GOYMEN_ERP_C01_E2E_RUN_ID
  assert.throws(() => runIdentity(), /GOYMEN_ERP_C01_E2E_RUN_ID is not set/)
})
