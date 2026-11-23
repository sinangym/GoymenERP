import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { isDateOnly } from '../finance/dateOnly'
import { POSTING_MAPPING_TEXT as T } from './postingMappingText'
import { fetchPostingMappingWorkspace, sendPostingMapping, type PostingMapping, type PostingMappingAttempt,
  type PostingMappingOutcome, type PostingMappingWorkspace } from './postingMappingApi'
import styles from './Accounting.module.css'

export function CogsMappingPage({ canPrepare = false, canActivate = false }: { readonly canPrepare?: boolean; readonly canActivate?: boolean }) {
  const shell = useShellState(); const context = contextKey(shell); const namespace = context === null ? null : contextKeyString(context)
  return <Page key={`${namespace}:${shell.routeEntryGeneration}:${canPrepare}:${canActivate}`} namespace={namespace}
    canPrepare={canPrepare} canActivate={canActivate} />
}
function Page({ namespace, canPrepare, canActivate }: { readonly namespace: string | null; readonly canPrepare: boolean; readonly canActivate: boolean }) {
  const shell = useShellState(); const boundary = useAuthBoundary()
  const company = shell.activeContext?.companyId ?? ''; const actor = shell.activeContext?.userId ?? ''
  const single = namespace !== null && company !== '' && shell.activeContext?.scopeMode === 'single'
  const [attempt, setAttempt] = useState<PostingMappingAttempt | null>(null); const [outcome, setOutcome] = useState<PostingMappingOutcome | null>(null)
  const [sending, setSending] = useState(false); const inFlight = useRef(false); const mounted = useRef(true)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'accounting-posting-mapping-workspace'], enabled: single,
    queryFn: async ({ signal }) => { const security = boundary.capture(); const answer = await fetchPostingMappingWorkspace(company, signal)
      if (answer.type === 'http' && [401, 403].includes(answer.status)) boundary.report(answer.status as 401 | 403, security)
      return answer } })
  const frozen = sending || outcome?.type === 'unknown'
  const send = async (next: PostingMappingAttempt, retry = false) => {
    if (!single || inFlight.current || outcome?.type === 'unknown' && !retry || next.kind === 'prepare' && !canPrepare || next.kind === 'activate' && !canActivate) return
    inFlight.current = true; setSending(true); setAttempt(next); setOutcome(null)
    try { const security = boundary.capture(); const answer = await sendPostingMapping(company, next, actor)
      if (answer.type === 'refused' && [401, 403].includes(answer.status)) boundary.report(answer.status as 401 | 403, security)
      if (mounted.current) { setOutcome(answer)
        if (answer.type === 'applied' || answer.type === 'refused' && answer.status === 409) void query.refetch() }
    } finally { inFlight.current = false; if (mounted.current) setSending(false) }
  }
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<PostingMappingWorkspace>
  return <section className={styles.page} aria-labelledby="cogs-mapping-title"><h1 id="cogs-mapping-title">{T.title}</h1><p>{T.intro}</p>
    {!single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
      : state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
        : <><button type="button" disabled={frozen} onClick={() => void query.refetch()}>{T.refresh}</button>
          {state.data.accountPlanState === 'Unavailable' ? <p role="alert">{T.unavailable}</p> : null}
          <h2>{T.active}</h2>{state.data.active === null ? <p>{T.noActive}</p> : <Mapping record={state.data.active} />}
          {canPrepare ? <Prepare key={`${state.data.latestMappingVersion}:${state.data.accountPlanSourceSha256}`} workspace={state.data}
            disabled={frozen} send={send} /> : null}
          <h2>{T.prepared}</h2>{state.data.prepared.length === 0 ? <p>{T.noPrepared}</p>
            : state.data.prepared.map(record => <article key={record.mappingVersion}><Mapping record={record} />
              {canActivate ? record.preparedByUserId.toLowerCase() === actor.toLowerCase() ? <p>{T.self}</p>
                : <button type="button" disabled={frozen || state.data.accountPlanState !== 'Available'} onClick={() => void send({ kind: 'activate',
                  key: crypto.randomUUID(), mappingVersion: record.mappingVersion, request: { expectedVersion: record.version,
                    expectedActiveMappingVersion: state.data.active?.mappingVersion ?? 0 } })}>{T.activate}</button> : null}
            </article>)}</>}
    {outcome?.type === 'unknown' ? <div role="alert"><p>{T.unknown}</p><button type="button" disabled={sending || attempt === null}
      onClick={() => attempt !== null && void send(attempt, true)}>{T.retry}</button></div> : null}
    {outcome?.type === 'refused' ? <p role="alert">{T.refused} ({outcome.status})</p> : null}
    {outcome?.type === 'applied' ? <p role="status">{T.done}</p> : null}
  </section>
}
function Mapping({ record }: { readonly record: PostingMapping }) {
  return <><p>{T.version}: {record.mappingVersion} · {T.date}: {record.effectiveFrom}</p>
    <div className={styles.tableWrap}><table aria-label={T.title} className={styles.table}><thead><tr><th scope="col">{T.purpose}</th><th scope="col">{T.key}</th><th scope="col">{T.account}</th></tr></thead>
      <tbody>{record.rows.map((row, index) => <tr key={index}><td>{T.purposes[row.purpose]}</td>
        <td>{T.keys[row.mappingKey]}</td><td>{row.accountCode}</td></tr>)}</tbody></table></div></>
}
function Prepare({ workspace, disabled, send }: { readonly workspace: PostingMappingWorkspace; readonly disabled: boolean
  readonly send: (attempt: PostingMappingAttempt) => Promise<void> }) {
  const current = (key: string) => workspace.active?.rows.find(row => row.purpose === 'cost-of-goods-sold' && row.mappingKey === key)?.accountId ?? ''
  const [trade, setTrade] = useState(current('trade-goods')); const [manufactured, setManufactured] = useState(current('manufactured'))
  const [date, setDate] = useState('')
  const accountExists = (id: string) => workspace.accounts.some(account => account.accountId === id)
  const applyDefaults = () => {
    const choice = (key: string) => workspace.cogsDefaults.find(row => row.mappingKey === key)?.accountId ?? ''
    if (trade === '') setTrade(choice('trade-goods'))
    if (manufactured === '') setManufactured(choice('manufactured'))
  }
  return <form onSubmit={event => { event.preventDefault()
    if (disabled || !isDateOnly(date) || !accountExists(trade) || !accountExists(manufactured)) return
    const retained = (workspace.active?.rows ?? []).filter(row => row.purpose !== 'cost-of-goods-sold')
      .map(({ accountCode: _code, ...row }) => row)
    const cogs = (mappingKey: string, accountId: string) => ({ purpose: 'cost-of-goods-sold', mappingKey, accountId,
      itemType: null, linePurpose: null, isExport: null, vatRateCode: null })
    void send({ kind: 'prepare', key: crypto.randomUUID(), request: { expectedLatestMappingVersion: workspace.latestMappingVersion,
      effectiveFrom: date, rows: [...retained, cogs('trade-goods', trade), cogs('manufactured', manufactured)] } })
  }}><fieldset disabled={disabled || workspace.accountPlanState !== 'Available'}><legend>{T.prepare}</legend>
    <p>{T.retained}</p><p>{T.missingDefault}</p><button type="button" onClick={applyDefaults}>{T.defaults}</button>
    <label>{T.trade}<select value={trade} onChange={event => setTrade(event.target.value)} required><option value="">{T.choose}</option>
      {workspace.accounts.map(account => <option key={account.accountId} value={account.accountId}>{account.code} · {account.name}</option>)}</select></label>
    <label>{T.manufactured}<select value={manufactured} onChange={event => setManufactured(event.target.value)} required><option value="">{T.choose}</option>
      {workspace.accounts.map(account => <option key={account.accountId} value={account.accountId}>{account.code} · {account.name}</option>)}</select></label>
    <label>{T.effective}<input type="date" value={date} onChange={event => setDate(event.target.value)} required /></label>
    <button type="submit" disabled={!isDateOnly(date) || !accountExists(trade) || !accountExists(manufactured)}>{T.prepare}</button>
  </fieldset></form>
}
