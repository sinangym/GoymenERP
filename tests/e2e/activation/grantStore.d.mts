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
  readonly artifactsDir?: string
}

export interface GrantRow {
  readonly RoleId: string
  readonly PermissionCode: string
}

export interface RevokedGrant {
  readonly removed: readonly GrantRow[]
  readonly restore: () => Promise<void>
}

export interface RunIdentity {
  readonly runId: string
  readonly attempt: string
  readonly composeProject: string
}

export interface Preimage {
  readonly runId: string
  readonly attempt: string
  readonly composeProject: string
  readonly database: string
  readonly actorId: string
  readonly revokedAt: string
  readonly rows: readonly GrantRow[]
}

export declare const READ_PERMISSION: string
export declare const PREIMAGE_FILENAME: string
export declare function runIdentity(): RunIdentity
export declare function assertDisposable(target: RevokeTarget): RunIdentity
export declare function revokeReadGrant(target: RevokeTarget): Promise<RevokedGrant>
export declare function readPreimage(artifactsDir: string): Preimage | null
export declare function restoreFromPreimage(
  connectionString: string,
  artifactsDir: string,
): Promise<{ restored: number; reason: string }>
export declare function verifyAgainstPreimage(
  connectionString: string,
  artifactsDir: string,
  actorId: string,
): Promise<{ verified: number; reason: string }>
