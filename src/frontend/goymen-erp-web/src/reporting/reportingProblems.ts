import type { ViewOutcome } from '../shell/uiState'
import { problemCodeOf } from './reportingContract'
import { REPORTING_TEXT as T } from './reportingText'

/** A refusal the screen can name (400/409/422/503 with a stable code); null leaves the state to the shared StateBlock. */
export function namedProblem(outcome: ViewOutcome<unknown> | undefined): string | null {
  if (outcome?.type !== 'http' || ![400, 409, 422, 503].includes(outcome.status)) return null
  const code = problemCodeOf(outcome.body)
  return code === null ? null : T.problems[code] ?? null
}

export function refusalText(status: number, code: string | null): string {
  if (status === 403) return T.forbidden
  return (code === null ? null : T.problems[code]) ?? T.problemFallback
}
