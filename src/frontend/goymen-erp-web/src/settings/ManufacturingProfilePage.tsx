import { useState, type FormEvent, type ReactNode } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { fetchWorkCenters } from '../organization/api'
import type { WorkCenterSummary } from '../organization/contract'
import { SETTINGS_CAPABILITY, SETTINGS_MANUFACTURING_PROFILE_CONTROL } from './capabilities'
import { Frame, stateOf, useBoundaryRead } from './SalesPolicyParts'
import { useCommandAttempt } from './useCommandAttempt'
import { fetchManufacturingProfiles, prepareManufacturingProfile, publishManufacturingProfile } from './manufacturingProfileApi'
import { MANUFACTURING_PROCESSES, type ManufacturingProfile, type ManufacturingProfileKind, type ManufacturingProfileList, type ManufacturingProfileReceipt, type ManufacturingProcess, type PrepareManufacturingProfileBody } from './manufacturingProfileContract'
import { MANUFACTURING_PROFILE_TEXT as T, manufacturingProcessLabel, manufacturingProfileStatus, parameterLabel } from './manufacturingProfileText'
import type { CommandContext, CommandOutcome } from './discountLimitApi'
import styles from './settings.module.css'

export function ManufacturingProfilePage({ kind }: { readonly kind: ManufacturingProfileKind }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <Screen key={`${kind}::${namespace}::${shell.routeEntryGeneration}`} kind={kind} namespace={namespace} />
}

function Screen({ kind, namespace }: { readonly kind: ManufacturingProfileKind; readonly namespace: string | null }) {
  const shell = useShellState()
  const company = shell.activeContext?.companyId ?? null
  const single = shell.activeContext?.scopeMode === 'single' && company !== null
  const capability = kind === 'parameters' ? SETTINGS_CAPABILITY.manufacturingParameters : SETTINGS_CAPABILITY.manufacturingTolerances
  const ready = namespace !== null && single && hasCapability(shell, capability)
  const controls = SETTINGS_MANUFACTURING_PROFILE_CONTROL[kind]
  const canPrepare = hasCapability(shell, controls.prepare)
  const query = useBoundaryRead([namespace, shell.routeEntryGeneration, kind, 'manufacturing-profiles'], ready, (signal) => fetchManufacturingProfiles(company ?? '', kind, signal))
  const centers = useBoundaryRead([namespace, shell.routeEntryGeneration, kind, 'manufacturing-work-centers'], ready && canPrepare, (signal) => fetchWorkCenters(company ?? '', signal))
  const state = stateOf<ManufacturingProfileList>(query.isPending, query.data)
  const centerState = stateOf<readonly WorkCenterSummary[]>(centers.isPending, centers.data)
  const title = kind === 'parameters' ? T.parametersTitle : T.tolerancesTitle
  if (!single) return <Frame title={title} intro={T.intro}><StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} /></Frame>
  if (!ready) return <Frame title={title} intro={T.intro}><StateBlock state={{ kind: 'not-found' }} subject={T.subject} /></Frame>
  return <Frame title={title} intro={T.intro}>{state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
    : <Profiles key={`${kind}:${state.data.businessDate}`} data={state.data} kind={kind} company={company!} canPrepare={canPrepare} canPublish={hasCapability(shell, controls.publish)}
      workCenters={centerState.kind === 'ready' ? centerState.data : null}
      workCenterState={canPrepare && centerState.kind !== 'ready' ? <StateBlock state={centerState} subject={T.workCenters} onRetry={() => void centers.refetch()} /> : null}
      changed={() => void query.refetch()} />}</Frame>
}

function Profiles({ data, kind, company, canPrepare, canPublish, workCenters, workCenterState, changed }: {
  readonly data: ManufacturingProfileList; readonly kind: ManufacturingProfileKind; readonly company: string; readonly canPrepare: boolean; readonly canPublish: boolean
  readonly workCenters: readonly WorkCenterSummary[] | null; readonly workCenterState: ReactNode; readonly changed: () => void
}) {
  const [editing, setEditing] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const finish = (message: string | null) => { setEditing(null); setNotice(message); if (message !== null) changed() }
  const all = [...data.startingValues, ...data.profiles]
  const centerName = (id: string | null) => id === null ? T.allLines : workCenters?.find((center) => center.id === id)?.name ?? id
  return <section><p role="status">{notice}</p><p>{T.catalogNote}</p>
    {all.length === 0 && <p>{T.empty}</p>}
    {all.map((profile) => <article key={profile.profileId} className={styles.section}>
      <h2>{manufacturingProcessLabel(profile.process)} · {T.version} {profile.profileVersion}</h2>
      <p>{T.status}: {manufacturingProfileStatus(profile.status)} · {T.scope}: {centerName(profile.workCenterId)}</p>
      <p>{T.window}: {profile.effectiveFrom} — {profile.effectiveThrough ?? T.noEnd}</p>
      {profile.source === 'Catalog' && <p>{T.catalog}</p>}
      {kind === 'parameters' ? <table className={styles.table}><caption>{T.values}</caption><thead><tr><th scope="col">{T.values}</th><th scope="col">{T.unit}</th></tr></thead>
        <tbody>{profile.values.map((value) => <tr key={value.key}><th scope="row">{parameterLabel(value.key)}</th><td>{value.value} {value.unit}</td></tr>)}</tbody></table>
        : <dl><dt>{T.fire}</dt><dd>{profile.maximumFirePercent}</dd><dt>{T.mass}</dt><dd>{profile.massBalanceDifferenceTolerancePercent ?? '—'}</dd></dl>}
      <details><summary>{T.history}</summary><p>{profile.preparedBy} {profile.preparedAtUtc} · {profile.preparationReason}</p>
        {profile.approvedBy !== null && <p>{profile.approvedBy} {profile.approvedAtUtc} · {profile.approvalReason}</p>}
        {profile.publishedBy !== null && <p>{profile.publishedBy} {profile.publishedAtUtc} · {profile.publicationReason}</p>}</details>
      {canPublish && profile.source === 'Settings' && (profile.status === 'Draft' || profile.status === 'Approved') && (editing === profile.profileId
        ? <PublishForm kind={kind} company={company} profile={profile} finish={finish} />
        : <Button type="button" disabled={editing !== null} onClick={() => setEditing(profile.profileId)}>{T.publish}</Button>)}
    </article>)}
    {canPrepare && <section className={styles.section}><h2>{T.prepare}</h2>{workCenterState}{workCenters !== null && (editing === 'prepare'
      ? <PrepareForm data={data} kind={kind} company={company} workCenters={workCenters} finish={finish} />
      : <Button type="button" disabled={editing !== null} onClick={() => setEditing('prepare')}>{T.prepare}</Button>)}</section>}
  </section>
}

type Finish = (message: string | null) => void
function Mutation<TBody extends { readonly reason: string }>({ company, body, valid, action, saved, finish, children }: {
  readonly company: string; readonly body: TBody; readonly valid: boolean; readonly action: (context: CommandContext, body: TBody) => Promise<CommandOutcome<ManufacturingProfileReceipt>>
  readonly saved: string; readonly finish: Finish; readonly children: (frozen: boolean) => ReactNode
}) {
  const attempt = useCommandAttempt<ManufacturingProfileReceipt>()
  const boundary = useAuthBoundary()
  const [frozen, setFrozen] = useState<TBody | null>(null)
  const busy = attempt.status.kind === 'busy'
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (busy || (!valid && frozen === null)) return
    const payload = frozen ?? body
    const snapshot = boundary.capture()
    const result = await attempt.run(payload, (idempotencyKey) => action({ companyId: company, idempotencyKey }, payload))
    if (result.type === 'unknown') { setFrozen(payload); return }
    setFrozen(null)
    if (result.type === 'applied') { finish(result.replayed ? T.replay : saved); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, snapshot)
    if (result.status === 404 || (result.status === 409 && result.code === 'settings_manufacturing_profile_version_conflict')) finish(T.conflict)
  }
  return <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={busy}>
    <fieldset disabled={busy || frozen !== null}>{children(busy || frozen !== null)}</fieldset>
    {attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}{attempt.status.kind === 'refused' && <p role="alert">{T.refused}</p>}
    <Button type="submit" variant="primary" disabled={busy || (!valid && frozen === null)}>{busy ? T.pending : frozen !== null ? T.retry : saved === T.prepared ? T.save : T.publish}</Button>
    <Button type="button" disabled={busy || frozen !== null} onClick={() => finish(null)}>{T.cancel}</Button>
  </form>
}

function PublishForm({ company, kind, profile, finish }: { readonly company: string; readonly kind: ManufacturingProfileKind; readonly profile: ManufacturingProfile; readonly finish: Finish }) {
  const [reason, setReason] = useState('')
  return <Mutation company={company} body={{ expectedVersion: profile.stateVersion, reason: reason.trim() }} valid={reason.trim().length > 0}
    action={(context, body) => publishManufacturingProfile(context, kind, profile.profileId, body)} saved={T.published} finish={finish}>
    {() => <><section aria-label={T.preview}><h3>{T.preview}</h3><p>{manufacturingProcessLabel(profile.process)} · {T.version} {profile.profileVersion}</p><p>{T.prospective}</p></section>
      <Field label={T.reason}>{(control) => <textarea {...control} required maxLength={500} value={reason} onChange={(event) => setReason(event.target.value)} />}</Field></>}
  </Mutation>
}

function PrepareForm({ data, kind, company, workCenters, finish }: { readonly data: ManufacturingProfileList; readonly kind: ManufacturingProfileKind; readonly company: string; readonly workCenters: readonly WorkCenterSummary[]; readonly finish: Finish }) {
  const [process, setProcess] = useState<ManufacturingProcess>('Slitting')
  const [center, setCenter] = useState('')
  const [from, setFrom] = useState(data.businessDate)
  const [through, setThrough] = useState('')
  const [reason, setReason] = useState('')
  const [values, setValues] = useState<Record<string, string>>({})
  const [fire, setFire] = useState('')
  const [mass, setMass] = useState('')
  const keys = data.keys.filter((key) => key.processes.includes(process))
  const inScope = data.profiles.filter((profile) => profile.source === 'Settings' && profile.process === process && (kind === 'tolerances' || profile.workCenterId === (center || null)))
  const expectedVersion = inScope.reduce((latest, profile) => BigInt(profile.profileVersion) > BigInt(latest) ? profile.profileVersion : latest, '0')
  const payload: PrepareManufacturingProfileBody = { expectedVersion, process, workCenterId: center || null, effectiveFrom: from, effectiveThrough: through || null, reason: reason.trim(),
    ...(kind === 'parameters' ? { values: keys.filter((key) => (values[key.key] ?? '').trim() !== '').map((key) => ({ key: key.key, value: values[key.key].trim() })) }
      : { maximumFirePercent: fire.trim(), massBalanceDifferenceTolerancePercent: mass.trim() }) }
  const valid = reason.trim().length > 0 && from >= data.businessDate && (through === '' || through >= from)
    && (center === '' || workCenters.some((item) => item.id === center && item.isActive))
    && (kind === 'parameters' ? payload.values!.length > 0 : fire.trim() !== '' && mass.trim() !== '')
  return <Mutation company={company} body={payload} valid={valid} action={(context, body) => prepareManufacturingProfile(context, kind, body)} saved={T.prepared} finish={finish}>
    {() => <><Field label={T.process}>{(control) => <select {...control} value={process} onChange={(event) => { setProcess(event.target.value as ManufacturingProcess); setValues({}) }}>{MANUFACTURING_PROCESSES.map((item) => <option key={item} value={item}>{manufacturingProcessLabel(item)}</option>)}</select>}</Field>
      <Field label={T.workCenter}>{(control) => <select {...control} value={center} onChange={(event) => setCenter(event.target.value)}><option value="">{T.allLines}</option>{workCenters.filter((item) => item.isActive).map((item) => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select>}</Field>
      <Field label={T.from}>{(control) => <input {...control} type="date" min={data.businessDate} required value={from} onChange={(event) => setFrom(event.target.value)} />}</Field>
      <Field label={T.through}>{(control) => <input {...control} type="date" min={from} value={through} onChange={(event) => setThrough(event.target.value)} />}</Field>
      {kind === 'parameters' ? <><p>{T.omit}</p>{keys.map((key) => <Field key={key.key} label={`${parameterLabel(key.key)} (${key.unit})`}>{(control) => key.kind === 'Choice'
        ? <select {...control} value={values[key.key] ?? ''} onChange={(event) => setValues((current) => ({ ...current, [key.key]: event.target.value }))}><option value="">{T.choose}</option>{key.choices.map((value) => <option key={value} value={value}>{value}</option>)}</select>
        : <input {...control} inputMode="decimal" value={values[key.key] ?? ''} onChange={(event) => setValues((current) => ({ ...current, [key.key]: event.target.value }))} />}</Field>)}</>
        : <><Field label={T.fire}>{(control) => <input {...control} inputMode="decimal" required value={fire} onChange={(event) => setFire(event.target.value)} />}</Field><Field label={T.mass}>{(control) => <input {...control} inputMode="decimal" required value={mass} onChange={(event) => setMass(event.target.value)} />}</Field></>}
      <section aria-label={T.preview}><h3>{T.preview}</h3><p>{manufacturingProcessLabel(process)} · {center === '' ? T.allLines : workCenters.find((item) => item.id === center)?.name}</p><p>{from} — {through || T.noEnd}</p><p>{T.prospective}</p>
        {kind === 'parameters' ? payload.values?.map((value) => <p key={value.key}>{parameterLabel(value.key)}: {value.value}</p>) : <p>{T.fire}: {fire} · {T.mass}: {mass}</p>}</section>
      <Field label={T.reason}>{(control) => <textarea {...control} required maxLength={500} value={reason} onChange={(event) => setReason(event.target.value)} />}</Field>
    </>}
  </Mutation>
}
