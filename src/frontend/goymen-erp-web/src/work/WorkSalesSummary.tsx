import { useLayoutEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { askAi, fetchAiCapabilities } from '../ai/aiApi'
import { AI_AVAILABILITY, type AiAskAnswer, type AiComposedAnswer } from '../ai/aiContract'
import { AI_PANEL_CAPABILITY } from '../ai/aiCapabilities'
import { AiFeedbackPanel } from '../ai/AiFeedbackPanel'
import { isAiFeedbackAnswerId } from '../ai/aiFeedbackContract'
import { isBoundUuid, isPositiveInt64Text } from '../finance/contract'
import { hasExactKeys } from '../finance/wireShape'
import { useShellState } from '../shell/shellContext'
import { capabilityFailClosed, contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'
import { WORK_SALES_SUMMARY_TEXT as T } from './workSalesSummaryText'

const TOOL = 'sales.draft-summary.v1', TARGET = 'sales.order.draft-summary', TOOL_VERSION = '1'
const uuid = (value: unknown): value is string => typeof value === 'string' && value.length === 36
  && isBoundUuid(value)
const same = (left: string, right: string) => left.toLowerCase() === right.toLowerCase()
const version = (value: string) => value === value.trim() && isPositiveInt64Text(value)
const exact = (value: unknown, keys: readonly string[]): value is Record<string, unknown> => typeof value === 'object'
  && value !== null && !Array.isArray(value) && hasExactKeys(value as Record<string, unknown>, keys)

// Only the existing owner's closed, no-price/no-people/no-free-text surface is accepted. Native row
// version and the released source digest are distinct; neither is computed from the other here.
function boundAnswer(reply: AiAskAnswer, draftId: string, expectedVersion: string): AiComposedAnswer | null {
  if (reply.mode !== undefined || reply.outcome !== 'answered' || reply.answer === null || reply.answer.grounded === false
    || !isAiFeedbackAnswerId(reply.answer.answerId) || reply.toolResults.length !== 1) return null
  const result = reply.toolResults[0]!, source = result.source
  if (result.toolId !== TOOL || result.toolVersion !== TOOL_VERSION || result.outcome !== 'succeeded'
    || source === null || source.targetKind !== TARGET || !same(source.targetId, draftId) || source.dataClass !== 'Internal'
    || source.sourceVersion.length !== 71 || !/^sha256:[0-9a-f]{64}$/.test(source.sourceVersion) || result.contentJson === null) return null
  let content: unknown
  try { content = JSON.parse(result.contentJson) } catch { return null }
  if (!exact(content, ['draftId', 'version', 'revision', 'state', 'approvalState', 'lines'])
    || !uuid(content.draftId) || !same(content.draftId, draftId) || content.version !== expectedVersion
    || typeof content.revision !== 'string' || !content.revision.trim()
    || !['Draft', 'Cancelled', 'Closed'].includes(String(content.state))
    || !['Preparing', 'PendingApproval', 'Approved', 'Rejected', 'Withdrawn'].includes(String(content.approvalState))
    || !Array.isArray(content.lines) || !content.lines.every(line => exact(line, ['lineId', 'lineNo', 'productId', 'orderUnitId', 'quantity'])
      && uuid(line.lineId) && Number.isSafeInteger(line.lineNo) && (line.lineNo as number) > 0
      && uuid(line.productId) && uuid(line.orderUnitId) && typeof line.quantity === 'string' && line.quantity === line.quantity.trim()
      && /^(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/.test(line.quantity) && /[1-9]/.test(line.quantity))) return null
  const lines = content.lines as Record<string, unknown>[]
  if (new Set(lines.map(line => String(line.lineId).toLowerCase())).size !== lines.length
    || new Set(lines.map(line => line.lineNo)).size !== lines.length || reply.answer.citations.length === 0
    || !reply.answer.citations.every(citation => citation.toolId === TOOL && citation.toolVersion === TOOL_VERSION
      && citation.targetKind === TARGET && same(citation.targetId, draftId) && citation.sourceVersion === source.sourceVersion
      && citation.asOfUtc === source.asOfUtc && citation.title === source.title)) return null
  return reply.answer
}

interface Props { readonly companyId: string; readonly draftId: string; readonly expectedVersion: string }

/** Optional B2 read surface. It does not mount a Work route or affect the owner's decision controls. */
export function WorkSalesSummary(props: Props) {
  const shell = useShellState(), context = contextKey(shell)
  if (context === null || capabilityFailClosed(shell) || shell.activeContext?.scopeMode !== 'single'
    || !uuid(props.companyId) || !uuid(props.draftId) || !version(props.expectedVersion)
    || !same(shell.activeContext.companyId!, props.companyId) || !hasCapability(shell, AI_PANEL_CAPABILITY)
    || !hasCapability(shell, 'sales.drafts.detail')) return <p role="status">{T.unavailable}</p>
  const namespace = contextKeyString(context)
  return <Summary key={`${namespace}:${shell.routeEntryGeneration}:${props.draftId}:${props.expectedVersion}`}
    {...props} namespace={namespace} generation={shell.routeEntryGeneration} />
}

type SummaryState = { readonly kind: 'idle' | 'pending' | 'stale' | 'cannot-answer' }
  | { readonly kind: 'answered'; readonly answer: AiComposedAnswer }
  | { readonly kind: 'failed'; readonly outcome: ViewOutcome<AiAskAnswer> }

function Summary({ companyId, draftId, expectedVersion, namespace, generation }: Props & { readonly namespace: string; readonly generation: number }) {
  const boundary = useAuthBoundary(), inFlight = useRef<AbortController | null>(null)
  const [view, setView] = useState<SummaryState>({ kind: 'idle' })
  const capabilities = useQuery({ queryKey: [namespace, generation, 'work-sales-summary-capabilities', draftId, expectedVersion],
    retry: false, staleTime: 0, gcTime: 0, refetchOnMount: 'always', queryFn: async ({ signal }) => {
      const snapshot = boundary.capture(), result = await fetchAiCapabilities(companyId, signal)
      if (result.type === 'http') boundary.report(result.status, snapshot)
      return result
    } })
  const result = capabilities.isFetchedAfterMount ? capabilities.data : undefined
  const ready = result?.type === 'ok-single' && result.data.availability === AI_AVAILABILITY.ready && result.data.panelAvailable
    && result.data.tools.some(tool => tool.toolId === TOOL && tool.version === TOOL_VERSION && tool.ownerModule === 'Sales' && tool.dataClass === 'Internal')
  useLayoutEffect(() => () => { inFlight.current?.abort(); inFlight.current = null }, [ready])
  const request = async () => {
    if (!ready || inFlight.current !== null) return
    const controller = new AbortController(), snapshot = boundary.capture()
    inFlight.current = controller; setView({ kind: 'pending' })
    const answer = await askAi({ question: T.question, toolId: null, toolVersion: null, arguments: null,
      openRecordContext: { targetKind: TARGET, targetId: draftId } }, companyId, controller.signal)
    if (controller.signal.aborted || inFlight.current !== controller) return
    inFlight.current = null
    if (answer.type === 'http') boundary.report(answer.status, snapshot)
    if (answer.type !== 'ok-single') { setView({ kind: 'failed', outcome: answer }); return }
    if (answer.data.outcome === 'cannot_answer') { setView({ kind: 'cannot-answer' }); return }
    const matched = boundAnswer(answer.data, draftId, expectedVersion)
    setView(matched === null ? { kind: 'stale' } : { kind: 'answered', answer: matched })
  }
  return <section aria-label={T.heading}><h3>{T.heading}</h3><p>{T.scope}</p>
    {result === undefined ? <StateBlock state={{ kind: 'initial-loading' }} subject={T.heading} />
      : result.type !== 'ok-single' ? <StateBlock state={classifyOutcome(result)} subject={T.heading} onRetry={() => void capabilities.refetch()} />
      : !ready ? <p role="status">{T.unavailable}</p> : <>
        <Button type="button" disabled={view.kind === 'pending'} onClick={() => void request()}>{view.kind === 'pending' ? T.pending : T.request}</Button>
        {view.kind === 'stale' ? <p role="alert">{T.stale}</p> : null}
        {view.kind === 'cannot-answer' ? <p role="status">{T.cannotAnswer}</p> : null}
        {view.kind === 'failed' ? <StateBlock state={classifyOutcome(view.outcome)} subject={T.heading} /> : null}
        {view.kind === 'answered' ? <div><p>{view.answer.text}</p><h4>{T.citations}</h4><ul>{view.answer.citations.map((citation, index) =>
          <li key={`${citation.targetId}:${index}`}>{citation.title} · {T.version} {citation.sourceVersion} · <time dateTime={citation.asOfUtc}>{citation.asOfUtc}</time></li>)}</ul>
          <AiFeedbackPanel companyId={companyId} answerId={view.answer.answerId!} />
        </div> : null}
      </>}
  </section>
}
