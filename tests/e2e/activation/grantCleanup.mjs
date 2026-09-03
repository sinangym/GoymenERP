/**
 * Out-of-process grant cleanup and verification (#199 P1-3).
 *
 * The workflow calls `restore` from an `always()` step and `verify` from the
 * post-browser check. Both read the durable preimage rather than any in-memory
 * state, which is the whole point: if the test process was killed, its `finally`
 * never ran and nothing in memory survives to tell us what to put back.
 *
 * `restore` is idempotent, so running it after a clean test -- which already
 * restored -- is a no-op rather than a second, conflicting write.
 */

import { restoreFromPreimage, verifyAgainstPreimage } from './grantStore.mjs'

function required(name) {
  const value = process.env[name]
  if (!value || !value.trim()) {
    console.error(`::error::ACTIVATION_CLEANUP_ENV_MISSING: ${name}`)
    process.exit(1)
  }
  return value.trim()
}

const command = process.argv[2]
if (command !== 'restore' && command !== 'verify') {
  console.error('usage: node grantCleanup.mjs <restore|verify>')
  process.exit(2)
}

const connectionString = required('ACTIVATION_E2E_SQL_CONNECTION')
const artifactsDir = required('ACTIVATION_E2E_ARTIFACTS_DIR')

try {
  if (command === 'restore') {
    const result = await restoreFromPreimage(connectionString, artifactsDir)
    console.log(`activation grant cleanup: ${result.reason} (${result.restored} row(s))`)
  } else {
    const actorId = required('ACTIVATION_E2E_ACTOR')
    const result = await verifyAgainstPreimage(connectionString, artifactsDir, actorId)
    console.log(`activation grant verification: ${result.reason} (${result.verified} row(s))`)
  }
  process.exit(0)
} catch (error) {
  // Fail closed. A cleanup that cannot prove it put the grant back is a failure,
  // not a warning -- the next run would otherwise start from an unknown state.
  console.error(`::error::ACTIVATION_CLEANUP_FAILED: ${error.message}`)
  process.exit(1)
}
