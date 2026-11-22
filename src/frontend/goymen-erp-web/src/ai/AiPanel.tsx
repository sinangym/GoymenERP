/**
 * Plan AI-5 — her sayfada sağ AI paneli; plan AI-3 — salt-okuma runtime'ı.
 *
 * Panel açılınca sunucunun AI yeteneğini okur (`GET /api/v1/ai/capabilities`, şirket istek bağlamıdır). AI kapalıysa,
 * yapılandırma hazır değilse, yetki yoksa veya okunabilir araç yoksa yalnız dürüst durum çizilir: giriş alanı, sahte
 * cevap veya çalışmayan düğme yoktur (ERP-000J §17, DEC-AI-001). Araç varsa soru, sunucunun izin verdiği salt-okuma
 * araçlarından biriyle sorulur; cevap yalnız sunucunun döndürdüğü araç sonucu ve kaynak kaydıdır. Şirket bilgisi
 * gövdede veya araç argümanında gönderilmez. ERP akışı panelden bağımsızdır.
 */

import { useEffect, useLayoutEffect, useRef, useState, type FormEvent, type KeyboardEvent, type RefObject } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { askAi, fetchAiCapabilities } from './aiApi'
import {
  AI_AVAILABILITY,
  AI_PROBLEM_CODE,
  AI_TOOL_RESULT_OUTCOME,
  parseToolArguments,
  problemCode,
  problemInvalidFields,
  type AiAskAnswer,
  type AiCapabilities,
  type AiComposedAnswer,
  type AiInvalidField,
  type AiToolCapability,
  type AiToolResult,
} from './aiContract'
import { AI_DATA_CLASS_LABEL, AI_TEXT } from './aiText'
import styles from './AiPanel.module.css'

export const AI_PANEL_ID = 'ai-panel'

export function AiPanel({
  onClose,
  returnFocusRef,
}: {
  readonly onClose: () => void
  readonly returnFocusRef: RefObject<HTMLButtonElement | null>
}) {
  const state = useShellState()
  const panelRef = useRef<HTMLElement>(null)
  const headingRef = useRef<HTMLHeadingElement>(null)
  const key = contextKey(state)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = state.activeContext?.scopeMode === 'single' ? state.activeContext.companyId : null

  useEffect(() => {
    headingRef.current?.focus()
  }, [])

  // Focus goes back to the toggle only when it was inside the panel; a company switch or a page action that
  // closes the panel must not pull focus away from where the user is working.
  useLayoutEffect(() => {
    const panel = panelRef.current
    const returnTo = returnFocusRef.current
    return () => {
      if (panel?.contains(document.activeElement)) returnTo?.focus()
    }
  }, [returnFocusRef])

  // Escape belongs to the panel only while focus is in it: Escape in a page form keeps the panel open, and a
  // shell dialog (nav drawer, switch confirmation) keeps its own Escape.
  const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    if (event.key !== 'Escape' || event.defaultPrevented) return
    if (document.querySelector('[aria-modal="true"]') !== null) return
    event.preventDefault()
    onClose()
  }

  return (
    <aside
      id={AI_PANEL_ID}
      ref={panelRef}
      className={styles.panel}
      aria-labelledby="ai-panel-heading"
      onKeyDown={onKeyDown}
    >
      <div className={styles.header}>
        <h2 id="ai-panel-heading" className={styles.heading} tabIndex={-1} ref={headingRef}>
          {AI_TEXT.heading}
        </h2>
        <button type="button" className={styles.closeButton} aria-label={AI_TEXT.close} onClick={onClose}>
          <span aria-hidden="true">×</span>
        </button>
      </div>
      {namespace === null || companyId === null ? (
        <PanelNotice kind="disabled" />
      ) : (
        // Remounted per committed context: nothing of one company's AI state survives into another.
        <AiPanelBody key={namespace} namespace={namespace} companyId={companyId} />
      )}
    </aside>
  )
}

type NoticeKind = 'disabled' | 'runtime-unavailable' | 'forbidden' | 'no-tools'

const NOTICE: Record<NoticeKind, { readonly title: string; readonly body: string; readonly hint?: string }> = {
  disabled: { title: AI_TEXT.unavailableTitle, body: AI_TEXT.unavailableBody, hint: AI_TEXT.unavailableHint },
  'runtime-unavailable': { title: AI_TEXT.runtimeUnavailableTitle, body: AI_TEXT.runtimeUnavailableBody },
  forbidden: { title: AI_TEXT.panelForbiddenTitle, body: AI_TEXT.panelForbiddenBody },
  'no-tools': { title: AI_TEXT.noToolsTitle, body: AI_TEXT.noToolsBody },
}

function PanelNotice({ kind }: { readonly kind: NoticeKind }) {
  const notice = NOTICE[kind]
  return (
    <div className={styles.state} role="status" data-ai-state={kind}>
      <p className={styles.stateTitle}>{notice.title}</p>
      <p>{notice.body}</p>
      {notice.hint ? <p className={styles.hint}>{notice.hint}</p> : null}
    </div>
  )
}

function noticeFor(capabilities: AiCapabilities): NoticeKind | null {
  if (capabilities.availability === AI_AVAILABILITY.disabled) return 'disabled'
  if (capabilities.availability !== AI_AVAILABILITY.ready) return 'runtime-unavailable'
  if (!capabilities.panelAvailable) return 'forbidden'
  if (capabilities.tools.length === 0) return 'no-tools'
  return null
}

function AiPanelBody({ namespace, companyId }: { readonly namespace: string; readonly companyId: string }) {
  const boundary = useAuthBoundary()
  const read = useQuery({
    queryKey: [namespace, 'ai-capabilities'],
    staleTime: 0,
    gcTime: 0,
    refetchOnMount: 'always',
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchAiCapabilities(companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const outcome = read.isFetchedAfterMount ? read.data : undefined
  if (outcome === undefined) return <StateBlock state={{ kind: 'initial-loading' }} subject={AI_TEXT.stateSubject} />
  if (outcome.type !== 'ok-single') {
    return <StateBlock state={classifyOutcome(outcome)} subject={AI_TEXT.stateSubject} onRetry={() => void read.refetch()} />
  }
  const notice = noticeFor(outcome.data)
  if (notice !== null) return <PanelNotice kind={notice} />
  return <AskForm companyId={companyId} tools={outcome.data.tools} />
}

type AskView =
  | { readonly kind: 'idle' }
  | { readonly kind: 'pending' }
  | { readonly kind: 'answered'; readonly answer: AiAskAnswer }
  | { readonly kind: 'invalid'; readonly fields: readonly AiInvalidField[] }
  | { readonly kind: 'notice'; readonly title: string; readonly body: string }
  | { readonly kind: 'failed'; readonly outcome: ViewOutcome<AiAskAnswer> }

function viewOf(outcome: ViewOutcome<AiAskAnswer>): AskView {
  if (outcome.type === 'ok-single') return { kind: 'answered', answer: outcome.data }
  if (outcome.type !== 'http') return { kind: 'failed', outcome }
  const code = problemCode(outcome.body)
  if (outcome.status === 400 && code === AI_PROBLEM_CODE.requestInvalid) {
    return { kind: 'invalid', fields: problemInvalidFields(outcome.body) }
  }
  if (outcome.status === 503 && code === AI_PROBLEM_CODE.disabled) {
    return { kind: 'notice', title: AI_TEXT.askDisabledTitle, body: AI_TEXT.askDisabledBody }
  }
  if (outcome.status === 503 && code === AI_PROBLEM_CODE.featureDisabled) {
    return { kind: 'notice', title: AI_TEXT.askFeatureDisabledTitle, body: AI_TEXT.askFeatureDisabledBody }
  }
  if (outcome.status === 503 && code === AI_PROBLEM_CODE.budgetExhausted) {
    return { kind: 'notice', title: AI_TEXT.askBudgetExhaustedTitle, body: AI_TEXT.askBudgetExhaustedBody }
  }
  if (outcome.status === 503 && code === AI_PROBLEM_CODE.toolUnavailable) {
    return { kind: 'notice', title: AI_TEXT.askToolUnavailableTitle, body: AI_TEXT.askToolUnavailableBody }
  }
  if (outcome.status === 403) return { kind: 'notice', title: AI_TEXT.askRefusedTitle, body: AI_TEXT.askRefusedBody }
  return { kind: 'failed', outcome }
}

function toolKey(tool: AiToolCapability): string {
  return `${tool.toolId}@${tool.version}`
}

function AskForm({ companyId, tools }: { readonly companyId: string; readonly tools: readonly AiToolCapability[] }) {
  const boundary = useAuthBoundary()
  const [question, setQuestion] = useState('')
  const [selected, setSelected] = useState(() => (tools.length > 0 ? toolKey(tools[0] as AiToolCapability) : ''))
  const [argumentsText, setArgumentsText] = useState('')
  const [localInvalid, setLocalInvalid] = useState<readonly AiInvalidField[]>([])
  const [view, setView] = useState<AskView>({ kind: 'idle' })
  const inFlight = useRef<AbortController | null>(null)

  // A late answer never lands after the panel closed or the context changed (the body remounts per context).
  useEffect(() => () => inFlight.current?.abort(), [])

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (view.kind === 'pending') return
    const trimmed = question.trim()
    const tool = tools.find((candidate) => toolKey(candidate) === selected)
    const parsedArguments = parseToolArguments(argumentsText)
    const invalid: AiInvalidField[] = []
    if (trimmed === '') invalid.push('question')
    if (tool === undefined) invalid.push('tool')
    if (parsedArguments === 'invalid') invalid.push('arguments')
    setLocalInvalid(invalid)
    if (invalid.length > 0 || tool === undefined || parsedArguments === 'invalid') return

    inFlight.current?.abort()
    const controller = new AbortController()
    inFlight.current = controller
    setView({ kind: 'pending' })
    const snapshot = boundary.capture()
    const outcome = await askAi(
      { question: trimmed, toolId: tool.toolId, toolVersion: tool.version, arguments: parsedArguments },
      companyId,
      controller.signal,
    )
    if (controller.signal.aborted || inFlight.current !== controller) return
    inFlight.current = null
    if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
    setView(viewOf(outcome))
  }

  const invalidFields = view.kind === 'invalid' ? [...localInvalid, ...view.fields] : localInvalid
  const isInvalid = (field: AiInvalidField) => invalidFields.includes(field)
  const pending = view.kind === 'pending'

  return (
    <div className={styles.ask}>
      <p className={styles.hint}>{AI_TEXT.readOnlyNotice}</p>
      <form className={styles.form} onSubmit={(event) => void submit(event)} noValidate aria-busy={pending || undefined}>
        <label className={styles.field}>
          {AI_TEXT.questionLabel}
          <textarea
            value={question}
            rows={3}
            aria-invalid={isInvalid('question') || undefined}
            aria-describedby="ai-question-hint ai-question-sensitive"
            onChange={(event) => setQuestion(event.target.value)}
          />
        </label>
        <p id="ai-question-hint" className={isInvalid('question') ? styles.fieldError : styles.hint}>
          {isInvalid('question') ? AI_TEXT.questionInvalid : AI_TEXT.questionHint}
        </p>
        <p id="ai-question-sensitive" className={styles.hint}>
          {AI_TEXT.questionSensitiveReminder}
        </p>
        <label className={styles.field}>
          {AI_TEXT.toolLabel}
          <select
            value={selected}
            aria-invalid={isInvalid('tool') || undefined}
            onChange={(event) => setSelected(event.target.value)}
          >
            {tools.map((tool) => (
              <option key={toolKey(tool)} value={toolKey(tool)}>
                {`${tool.toolId} (${AI_DATA_CLASS_LABEL[tool.dataClass] ?? tool.dataClass})`}
              </option>
            ))}
          </select>
        </label>
        {isInvalid('tool') ? <p className={styles.fieldError}>{AI_TEXT.toolInvalid}</p> : null}
        <label className={styles.field}>
          {AI_TEXT.argumentsLabel}
          <textarea
            value={argumentsText}
            rows={2}
            spellCheck={false}
            aria-invalid={isInvalid('arguments') || undefined}
            aria-describedby="ai-arguments-hint"
            onChange={(event) => setArgumentsText(event.target.value)}
          />
        </label>
        <p id="ai-arguments-hint" className={isInvalid('arguments') ? styles.fieldError : styles.hint}>
          {isInvalid('arguments') ? AI_TEXT.argumentsInvalid : AI_TEXT.argumentsHint}
        </p>
        <button type="submit" className={styles.submitButton} disabled={pending}>
          {pending ? AI_TEXT.submitting : AI_TEXT.submit}
        </button>
      </form>
      <AskResult view={view} />
    </div>
  )
}

function AskResult({ view }: { readonly view: AskView }) {
  switch (view.kind) {
    case 'pending':
      return <StateBlock state={{ kind: 'initial-loading' }} subject={AI_TEXT.stateSubject} />
    case 'failed':
      return <StateBlock state={classifyOutcome(view.outcome)} subject={AI_TEXT.stateSubject} />
    case 'notice':
      return (
        <div className={styles.state} role="alert">
          <p className={styles.stateTitle}>{view.title}</p>
          <p>{view.body}</p>
        </div>
      )
    case 'answered':
      return (
        <section className={styles.answer} aria-labelledby="ai-answer-heading">
          <h3 id="ai-answer-heading" className={styles.answerHeading}>
            {AI_TEXT.answerHeading}
          </h3>
          {view.answer.answer !== null ? (
            <ComposedAnswerView answer={view.answer.answer} />
          ) : (
            <p data-ai-answer="cannot-answer">{AI_TEXT.cannotAnswer}</p>
          )}
          <h4 className={styles.answerHeading}>{AI_TEXT.readSourcesHeading}</h4>
          {view.answer.toolResults.length === 0 ? (
            <p>{AI_TEXT.answerEmpty}</p>
          ) : (
            view.answer.toolResults.map((result, index) => <ToolResultView key={`${result.toolId}-${index}`} result={result} />)
          )}
        </section>
      )
    default:
      return null
  }
}

function ComposedAnswerView({ answer }: { readonly answer: AiComposedAnswer }) {
  return (
    <div data-ai-answer="answered">
      {/* Composed text is shown as text, never as markup. */}
      <p className={styles.content}>{answer.text}</p>
      <h4 className={styles.answerHeading}>{AI_TEXT.citationsHeading}</h4>
      <ul>
        {answer.citations.map((citation) => (
          <li key={`${citation.toolId}-${citation.targetId}-${citation.sourceVersion}`}>
            {citation.title} · {AI_TEXT.sourceVersion} {citation.sourceVersion} ·{' '}
            <time dateTime={citation.asOfUtc}>{citation.asOfUtc}</time>
          </li>
        ))}
      </ul>
    </div>
  )
}

function ToolResultView({ result }: { readonly result: AiToolResult }) {
  if (result.outcome === AI_TOOL_RESULT_OUTCOME.notFound) {
    return <p data-ai-result="not-found">{AI_TEXT.resultNotFound}</p>
  }
  return (
    <article className={styles.result} data-ai-result="succeeded">
      {/* Owner content is shown as text, never as markup. */}
      {result.contentJson !== null ? <pre className={styles.content}>{result.contentJson}</pre> : null}
      {result.source !== null ? (
        <dl className={styles.source}>
          <dt>{AI_TEXT.sourceLabel}</dt>
          <dd>{result.source.title}</dd>
          <dt>{AI_TEXT.sourceVersion}</dt>
          <dd>{result.source.sourceVersion}</dd>
          <dt>{AI_TEXT.sourceAsOf}</dt>
          <dd>
            <time dateTime={result.source.asOfUtc}>{result.source.asOfUtc}</time>
          </dd>
          <dt>{AI_TEXT.sourceDataClass}</dt>
          <dd>{AI_DATA_CLASS_LABEL[result.source.dataClass] ?? result.source.dataClass}</dd>
        </dl>
      ) : null}
    </article>
  )
}
