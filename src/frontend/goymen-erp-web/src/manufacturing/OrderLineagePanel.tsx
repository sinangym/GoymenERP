/**
 * URT-S22 (UI-18, API-28, URT-D63, KLN-D44): the order's lineage, a read-only part of the order screen under the order
 * read permission it already needs (no new permission or control).
 *
 * Opened on demand: nothing is requested until the user opens it, so the order screen's own reads stay as they were.
 * Once open it is keyed by the order's VERSION, so every re-read of the order after an accepted fact reads the lineage
 * again. Two directions in a WAI-ARIA tablist: "Geri iz" (each output label with ALL its input labels, their kg and
 * their kg shares) and "İleri iz" (each input label with every output it went into). One step beyond the order, the
 * other orders that produced its inputs or consumed its outputs. Every kg and share is the server's text, shown as it
 * came — never summed, rounded or recomputed here; while the shares are not calculated the panel says why, never a 0.
 */

import { useId, useRef, useState, type KeyboardEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchOrderLineage } from './lineageApi'
import type { OrderLineageInput, OrderLineageOutput, OrderLineageView } from './lineageContract'
import { LINEAGE_SHARE_STATUS_TEXT, LINEAGE_TEXT, lineageCodeText } from './lineageText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'
import styles from './OrderDetailPage.module.css'

const LINEAGE_TABS = ['backward', 'forward'] as const
type LineageTab = (typeof LINEAGE_TABS)[number]

const TAB_TEXT: Record<LineageTab, string> = {
  backward: LINEAGE_TEXT.backwardTab,
  forward: LINEAGE_TEXT.forwardTab,
}

export function OrderLineagePanel({
  namespace,
  routeEntryGeneration,
  companyId,
  orderId,
  orderVersion,
}: {
  readonly namespace: string
  readonly routeEntryGeneration: number
  readonly companyId: string
  readonly orderId: string
  readonly orderVersion: number
}) {
  const [open, setOpen] = useState(false)
  const bodyId = useId()

  return (
    <section aria-labelledby="mfg-lineage-heading" className={styles.section} data-testid="order-lineage">
      <h2 id="mfg-lineage-heading">{LINEAGE_TEXT.heading}</h2>
      <p className={styles.help}>{LINEAGE_TEXT.help}</p>
      <div className={styles.actions}>
        <Button
          type="button"
          aria-expanded={open}
          aria-controls={bodyId}
          data-testid="order-lineage-toggle"
          onClick={() => setOpen((current) => !current)}
        >
          {open ? LINEAGE_TEXT.hide : LINEAGE_TEXT.show}
        </Button>
      </div>
      <div id={bodyId} hidden={!open}>
        {open ? (
          <LineageRead
            namespace={namespace}
            routeEntryGeneration={routeEntryGeneration}
            companyId={companyId}
            orderId={orderId}
            orderVersion={orderVersion}
          />
        ) : null}
      </div>
    </section>
  )
}

function LineageRead({
  namespace,
  routeEntryGeneration,
  companyId,
  orderId,
  orderVersion,
}: {
  readonly namespace: string
  readonly routeEntryGeneration: number
  readonly companyId: string
  readonly orderId: string
  readonly orderVersion: number
}) {
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace, routeEntryGeneration, 'manufacturing-order-lineage', orderId, orderVersion],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchOrderLineage(companyId, orderId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<OrderLineageView>

  if (state.kind !== 'ready') {
    return <StateBlock state={state} subject={LINEAGE_TEXT.subject} onRetry={() => void query.refetch()} />
  }
  // The order's own outputs are where every edge of it starts: none means no lineage yet (upstream and downstream are
  // reached only through them).
  if (state.data.outputs.length === 0) {
    return <StateBlock state={{ kind: 'empty-no-data' }} subject={LINEAGE_TEXT.subject} />
  }
  return <LineageBody lineage={state.data} />
}

function LineageBody({ lineage }: { readonly lineage: OrderLineageView }) {
  const [active, setActive] = useState<LineageTab>('backward')
  const baseId = useId()
  const tabRefs = useRef<Partial<Record<LineageTab, HTMLButtonElement | null>>>({})
  const tabId = (tab: LineageTab) => `${baseId}-tab-${tab}`
  const panelId = (tab: LineageTab) => `${baseId}-panel-${tab}`

  function move(event: KeyboardEvent<HTMLButtonElement>, current: LineageTab) {
    const index = LINEAGE_TABS.indexOf(current)
    let next: LineageTab | null = null
    if (event.key === 'ArrowRight') next = LINEAGE_TABS[(index + 1) % LINEAGE_TABS.length]
    else if (event.key === 'ArrowLeft') next = LINEAGE_TABS[(index - 1 + LINEAGE_TABS.length) % LINEAGE_TABS.length]
    else if (event.key === 'Home') next = LINEAGE_TABS[0]
    else if (event.key === 'End') next = LINEAGE_TABS[LINEAGE_TABS.length - 1]
    if (next === null) return
    event.preventDefault()
    setActive(next)
    tabRefs.current[next]?.focus()
  }

  return (
    <div className={styles.tabs} data-testid="order-lineage-body">
      {lineage.truncated ? (
        <div className={styles.noticeWarn} role="status" data-testid="order-lineage-truncated">
          <p>{LINEAGE_TEXT.truncated}</p>
        </div>
      ) : null}
      <div role="tablist" aria-label={LINEAGE_TEXT.tabsLabel} className={styles.tabList}>
        {LINEAGE_TABS.map((tab) => {
          const selected = tab === active
          return (
            <button
              key={tab}
              ref={(element) => {
                tabRefs.current[tab] = element
              }}
              type="button"
              role="tab"
              id={tabId(tab)}
              aria-selected={selected}
              aria-controls={panelId(tab)}
              tabIndex={selected ? 0 : -1}
              className={styles.tab}
              data-testid={`order-lineage-tab-${tab}`}
              onClick={() => setActive(tab)}
              onKeyDown={(event) => move(event, tab)}
            >
              {TAB_TEXT[tab]}
            </button>
          )
        })}
      </div>
      <div
        role="tabpanel"
        id={panelId('backward')}
        aria-labelledby={tabId('backward')}
        hidden={active !== 'backward'}
        tabIndex={0}
        className={styles.tabPanel}
        data-testid="order-lineage-backward"
      >
        <OutputsTable caption={LINEAGE_TEXT.backwardCaption} outputs={lineage.outputs} showOrder={false} />
        <h3>{LINEAGE_TEXT.upstreamHeading}</h3>
        {lineage.upstream.length === 0 ? (
          <p className={styles.help} data-testid="order-lineage-upstream">
            {LINEAGE_TEXT.noUpstream}
          </p>
        ) : (
          <div data-testid="order-lineage-upstream">
            <OutputsTable caption={LINEAGE_TEXT.upstreamHeading} outputs={lineage.upstream} showOrder />
          </div>
        )}
      </div>
      <div
        role="tabpanel"
        id={panelId('forward')}
        aria-labelledby={tabId('forward')}
        hidden={active !== 'forward'}
        tabIndex={0}
        className={styles.tabPanel}
        data-testid="order-lineage-forward"
      >
        <InputsTable inputs={lineage.inputs} />
        <h3>{LINEAGE_TEXT.downstreamHeading}</h3>
        {lineage.downstream.length === 0 ? (
          <p className={styles.help} data-testid="order-lineage-downstream">
            {LINEAGE_TEXT.noDownstream}
          </p>
        ) : (
          <div data-testid="order-lineage-downstream">
            <OutputsTable caption={LINEAGE_TEXT.downstreamHeading} outputs={lineage.downstream} showOrder />
          </div>
        )}
      </div>
    </div>
  )
}

function statusText(output: Pick<OrderLineageOutput, 'shareStatus' | 'shareCode'>): string {
  const status = LINEAGE_SHARE_STATUS_TEXT[output.shareStatus]
  return output.shareCode === null ? status : `${status} — ${LINEAGE_TEXT.reason}: ${lineageCodeText(output.shareCode)}`
}

/** The backward trace: one row per input line, the output's cells spanning all its lines. */
function OutputsTable({
  caption,
  outputs,
  showOrder,
}: {
  readonly caption: string
  readonly outputs: readonly OrderLineageOutput[]
  readonly showOrder: boolean
}) {
  return (
    <div className={styles.tableWrap}>
      <table className={styles.table}>
        <caption>{caption}</caption>
        <thead>
          <tr>
            {showOrder ? <th scope="col">{LINEAGE_TEXT.order}</th> : null}
            <th scope="col">{LINEAGE_TEXT.outputLabel}</th>
            <th scope="col">{LINEAGE_TEXT.outputDeclaration}</th>
            <th scope="col">{LINEAGE_TEXT.shareStatus}</th>
            <th scope="col">{LINEAGE_TEXT.totalConsumedKg}</th>
            <th scope="col">{LINEAGE_TEXT.inputLabel}</th>
            <th scope="col">{LINEAGE_TEXT.consumedKg}</th>
            <th scope="col">{LINEAGE_TEXT.share}</th>
          </tr>
        </thead>
        {outputs.map((output) => {
          const span = Math.max(output.inputs.length, 1)
          const head = (
            <>
              {showOrder ? (
                <td rowSpan={span} className={styles.mono}>
                  {output.orderId}
                </td>
              ) : null}
              <th scope="rowgroup" rowSpan={span} className={styles.mono}>
                {output.outputLabelId}
              </th>
              <td rowSpan={span} className={styles.mono}>
                {output.outputId}
              </td>
              <td rowSpan={span} data-testid="lineage-share-status">
                {statusText(output)}
              </td>
              <td rowSpan={span} data-testid="lineage-total-kg">
                {output.totalConsumedKg ?? LINEAGE_TEXT.noShare}
              </td>
            </>
          )
          return (
            <tbody key={output.outputId} data-lineage-output={output.outputId}>
              {output.inputs.length === 0 ? (
                <tr>
                  {head}
                  <td colSpan={3}>{LINEAGE_TEXT.noInputs}</td>
                </tr>
              ) : (
                output.inputs.map((input, index) => (
                  <tr key={input.inputLabelId} data-lineage-input={input.inputLabelId}>
                    {index === 0 ? head : null}
                    <td className={styles.mono}>{input.inputLabelId}</td>
                    <td data-testid="lineage-consumed-kg">{input.consumedKg}</td>
                    <td data-testid="lineage-share">{input.share ?? LINEAGE_TEXT.noShare}</td>
                  </tr>
                ))
              )}
            </tbody>
          )
        })}
      </table>
    </div>
  )
}

/** The forward trace: one row per output an input label went into, the input's cell spanning them. */
function InputsTable({ inputs }: { readonly inputs: readonly OrderLineageInput[] }) {
  return (
    <div className={styles.tableWrap}>
      <table className={styles.table}>
        <caption>{LINEAGE_TEXT.forwardCaption}</caption>
        <thead>
          <tr>
            <th scope="col">{LINEAGE_TEXT.inputLabel}</th>
            <th scope="col">{LINEAGE_TEXT.outputLabel}</th>
            <th scope="col">{LINEAGE_TEXT.outputDeclaration}</th>
            <th scope="col">{LINEAGE_TEXT.shareStatus}</th>
            <th scope="col">{LINEAGE_TEXT.consumedKg}</th>
            <th scope="col">{LINEAGE_TEXT.share}</th>
          </tr>
        </thead>
        {inputs.map((input) => (
          <tbody key={input.inputLabelId} data-lineage-input={input.inputLabelId}>
            {input.outputs.map((output, index) => (
              <tr key={output.outputId} data-lineage-output={output.outputId}>
                {index === 0 ? (
                  <th scope="rowgroup" rowSpan={input.outputs.length} className={styles.mono}>
                    {input.inputLabelId}
                  </th>
                ) : null}
                <td className={styles.mono}>{output.outputLabelId}</td>
                <td className={styles.mono}>{output.outputId}</td>
                <td data-testid="lineage-share-status">{LINEAGE_SHARE_STATUS_TEXT[output.shareStatus]}</td>
                <td data-testid="lineage-consumed-kg">{output.consumedKg}</td>
                <td data-testid="lineage-share">{output.share ?? LINEAGE_TEXT.noShare}</td>
              </tr>
            ))}
          </tbody>
        ))}
      </table>
    </div>
  )
}
