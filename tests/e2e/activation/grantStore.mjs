/**
 * Revoking and restoring a real grant, bound to one disposable run (#199 P1-3).
 *
 * WHY THIS IS ONE FILE AND NOT TWO. The cleanup has to be runnable from a
 * workflow `always()` step with no browser and no Playwright, while the test
 * needs the same SQL. Writing it twice -- once in TS for the spec, once in JS
 * for the cleanup -- is how the two drift until the cleanup silently stops
 * matching what the test did. So the implementation lives here, in plain ESM,
 * and both the spec and the CLI import it.
 *
 * WHAT CHANGED AFTER #199 P1-3. The version before this held the preimage only
 * in process memory and restored it in a `finally`. Kill the process and the
 * `finally` never runs; the outer cleanup only did `docker compose down -v`, so
 * a revoked grant vanished with the stack instead of being put back and checked.
 *
 * WHAT CHANGED AFTER CODEX'S REVIEW OF #203. The bindings guarded the DELETE
 * and nothing else. `restoreFromPreimage` INSERTs into `RolePermissions`, and it
 * checked only that the preimage named this compose project -- no confirmation
 * token, no loopback check, no database check -- so a wrong or corrupted
 * `ACTIVATION_E2E_SQL_CONNECTION` could have written preimage rows into another
 * database entirely. That is the same hole I had already closed on the revoke
 * path and failed to carry across. Every entry point now runs the SAME validator
 * BEFORE it connects, and the preimage must additionally match the target it is
 * being applied to.
 *
 * Six bindings now stand between a caller and any write:
 *
 *  1. `GOYMEN_ERP_C01_E2E_DISPOSABLE` must literally be `CONFIRMED`.
 *  2. The server must be on loopback.
 *  3. The database name must match the disposable run's.
 *  4. The compose project must be `goymen-c01-<runId>-<attempt>` -- the same
 *     envelope `C01E2eShellCapabilityActivation` validates before it will serve
 *     the overridden capability.
 *  5. Only the roles the test actor actually holds are touched.
 *  6. On restore and verify, the preimage's own run/attempt/project/database/
 *     actor must equal the target's, and its rows must carry only the expected
 *     permission code.
 */

import sql from 'mssql'
import { readFileSync, writeFileSync, existsSync } from 'node:fs'

export const READ_PERMISSION = 'counterparty.parties.read'
export const PREIMAGE_FILENAME = 'activation-grant-preimage.json'

const DISPOSABLE_TOKEN = 'CONFIRMED'
const LOOPBACK = new Set(['127.0.0.1', 'localhost', '::1'])
const NUMERIC = /^[0-9]+$/
const GUID = /^[0-9a-fA-F-]{36}$/

/** Injectable so the failure paths can be tested without a live SQL Server. */
const defaultDeps = {
  createPool: (connectionString) => new sql.ConnectionPool(connectionString).connect(),
  Transaction: sql.Transaction,
  Request: sql.Request,
}

/**
 * Canonical connection-string parse (#203 review P1-1).
 *
 * The version this replaces read only the literal `server` and `database` keys.
 * The pinned `mssql` driver also honours `Data Source` and `Initial Catalog`,
 * and resolves them LATER -- so a string carrying loopback `Server` plus the
 * expected `Database`, and also `Data Source=prod.example.com` and
 * `Initial Catalog=master`, passed the guard while the driver connected
 * somewhere else entirely. A guard that reads a different string than the
 * driver does is not a guard.
 *
 * The answer here is not to imitate the driver's precedence -- that is a race
 * to track someone else's parser. It is to REFUSE ambiguity: any repeated key,
 * and any two different aliases for the same concept, is a hard failure before
 * anything connects.
 */
const SERVER_ALIASES = new Set(['server', 'data source', 'addr', 'address', 'network address'])
const DATABASE_ALIASES = new Set(['database', 'initial catalog'])

function parse(connectionString) {
  const seen = new Map()
  for (const segment of connectionString.split(';')) {
    if (segment.trim() === '') continue
    const index = segment.indexOf('=')
    if (index <= 0) {
      throw new Error(`refusing to mutate grants: unparsable connection segment ${segment.trim()}`)
    }
    const key = segment.slice(0, index).trim().toLowerCase()
    if (seen.has(key)) {
      throw new Error(`refusing to mutate grants: connection string repeats ${key}`)
    }
    seen.set(key, segment.slice(index + 1).trim())
  }
  const resolve = (aliases, label) => {
    const present = [...aliases].filter((alias) => seen.has(alias))
    if (present.length === 0) {
      throw new Error(`refusing to mutate grants: connection string declares no ${label}`)
    }
    if (present.length > 1) {
      throw new Error(
        `refusing to mutate grants: connection string declares ${label} twice as ` +
          `${present.join(' and ')}; the driver would pick one and this guard the other`,
      )
    }
    return seen.get(present[0])
  }
  return { server: resolve(SERVER_ALIASES, 'server'), database: resolve(DATABASE_ALIASES, 'database') }
}

function required(name) {
  const value = process.env[name]
  if (!value || !value.trim()) {
    throw new Error(`refusing to mutate grants: ${name} is not set`)
  }
  return value.trim()
}

/** The identity this mutation is authorized for, derived from the run itself. */
export function runIdentity() {
  const runId = required('GOYMEN_ERP_C01_E2E_RUN_ID')
  const composeProject = required('GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT')
  if (!NUMERIC.test(runId)) {
    throw new Error(`refusing to mutate grants: run id ${runId} is not numeric`)
  }
  const expectedPrefix = `goymen-c01-${runId}-`
  if (!composeProject.startsWith(expectedPrefix)) {
    throw new Error(
      `refusing to mutate grants: compose project ${composeProject} is not bound to run ${runId}`,
    )
  }
  const attempt = composeProject.slice(expectedPrefix.length)
  if (!NUMERIC.test(attempt)) {
    throw new Error('refusing to mutate grants: compose project has no numeric attempt')
  }
  // Derived, never caller-supplied. The workflow builds both from the same two
  // values, so deriving them here removes the caller's ability to point the
  // mutation at another database by passing different metadata (#203 review
  // P1-1).
  return {
    runId,
    attempt,
    composeProject,
    database: `GoymenErpC01_${runId}_${attempt}`,
  }
}

/**
 * The one validator. Every entry point that writes calls this before it opens a
 * connection -- a binding that only fails after connecting has already told a
 * caller the server is reachable, and one that fails after the first write is
 * not a binding at all.
 */
export function assertDisposable(target) {
  const confirmation = process.env.GOYMEN_ERP_C01_E2E_DISPOSABLE
  const trimmed = typeof confirmation === 'string' ? confirmation.trim() : undefined
  if (trimmed !== DISPOSABLE_TOKEN) {
    throw new Error(
      `refusing to mutate grants: GOYMEN_ERP_C01_E2E_DISPOSABLE is ${trimmed ?? 'unset'}, ` +
        `not ${DISPOSABLE_TOKEN}. Only the disposable compose stack sets this.`,
    )
  }
  if (!target || typeof target.connectionString !== 'string' || target.connectionString === '') {
    throw new Error('refusing to mutate grants: no connection string')
  }
  if (typeof target.actorId !== 'string' || target.actorId === '') {
    throw new Error('refusing to mutate grants: no target actor')
  }
  const identity = runIdentity()

  // The database is DERIVED from the run, and a caller-supplied one may only
  // agree with it. Previously the caller named the database and the guard
  // merely checked the connection string agreed with the caller -- two inputs
  // an attacker controls, checked against each other.
  if (typeof target.database === 'string' && target.database !== identity.database) {
    throw new Error(
      `refusing to mutate grants: caller declares database ${target.database} but run ` +
        `${identity.runId}/${identity.attempt} derives ${identity.database}`,
    )
  }

  const parts = parse(target.connectionString)
  const [host, port] = parts.server.split(',').map((piece) => piece.trim())
  if (!LOOPBACK.has(host)) {
    throw new Error(`refusing to mutate grants on a non-loopback server: ${host || '(none)'}`)
  }
  const expectedPort = process.env.C01_E2E_SQL_PORT?.trim()
  if (!expectedPort || !NUMERIC.test(expectedPort)) {
    throw new Error('refusing to mutate grants: C01_E2E_SQL_PORT is not a numeric port')
  }
  if (port !== expectedPort) {
    throw new Error(
      `refusing to mutate grants: connection targets port ${port ?? '(default)'} but the ` +
        `disposable stack publishes ${expectedPort}`,
    )
  }
  if (parts.database !== identity.database) {
    throw new Error(
      `refusing to mutate grants: connection targets ${parts.database || '(none)'} but the ` +
        `disposable run derives ${identity.database}.`,
    )
  }
  return identity
}

/**
 * A preimage may only be applied to the exact target it was taken from.
 *
 * Checked before connecting, and before anything from the file reaches SQL: the
 * file is durable state a previous process wrote, so it is input, not truth.
 */
export function assertPreimageMatches(preimage, target, identity) {
  if (preimage === null || typeof preimage !== 'object') {
    throw new Error('refusing to apply a preimage that is not an object')
  }
  const expectations = [
    ['runId', identity.runId],
    ['attempt', identity.attempt],
    ['composeProject', identity.composeProject],
    ['database', identity.database],
    ['actorId', target.actorId],
  ]
  for (const [field, expected] of expectations) {
    if (preimage[field] !== expected) {
      throw new Error(
        `refusing to apply a preimage from a different target: ${field} is ` +
          `${String(preimage[field])}, this run declares ${expected}`,
      )
    }
  }
  if (!Array.isArray(preimage.rows) || preimage.rows.length === 0) {
    throw new Error('refusing to apply a preimage with no rows')
  }
  for (const row of preimage.rows) {
    if (row === null || typeof row !== 'object') {
      throw new Error('refusing to apply a preimage row that is not an object')
    }
    if (typeof row.RoleId !== 'string' || !GUID.test(row.RoleId)) {
      throw new Error(`refusing to apply a preimage row with role id ${String(row.RoleId)}`)
    }
    if (row.PermissionCode !== READ_PERMISSION) {
      throw new Error(
        `refusing to apply a preimage row for ${String(row.PermissionCode)}; this tool only ` +
          `handles ${READ_PERMISSION}`,
      )
    }
  }
  return preimage
}

function scopedQuery(pool, actorId) {
  return pool
    .request()
    .input('actor', sql.NVarChar, actorId)
    .input('code', sql.NVarChar, READ_PERMISSION).query(`
      SELECT DISTINCT rp.RoleId, rp.PermissionCode
      FROM [identity].[RolePermissions] rp
      INNER JOIN [identity].[RoleAssignments] ra ON ra.RoleId = rp.RoleId
      INNER JOIN [identity].[ExternalIdentities] ei ON ei.UserId = ra.UserId
      WHERE rp.PermissionCode = @code AND ei.Subject = @actor
    `)
}

/** The roles this actor actually holds, regardless of what permissions remain. */
function actorRoles(pool, actorId) {
  return pool.request().input('actor', sql.NVarChar, actorId).query(`
      SELECT DISTINCT ra.RoleId
      FROM [identity].[RoleAssignments] ra
      INNER JOIN [identity].[ExternalIdentities] ei ON ei.UserId = ra.UserId
      WHERE ei.Subject = @actor
    `)
}

/**
 * Prove every preimage role belongs to this actor before granting anything.
 *
 * A syntactically valid GUID is not authority. Without this, a tampered preimage
 * could name any role at all and the restore would hand it
 * `counterparty.parties.read` -- turning a cleanup tool into a privilege-
 * granting one (#203 review P1-1).
 */
async function assertRolesBelongToActor(pool, rows, actorId) {
  const held = await actorRoles(pool, actorId)
  const allowed = new Set(held.recordset.map((row) => String(row.RoleId).toLowerCase()))
  for (const row of rows) {
    if (!allowed.has(String(row.RoleId).toLowerCase())) {
      throw new Error(
        `refusing to restore: role ${row.RoleId} is not assigned to ${actorId}; a preimage ` +
          'cannot widen the blast radius beyond the actor it was taken from',
      )
    }
  }
}

function preimagePath(artifactsDir) {
  return `${artifactsDir}/${PREIMAGE_FILENAME}`
}

/** Idempotent, and atomic. Safe after a killed process, and safe twice. */
async function applyRestore(pool, rows, deps) {
  const transaction = new deps.Transaction(pool)
  await transaction.begin()
  try {
    for (const row of rows) {
      await new deps.Request(transaction)
        .input('roleId', sql.UniqueIdentifier, row.RoleId)
        .input('code', sql.NVarChar, row.PermissionCode)
        .query(
          'IF NOT EXISTS (SELECT 1 FROM [identity].[RolePermissions] ' +
            'WHERE RoleId = @roleId AND PermissionCode = @code) ' +
            'INSERT INTO [identity].[RolePermissions] (RoleId, PermissionCode) ' +
            'VALUES (@roleId, @code)',
        )
    }
    await transaction.commit()
  } catch (error) {
    await transaction.rollback()
    throw error
  }
}

/**
 * Remove the read grant from the roles THIS actor holds.
 *
 * The preimage is durable before the first DELETE, and every DELETE runs inside
 * one transaction: a half-applied revoke is not a state anything downstream has
 * to reason about.
 */
export async function revokeReadGrant(target, deps = defaultDeps) {
  const identity = assertDisposable(target)
  const artifactsDir = target.artifactsDir ?? required('ACTIVATION_E2E_ARTIFACTS_DIR')
  const pool = await deps.createPool(target.connectionString)
  try {
    const scoped = await scopedQuery(pool, target.actorId)
    const removed = scoped.recordset
    if (removed.length === 0) {
      throw new Error(
        `no ${READ_PERMISSION} grant reachable from actor ${target.actorId} -- the fixture ` +
          'is not what this case assumes, and deleting more broadly would be guessing',
      )
    }

    // Durable BEFORE the mutation. Redacted on purpose: role ids and permission
    // codes are enough to restore, and the connection string must never reach a
    // file the runner might keep.
    writeFileSync(
      preimagePath(artifactsDir),
      JSON.stringify(
        {
          runId: identity.runId,
          attempt: identity.attempt,
          composeProject: identity.composeProject,
          // Derived, matching what the guard now enforces. Writing the caller's
          // value here made the preimage unreadable by its own validator.
          database: identity.database,
          actorId: target.actorId,
          revokedAt: new Date().toISOString(),
          rows: removed.map((row) => ({
            RoleId: row.RoleId,
            PermissionCode: row.PermissionCode,
          })),
        },
        null,
        2,
      ),
      'utf8',
    )

    const transaction = new deps.Transaction(pool)
    await transaction.begin()
    try {
      for (const row of removed) {
        await new deps.Request(transaction)
          .input('roleId', sql.UniqueIdentifier, row.RoleId)
          .input('code', sql.NVarChar, row.PermissionCode)
          .query(
            'DELETE FROM [identity].[RolePermissions] WHERE RoleId = @roleId AND PermissionCode = @code',
          )
      }
      await transaction.commit()
    } catch (error) {
      // Rollback, so a DELETE that failed halfway leaves the grant whole rather
      // than partially removed with a preimage claiming otherwise.
      await transaction.rollback()
      throw error
    }

    return {
      removed,
      restore: async () => {
        await applyRestore(pool, removed, deps)
      },
    }
  } catch (error) {
    await pool.close()
    throw error
  }
}

export function readPreimage(artifactsDir) {
  const path = preimagePath(artifactsDir)
  if (!existsSync(path)) return null
  return JSON.parse(readFileSync(path, 'utf8'))
}

/**
 * Restore from the durable preimage. Used by the workflow's always() cleanup,
 * and safe to call after a clean run that already restored.
 */
export async function restoreFromPreimage(target, artifactsDir, deps = defaultDeps) {
  const identity = assertDisposable(target)
  const preimage = readPreimage(artifactsDir)
  if (preimage === null) {
    return { restored: 0, reason: 'no preimage -- nothing was revoked' }
  }
  assertPreimageMatches(preimage, target, identity)
  const pool = await deps.createPool(target.connectionString)
  try {
    await assertRolesBelongToActor(pool, preimage.rows, target.actorId)
    await applyRestore(pool, preimage.rows, deps)
    return { restored: preimage.rows.length, reason: 'restored from preimage' }
  } finally {
    await pool.close()
  }
}

/**
 * Exact equality against the preimage.
 *
 * "The rows are back" is not the claim; "the rows are exactly what they were"
 * is. A restore that left one missing, or added one, fails here.
 */
export async function verifyAgainstPreimage(target, artifactsDir, deps = defaultDeps) {
  const identity = assertDisposable(target)
  const preimage = readPreimage(artifactsDir)
  if (preimage === null) {
    return { verified: 0, reason: 'no preimage -- nothing was revoked' }
  }
  assertPreimageMatches(preimage, target, identity)
  const pool = await deps.createPool(target.connectionString)
  try {
    const scoped = await scopedQuery(pool, target.actorId)
    const key = (row) => `${String(row.RoleId).toLowerCase()}|${row.PermissionCode}`
    const now = new Set(scoped.recordset.map(key))
    const before = new Set(preimage.rows.map(key))
    const missing = [...before].filter((entry) => !now.has(entry))
    const extra = [...now].filter((entry) => !before.has(entry))
    if (missing.length > 0 || extra.length > 0) {
      throw new Error(
        `RolePermissions does not match the preimage: ${missing.length} missing, ` +
          `${extra.length} unexpected`,
      )
    }
    return { verified: before.size, reason: 'exact match with preimage' }
  } finally {
    await pool.close()
  }
}
