import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { capabilityFailClosed, contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { Button } from '../ui/Button'
import { addRecordNote, commandScope, fetchRecordNotes, passivateNote, pendingKey, searchWorkPeople, settleKey } from './collaborationApi'
import { payloadFingerprint, refusalMessage } from './collaborationOutcome'
import { WORK_LIMITS, type WorkNote, type WorkPerson, type WorkRecordType } from './collaborationContract'
import { COLLABORATION_TEXT as T } from './collaborationText'
import styles from './Work.module.css'

/**
 * W1-C: a record's notes, attachable to any owner screen. The server decides everything — whether the record is readable,
 * whether the writer may write and whether every mentioned person can read the record; the panel only shows the answer.
 */
export function RecordNotesPanel({ recordType, recordId }: { readonly recordType: WorkRecordType; readonly recordId: string }) {
  const shell = useShellState()
  const context = contextKey(shell)
  return <RecordNotesBody key={`${context === null ? 'no-context' : contextKeyString(context)}:${shell.routeEntryGeneration}:${recordType}:${recordId}`}
    recordType={recordType} recordId={recordId} />
}

function RecordNotesBody({ recordType, recordId }: { readonly recordType: WorkRecordType; readonly recordId: string }) {
  const shell = useShellState()
  const context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const [includePassive, setIncludePassive] = useState(false)
  const [body, setBody] = useState('')
  const [mentions, setMentions] = useState<readonly WorkPerson[]>([])
  const [correcting, setCorrecting] = useState<WorkNote | null>(null)
  const [peopleQuery, setPeopleQuery] = useState('')
  const [people, setPeople] = useState<readonly WorkPerson[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [passivating, setPassivating] = useState<{ readonly noteId: string; readonly reason: string } | null>(null)
  const queryKey = [namespace, shell.routeEntryGeneration, 'work-record-notes', recordType, recordId, includePassive]
  const query = useQuery({
    queryKey,
    enabled: namespace !== null && companyId !== null && !capabilityFailClosed(shell),
    queryFn: async ({ signal }) => {
      const captured = boundary.capture()
      const answer = await fetchRecordNotes(companyId!, recordType, recordId, includePassive, signal)
      if (answer.type === 'http') boundary.report(answer.status, captured)
      return answer
    },
  })
  if (companyId === null || actorId === null || capabilityFailClosed(shell)) return null
  const page = query.data?.type === 'ok-single' ? query.data.data : null

  const submit = async () => {
    if (busy || body.trim().length === 0 || body.length > WORK_LIMITS.noteBody) return
    const payload = { body, mentionedUserIds: mentions.map((person) => person.userId), correctsNoteId: correcting?.id ?? null }
    const scope = commandScope('note-add', shell.sessionEpoch, actorId, companyId, `${recordType}:${recordId}:${payloadFingerprint(payload)}`)
    setBusy(true)
    setMessage(null)
    const outcome = await addRecordNote(companyId, pendingKey(scope), recordType, recordId, payload)
    setBusy(false)
    if (outcome.type === 'unknown') { setMessage(T.unknownOutcome); return }
    settleKey(scope)
    if (outcome.type === 'refused') { setMessage(refusalMessage(outcome)); return }
    setBody(''); setMentions([]); setCorrecting(null); setPeople(null); setPeopleQuery('')
    setMessage(outcome.replayed ? T.noteReplayed : T.noteSaved)
    void queryClient.invalidateQueries({ queryKey: [namespace, shell.routeEntryGeneration, 'work-record-notes', recordType, recordId] })
  }

  const confirmPassivate = async () => {
    if (passivating === null || busy || passivating.reason.trim().length === 0) return
    const scope = commandScope('note-passivate', shell.sessionEpoch, actorId, companyId, `${passivating.noteId}:${payloadFingerprint(passivating.reason)}`)
    setBusy(true)
    setMessage(null)
    const outcome = await passivateNote(companyId, pendingKey(scope), passivating.noteId, passivating.reason)
    setBusy(false)
    if (outcome.type === 'unknown') { setMessage(T.unknownOutcome); return }
    settleKey(scope)
    if (outcome.type === 'refused') { setMessage(refusalMessage(outcome)); return }
    setPassivating(null)
    setMessage(T.notePassivated)
    void queryClient.invalidateQueries({ queryKey: [namespace, shell.routeEntryGeneration, 'work-record-notes', recordType, recordId] })
  }

  const findPeople = async () => {
    const answer = await searchWorkPeople(companyId, peopleQuery)
    setPeople(answer.type === 'ok-single' ? answer.data.people.filter((person) => person.userId !== actorId) : [])
  }

  return <section className={styles.section} aria-labelledby={`work-notes-${recordId}`} data-testid="work-record-notes">
    <h2 id={`work-notes-${recordId}`}>{T.notesHeading}</h2>
    <label><input type="checkbox" checked={includePassive} onChange={(event) => setIncludePassive(event.target.checked)} />{T.includePassive}</label>
    {page === null
      ? <StateBlock state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data) as RemoteViewState<unknown>} subject={T.notesSubject} onRetry={() => void query.refetch()} />
      : page.items.length === 0 ? <p>{T.notesEmpty}</p>
        : <ul className={styles.noteList}>{page.items.map((note) => <li key={note.id} data-testid={`work-note-${note.id}`}>
          <p className={styles.noteMeta}>{T.author}: {note.authorDisplayName} · <time dateTime={note.createdAtUtc}>{new Date(note.createdAtUtc).toLocaleString('tr-TR')}</time>
            {note.correctsNoteId === null ? null : <> · {T.correctionOf}</>}{note.passivatedAtUtc === null ? null : <> · <strong>{T.passive}</strong> ({note.passivationReason})</>}</p>
          <p className={styles.noteBody}>{note.body}</p>
          {note.passivatedAtUtc === null && note.authorUserId === actorId ? <div>
            <Button type="button" size="sm" onClick={() => { setCorrecting(note); setBody(note.body) }}>{T.correct}</Button>{' '}
            <Button type="button" size="sm" variant="ghost" onClick={() => setPassivating({ noteId: note.id, reason: '' })}>{T.passivate}</Button>
          </div> : null}
          {passivating?.noteId === note.id ? <div>
            <label>{T.passivateReason}<input value={passivating.reason} maxLength={WORK_LIMITS.reason}
              onChange={(event) => setPassivating({ noteId: note.id, reason: event.target.value })} /></label>
            <Button type="button" size="sm" variant="danger" disabled={busy || passivating.reason.trim().length === 0} onClick={() => void confirmPassivate()}>{T.confirmPassivate}</Button>{' '}
            <Button type="button" size="sm" onClick={() => setPassivating(null)}>{T.cancel}</Button>
          </div> : null}
        </li>)}</ul>}
    {page?.hasMore ? <p>{T.moreNotes}</p> : null}
    <form onSubmit={(event) => { event.preventDefault(); void submit() }}>
      {correcting === null ? null : <p>{T.correctingHint} <Button type="button" size="sm" onClick={() => setCorrecting(null)}>{T.cancel}</Button></p>}
      <label htmlFor={`work-note-body-${recordId}`}>{T.noteLabel}</label>
      <textarea id={`work-note-body-${recordId}`} value={body} maxLength={WORK_LIMITS.noteBody} placeholder={T.notePlaceholder}
        onChange={(event) => setBody(event.target.value)} />
      <fieldset>
        <legend>{T.mentionHeading}</legend>
        <label>{T.mentionSearch}<input value={peopleQuery} maxLength={WORK_LIMITS.peopleQuery} onChange={(event) => setPeopleQuery(event.target.value)} /></label>
        <Button type="button" size="sm" onClick={() => void findPeople()}>{T.mentionSearch}</Button>
        {people === null ? null : people.length === 0 ? <p>{T.peopleEmpty}</p> : <ul>{people.map((person) => <li key={person.userId}>{person.displayName}{' '}
          <Button type="button" size="sm" disabled={mentions.length >= WORK_LIMITS.mentions || mentions.some((item) => item.userId === person.userId)}
            onClick={() => setMentions([...mentions, person])}>{T.mentionAdd}</Button></li>)}</ul>}
        {mentions.length === 0 ? null : <p>{T.mentioned}: {mentions.map((person) => <span key={person.userId}>@{person.displayName}{' '}
          <Button type="button" size="sm" variant="ghost" aria-label={`${T.mentionRemove} ${person.displayName}`} onClick={() => setMentions(mentions.filter((item) => item.userId !== person.userId))}>{T.mentionRemove}</Button> </span>)}</p>}
      </fieldset>
      <Button type="submit" variant="primary" disabled={busy || body.trim().length === 0}>{busy ? T.saving : T.addNote}</Button>
    </form>
    {message === null ? null : <p role="status">{message}</p>}
  </section>
}
