import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import type { ViewOutcome } from '../shell/uiState'
import type { InventoryStockPositions } from '../inventory/contract'
import { inventoryLabelDetailPath } from '../inventory/labelDetailRoute'
import { LABEL_CODE_MAX_LENGTH } from '../inventory/labelTransformationContract'
import { allocationLabelBlockers, hasAvailableKg, searchAllocationLabels } from './allocationLabelApi'
import { ALLOCATION_TEXT as T } from './allocationText'
import styles from './SalesPages.module.css'

/** What the allocation command needs from the chosen label; the version is Inventory's, read just now. */
export interface AllocationLabelChoice {
  readonly labelId: string
  readonly labelVersion: string
  readonly labelCode: string
  readonly availableKg: string
}

export interface AllocationLabelPickerProps {
  readonly companyId: string
  readonly productId: string
  readonly lineId: string
  readonly disabled: boolean
  /**
   * SAT-D20 (2): the viewer may open Inventory's label detail (ST-02: genealogy, movement-timeline entry). That route is
   * gated by `inventory.stock-positions.list`; without it the picker renders no link rather than a dead one.
   */
  readonly canOpenLabelHistory: boolean
  readonly selected: AllocationLabelChoice | null
  readonly onSelect: (choice: AllocationLabelChoice | null) => void
}

type SearchState =
  | { readonly kind: 'idle' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly page: InventoryStockPositions }
  | { readonly kind: 'forbidden' }
  | { readonly kind: 'failed'; readonly malformed: boolean }

function stateOf(outcome: ViewOutcome<InventoryStockPositions>): SearchState | null {
  if (outcome.type === 'fence-stale') return null
  if (outcome.type === 'ok-single') return { kind: 'ready', page: outcome.data }
  if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) return { kind: 'forbidden' }
  return { kind: 'failed', malformed: outcome.type === 'malformed' }
}

/**
 * UI-13: replaces the hand-typed label id/version of the allocation panel. The user searches this line's
 * product's stocked labels by label code through Inventory's company-scoped read (MOD-12) and picks one;
 * the pick carries the label id and the version Inventory just reported. Searching is an explicit action
 * (button or Enter), never a request on mount, and a newer search aborts an older one.
 *
 * SAT-D20: a label Inventory would refuse is listed but not selectable, with the reason on its row; every row links to
 * the label's history when the viewer may open it; labels with zero available kilograms are hidden by the default
 * filter, which the user can lift. All of it is view-only: Inventory re-applies the rules on the allocation command.
 */
export function AllocationLabelPicker({
  companyId, productId, lineId, disabled, canOpenLabelHistory, selected, onSelect,
}: AllocationLabelPickerProps) {
  const boundary = useAuthBoundary()
  const [q, setQ] = useState('')
  const [showZeroAvailable, setShowZeroAvailable] = useState(false)
  const [state, setState] = useState<SearchState>({ kind: 'idle' })
  const inFlight = useRef<AbortController | null>(null)

  useEffect(() => () => inFlight.current?.abort(), [])

  const search = async () => {
    inFlight.current?.abort()
    const controller = new AbortController()
    inFlight.current = controller
    setState({ kind: 'loading' })
    const snapshot = boundary.capture()
    const outcome = await searchAllocationLabels({ companyId, productId, q }, controller.signal)
    if (controller.signal.aborted || inFlight.current !== controller) return
    inFlight.current = null
    if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
    const next = stateOf(outcome)
    if (next !== null) setState(next)
  }

  const searchId = `allocation-label-search-${lineId}`
  const hintId = `allocation-label-search-hint-${lineId}`
  const resultsId = `allocation-label-results-${lineId}`
  const showZeroId = `allocation-label-show-zero-${lineId}`
  const read = state.kind === 'ready' ? state.page.items : []
  const visible = showZeroAvailable ? read : read.filter(hasAvailableKg)
  const hiddenCount = read.length - visible.length
  const windowFull = state.kind === 'ready' && state.page.searchWindowFull
  return (
    <fieldset className={styles.fieldset} data-testid={`sales-allocation-label-picker-${lineId}`}>
      <legend>{T.pickerLegend}</legend>
      <label htmlFor={searchId}>{T.labelSearch}</label>
      <div className={styles.actions}>
        <input id={searchId} type="search" value={q} aria-describedby={hintId}
          maxLength={LABEL_CODE_MAX_LENGTH} disabled={disabled}
          onChange={(event) => setQ(event.target.value)}
          onKeyDown={(event) => {
            if (event.key !== 'Enter') return
            event.preventDefault()
            void search()
          }} />
        <button type="button" className={styles.secondaryButton} disabled={disabled}
          onClick={() => void search()}>{T.labelSearchSubmit}</button>
      </div>
      <p id={hintId} className={styles.muted}>{T.labelSearchHint}</p>
      <div className={styles.actions}>
        <input id={showZeroId} type="checkbox" checked={showZeroAvailable} disabled={disabled}
          onChange={(event) => setShowZeroAvailable(event.target.checked)} />
        <label htmlFor={showZeroId}>{T.labelShowZeroAvailable}</label>
      </div>
      <div id={resultsId} role="status" aria-live="polite" aria-busy={state.kind === 'loading'}>
        {state.kind === 'loading' ? <p>{T.labelSearchLoading}</p> : null}
        {state.kind === 'ready' && read.length === 0
          ? <p data-testid={`sales-allocation-label-empty-${lineId}`}>{T.labelSearchEmpty}</p> : null}
        {state.kind === 'ready' && read.length > 0 && visible.length === 0 ? (
          <p data-testid={`sales-allocation-label-all-hidden-${lineId}`}>{T.labelAllZeroHidden(hiddenCount)}</p>
        ) : null}
        {state.kind === 'ready' && hiddenCount > 0 && visible.length > 0
          ? <p className={styles.muted}>{T.labelZeroHidden(hiddenCount)}</p> : null}
        {windowFull ? (
          <p className={styles.notice} data-tone="unknown">
            {hiddenCount > 0 ? T.labelSearchWindowFullFiltered : T.labelSearchWindowFull}
          </p>
        ) : null}
      </div>
      {state.kind === 'forbidden' ? (
        <p className={styles.notice} data-tone="error" role="alert">{T.labelSearchForbidden}</p>
      ) : null}
      {state.kind === 'failed' ? (
        <div className={styles.notice} data-tone="error" role="alert">
          {state.malformed ? T.labelSearchMalformed : T.labelSearchUnavailable}{' '}
          <button type="button" className={styles.linkButton} disabled={disabled}
            onClick={() => void search()}>{T.labelSearchRetry}</button>
        </div>
      ) : null}
      {visible.length > 0 ? (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <caption className={styles.caption}>{T.labelResultsCaption}</caption>
            <thead><tr>
              <th scope="col">{T.labelCode}</th><th scope="col">{T.labelHeatCoil}</th>
              <th scope="col">{T.labelAvailableKg}</th><th scope="col">{T.labelVersion}</th>
              <th scope="col">{T.labelChoiceColumn}</th>
              {canOpenLabelHistory ? <th scope="col">{T.labelHistoryColumn}</th> : null}
            </tr></thead>
            <tbody>{visible.map((item) => {
              const blockers = allocationLabelBlockers(item, companyId)
              // Chosen means this exact Inventory read: a fresher version of the picked label is a new pick.
              const chosen = selected !== null && selected.labelId.toLowerCase() === item.labelId.toLowerCase()
                && selected.labelVersion === item.labelVersion
              return (
                <tr key={item.labelId} data-testid={`sales-allocation-label-row-${item.labelId}`}
                  data-selectable={blockers.length === 0}>
                  <td className={styles.identifier}>{item.labelCode}</td>
                  <td>{[item.heatNumber, item.coilNumber].map((value) => value ?? '—').join(' / ')}</td>
                  <td className={styles.numeric}>{item.availableKg}</td>
                  <td>{item.labelVersion}</td>
                  <td>
                    {blockers.length === 0 ? (
                      <button type="button" className={styles.secondaryButton} aria-pressed={chosen}
                        aria-label={`${item.labelCode} ${chosen ? T.labelChosenAria : T.labelChooseAria}`} disabled={disabled}
                        data-testid={`sales-allocation-label-option-${item.labelId}`}
                        onClick={() => onSelect({ labelId: item.labelId, labelVersion: item.labelVersion,
                          labelCode: item.labelCode, availableKg: item.availableKg })}>
                        {chosen ? T.labelChosen : T.labelChoose}
                      </button>
                    ) : (
                      <span data-testid={`sales-allocation-label-unselectable-${item.labelId}`}>
                        <strong>{T.labelUnselectable}</strong>{' '}
                        {blockers.map((blocker) => T.labelBlocker[blocker]).join(' ')}
                      </span>
                    )}
                  </td>
                  {canOpenLabelHistory ? (
                    <td>
                      <Link to={inventoryLabelDetailPath(item.labelId)}
                        aria-label={`${item.labelCode} ${T.labelHistoryAria}`}
                        data-testid={`sales-allocation-label-history-${item.labelId}`}>{T.labelHistory}</Link>
                    </td>
                  ) : null}
                </tr>
              )
            })}</tbody>
          </table>
        </div>
      ) : null}
      {selected === null ? <p className={styles.muted} data-testid={`sales-allocation-label-selected-${lineId}`}>{T.labelNoneSelected}</p> : (
        <p data-testid={`sales-allocation-label-selected-${lineId}`}>
          {T.labelSelected} <span className={styles.identifier}>{selected.labelCode}</span>
          {` · ${T.labelSelectedVersion} ${selected.labelVersion} · ${T.labelSelectedAvailable(selected.availableKg)}`}{' '}
          <button type="button" className={styles.linkButton} disabled={disabled}
            onClick={() => onSelect(null)}>{T.labelClear}</button>
        </p>
      )}
    </fieldset>
  )
}
