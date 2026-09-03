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

test('a database the run did not declare is refused', () => {
  // A confirmation token left in the environment must not authorize a database
  // it was not issued for.
  assert.throws(
    () => assertDisposable({ ...GOOD, database: 'GoymenErpC01_99_1' }),
    /but the disposable run declares/,
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
