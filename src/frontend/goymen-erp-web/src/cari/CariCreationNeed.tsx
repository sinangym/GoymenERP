import { useEffect, useRef, useState } from 'react'
import { useShellState } from '../shell/shellContext'
import { capabilityFailClosed, contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { WORK_CAPABILITY } from '../work/capabilities'
import { searchWorkPeople, type CreateTaskBody } from '../work/collaborationApi'
import { WORK_LIMITS, type WorkPerson } from '../work/collaborationContract'
import { generateAttemptKey } from './commandAttempt'
import { createCounterpartyNeed } from './creationNeedApi'
import { QUICK_CARI_TEXT as T } from './quickCreateText'

interface FrozenNeed { readonly key: string; readonly body: Readonly<CreateTaskBody> }
// Ephemeral recovery only: neither the task details nor its key enter browser storage or URLs.
const pending = new Map<string, FrozenNeed>()
export function resetPendingCreationNeeds(): void { pending.clear() }

interface Props {
  readonly disabled?: boolean; readonly onUnresolvedChange?: (value: boolean) => void
}
export function CariCreationNeed(props: Props) {
  const shell = useShellState()
  return <CreationNeed key={JSON.stringify([shell.activeContext?.userId, shell.activeContext?.companyId,
    shell.sessionEpoch, shell.sessionBinding])} {...props} />
}
function CreationNeed({ disabled = false, onUnresolvedChange }: Props) {
  const shell = useShellState(), boundary = useAuthBoundary()
  const context = contextKey(shell), namespace = context === null ? null : contextKeyString(context)
  const actor = shell.activeContext?.userId ?? null
  const company = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const scope = company === null || actor === null ? null : JSON.stringify([actor, company, shell.sessionEpoch, shell.sessionBinding, 'counterparty-creation-need'])
  // An epoch restarts when ShellProvider remounts. Cross-mount recovery requires the server's session identity.
  const recoveryKey = typeof shell.sessionBinding === 'string' && shell.sessionBinding.trim().length > 0 ? scope : null
  const allowed = !capabilityFailClosed(shell) && company !== null && hasCapability(shell, WORK_CAPABILITY.assignTask)
  const [opened, setOpened] = useState(false)
  const [held, setHeld] = useState<FrozenNeed | null>(() => recoveryKey === null ? null : pending.get(recoveryKey) ?? null)
  const [title, setTitle] = useState(held?.body.title ?? ''), [details, setDetails] = useState(held?.body.details ?? '')
  const [due, setDue] = useState(''), [query, setQuery] = useState('')
  const [people, setPeople] = useState<readonly WorkPerson[]>([]), [recipient, setRecipient] = useState<string | null>(held?.body.assigneeUserId ?? null)
  const [searching, setSearching] = useState(false), [busy, setBusy] = useState(false), [completed, setCompleted] = useState(false)
  const [message, setMessage] = useState<string | null>(held === null ? null : T.needUnknown)
  const live = useRef({ scope, namespace, allowed }), mounted = useRef(false), inFlight = useRef(false), searchGeneration = useRef(0)
  useEffect(() => { live.current = { scope, namespace, allowed } })
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; searchGeneration.current++ } }, [])
  useEffect(() => { onUnresolvedChange?.(held !== null); return () => onUnresolvedChange?.(false) }, [held, onUnresolvedChange])

  const findPeople = async () => {
    if (!allowed || busy || searching || held !== null || company === null) return
    const captured = boundary.capture(), capturedNamespace = namespace, generation = ++searchGeneration.current
    setSearching(true); setPeople([]); setRecipient(null)
    const answer = await searchWorkPeople(company, query)
    if (answer.type === 'http') boundary.report(answer.status, captured)
    if (!mounted.current || live.current.namespace !== capturedNamespace || !live.current.allowed || generation !== searchGeneration.current) return
    setSearching(false)
    if (answer.type === 'ok-single') { setPeople(answer.data.people); setMessage(answer.data.people.length === 0 ? T.needNoPeople : null) }
    else setMessage(T.needPeopleUnavailable)
  }
  const send = async (snapshot: FrozenNeed) => {
    if (!allowed || disabled || inFlight.current || company === null || actor === null || scope === null) return
    const captured = boundary.capture(), capturedNamespace = namespace, capturedScope = scope
    inFlight.current = true; setBusy(true); setMessage(null); setHeld(snapshot)
    if (recoveryKey !== null) pending.set(recoveryKey, snapshot)
    const outcome = await createCounterpartyNeed(company, actor, snapshot.key, snapshot.body)
    if (outcome.type === 'unknown' && outcome.reason === 'authority') boundary.report(outcome.status, captured)
    inFlight.current = false
    if (!mounted.current || live.current.scope !== capturedScope || live.current.namespace !== capturedNamespace || !live.current.allowed) return
    setBusy(false)
    if (outcome.type === 'unknown') { setMessage(T.needUnknown); return }
    if (recoveryKey !== null) pending.delete(recoveryKey)
    setHeld(null)
    if (outcome.type === 'refused') { setMessage(T.needRefused); return }
    setCompleted(true); setMessage(outcome.replayed ? T.needReplayed : T.needCreated)
  }
  const prepare = () => {
    if (!allowed || held !== null || completed || busy || recipient === null || !people.some(person => person.userId === recipient)) return
    const instant = new Date(due)
    if (title.trim().length === 0 || title.length > WORK_LIMITS.taskTitle || details.length > WORK_LIMITS.taskDetails
      || (details.length > 0 && details.trim().length === 0) || !Number.isFinite(instant.getTime()) || instant.getTime() <= Date.now()) {
      setMessage(T.needInvalid); return
    }
    const body = Object.freeze({ title, details: details.length === 0 ? null : details, assigneeUserId: recipient,
      dueAtUtc: instant.toISOString(), recordType: null, recordId: null })
    void send(Object.freeze({ key: generateAttemptKey(), body }))
  }
  if (!allowed) return <p>{T.needDenied}</p>
  const frozen = held !== null || busy || completed
  return <section aria-label={T.needTitle} onKeyDown={event => {
    if (event.key === 'Enter' && event.target instanceof HTMLElement && !['BUTTON', 'TEXTAREA'].includes(event.target.tagName)) event.preventDefault()
  }}>
    <button type="button" disabled={disabled} onClick={() => setOpened(!opened)}>{opened ? T.needHide : held === null ? T.needOpen : T.needResume}</button>
    <div hidden={!opened}>
      <p>{T.needNote}</p>
      <fieldset disabled={disabled || frozen}>
        <label>{T.needSubject}<input value={title} maxLength={WORK_LIMITS.taskTitle} onChange={event => setTitle(event.target.value)} /></label>
        <label>{T.needDetails}<textarea value={details} maxLength={WORK_LIMITS.taskDetails} onChange={event => setDetails(event.target.value)} /></label>
        <label>{T.needSearch}<input value={query} maxLength={WORK_LIMITS.peopleQuery} onChange={event => { searchGeneration.current++; setSearching(false); setQuery(event.target.value); setPeople([]); setRecipient(null) }} /></label>
        <button type="button" disabled={searching} onClick={() => void findPeople()}>{searching ? T.needSearching : T.needFind}</button>
        <label>{T.needRecipient}<select value={recipient ?? ''} onChange={event => setRecipient(event.target.value || null)}>
          <option value="">{T.needChoose}</option>
          {people.map(person => <option key={person.userId} value={person.userId}>{person.displayName}</option>)}
        </select></label>
        <label>{T.needDue}<input type="datetime-local" value={due} onChange={event => setDue(event.target.value)} /></label>
        <button type="button" onClick={prepare} disabled={recipient === null || title.trim().length === 0 || due.length === 0}>{T.needSend}</button>
      </fieldset>
      {held === null ? null : <>
        <p>{T.needFrozenRecipient} {held.body.assigneeUserId}</p>
        <p>{T.needFrozenDue} <time dateTime={held.body.dueAtUtc}>{held.body.dueAtUtc}</time></p>
        <button type="button" disabled={disabled || busy} onClick={() => void send(held)}>{busy ? T.needSending : T.needRetry}</button>
      </>}
      {message === null ? null : <p role="status">{message}</p>}
    </div>
  </section>
}
