import type { WorkCommandOutcome } from './collaborationApi'
import { COLLABORATION_TEXT as T } from './collaborationText'

/** Stable code → Turkish message; unknown codes fall back by status. */
export function refusalMessage(outcome: WorkCommandOutcome<unknown>): string {
  if (outcome.type === 'unknown') return T.unknownOutcome
  if (outcome.type !== 'refused') return ''
  switch (outcome.code) {
    case 'work_mention_refused': return T.mentionRefused
    case 'work_corrected_note_invalid': return T.correctedInvalid
    case 'work_note_not_own': return T.notOwn
    case 'work_note_already_passive': return T.alreadyPassive
    case 'work_task_not_open': return T.taskNotOpen
    case 'work_task_not_assignee': return T.taskNotAssignee
    case 'version_conflict': return T.versionConflict
    case 'same_key_payload_mismatch': return T.sameKeyMismatch
    case 'concurrent_change': return T.concurrent
    default:
      if (outcome.status === 403 || outcome.status === 401) return T.forbidden
      if (outcome.status === 404) return T.notFound
      return T.validation
  }
}

/** FNV-1a of the payload: a changed payload is a new attempt (new key), the same payload after an unknown outcome reuses it. */
export function payloadFingerprint(value: unknown): string {
  let hash = 0x811c9dc5
  for (const character of JSON.stringify(value)) {
    hash ^= character.codePointAt(0) ?? 0
    hash = Math.imul(hash, 0x01000193) >>> 0
  }
  return hash.toString(16)
}
