/**
 * Typed facade over the grant store.
 *
 * The SQL used to live here. It moved to `grantStore.mjs` because #199 P1-3
 * requires the same revoke/restore to be callable from a workflow `always()`
 * step with no Playwright and no browser -- and keeping a second copy in plain
 * JS for that path is exactly how a cleanup drifts out of step with the test it
 * is supposed to undo. One implementation, two entry points.
 *
 * This file exists so the spec keeps a typed import; it adds no behaviour.
 */

export {
  READ_PERMISSION,
  PREIMAGE_FILENAME,
  assertDisposable,
  revokeReadGrant,
  readPreimage,
  restoreFromPreimage,
  verifyAgainstPreimage,
  runIdentity,
} from './grantStore.mjs'

export type {
  RevokeTarget,
  RevokedGrant,
  GrantRow,
  Preimage,
  RunIdentity,
} from './grantStore.mjs'
