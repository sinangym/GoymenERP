/**
 * A child process that revokes and is then killed (#203 review P1-4).
 *
 * Codex's point was exact: the integration test wrote a preimage and restored in
 * the SAME process, so it never proved the ordering it depends on. A regression
 * moving the durable preimage write to AFTER the transaction commit would keep
 * every test green, while a real kill between commit and write would leave the
 * grant revoked and the external cleanup with nothing to restore from.
 *
 * So this is a real process, killed with a real signal, and it records whether
 * the preimage file already existed at the moment the first DELETE was issued.
 * If the write ever moves after the commit, that recorded value flips to false
 * and the parent test fails -- the ordering is proven, not assumed.
 *
 * SQL is faked through a file so the parent can observe the same state after the
 * child is gone. This proves the ORDERING and the cross-process handoff; the
 * real-SQL equality proof is the workflow's post-browser verification step.
 */

import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { revokeReadGrant, PREIMAGE_FILENAME } from './grantStore.mjs'

const [, , artifactsDir, mode] = process.argv
if (!artifactsDir || (mode !== 'kill' && mode !== 'clean')) {
  console.error('usage: node killHarness.mjs <artifactsDir> <kill|clean>')
  process.exit(2)
}

const statePath = `${artifactsDir}/fake-sql-state.json`
const orderPath = `${artifactsDir}/fake-sql-order.json`
const state = JSON.parse(readFileSync(statePath, 'utf8'))
const order = { deletes: [], commits: 0 }

const record = () => writeFileSync(orderPath, JSON.stringify(order), 'utf8')

const chain = () => {
  const request = {
    input: () => request,
    query: async (text) => {
      if (/DELETE/.test(text)) {
        // The observation this whole harness exists for.
        order.deletes.push({
          preimageExisted: existsSync(`${artifactsDir}/${PREIMAGE_FILENAME}`),
        })
        record()
        return { recordset: [] }
      }
      if (/FROM \[identity\]\.\[RoleAssignments\]/.test(text) && !/RolePermissions/.test(text)) {
        return { recordset: state.actorRoles.map((RoleId) => ({ RoleId })) }
      }
      return { recordset: /SELECT/.test(text) ? state.rows : [] }
    },
  }
  return request
}

const deps = {
  createPool: async () => ({ request: chain, close: async () => {} }),
  Transaction: class {
    async begin() {}
    async commit() {
      order.commits += 1
      record()
      if (mode === 'kill') {
        // Killed the instant the delete is durable, which is precisely the
        // window in which the in-process `finally` never gets to restore.
        process.kill(process.pid, 'SIGKILL')
        // Unreachable on a real kill; if the platform ignored the signal, make
        // that visible rather than letting the run look clean.
        throw new Error('SIGKILL did not terminate the harness')
      }
    }
    async rollback() {}
  },
  Request: class {
    constructor() {
      return chain()
    }
  },
}

const grant = await revokeReadGrant(
  {
    connectionString: state.connectionString,
    actorId: state.actorId,
    artifactsDir,
  },
  deps,
)
// Only reached in `clean` mode.
await grant.restore()
record()
process.exit(0)
