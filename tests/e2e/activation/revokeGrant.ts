/**
 * Revoking a real grant, bound to a disposable target.
 *
 * The first version of this took a connection string and deleted
 * `counterparty.parties.read` from EVERY role, guarded only by "the env var is
 * non-empty". Pointed at the wrong database that is a destructive operation with
 * no undo, and Codex was right to block on it (#196 P1-1).
 *
 * Four bindings now stand between the caller and a DELETE:
 *
 *  1. `GOYMEN_ERP_C01_E2E_DISPOSABLE` must literally be `CONFIRMED` — the same
 *     token the disposable compose stack sets, and nothing else sets.
 *  2. The server must be on loopback. A remote host is refused outright.
 *  3. The database name must match the disposable run's, so a confirmed token
 *     left in the environment cannot authorize a different database.
 *  4. Only the roles the test actor actually holds are touched, not every role
 *     that happens to carry the permission.
 *
 * Restore is idempotent and re-runnable, so an outer `always()` cleanup can call
 * it after a killed process without needing the original in-memory state.
 */

import sql from 'mssql'

export const READ_PERMISSION = 'counterparty.parties.read'

const DISPOSABLE_TOKEN = 'CONFIRMED'
const LOOPBACK = new Set(['127.0.0.1', 'localhost', '::1'])

export interface RevokeTarget {
  readonly connectionString: string
  readonly database: string
  /**
   * The dev identity whose effective roles are in scope, as it appears in
   * `identity.ExternalIdentities.Subject`.
   *
   * Checked against the schema rather than assumed: `identity.Users` has no
   * external-key column, so joining through it would have been a fabricated
   * column name that happened to compile.
   */
  readonly actorId: string
}

function parse(connectionString: string): Map<string, string> {
  const parts = new Map<string, string>()
  for (const segment of connectionString.split(';')) {
    const index = segment.indexOf('=')
    if (index <= 0) continue
    parts.set(segment.slice(0, index).trim().toLowerCase(), segment.slice(index + 1).trim())
  }
  return parts
}

/** Refuse anything that is not demonstrably the disposable stack. */
export function assertDisposable(target: RevokeTarget): void {
  const confirmation = process.env.GOYMEN_ERP_C01_E2E_DISPOSABLE?.trim()
  if (confirmation !== DISPOSABLE_TOKEN) {
    throw new Error(
      `refusing to mutate grants: GOYMEN_ERP_C01_E2E_DISPOSABLE is ${confirmation ?? 'unset'}, ` +
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
        `disposable run declares ${target.database}. A confirmation token must not ` +
        `authorize a database it was not issued for.`,
    )
  }
}

export interface RevokedGrant {
  readonly removed: readonly { RoleId: string; PermissionCode: string }[]
  readonly restore: () => Promise<void>
}

/**
 * Remove the read grant from the roles THIS actor holds, and hand back the undo.
 *
 * Scoped to `[identity]` — bracketed because `identity` is a reserved word in
 * T-SQL — so the Counterparty fixture, `AuditEvent` and `CommandExecution` that
 * the workflow verifies afterwards are untouched.
 */
export async function revokeReadGrant(target: RevokeTarget): Promise<RevokedGrant> {
  assertDisposable(target)
  const pool = await new sql.ConnectionPool(target.connectionString).connect()
  try {
    const scoped = await pool.request().input('actor', sql.NVarChar, target.actorId).input(
      'code',
      sql.NVarChar,
      READ_PERMISSION,
    ).query(`
      SELECT DISTINCT rp.RoleId, rp.PermissionCode
      FROM [identity].[RolePermissions] rp
      INNER JOIN [identity].[RoleAssignments] ra ON ra.RoleId = rp.RoleId
      INNER JOIN [identity].[ExternalIdentities] ei ON ei.UserId = ra.UserId
      WHERE rp.PermissionCode = @code AND ei.Subject = @actor
    `)
    const removed = scoped.recordset as { RoleId: string; PermissionCode: string }[]
    if (removed.length === 0) {
      throw new Error(
        `no ${READ_PERMISSION} grant reachable from actor ${target.actorId} — the fixture ` +
          `is not what this case assumes, and deleting more broadly would be guessing`,
      )
    }
    for (const row of removed) {
      await pool
        .request()
        .input('roleId', sql.UniqueIdentifier, row.RoleId)
        .input('code', sql.NVarChar, row.PermissionCode)
        .query(
          'DELETE FROM [identity].[RolePermissions] WHERE RoleId = @roleId AND PermissionCode = @code',
        )
    }
    return {
      removed,
      restore: async () => {
        for (const row of removed) {
          await pool
            .request()
            .input('roleId', sql.UniqueIdentifier, row.RoleId)
            .input('code', sql.NVarChar, row.PermissionCode)
            .query(
              'IF NOT EXISTS (SELECT 1 FROM [identity].[RolePermissions] ' +
                'WHERE RoleId = @roleId AND PermissionCode = @code) ' +
                'INSERT INTO [identity].[RolePermissions] (RoleId, PermissionCode) ' +
                'VALUES (@roleId, @code)',
            )
        }
      },
    }
  } catch (error) {
    await pool.close()
    throw error
  }
}
