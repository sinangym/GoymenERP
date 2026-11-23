import { useCallback, useMemo, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { addPartyIban, deactivatePartyIban, fetchPartyIbans, type PartyIbanReply } from './partyIbanApi'
import { groupIban, isTurkishIban, normalizeIban, type PartyIbanView } from './partyIban'
import { PARTY_IBAN_TEXT as T } from './partyIbanText'

interface Props {
  readonly partyId: string
  readonly companyId: string
  readonly namespace: string
}

/** One pending command per panel: an unknown outcome keeps the same key and body for the retry (never a new key). */
type Pending = { readonly key: string; readonly send: (key: string) => Promise<PartyIbanReply>; readonly success: string }

/**
 * FNS-D121: the cari card's IBAN list in the selected company — read, add, deactivate (no edit, no delete). The server
 * decides who may read or manage; a 403 is shown as the panel's own forbidden state and nothing is assumed.
 */
export function CariPartyIbanPanel({ partyId, companyId, namespace }: Props) {
  const boundary = useAuthBoundary()
  const client = useQueryClient()
  const queryKey = useMemo(() => [namespace, 'cari.party-ibans', partyId, companyId], [namespace, partyId, companyId])
  const read = useQuery({
    queryKey,
    queryFn: async ({ signal }) => {
      const fence = boundary.capture()
      const answer = await fetchPartyIbans(partyId, companyId, signal)
      if (answer.type === 'http' && answer.status === 401) boundary.report(401, fence)
      return answer
    },
  })
  const [iban, setIban] = useState('')
  const [bank, setBank] = useState('')
  const [description, setDescription] = useState('')
  const [closing, setClosing] = useState<PartyIbanView | null>(null)
  const [reason, setReason] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [unknown, setUnknown] = useState(false)
  const busy = useRef(false)

  const run = useCallback(async (value: Pending) => {
    if (busy.current) return
    busy.current = true
    const fence = boundary.capture()
    const reply = await value.send(value.key)
    busy.current = false
    if (reply.kind === 'refused' && (reply.status === 401 || reply.status === 403)) boundary.report(reply.status, fence)
    if (reply.kind === 'unknown') { setUnknown(true); setMessage(T.unknown); return }
    setPending(null); setUnknown(false)
    if (reply.kind === 'answered' && reply.result.outcome === 'Succeeded') {
      setMessage(value.success); setIban(''); setBank(''); setDescription(''); setClosing(null); setReason('')
    } else if (reply.kind === 'answered' && reply.result.outcome === 'VersionConflict') {
      setMessage(reply.result.code === null ? T.conflict : T.codes[reply.result.code] ?? T.conflict)
    } else if (reply.kind === 'answered' && reply.result.code !== null) {
      setMessage(T.codes[reply.result.code] ?? T.refused)
    } else {
      setMessage(T.refused)
    }
    void client.invalidateQueries({ queryKey })
  }, [boundary, client, queryKey])

  const start = (send: (key: string) => Promise<PartyIbanReply>, success: string) => {
    if (pending !== null) return
    const value = { key: crypto.randomUUID(), send, success }
    setPending(value)
    void run(value)
  }

  const normalized = normalizeIban(iban)
  const submitAdd = () => {
    if (!isTurkishIban(normalized)) { setMessage(T.invalidIban); return }
    if (bank.trim() === '') { setMessage(T.bankRequired); return }
    const request = { ibanId: crypto.randomUUID(), iban: normalized, bankName: bank.trim(),
      description: description.trim() === '' ? null : description.trim() }
    start((key) => addPartyIban(partyId, companyId, key, request), T.added)
  }
  const submitDeactivate = () => {
    if (closing === null) return
    if (reason.trim() === '') { setMessage(T.reasonRequired); return }
    const request = { expectedVersion: closing.version, reason: reason.trim() }
    const target = closing.id
    start((key) => deactivatePartyIban(partyId, companyId, target, key, request), T.deactivated)
  }

  const data = read.data
  let body: React.ReactNode
  if (read.isFetching && data === undefined) body = <StateBlock subject={T.heading} state={{ kind: 'initial-loading' }} />
  else if (data === undefined) body = null
  else if (data.type === 'http' && data.status === 403) body = <p role="status">{T.forbidden}</p>
  else if (data.type === 'http' && data.status === 404) body = <StateBlock subject={T.heading} state={{ kind: 'not-found' }} />
  else if (data.type === 'http' && data.status === 401) body = <StateBlock subject={T.heading} state={{ kind: 'unauthenticated' }} />
  else if (data.type === 'http') body = <StateBlock subject={T.heading} state={{ kind: 'service-unavailable' }}
    onRetry={() => void read.refetch()} />
  else if (data.type === 'malformed') body = <StateBlock subject={T.heading} state={{ kind: 'malformed-response' }}
    onRetry={() => void read.refetch()} />
  else if (data.type === 'offline') body = <StateBlock subject={T.heading} state={{ kind: 'offline' }} onRetry={() => void read.refetch()} />
  else if (data.type === 'network-error') body = <StateBlock subject={T.heading} state={{ kind: 'network-error' }}
    onRetry={() => void read.refetch()} />
  else if (data.type === 'aborted') body = null
  else body = <>
    {data.data.ibans.length === 0 ? <p>{T.empty}</p> : <table>
      <caption>{T.heading}</caption>
      <thead><tr><th scope="col">{T.iban}</th><th scope="col">{T.bank}</th><th scope="col">{T.description}</th>
        <th scope="col">{T.state}</th><th scope="col">{T.addedBy}</th><th scope="col"><span className="visually-hidden">{T.deactivate}</span></th></tr></thead>
      <tbody>{data.data.ibans.map((row) => <tr key={row.id} data-iban-state={row.isActive ? 'active' : 'passive'}>
        <td>{groupIban(row.iban)}</td><td>{row.bankName}</td><td>{row.description ?? '—'}</td>
        <td>{row.isActive ? T.active : `${T.passive} — ${row.deactivationReason ?? ''}`}</td><td>{row.addedBy}</td>
        <td>{row.isActive ? <button type="button" disabled={pending !== null} onClick={() => { setClosing(row); setReason(''); setMessage(null) }}>
          {T.deactivate}</button> : null}</td>
      </tr>)}</tbody>
    </table>}
    {closing === null ? null : <div>
      <label>{T.reason}<textarea value={reason} maxLength={1000} disabled={pending !== null}
        onChange={(event) => setReason(event.target.value)} /></label>
      <button type="button" disabled={pending !== null} onClick={submitDeactivate}>{T.confirmDeactivate}</button>
      <button type="button" disabled={pending !== null} onClick={() => setClosing(null)}>{T.cancel}</button>
    </div>}
    <fieldset disabled={pending !== null}>
      <legend>{T.add}</legend>
      <label>{T.iban}<input value={iban} maxLength={40} onChange={(event) => setIban(event.target.value)} /></label>
      <label>{T.bank}<input value={bank} maxLength={200} onChange={(event) => setBank(event.target.value)} /></label>
      <label>{T.description}<input value={description} maxLength={500} onChange={(event) => setDescription(event.target.value)} /></label>
      <button type="button" onClick={submitAdd}>{T.add}</button>
    </fieldset>
  </>

  return <section aria-labelledby={`cari-iban-${partyId}`} data-section="party-ibans">
    <h3 id={`cari-iban-${partyId}`}>{T.heading}</h3>
    <p>{T.explanation}</p>
    {body}
    {unknown && pending !== null ? <button type="button" onClick={() => void run(pending)}>{T.retry}</button> : null}
    {message === null ? null : <p role="status">{message}</p>}
  </section>
}
