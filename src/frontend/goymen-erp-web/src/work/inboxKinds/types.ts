export interface WorkInboxKind {
  readonly kind: string
  readonly module: string
  /** The owner's existing approve capability in the shell snapshot, or null when the frontend has none. */
  readonly capability: string | null
}
