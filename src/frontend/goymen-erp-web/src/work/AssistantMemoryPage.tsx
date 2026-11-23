import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { capabilityFailClosed, contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { Button } from '../ui/Button'
import type { WorkCommandOutcome } from './collaborationApi'
import { commandScope, pendingKey, settleKey } from './collaborationApi'
import { payloadFingerprint } from './collaborationOutcome'
import {
  createAssistantMemoryEntry, deleteAssistantMemoryEntry, fetchAssistantMemoryStatus, fetchMyAssistantMemory, reviseAssistantMemoryEntry,
} from './assistantMemoryApi'
import { ASSISTANT_MEMORY_KINDS, ASSISTANT_MEMORY_LIMITS, type AssistantMemoryEntry, type AssistantMemoryKind } from './assistantMemoryContract'
import {
  ASSISTANT_MEMORY_KIND_TEXT as KIND, ASSISTANT_MEMORY_REASON_TEXT as REASON, ASSISTANT_MEMORY_STATE_TEXT as STATE, ASSISTANT_MEMORY_TEXT as T,
} from './assistantMemoryText'
import styles from './Work.module.css'

/** One printable line, as the server requires (it re-validates; this only avoids a pointless round trip). */
const validBody = (value: string) => value.trim() === value && value.length > 0 && value.length <= ASSISTANT_MEMORY_LIMITS.body
  && [...value].every((character) => {
    const code = character.codePointAt(0) ?? 0
    return code > 0x1f && (code < 0x7f || code > 0x9f) && code !== 0x2028 && code !== 0x2029
  })

function assistantMemoryRefusal(outcome: WorkCommandOutcome<unknown>): string {
  if (outcome.type === 'unknown') return T.unknownOutcome
  if (outcome.type !== 'refused') return ''
  switch (outcome.code) {
    case 'work_assistant_memory_disabled': return T.disabled
    case 'work_assistant_memory_closed_for_user': return T.closedForUser
    case 'work_assistant_memory_limit_reached': return T.limitReached
    case 'work_assistant_memory_entry_not_active': return T.notActive
    case 'version_conflict': return T.versionConflict
    case 'same_key_payload_mismatch': return T.sameKeyMismatch
    case 'concurrent_change': return T.concurrent
    default:
      if (outcome.status === 401 || outcome.status === 403) return T.forbidden
      if (outcome.status === 404) return T.notFound
      return T.validation
  }
}

/** WP-C6 "Asistan hafızam": the actor's own memory only; the server accepts no owner override. */
export function AssistantMemoryPage() {
  const shell = useShellState()
  const context = contextKey(shell)
  return <AssistantMemoryBody key={`${context === null ? 'no-context' : contextKeyString(context)}:${shell.routeEntryGeneration}`} />
}

function AssistantMemoryBody() {
  const shell = useShellState()
  const context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const enabled = namespace !== null && companyId !== null && actorId !== null && !capabilityFailClosed(shell)
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const [includeInactive, setIncludeInactive] = useState(false)
  const [kind, setKind] = useState<AssistantMemoryKind>('preference')
  const [body, setBody] = useState('')
  const [editing, setEditing] = useState<{ readonly id: string; readonly kind: AssistantMemoryKind; readonly body: string } | null>(null)
  const [removing, setRemoving] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const base = [namespace, shell.routeEntryGeneration, 'work-assistant-memory'] as const

  const read = <T,>(key: readonly unknown[], load: (signal: AbortSignal) => Promise<T & { type: string; status?: number }>) => ({
    queryKey: [...base, ...key],
    enabled,
    queryFn: async ({ signal }: { signal: AbortSignal }) => {
      const captured = boundary.capture()
      const answer = await load(signal)
      if (answer.type === 'http' && typeof answer.status === 'number') boundary.report(answer.status, captured)
      return answer
    },
  })
  const status = useQuery(read(['status'], (signal) => fetchAssistantMemoryStatus(companyId!, actorId!, signal)))
  const entries = useQuery(read(['entries', includeInactive], (signal) => fetchMyAssistantMemory(companyId!, actorId!, includeInactive, signal)))
  if (!enabled) return <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />

  const statusData = status.data?.type === 'ok-single' ? status.data.data : null
  const page = entries.data?.type === 'ok-single' ? entries.data.data : null
  const refresh = () => void queryClient.invalidateQueries({ queryKey: base })

  const run = async <T,>(action: string, target: string, command: (key: string) => Promise<WorkCommandOutcome<T>>, done: string, after: () => void) => {
    if (busy) return
    const scope = commandScope(action, shell.sessionEpoch, actorId, companyId, target)
    setBusy(true)
    setMessage(null)
    const outcome = await command(pendingKey(scope))
    setBusy(false)
    if (outcome.type === 'unknown') { setMessage(T.unknownOutcome); return }
    settleKey(scope)
    if (outcome.type === 'applied') {
      setMessage(outcome.replayed ? T.replayed : done)
      after()
    } else {
      setMessage(assistantMemoryRefusal(outcome))
    }
    refresh()
  }

  const create = () => {
    if (!validBody(body)) { setMessage(T.validation); return }
    void run('assistant-memory-create', payloadFingerprint({ kind, body }),
      (key) => createAssistantMemoryEntry(companyId, actorId, key, kind, body), T.saved, () => setBody(''))
  }
  const revise = (entry: AssistantMemoryEntry) => {
    if (editing === null || !validBody(editing.body)) { setMessage(T.validation); return }
    const next = editing
    void run('assistant-memory-revise', `${entry.id}:${entry.version}:${payloadFingerprint({ kind: next.kind, body: next.body })}`,
      (key) => reviseAssistantMemoryEntry(companyId, actorId, key, entry.id, entry.version, next.kind, next.body), T.revised, () => setEditing(null))
  }
  const remove = (entry: AssistantMemoryEntry) => void run('assistant-memory-delete', `${entry.id}:${entry.version}`,
    (key) => deleteAssistantMemoryEntry(companyId, key, entry.id, entry.version), T.removed, () => setRemoving(null))

  return <section className={styles.page} aria-labelledby="work-assistant-memory-heading">
    <h1 id="work-assistant-memory-heading">{T.heading}</h1>
    <p>{T.intro}</p>
    <p>{T.dataOnly}</p>
    <p>{T.retentionNote}</p>
    <section aria-labelledby="work-assistant-memory-notice">
      <h2 id="work-assistant-memory-notice">{T.noticeHeading}</h2>
      <p>{T.noticePlaceholder}</p>
      {statusData?.informationNoticeVersion ? <p>{T.noticeVersion}: {statusData.informationNoticeVersion}</p> : null}
    </section>
    {statusData === null
      ? <StateBlock state={status.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(status.data) as RemoteViewState<unknown>} subject={T.subject} onRetry={() => void status.refetch()} />
      : statusData.writable ? <p role="note">{T.writableYes}</p>
        : <div role="note"><p>{T.writableNo}</p><ul>{statusData.disabledReasons.map((item) => <li key={item}>{REASON[item]}</li>)}</ul></div>}
    {statusData?.maxEntriesPerUser == null ? null
      : <p data-testid="work-assistant-memory-usage">{T.usage}: {statusData.activeEntryCount} / {statusData.maxEntriesPerUser}</p>}

    {statusData?.writable ? <form aria-labelledby="work-assistant-memory-new" onSubmit={(event) => { event.preventDefault(); create() }}>
      <h2 id="work-assistant-memory-new">{T.newHeading}</h2>
      <label>{T.kindLabel}{' '}
        <select value={kind} onChange={(event) => setKind(event.target.value as AssistantMemoryKind)}>
          {ASSISTANT_MEMORY_KINDS.map((item) => <option key={item} value={item}>{KIND[item]}</option>)}
        </select>
      </label>
      <label>{T.bodyLabel}{' '}
        <input type="text" value={body} maxLength={ASSISTANT_MEMORY_LIMITS.body} aria-describedby="work-assistant-memory-hint" onChange={(event) => setBody(event.target.value)} />
      </label>
      <small id="work-assistant-memory-hint">{T.bodyHint}</small>
      <Button type="submit" size="sm" variant="primary" disabled={busy}>{busy ? T.saving : T.save}</Button>
    </form> : null}

    <label><input type="checkbox" checked={includeInactive} onChange={(event) => setIncludeInactive(event.target.checked)} />{T.includeInactive}</label>
    {page === null
      ? <StateBlock state={entries.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(entries.data) as RemoteViewState<unknown>} subject={T.subject} onRetry={() => void entries.refetch()} />
      : page.items.length === 0 ? <StateBlock state={{ kind: includeInactive ? 'empty-no-data' : 'empty-filtered' }} subject={T.subject} />
        : <div className={styles.tableWrap} role="region" aria-label={T.heading} tabIndex={0}><table className={styles.table}><caption>{T.heading}</caption>
          <thead><tr><th scope="col">{T.colKind}</th><th scope="col">{T.colBody}</th><th scope="col">{T.colState}</th><th scope="col">{T.colExpires}</th><th scope="col">{T.colActions}</th></tr></thead>
          <tbody>{page.items.map((entry) => <tr key={entry.id} data-testid={`work-assistant-memory-${entry.id}`}>
            <td>{editing?.id === entry.id
              ? <select aria-label={T.kindLabel} value={editing.kind} onChange={(event) => setEditing({ ...editing, kind: event.target.value as AssistantMemoryKind })}>
                {ASSISTANT_MEMORY_KINDS.map((item) => <option key={item} value={item}>{KIND[item]}</option>)}</select>
              : KIND[entry.kind]}</td>
            <td>{editing?.id === entry.id
              ? <input type="text" aria-label={T.bodyLabel} value={editing.body} maxLength={ASSISTANT_MEMORY_LIMITS.body} onChange={(event) => setEditing({ ...editing, body: event.target.value })} />
              : entry.body}</td>
            <td>{STATE[entry.state]}</td>
            <td><time dateTime={entry.expiresAtUtc}>{new Date(entry.expiresAtUtc).toLocaleString('tr-TR')}</time></td>
            <td>{entry.state === 'closed' ? null : editing?.id === entry.id
              ? <><Button type="button" size="sm" variant="primary" disabled={busy} onClick={() => revise(entry)}>{T.saveEdit}</Button>{' '}
                <Button type="button" size="sm" disabled={busy} onClick={() => setEditing(null)}>{T.cancel}</Button></>
              : removing === entry.id
                ? <><Button type="button" size="sm" variant="primary" disabled={busy} onClick={() => remove(entry)}>{T.confirmRemove}</Button>{' '}
                  <Button type="button" size="sm" disabled={busy} onClick={() => setRemoving(null)}>{T.cancel}</Button></>
                : <>{statusData?.writable ? <><Button type="button" size="sm" disabled={busy} onClick={() => { setRemoving(null); setEditing({ id: entry.id, kind: entry.kind, body: entry.body }) }}>{T.edit}</Button>{' '}</> : null}
                  <Button type="button" size="sm" disabled={busy} onClick={() => { setEditing(null); setRemoving(entry.id) }}>{T.remove}</Button></>}</td>
          </tr>)}</tbody></table></div>}
    {page?.hasMore ? <p>{T.more}</p> : null}
    {message === null ? null : <p role="status">{message}</p>}

  </section>
}
