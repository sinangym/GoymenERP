import { useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { SETTINGS_CAPABILITY } from './capabilities'
import { EffectiveFromField, Frame, MetaCells, MetaHeaders, effectiveFromBody, isEffectiveFromValid, stateOf, useBoundaryRead } from './SalesPolicyParts'
import { fetchMoneyProfiles, recordMoneyProfile } from './moneyProfileApi'
import { type MoneyProfileSeries, type MoneyProfileView, type RecordMoneyProfileBody } from './moneyProfileContract'
import { MONEY_PROFILE_TEXT as T, moneyOperationLabel } from './moneyProfileText'
import { useCommandAttempt } from './useCommandAttempt'
import styles from './settings.module.css'

export function MoneyProfilePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <Screen key={`${namespace}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function Screen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const company = shell.activeContext?.companyId ?? null
  const single = shell.activeContext?.scopeMode === 'single' && company !== null
  const ready = namespace !== null && single && hasCapability(shell, SETTINGS_CAPABILITY.moneyProfiles)
  const query = useBoundaryRead([namespace, shell.routeEntryGeneration, 'money-profiles'], ready, (signal) => fetchMoneyProfiles(company ?? '', signal))
  const state = stateOf<MoneyProfileView>(query.isPending, query.data)
  if (!single) return <Frame title={T.title} intro={T.intro}><StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} /></Frame>
  if (!ready) return <Frame title={T.title} intro={T.intro}><StateBlock state={{ kind: 'not-found' }} subject={T.subject} /></Frame>
  return <Frame title={T.title} intro={T.intro}>{state.kind === 'ready'
    ? <Profiles data={state.data} company={company!} changed={() => void query.refetch()} />
    : <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />}</Frame>
}

function Profiles({ data, company, changed }: { readonly data: MoneyProfileView; readonly company: string; readonly changed: () => void }) {
  const [editing, setEditing] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  return <section><div role="status">{notice}</div>{data.profiles.length === 0 ? <p>{T.empty}</p> : null}
    {data.profiles.map((profile) => {
      const key = `${profile.currencyId}:${profile.operation}`
      return <section key={key} className={styles.section}><h2>{profile.currencyCode} · {moneyOperationLabel(profile.operation)}</h2>
        {profile.versions.length === 0 && <p>{T.unconfigured}</p>}
        <table className={styles.table}><caption>{T.title} · {profile.currencyCode} · {moneyOperationLabel(profile.operation)}</caption><thead><tr>
          <th scope="col">{T.version}</th><th scope="col">{T.scale}</th><th scope="col">{T.priceScale}</th><th scope="col">{T.active}</th><MetaHeaders />
        </tr></thead><tbody>{profile.versions.map((entry) => <tr key={entry.version}>
          <td>{entry.version}</td><td>{entry.scale}</td><td>{entry.unitPriceScale ?? T.undefined}</td><td>{entry.isActive ? T.yes : T.no}</td><MetaCells entry={entry} />
        </tr>)}</tbody></table>
        {editing === key ? <Editor profile={profile} today={data.today} company={company} finish={(text) => { setEditing(null); setNotice(text); if (text !== null) changed() }} />
          : <Button type="button" disabled={editing !== null} onClick={() => setEditing(key)}>{profile.version === '0' ? T.create : T.edit}</Button>}
      </section>
    })}</section>
}

function Editor({ profile, today, company, finish }: { readonly profile: MoneyProfileSeries; readonly today: string; readonly company: string; readonly finish: (text: string | null) => void }) {
  const current = profile.versions.find((entry) => entry.state === 'InForce') ?? null
  const [scaleText, setScale] = useState(current === null ? '' : String(current.scale))
  const [priceText, setPrice] = useState(current?.unitPriceScale === null || current === null ? '' : String(current.unitPriceScale))
  const [active, setActive] = useState(current?.isActive ?? false)
  const [from, setFrom] = useState(today)
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const boundary = useAuthBoundary()
  const [frozen, setFrozen] = useState<RecordMoneyProfileBody | null>(null)
  const parse = (value: string) => /^(0|[1-9]\d?)$/.test(value) && Number(value) <= 28 ? Number(value) : null
  const scale = parse(scaleText)
  const price = priceText === '' ? null : parse(priceText)
  const valid = scale !== null && (priceText === '' || price !== null) && isEffectiveFromValid(from, today) && reason.trim().length > 0
  const pending = attempt.status.kind === 'busy'
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (pending || !valid) return
    const payload = frozen ?? { expectedVersion: profile.version, currencyId: profile.currencyId, operation: profile.operation,
      scale: scale!, unitPriceScale: price, isActive: active, reason: reason.trim(), effectiveFrom: effectiveFromBody(from, today) }
    const snapshot = boundary.capture()
    const result = await attempt.run(payload, (idempotencyKey) => recordMoneyProfile({ companyId: company, idempotencyKey }, payload))
    if (result.type === 'unknown') { setFrozen(payload); return }
    setFrozen(null)
    if (result.type === 'applied') { finish(result.replayed ? T.replay : T.saved); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, snapshot)
    if (result.status === 409 && result.code === 'settings_money_profile_version_conflict') finish(T.conflict)
  }
  return <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={pending}>
    <Field label={T.scale}>{(control) => <input {...control} inputMode="numeric" value={scaleText} disabled={pending || frozen !== null} onChange={(event) => setScale(event.target.value)} />}</Field>
    <Field label={T.priceScale}>{(control) => <input {...control} inputMode="numeric" value={priceText} disabled={pending || frozen !== null} onChange={(event) => setPrice(event.target.value)} />}</Field>
    <Field label={T.active}>{(control) => <input {...control} type="checkbox" checked={active} disabled={pending || frozen !== null} onChange={(event) => setActive(event.target.checked)} />}</Field>
    <EffectiveFromField value={from} today={today} onChange={(value) => { if (frozen === null) setFrom(value) }} />
    <section aria-label={T.preview} className={styles.notice}><h3>{T.preview}</h3>
      <p>{T.companyScope} {profile.currencyCode} · {moneyOperationLabel(profile.operation)}</p>
      <p>{T.previous}: {current?.scale ?? T.undefined} / {current?.unitPriceScale ?? T.undefined} / {current?.isActive ? T.yes : T.no}</p>
      <p>{T.next}: {scale ?? T.undefined} / {price ?? T.undefined} / {active ? T.yes : T.no}</p>
      <p>{T.previewHistory}</p>{!valid && <p>{T.previewIncomplete}</p>}
    </section>
    <Field label={T.reason}>{(control) => <textarea {...control} required maxLength={4000} value={reason} disabled={pending || frozen !== null} onChange={(event) => setReason(event.target.value)} />}</Field>
    {attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}
    {attempt.status.kind === 'refused' && <p role="alert">{T.refused}</p>}
    <Button type="submit" variant="primary" disabled={pending || !valid}>{pending ? T.pending : frozen ? T.retry : T.save}</Button>
    <Button type="button" disabled={pending || frozen !== null} onClick={() => finish(null)}>{T.cancel}</Button>
  </form>
}
