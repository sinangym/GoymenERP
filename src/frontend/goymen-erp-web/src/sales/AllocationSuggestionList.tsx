import { useEffect, useRef, useState } from 'react'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import type { ViewOutcome } from '../shell/uiState'
import { fetchAllocationSuggestions } from './allocationApi'
import type { AllocationScope, AllocationSuggestion, AllocationSuggestions } from './allocationContract'
import { ALLOCATION_TEXT as T } from './allocationText'
import type { AllocationLabelChoice } from './AllocationLabelPicker'
import styles from './SalesPages.module.css'

/** A picked label; `isSubstitute` marks a confirmed similar-product ("muadil") suggestion. */
export interface AllocationSuggestionChoice extends AllocationLabelChoice {
  readonly isSubstitute?: true
  readonly productText?: string
}

export interface AllocationSuggestionListProps {
  readonly scope: AllocationScope
  readonly lineId: string
  /** The line's own product, shown for same-product suggestions (Inventory does not repeat it). */
  readonly lineProductText: string
  readonly disabled: boolean
  readonly selected: AllocationLabelChoice | null
  readonly onSelect: (choice: AllocationSuggestionChoice) => void
}

type SuggestionState =
  | { readonly kind: 'idle' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly answer: AllocationSuggestions }
  | { readonly kind: 'refused'; readonly text: string }

function stateOf(outcome: ViewOutcome<AllocationSuggestions>): SuggestionState | null {
  switch (outcome.type) {
    case 'ok-single':
      return { kind: 'ready', answer: outcome.data }
    case 'fence-stale':
      return null
    case 'offline':
      return { kind: 'refused', text: T.suggestionOffline }
    case 'malformed':
      return { kind: 'refused', text: T.suggestionMalformed }
    case 'http':
      if (outcome.status === 401 || outcome.status === 403) return { kind: 'refused', text: T.suggestionForbidden }
      if (outcome.status === 404) return { kind: 'refused', text: T.suggestionLineNotFound }
      return { kind: 'refused', text: T.suggestionUnavailable }
    default:
      return { kind: 'refused', text: T.suggestionUnavailable }
  }
}

function productText(item: AllocationSuggestion, lineProductText: string): string {
  if (item.kind === 'same-product') return lineProductText
  if (item.productCode !== null && item.productName !== null) return `${item.productCode} — ${item.productName}`
  return T.suggestionProductUnknown
}

/**
 * KLN-D34/D39/D40: the line's "Öneri" button and the server's label suggestions. Choosing a suggestion only fills the
 * allocation form; the user still confirms with quantity and reason, and Inventory re-checks every rule on create.
 */
export function AllocationSuggestionList({
  scope, lineId, lineProductText, disabled, selected, onSelect,
}: AllocationSuggestionListProps) {
  const boundary = useAuthBoundary()
  const [state, setState] = useState<SuggestionState>({ kind: 'idle' })
  const inFlight = useRef<AbortController | null>(null)

  useEffect(() => () => inFlight.current?.abort(), [])

  const suggest = async () => {
    inFlight.current?.abort()
    const controller = new AbortController()
    inFlight.current = controller
    setState({ kind: 'loading' })
    const snapshot = boundary.capture()
    const outcome = await fetchAllocationSuggestions(scope, controller.signal)
    if (controller.signal.aborted || inFlight.current !== controller) return
    inFlight.current = null
    if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
    const next = stateOf(outcome)
    if (next !== null) setState(next)
  }

  const resultsId = `allocation-suggestions-${lineId}`
  const answer = state.kind === 'ready' ? state.answer : null
  const similar = answer !== null && answer.items.some((item) => item.isSubstitute)
  return (
    <div className={styles.fieldset} data-testid={`sales-allocation-suggestions-${lineId}`}>
      <div className={styles.actions}>
        <button type="button" className={styles.secondaryButton} disabled={disabled || state.kind === 'loading'}
          aria-controls={resultsId} onClick={() => void suggest()}>
          {state.kind === 'idle' ? T.suggest : T.suggestAgain}
        </button>
        <span className={styles.muted}>{T.suggestHint}</span>
      </div>
      <div id={resultsId} role="status" aria-live="polite" aria-busy={state.kind === 'loading'}>
        {state.kind === 'loading' ? <p>{T.suggestionLoading}</p> : null}
        {answer !== null ? (
          <>
            <p className={styles.muted}>
              {answer.similarProductMode === 'same-product-only' ? T.suggestionSameOnly : T.suggestionToleranceOn}
              {' '}{answer.aiSuggestions === 'disabled' ? T.suggestionAiDisabled : T.suggestionAiEnabled}
            </p>
            {similar ? <p className={styles.notice} data-tone="unknown">{T.suggestionSimilarNote}</p> : null}
            {answer.items.length === 0 ? (
              <p data-testid={`sales-allocation-suggestions-empty-${lineId}`}>
                {answer.similarProductMode === 'same-product-only' ? T.suggestionEmptySameOnly : T.suggestionEmpty}
              </p>
            ) : null}
            {answer.truncated ? <p className={styles.notice} data-tone="unknown">{T.suggestionTruncated}</p> : null}
          </>
        ) : null}
      </div>
      {state.kind === 'refused' ? (
        <p className={styles.notice} data-tone="error" role="alert">{state.text}</p>
      ) : null}
      {answer !== null && answer.items.length > 0 ? (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <caption className={styles.caption}>{T.suggestionCaption}</caption>
            <thead>
              <tr>
                <th scope="col">{T.labelCode}</th>
                <th scope="col">{T.suggestionProduct}</th>
                <th scope="col">{T.suggestionUnreservedKg}</th>
                <th scope="col">{T.suggestionDeviation}</th>
                <th scope="col">{T.labelChoiceColumn}</th>
              </tr>
            </thead>
            <tbody>{answer.items.map((item) => {
              const product = productText(item, lineProductText)
              const chosen = selected !== null && selected.labelId.toLowerCase() === item.labelId.toLowerCase()
                && selected.labelVersion === item.labelVersion
              return (
                <tr key={item.labelId} data-testid={`sales-allocation-suggestion-${item.labelId}`}>
                  <td className={styles.identifier}>{item.labelCode}</td>
                  <td>
                    {product}
                    {item.isSubstitute ? <>{' '}<span className={styles.badge}>{T.substituteBadge}</span></> : null}
                  </td>
                  <td>{item.unreservedKg}</td>
                  <td>{item.maximumDeviationPercent === null ? '—' : T.suggestionDeviationValue(item.maximumDeviationPercent)}</td>
                  <td>
                    <button type="button" className={styles.secondaryButton} disabled={disabled || chosen}
                      aria-label={chosen ? `${item.labelCode} ${T.labelChosenAria}` : `${item.labelCode} ${T.suggestionChooseAria}`}
                      onClick={() => onSelect({
                        labelId: item.labelId,
                        labelVersion: item.labelVersion,
                        labelCode: item.labelCode,
                        availableKg: item.unreservedKg,
                        productText: product,
                        ...(item.isSubstitute ? { isSubstitute: true as const } : {}),
                      })}>
                      {chosen ? T.labelChosen : T.suggestionChoose}
                    </button>
                  </td>
                </tr>
              )
            })}</tbody>
          </table>
        </div>
      ) : null}
    </div>
  )
}
