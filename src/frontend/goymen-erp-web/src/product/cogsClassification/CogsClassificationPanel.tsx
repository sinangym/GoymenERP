import { useLayoutEffect, useRef, useState, useSyncExternalStore, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useShellState } from '../../shell/shellContext'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../../shell/shellState'
import { StateBlock } from '../../shell/StateBlock'
import { classifyOutcome } from '../../shell/uiState'
import { useAuthBoundary } from '../../shell/useAuthBoundary'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import { PRODUCT_CAPABILITY, PRODUCT_SET_COGS_CLASSIFICATION_CAPABILITY } from '../capabilities'
import { isBoundUuid } from '../wireGuards'
import styles from '../ProductDetailPage.module.css'
import { fetchCogs, setCogs } from './api'
import { beginCogsAttempt, cogsAttempt, cogsAttemptFrozen, cogsBinding, finishCogsAttempt, leaveCogsAttempt, subscribeCogsAttempt } from './attempt'
import { COGS_REASON_MAXIMUM, cogsKind, sameCogsId, type CogsView, type SetCogsRequest } from './contract'
import { COGS_TEXT as T } from './text'

export function CogsClassificationPanel({ companyId, productId, queryNamespace }: {
  readonly companyId: string; readonly productId: string; readonly queryNamespace: string
}) {
  const shell = useShellState()
  const context = contextKey(shell)
  const actualCompany = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  if (context === null || actualCompany === null) return <StateBlock subject={T.subject} state={{ kind: 'no-authorized-company' }} />
  if (!hasCapability(shell, PRODUCT_CAPABILITY.productsDetail)) return <StateBlock subject={T.subject} state={{ kind: 'forbidden' }} />
  if (!isBoundUuid(companyId) || !isBoundUuid(productId) || !sameCogsId(companyId, actualCompany) || queryNamespace !== contextKeyString(context)) {
    return <StateBlock subject={T.subject} state={{ kind: 'stale-rejected' }} />
  }
  const canWrite = hasCapability(shell, PRODUCT_SET_COGS_CLASSIFICATION_CAPABILITY)
  return <CogsPanel key={JSON.stringify([queryNamespace, shell.sessionEpoch, shell.routeEntryGeneration, shell.requestFenceGeneration, canWrite, productId])}
    companyId={companyId} productId={productId} namespace={queryNamespace} shell={shell} canWrite={canWrite} />
}
function CogsPanel({ companyId, productId, namespace, shell, canWrite }: {
  readonly companyId: string; readonly productId: string; readonly namespace: string; readonly shell: ShellState; readonly canWrite: boolean
}) {
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const binding = cogsBinding(shell, productId)
  const attempt = useSyncExternalStore((listener) => subscribeCogsAttempt(binding, listener), () => cogsAttempt(binding), () => cogsAttempt(null))
  const frozen = cogsAttemptFrozen(attempt)
  const alive = useRef(false)
  const [editing, setEditing] = useState(false)
  useLayoutEffect(() => {
    alive.current = true
    return () => { alive.current = false; leaveCogsAttempt(binding) }
  }, [binding])
  const query = useQuery({
    queryKey: [namespace, shell.routeEntryGeneration, shell.requestFenceGeneration, 'product-cogs-classification', productId],
    retry: false, staleTime: 0, refetchOnMount: 'always',
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const result = await fetchCogs(companyId, productId, signal)
      if (result.type === 'http') boundary.report(result.status, security)
      return result
    },
  })
  async function send(request: SetCogsRequest) {
    if (!canWrite) return
    const snapshot = beginCogsAttempt(binding, companyId, productId, request)
    if (snapshot === null) return
    const security = boundary.capture()
    const result = await setCogs(snapshot.companyId, snapshot.productId, snapshot.request, snapshot.key)
    if (result.type === 'refused') boundary.report(result.status, security)
    // Departure keeps an unknown key. A late reply under former authority may not settle a newly mounted UI;
    // that UI can resolve it only by explicitly replaying the frozen snapshot under its fresh authority.
    if (!alive.current) return
    finishCogsAttempt(snapshot, result)
    if (result.type === 'applied') {
      void queryClient.invalidateQueries({ queryKey: [namespace, shell.routeEntryGeneration, 'product-history', productId], exact: true })
    }
    if (result.type === 'applied' || (result.type === 'refused' && (result.status === 409 || result.status === 404)
      && !cogsAttemptFrozen(cogsAttempt(binding)))) {
      setEditing(false)
      void query.refetch()
    }
  }
  const view = query.data?.type === 'ok-single' && !query.isFetching ? query.data.data : null
  const readState = query.isFetching ? { kind: 'initial-loading' as const } : classifyOutcome(query.data ?? { type: 'loading' })
  return <section className={styles.section} aria-labelledby={`cogs-heading-${productId}`}>
    <h2 id={`cogs-heading-${productId}`}>{T.heading}</h2>
    <p className={styles.note}>{T.shared}</p>
    {attempt.phase === 'sending' ? <p role="status">{T.sending}</p> : null}
    {attempt.phase === 'completed' ? <p role="status" className={styles.activationSuccess}>{attempt.result.isReplay ? T.replayed : T.saved}</p> : null}
    {attempt.phase === 'unknown' || (attempt.phase === 'failed' && attempt.unresolved) ? <div>
      <p role="alert">{T.unknown}</p>
      <StateBlock subject={T.subject} state={attempt.phase === 'unknown' ? { kind: attempt.reason } : classifyOutcome({ type: 'http', status: attempt.status })} />
      {attempt.phase === 'failed' ? <p>{T.unknownRefusal}</p> : null}
      {canWrite ? <Button type="button" onClick={() => void send(attempt.snapshot.request)}>{T.retry}</Button> : <p>{T.restoreAuthority}</p>}
    </div> : null}
    {attempt.phase === 'failed' && !attempt.unresolved ? (
      attempt.status === 400 || attempt.status === 422 ? <p role="alert" className={styles.fieldError}>{T.refused}</p>
        : <StateBlock subject={T.subject} state={classifyOutcome({ type: 'http', status: attempt.status })} />
    ) : null}
    {view === null ? <StateBlock subject={T.subject} state={readState} onRetry={() => void query.refetch()} /> : <>
      <p><strong>{T.classification}:</strong> {view.classification === null ? T.unset : view.classification === 1 ? T.tradeGoods : T.manufactured}</p>
      {view.changedInCompanyId !== null && !sameCogsId(view.changedInCompanyId, companyId) ? <p className={styles.note}>{T.changedElsewhere}</p> : null}
      {!canWrite ? <p className={styles.note}>{T.readOnly}</p> : editing ? <CogsForm key={JSON.stringify([view.productVersion, view.classificationVersion])}
        view={view} frozen={frozen} onSave={(request) => void send(request)} onCancel={() => setEditing(false)} />
        : <Button type="button" disabled={frozen} onClick={() => setEditing(true)}>{T.edit}</Button>}
    </>}
  </section>
}
function CogsForm({ view, frozen, onSave, onCancel }: {
  readonly view: CogsView; readonly frozen: boolean; readonly onSave: (request: SetCogsRequest) => void; readonly onCancel: () => void
}) {
  const [choice, setChoice] = useState('')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  function submit(event: FormEvent) {
    event.preventDefault()
    if (frozen) return
    const classification = choice === '1' ? 1 : choice === '2' ? 2 : null
    const canonicalReason = reason.trim()
    if (!cogsKind(classification) || canonicalReason === '' || canonicalReason.length > COGS_REASON_MAXIMUM || /[\u0000-\u001f\u007f-\u009f]/.test(canonicalReason)) {
      setError(T.required); return
    }
    onSave({ expectedProductVersion: view.productVersion, expectedClassificationVersion: view.classificationVersion, classification, reason: canonicalReason })
  }
  return <form aria-label={T.form} onSubmit={submit} className={styles.lifecycleForm}>
    {error === null ? null : <p role="alert" className={styles.fieldError}>{error}</p>}
    <fieldset disabled={frozen}>
      <Field label={T.classification}>{(control) => <select {...control} value={choice} onChange={(event) => setChoice(event.currentTarget.value)}>
        <option value="">{T.select}</option><option value="1">{T.tradeGoods}</option><option value="2">{T.manufactured}</option>
      </select>}</Field>
      <Field label={T.reason} help={T.reasonHelp}>{(control) => <input {...control} type="text" maxLength={COGS_REASON_MAXIMUM}
        value={reason} onChange={(event) => setReason(event.currentTarget.value)} />}</Field>
    </fieldset>
    <div className={styles.lifecycleButtons}>
      <Button type="submit" variant="primary" disabled={frozen}>{T.save}</Button>
      <Button type="button" disabled={frozen} onClick={onCancel}>{T.cancel}</Button>
    </div>
  </form>
}
