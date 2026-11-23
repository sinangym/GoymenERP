import type { AttemptStatus } from './useCommandAttempt'
import { COMMAND_ATTEMPT_TEXT as T } from './commandAttemptText'

/** Explicit state for an edited submit that the hook refused to send while an earlier attempt is unresolved. */
export function EditedAttemptNotice<D>({ status }: { readonly status: AttemptStatus<D> }) {
  return status.kind === 'unknown' && status.edited === true
    ? <p role="alert" data-attempt-state="edited">{T.edited}</p>
    : null
}
