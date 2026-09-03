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
 * WHAT CHANGED AFTER #199 P1-3. The previous version held the preimage only in
 * process memory and restored it in a `finally`. If the process was killed the
 * `finally` never ran, the outer cleanup only did `docker compose down -v`, and
 * nothing ever checked that the grant came back. The bindings were also weaker
 * than the API's own: loopback plus database name, but nothing tying the
 * mutation to THIS run.
 *
 * Five bindings now stand between a caller and a DELETE:
 *
 *  1. `GOYMEN_ERP_C01_E2E_DISPOSABLE` must literally be `CONFIRMED`.
 *  2. The server must be on loopback.
 *  3. The database name must match the disposable run's.
 *  4. The compose project must be `goymen-c01-<runId>-<attempt>` -- the same
 *     envelope `C01E2eShellCapabilityActivation` validates before it will serve
 *     the overridden capability. A confirmation token left in the environment
 *     therefore cannot authorize a mutation against a different run.
 *  5. Only the roles the test actor actually holds are touched.
 *
 * The preimage is written OUTSIDE the process before the first DELETE, redacted
 * to role ids and permission codes -- never the connection string.
 */

import sql from 'mssql'
import { readFileSync, writeFileSync, existsSync } from 'node:fs'

export const READ_PERMISSION = 'counterparty.parties.read'
export const PREIMAGE_FILENAME = 'activation-grant-preimage.json'

const DISPOSABLE_TOKEN = 'CONFIRMED'
const LOOPBACK = new Set(['127.0.0.1', 'localhost', '::1'])
const NUMERIC = /^[0-9]+$/

function parse(connectionString) {
  const parts = new Map()
  for (const segment of connectionString.split(';')) {
    const index = segment.indexOf('=')
    if (index <= 0) continue
    parts.set(segment.slice(0, index).trim().toLowerCase(), segment.slice(index + 1).trim())
  }
  return parts
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
  return { runId, attempt, composeProject }
}

export function assertDisposable(target) {
  const confirmation = process.env.GOYMEN_ERP_C01_E2E_DISPOSABLE
  const trimmed = typeof confirmation === 'string' ? confirmation.trim() : undefined
  if (trimmed !== DISPOSABLE_TOKEN) {
    throw new Error(
      `refusing to mutate grants: GOYMEN_ERP_C01_E2E_DISPOSABLE is ${trimmed ?? 'unset'}, ` +
        `not ${DISPOSABLE_TOKEN}. Only the disposable compose stack sets this.`,
    )
  }
  const parts = parse(target.connectionString)
  const server = (parts.get('server') ?? '').split(',')[0].trim()
  if (!LOOPBACK.has(server)) {
    throw new Error(`refusing to mutate grants on a non-loopback server: ${server || '(none)'}`)
  }
  const database = parts.get('database') ?? ''
  if (database !== target.database) {
    throw new Error(
      `refusing to mutate grants: connection targets ${database || '(none)'} but the ` +
        `disposable run declares ${target.database}.`,
    )
  }
  return runIdentity()
}

async function connect(connectionString) {
  return new sql.ConnectionPool(connectionString).connect()
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

function preimagePath(artifactsDir) {
  return `${artifactsDir}/${PREIMAGE_FILENAME}`
}

/** Idempotent, and atomic. Safe to call after a killed process, and safe twice. */
async function applyRestore(pool, rows) {
  const transaction = new sql.Transaction(pool)
  await transaction.begin()
  try {
    for (const row of rows) {
      await new sql.Request(transaction)
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
export async function revokeReadGrant(target) {
  const identity = assertDisposable(target)
  const artifactsDir = target.artifactsDir ?? required('ACTIVATION_E2E_ARTIFACTS_DIR')
  const pool = await connect(target.connectionString)
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
          database: target.database,
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

    const transaction = new sql.Transaction(pool)
    await transaction.begin()
    try {
      for (const row of removed) {
        await new sql.Request(transaction)
          .input('roleId', sql.UniqueIdentifier, row.RoleId)
          .input('code', sql.NVarChar, row.PermissionCode)
          .query(
            'DELETE FROM [identity].[RolePermissions] WHERE RoleId = @roleId AND PermissionCode = @code',
          )
      }
      await transaction.commit()
    } catch (error) {
      await transaction.rollback()
      throw error
    }

    return {
      removed,
      restore: async () => {
        await applyRestore(pool, removed)
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

/** Restore from the durable preimage. Used by the workflow's always() cleanup. */
export async function restoreFromPreimage(connectionString, artifactsDir) {
  const preimage = readPreimage(artifactsDir)
  if (preimage === null) {
    return { restored: 0, reason: 'no preimage -- nothing was revoked' }
  }
  const identity = runIdentity()
  if (preimage.composeProject !== identity.composeProject) {
    throw new Error(
      `refusing to restore: preimage belongs to ${preimage.composeProject}, this run is ` +
        `${identity.composeProject}`,
    )
  }
  const pool = await connect(connectionString)
  try {
    await applyRestore(pool, preimage.rows)
    return { restored: preimage.rows.length, reason: 'restored from preimage' }
  } finally {
    await pool.close()
  }
}

/**
 * Exact equality against the preimage.
 *
 * "The rows are back" is not the claim; "the rows are exactly what they were" is.
 * A restore that left one missing, or added one, fails here.
 */
export async function verifyAgainstPreimage(connectionString, artifactsDir, actorId) {
  const preimage = readPreimage(artifactsDir)
  if (preimage === null) {
    return { verified: 0, reason: 'no preimage -- nothing was revoked' }
  }
  const pool = await connect(connectionString)
  try {
    const scoped = await scopedQuery(pool, actorId)
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
