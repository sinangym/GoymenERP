/**
 * URT-S10 (F-010b, B-15): the plan's termin SUGGESTION inside the open termin form. Nothing is read until the user asks
 * ("Termin önerisi al"); the server computes the day from the work center calendar and the hourly capacity, and this
 * control shows it as it came — no day, hour or capacity is computed here. "Öneriyi kullan" only fills the termin field;
 * the user still saves it through the hand-entered termin command (or types another day), so every server check holds.
 *
 * A suggestion computed for another plan version or termin revision than the one on screen is shown as stale and cannot
 * be taken. `unavailable` names its reason, never a guessed day.
 */

import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { ProductionPlanView } from './contract'
import { fetchDueDateSuggestion } from './dueDateSuggestionApi'
import type { DueDateSuggestionView } from './dueDateSuggestionContract'
import {
  DUE_DATE_SUGGESTION_TEXT,
  dueDateSuggestionReasonText,
  dueDateSuggestionSourceText,
} from './dueDateSuggestionText'
import styles from './PlanPage.module.css'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'

export function DueDateSuggestionPanel({
  namespace,
  routeEntryGeneration,
  companyId,
  plan,
  busy,
  onUse,
}: {
  readonly namespace: string
  readonly routeEntryGeneration: number
  readonly companyId: string
  readonly plan: ProductionPlanView
  readonly busy: boolean
  /** Fills the termin field with the suggested day; it saves nothing. */
  readonly onUse: (suggestedDueDate: string) => void
}) {
  const boundary = useAuthBoundary()
  const [requested, setRequested] = useState(false)
  const [used, setUsed] = useState(false)
  const query = useQuery({
    queryKey: [
      namespace,
      routeEntryGeneration,
      'manufacturing-plan-due-date-suggestion',
      plan.planId,
      plan.version,
      plan.dueDateRevision,
    ],
    enabled: requested,
    // The suggestion follows the clock: every request asks the server again.
    staleTime: 0,
    gcTime: 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDueDateSuggestion(companyId, plan.planId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  if (!requested) {
    return (
      <div className={styles.actions}>
        <Button type="button" disabled={busy} onClick={() => setRequested(true)}>
          {DUE_DATE_SUGGESTION_TEXT.request}
        </Button>
      </div>
    )
  }

  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<DueDateSuggestionView>

  return (
    <section aria-labelledby="manufacturing-plan-due-date-suggestion-heading" data-testid="due-date-suggestion">
      <h3 id="manufacturing-plan-due-date-suggestion-heading">{DUE_DATE_SUGGESTION_TEXT.heading}</h3>
      <p className={styles.note}>{DUE_DATE_SUGGESTION_TEXT.help}</p>
      {state.kind !== 'ready' ? (
        <StateBlock
          state={state}
          subject={DUE_DATE_SUGGESTION_TEXT.subject}
          onRetry={() => void query.refetch()}
        />
      ) : (
        <SuggestionBody
          suggestion={state.data}
          plan={plan}
          busy={busy || query.isFetching}
          used={used}
          onUse={(day) => {
            onUse(day)
            setUsed(true)
          }}
        />
      )}
      {state.kind === 'ready' ? (
        <div className={styles.actions}>
          <Button
            type="button"
            disabled={busy || query.isFetching}
            aria-busy={query.isFetching}
            onClick={() => {
              setUsed(false)
              void query.refetch()
            }}
          >
            {DUE_DATE_SUGGESTION_TEXT.refresh}
          </Button>
        </div>
      ) : null}
    </section>
  )
}

function SuggestionBody({
  suggestion,
  plan,
  busy,
  used,
  onUse,
}: {
  readonly suggestion: DueDateSuggestionView
  readonly plan: ProductionPlanView
  readonly busy: boolean
  readonly used: boolean
  readonly onUse: (suggestedDueDate: string) => void
}) {
  const stale = suggestion.planVersion !== plan.version || suggestion.dueDateRevision !== plan.dueDateRevision
  if (stale) {
    return (
      <p role="alert" className={styles.error}>
        {DUE_DATE_SUGGESTION_TEXT.stalePlan}
      </p>
    )
  }

  return (
    <>
      {suggestion.status === 'suggested' && suggestion.suggestedDueDate !== null ? (
        <p data-testid="due-date-suggestion-day">
          {DUE_DATE_SUGGESTION_TEXT.suggested}:{' '}
          <span className={styles.mono}>{suggestion.suggestedDueDate}</span>
        </p>
      ) : (
        <p role="status" className={styles.note} data-testid="due-date-suggestion-reason">
          {dueDateSuggestionReasonText(suggestion.reasonCode)}
        </p>
      )}
      <dl>
        {suggestion.completesAtLocal === null ? null : (
          <div>
            <dt>{DUE_DATE_SUGGESTION_TEXT.completesAt}</dt>
            <dd>
              <span className={styles.mono}>{suggestion.completesAtLocal.replace('T', ' ')}</span>
              {suggestion.timeZoneId === null ? null : ` (${suggestion.timeZoneId})`}
            </dd>
          </div>
        )}
        <div>
          <dt>{DUE_DATE_SUGGESTION_TEXT.requiredKg}</dt>
          <dd>{suggestion.requiredKg} kg</dd>
        </div>
        {suggestion.requiredWorkingMinutes === null ? null : (
          <div>
            <dt>{DUE_DATE_SUGGESTION_TEXT.requiredTime}</dt>
            <dd>
              {suggestion.requiredWorkingMinutes} {DUE_DATE_SUGGESTION_TEXT.minutes}
            </dd>
          </div>
        )}
        {suggestion.capacityKgPerHour === null ? null : (
          <div>
            <dt>{DUE_DATE_SUGGESTION_TEXT.capacity}</dt>
            <dd>
              {suggestion.capacityKgPerHour} {DUE_DATE_SUGGESTION_TEXT.capacityUnit}
              {` · ${dueDateSuggestionSourceText(suggestion.capacitySource)}, ${DUE_DATE_SUGGESTION_TEXT.profile} `}
              {suggestion.capacityProfileVersion}
            </dd>
          </div>
        )}
        {suggestion.calendarVersion === null ? null : (
          <div>
            <dt>{DUE_DATE_SUGGESTION_TEXT.calendar}</dt>
            <dd>
              {dueDateSuggestionSourceText(suggestion.calendarSource)}
              {` · ${DUE_DATE_SUGGESTION_TEXT.calendarRevision} ${suggestion.calendarVersion}`}
            </dd>
          </div>
        )}
        <div>
          <dt>{DUE_DATE_SUGGESTION_TEXT.businessDate}</dt>
          <dd>
            <span className={styles.mono}>{suggestion.businessDate}</span>
          </dd>
        </div>
      </dl>
      {suggestion.status === 'suggested' && suggestion.suggestedDueDate !== null ? (
        <div className={styles.actions}>
          <Button
            type="button"
            disabled={busy}
            onClick={() => {
              if (suggestion.suggestedDueDate !== null) onUse(suggestion.suggestedDueDate)
            }}
          >
            {DUE_DATE_SUGGESTION_TEXT.use}
          </Button>
        </div>
      ) : null}
      {used ? (
        <p role="status" className={styles.note}>
          {DUE_DATE_SUGGESTION_TEXT.used}
        </p>
      ) : null}
    </>
  )
}
