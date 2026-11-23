/**
 * WP4–WP7: one module's words for the Work inbox (kind names, rule sentences, reference roles, approve permission names and
 * the module name). Each module keeps its own file so packages add kinds without touching each other; `workInboxText.ts`
 * merges them into the one set of maps.
 */
export interface WorkInboxModuleText {
  readonly modules?: Readonly<Record<string, string>>
  readonly kinds: Readonly<Record<string, string>>
  readonly rules: Readonly<Record<string, string>>
  readonly roles?: Readonly<Record<string, string>>
  readonly permissions: Readonly<Record<string, string>>
}
