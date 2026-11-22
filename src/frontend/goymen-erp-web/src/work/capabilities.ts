/**
 * WP9: Work's own shell capabilities, mirroring `WorkShellCapabilityProvider` (owner WORK, contract 1.0). Listing an id grants
 * nothing; the server publishes it only to a holder of the permissions (registered, granted to no role).
 * - `work.tasks.mine` — "Bana atanan" (`/gorevlerim`), permission `work.tasks.complete`.
 * - `work.tasks.assign` — the assign CONTROL of that page (`work.tasks.complete` + `work.tasks.assign`), never a route.
 * The personal inbox (`/bildirimlerim`) and the record notes panel are not gated here: the first is recipient-only, the second
 * sits behind each owner's own detail read; the server decides every read and command.
 */
export const WORK_CAPABILITY = {
  myTasks: 'work.tasks.mine',
  assignTask: 'work.tasks.assign',
} as const

export const WORK_MY_TASKS_CAPABILITIES: readonly string[] = [WORK_CAPABILITY.myTasks]
