/**
 * AY-YZG-01 `/ayarlar/yapay-zeka-ag-gecidi` (WP-A5, AI-20, DALGA_PLANI_v2 A.5): the single AI management screen on
 * `settings.ai-gateway.manage` (Settings provider 1.21).
 *
 *  - The versioned gateway policy: feature switches, the "task kind × data class" model table, the monthly ceiling and the
 *    per-request reservation. A new version is one K3 command (expected version, Idempotency-Key, reason); an unknown
 *    outcome keeps the same key and payload. The editor never proposes a budget value (S5); without both budget values
 *    every feature stays closed (YOL-D21) and the switches are disabled.
 *  - The fixed rules (cloud ≤ Internal; document/e-mail text only on a company-internal model) are shown, never editable;
 *    the editor cannot offer a cloud placement above Internal and the server refuses one anyway.
 *  - Usage and acceptance counts of the current month (read-only). Acceptance is "not measured yet" until WP-C9.
 *  - The AI security events (read-only), only with the `settings.ai-gateway.events.read` control.
 *
 * Each read stands alone: a failure of one never hides the others.
 */

import { useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { SETTINGS_AI_GATEWAY_CONTROL, SETTINGS_CAPABILITY } from './capabilities'
import { Frame, stateOf, useBoundaryRead } from './SalesPolicyParts'
import { useCommandAttempt } from './useCommandAttempt'
import { fetchAiGatewayPolicy, fetchAiGatewayUsage, fetchAiSecurityEvents, recordAiGatewayPolicy } from './aiGatewayApi'
import {
  AI_DATA_CLASSES,
  AI_EVENT_TYPES,
  AI_FEATURES,
  AI_GATEWAY_LIMITS,
  AI_PLACEMENTS,
  AI_TASK_KINDS,
  ON_PREMISES_ONLY_FEATURES,
  budgetEntered,
  placementAllows,
  type AiDataClass,
  type AiEventType,
  type AiFeature,
  type AiGatewayPolicy,
  type AiGatewayUsage,
  type AiPlacement,
  type AiSecurityEventPage,
  type AiTaskKind,
  type RecordAiGatewayPolicyBody,
} from './aiGatewayContract'
import {
  AI_DATA_CLASS_LABEL,
  AI_EVENT_TYPE_LABEL,
  AI_FEATURE_LABEL,
  AI_GATEWAY_TEXT as T,
  AI_PLACEMENT_LABEL,
  AI_TASK_KIND_LABEL,
  budgetMonthLabel,
} from './aiGatewayText'
import styles from './settings.module.css'

const instantText = (value: string) => {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}
const budgetText = (value: number | null) => value === null ? T.noBudget : T.units(value)

export function AiGatewayPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <Screen key={`${namespace}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function Screen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const company = shell.activeContext?.companyId ?? null
  const single = shell.activeContext?.scopeMode === 'single' && company !== null
  const ready = namespace !== null && single && hasCapability(shell, SETTINGS_CAPABILITY.aiGateway)
  const eventsAllowed = ready && hasCapability(shell, SETTINGS_AI_GATEWAY_CONTROL.events)
  const policyQuery = useBoundaryRead([namespace, shell.routeEntryGeneration, 'ai-gateway'], ready,
    (signal) => fetchAiGatewayPolicy(company ?? '', signal))
  const usageQuery = useBoundaryRead([namespace, shell.routeEntryGeneration, 'ai-gateway-usage'], ready,
    (signal) => fetchAiGatewayUsage(company ?? '', signal))
  const policy = stateOf<AiGatewayPolicy>(policyQuery.isPending, policyQuery.data)
  const usage = stateOf<AiGatewayUsage>(usageQuery.isPending, usageQuery.data)
  if (!single) return <Frame title={T.title} intro={T.intro}><StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} /></Frame>
  if (!ready) return <Frame title={T.title} intro={T.intro}><StateBlock state={{ kind: 'not-found' }} subject={T.subject} /></Frame>
  const changed = () => { void policyQuery.refetch(); void usageQuery.refetch() }
  return <Frame title={T.title} intro={T.intro}>
    <FixedRules />
    {policy.kind === 'ready'
      ? <PolicySection policy={policy.data} changed={changed} />
      : <StateBlock state={policy} subject={T.subject} onRetry={() => void policyQuery.refetch()} />}
    {usage.kind === 'ready'
      ? <UsageSection usage={usage.data} ceiling={policy.kind === 'ready' ? policy.data.monthlyUsageCeiling : null} />
      : <StateBlock state={usage} subject={T.usageSubject} onRetry={() => void usageQuery.refetch()} />}
    {eventsAllowed && company !== null
      ? <EventsSection companyId={company} namespace={namespace} routeEntryGeneration={shell.routeEntryGeneration} />
      : null}
  </Frame>
}

function FixedRules() {
  return <section aria-labelledby="ai-fixed" data-testid="ai-fixed-rules">
    <h2 id="ai-fixed">{T.fixedHeading}</h2>
    <ul className={styles.plainList}>{T.fixedRules.map((rule) => <li key={rule}>{rule}</li>)}</ul>
    <h3>{T.decisionsHeading}</h3>
    <ul className={styles.plainList}>{T.decisions.map((rule) => <li key={rule}>{rule}</li>)}</ul>
  </section>
}

function PolicySection({ policy, changed }: { readonly policy: AiGatewayPolicy; readonly changed: () => void }) {
  const [editing, setEditing] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  return <section aria-labelledby="ai-policy" data-testid="ai-policy">
    <h2 id="ai-policy">{T.currentHeading}</h2>
    <p role="status">{notice}</p>
    <p>{T.version}: {policy.version}</p>
    {policy.version === '0' ? <p>{T.notRecorded}</p> : null}
    <dl>
      <dt>{T.ceiling}</dt><dd>{budgetText(policy.monthlyUsageCeiling)}</dd>
      <dt>{T.perRequest}</dt><dd>{budgetText(policy.maxUnitsPerRequest)}</dd>
    </dl>
    <table className={styles.table}>
      <caption>{T.featuresHeading}</caption>
      <thead><tr><th scope="col">{T.feature}</th><th scope="col">{T.open}/{T.closed}</th></tr></thead>
      <tbody>{AI_FEATURES.map((feature) => <tr key={feature}>
        <th scope="row">{AI_FEATURE_LABEL[feature]}</th>
        <td>{policy.enabledFeatures.includes(feature) ? T.open : T.closed}</td>
      </tr>)}</tbody>
    </table>
    <RouteMatrix policy={policy} />
    <History policy={policy} />
    {editing
      ? <Editor policy={policy} finish={(message) => { setEditing(false); setNotice(message); if (message !== null) changed() }} />
      : <Button type="button" onClick={() => setEditing(true)}>{T.edit}</Button>}
  </section>
}

function RouteMatrix({ policy }: { readonly policy: AiGatewayPolicy }) {
  if (policy.routes.length === 0) return <p>{T.routesEmpty}</p>
  return <table className={styles.table} data-testid="ai-route-matrix">
    <caption>{T.routesHeading}</caption>
    <thead><tr><th scope="col">{T.taskKind}</th>{AI_DATA_CLASSES.map((dataClass) => <th scope="col" key={dataClass}>{AI_DATA_CLASS_LABEL[dataClass]}</th>)}</tr></thead>
    <tbody>{AI_TASK_KINDS.map((task) => <tr key={task}>
      <th scope="row">{AI_TASK_KIND_LABEL[task]}</th>
      {AI_DATA_CLASSES.map((dataClass) => {
        const route = policy.routes.find((item) => item.taskKind === task && item.dataClass === dataClass)
        return <td key={dataClass}>{route === undefined ? T.noRoute : `${AI_PLACEMENT_LABEL[route.placement]} · ${route.modelDeployment}`}</td>
      })}
    </tr>)}</tbody>
  </table>
}

function History({ policy }: { readonly policy: AiGatewayPolicy }) {
  if (policy.versions.length === 0) return <p>{T.historyEmpty}</p>
  return <table className={styles.table}>
    <caption>{T.historyHeading}</caption>
    <thead><tr>
      <th scope="col">{T.version}</th><th scope="col">{T.ceiling}</th><th scope="col">{T.perRequest}</th>
      <th scope="col">{T.featureCount}</th><th scope="col">{T.routeCount}</th><th scope="col">{T.who}</th>
      <th scope="col">{T.recordedAt}</th><th scope="col">{T.reason}</th>
    </tr></thead>
    <tbody>{policy.versions.map((item) => <tr key={item.version}>
      <td>{item.version}</td><td>{budgetText(item.monthlyUsageCeiling)}</td><td>{budgetText(item.maxUnitsPerRequest)}</td>
      <td>{item.featureCount}</td><td>{item.routeCount}</td><td>{item.recordedBy}</td>
      <td><time dateTime={item.recordedAtUtc}>{instantText(item.recordedAtUtc)}</time></td><td>{item.reason}</td>
    </tr>)}</tbody>
  </table>
}

interface DraftRoute {
  readonly id: number
  readonly taskKind: AiTaskKind
  readonly dataClass: AiDataClass
  readonly placement: AiPlacement
  readonly modelDeployment: string
}

const parseCeiling = (value: string): number | null | undefined => {
  const text = value.trim()
  if (text === '') return null
  if (!/^\d{1,13}$/.test(text)) return undefined
  const parsed = Number(text)
  return parsed <= AI_GATEWAY_LIMITS.maximumMonthlyUsageCeiling ? parsed : undefined
}
const parsePerRequest = (value: string): number | null | undefined => {
  const text = value.trim()
  if (text === '') return null
  if (!/^[1-9]\d{0,7}$/.test(text)) return undefined
  const parsed = Number(text)
  return parsed <= AI_GATEWAY_LIMITS.maximumUnitsPerRequest ? parsed : undefined
}
// eslint-disable-next-line no-control-regex
const modelValid = (value: string) => value.trim().length > 0 && value.trim().length <= AI_GATEWAY_LIMITS.modelDeploymentMaximumLength && !/[\u0000-\u001f\u007f]/.test(value)

function Editor({ policy, finish }: { readonly policy: AiGatewayPolicy; readonly finish: (message: string | null) => void }) {
  const [ceiling, setCeiling] = useState(policy.monthlyUsageCeiling === null ? '' : String(policy.monthlyUsageCeiling))
  const [perRequest, setPerRequest] = useState(policy.maxUnitsPerRequest === null ? '' : String(policy.maxUnitsPerRequest))
  const [features, setFeatures] = useState<readonly AiFeature[]>(policy.enabledFeatures)
  const [routes, setRoutes] = useState<readonly DraftRoute[]>(policy.routes.map((route, index) => ({ id: index, ...route })))
  const [nextId, setNextId] = useState(policy.routes.length)
  const [reason, setReason] = useState('')
  const [frozen, setFrozen] = useState<RecordAiGatewayPolicyBody | null>(null)
  const attempt = useCommandAttempt<true>()
  const boundary = useAuthBoundary()
  const busy = attempt.status.kind === 'busy'

  const ceilingValue = parseCeiling(ceiling)
  const perRequestValue = parsePerRequest(perRequest)
  const budgetOk = ceilingValue !== undefined && perRequestValue !== undefined && budgetEntered(ceilingValue, perRequestValue)
  const effectiveFeatures = budgetOk ? AI_FEATURES.filter((feature) => features.includes(feature)) : []
  const duplicate = (route: DraftRoute) =>
    routes.some((other) => other.id !== route.id && other.taskKind === route.taskKind && other.dataClass === route.dataClass)
  const routesValid = routes.length <= AI_GATEWAY_LIMITS.maximumRoutes
    && routes.every((route) => modelValid(route.modelDeployment) && placementAllows(route.placement, route.dataClass) && !duplicate(route))
  const valid = ceilingValue !== undefined && perRequestValue !== undefined && routesValid && reason.trim().length > 0

  const updateRoute = (id: number, change: Partial<DraftRoute>) =>
    setRoutes((current) => current.map((route) => {
      if (route.id !== id) return route
      const next = { ...route, ...change }
      // A data class above the cloud maximum moves the row to a company-internal model; it never stays on the cloud.
      return placementAllows(next.placement, next.dataClass) ? next : { ...next, placement: 'OnPremises' }
    }))
  const addRoute = () => {
    setRoutes((current) => [...current, { id: nextId, taskKind: 'Answer', dataClass: 'Internal', placement: 'OnPremises', modelDeployment: '' }])
    setNextId((id) => id + 1)
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (busy || (!valid && frozen === null)) return
    const payload: RecordAiGatewayPolicyBody = frozen ?? {
      expectedVersion: policy.version,
      monthlyUsageCeiling: ceilingValue ?? null,
      maxUnitsPerRequest: perRequestValue ?? null,
      enabledFeatures: effectiveFeatures,
      routes: routes.map(({ taskKind, dataClass, placement, modelDeployment }) => ({ taskKind, dataClass, placement, modelDeployment: modelDeployment.trim() })),
      reason: reason.trim(),
    }
    const snapshot = boundary.capture()
    const result = await attempt.run(payload, (idempotencyKey) => recordAiGatewayPolicy({ companyId: policy.companyId, idempotencyKey }, payload))
    if (result.type === 'unknown') { setFrozen(payload); return }
    setFrozen(null)
    if (result.type === 'applied') { finish(result.replayed ? T.replay : T.saved); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, snapshot)
    if (result.status === 409 && result.code === 'settings_company_policy_version_conflict') finish(T.conflict)
  }

  return <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={busy} aria-label={T.editorHeading}>
    <h3>{T.editorHeading}</h3>
    <fieldset disabled={busy || frozen !== null}>
      <p className={styles.muted}>{T.budgetHint}</p>
      <Field label={T.ceilingInput(AI_GATEWAY_LIMITS.maximumMonthlyUsageCeiling)}>{(control) =>
        <input {...control} inputMode="numeric" value={ceiling} onChange={(event) => setCeiling(event.target.value)} />}</Field>
      {ceilingValue === undefined ? <p role="alert">{T.invalidCeiling}</p> : null}
      <Field label={T.perRequestInput(AI_GATEWAY_LIMITS.maximumUnitsPerRequest)}>{(control) =>
        <input {...control} inputMode="numeric" value={perRequest} onChange={(event) => setPerRequest(event.target.value)} />}</Field>
      {perRequestValue === undefined ? <p role="alert">{T.invalidPerRequest}</p> : null}

      <fieldset className={styles.fieldset} disabled={!budgetOk}>
        <legend>{T.featuresHeading}</legend>
        {!budgetOk ? <p>{T.featuresLocked}</p> : null}
        {AI_FEATURES.map((feature) => <label key={feature} className={styles.choice}>
          <input type="checkbox" disabled={!budgetOk} checked={budgetOk && features.includes(feature)}
            onChange={(event) => setFeatures((current) => event.target.checked ? [...current, feature] : current.filter((item) => item !== feature))} />
          {AI_FEATURE_LABEL[feature]}{ON_PREMISES_ONLY_FEATURES.includes(feature) ? ` (${AI_PLACEMENT_LABEL.OnPremises})` : ''}
        </label>)}
      </fieldset>

      <fieldset className={styles.fieldset}>
        <legend>{T.routesHeading}</legend>
        {routes.length === 0 ? <p>{T.routesEmpty}</p> : null}
        {routes.map((route) => {
          return <div key={route.id} className={styles.commandFields} data-testid="ai-route-row">
            <Field label={T.taskKind}>{(control) => <select {...control} value={route.taskKind}
              onChange={(event) => updateRoute(route.id, { taskKind: event.target.value as AiTaskKind })}>
              {AI_TASK_KINDS.map((task) => <option key={task} value={task}>{AI_TASK_KIND_LABEL[task]}</option>)}
            </select>}</Field>
            <Field label={T.dataClass}>{(control) => <select {...control} value={route.dataClass}
              onChange={(event) => updateRoute(route.id, { dataClass: event.target.value as AiDataClass })}>
              {AI_DATA_CLASSES.map((dataClass) => <option key={dataClass} value={dataClass}>{AI_DATA_CLASS_LABEL[dataClass]}</option>)}
            </select>}</Field>
            <Field label={T.placement}>{(control) => <select {...control} value={route.placement}
              onChange={(event) => updateRoute(route.id, { placement: event.target.value as AiPlacement })}>
              {AI_PLACEMENTS.map((placement) => <option key={placement} value={placement} disabled={!placementAllows(placement, route.dataClass)}>
                {AI_PLACEMENT_LABEL[placement]}</option>)}
            </select>}</Field>
            {!placementAllows('Cloud', route.dataClass) ? <p className={styles.muted}>{T.cloudForbidden}</p> : null}
            <Field label={T.modelDeployment}>{(control) => <input {...control} maxLength={AI_GATEWAY_LIMITS.modelDeploymentMaximumLength}
              value={route.modelDeployment} onChange={(event) => updateRoute(route.id, { modelDeployment: event.target.value })} />}</Field>
            {duplicate(route) ? <p role="alert">{T.duplicateRoute}</p> : null}
            {!modelValid(route.modelDeployment) ? <p role="alert">{T.invalidRoute}</p> : null}
            <Button type="button" onClick={() => setRoutes((current) => current.filter((item) => item.id !== route.id))}>{T.removeRoute(AI_TASK_KIND_LABEL[route.taskKind], AI_DATA_CLASS_LABEL[route.dataClass])}</Button>
          </div>
        })}
        <Button type="button" disabled={routes.length >= AI_GATEWAY_LIMITS.maximumRoutes} onClick={addRoute}>{T.addRoute}</Button>
      </fieldset>

      <section aria-label={T.preview}>
        <h3>{T.preview}</h3>
        <p>{T.ceiling}: {budgetText(policy.monthlyUsageCeiling)} → {ceilingValue === undefined ? '—' : budgetText(ceilingValue)}</p>
        <p>{T.perRequest}: {budgetText(policy.maxUnitsPerRequest)} → {perRequestValue === undefined ? '—' : budgetText(perRequestValue)}</p>
        <p>{T.featuresHeading}: {effectiveFeatures.length === 0 ? T.closed : effectiveFeatures.map((feature) => AI_FEATURE_LABEL[feature]).join(', ')}</p>
        <p>{T.effect}</p>
      </section>
      <Field label={T.reason}>{(control) => <textarea {...control} required maxLength={AI_GATEWAY_LIMITS.reasonMaximumLength} value={reason}
        onChange={(event) => setReason(event.target.value)} />}</Field>
    </fieldset>
    {attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}
    {attempt.status.kind === 'refused' && <p role="alert">{T.refused}</p>}
    <Button type="submit" variant="primary" disabled={busy || (!valid && frozen === null)}>{busy ? T.pending : frozen !== null ? T.retry : T.save}</Button>
    <Button type="button" disabled={busy || frozen !== null} onClick={() => finish(null)}>{T.cancel}</Button>
  </form>
}

function UsageSection({ usage, ceiling }: { readonly usage: AiGatewayUsage; readonly ceiling: number | null }) {
  const acceptanceByFeature = new Map(usage.acceptance.map((line) => [line.feature, line]))
  return <section aria-labelledby="ai-usage" data-testid="ai-usage">
    <h2 id="ai-usage">{T.usageHeading(budgetMonthLabel(usage.budgetMonth))}</h2>
    <p>{T.committed}: {ceiling === null ? T.units(usage.committedUnits) : T.ceilingShare(usage.committedUnits, ceiling)} · {T.entries}: {usage.entryCount}</p>
    {usage.features.length === 0 ? <p>{T.usageEmpty}</p> : <table className={styles.table}>
      <caption>{T.usageHeading(budgetMonthLabel(usage.budgetMonth))}</caption>
      <thead><tr><th scope="col">{T.feature}</th><th scope="col">{T.entries}</th><th scope="col">{T.openReservations}</th><th scope="col">{T.committed}</th></tr></thead>
      <tbody>{usage.features.map((line) => <tr key={line.feature}>
        <th scope="row">{AI_FEATURE_LABEL[line.feature]}</th><td>{line.entryCount}</td><td>{line.openReservationCount}</td><td>{T.units(line.committedUnits)}</td>
      </tr>)}</tbody>
    </table>}
    <h3>{T.acceptanceHeading}</h3>
    {!usage.acceptanceMeasured ? <p data-testid="ai-acceptance-not-measured">{T.acceptanceNotMeasured}</p>
      : usage.acceptance.length === 0 ? <p>{T.acceptanceEmpty}</p>
        : <table className={styles.table}>
          <caption>{T.acceptanceHeading}</caption>
          <thead><tr><th scope="col">{T.feature}</th><th scope="col">{T.accepted}</th><th scope="col">{T.corrected}</th><th scope="col">{T.rejected}</th><th scope="col">{T.reverted}</th></tr></thead>
          <tbody>{AI_FEATURES.filter((feature) => acceptanceByFeature.has(feature)).map((feature) => {
            const line = acceptanceByFeature.get(feature)!
            return <tr key={feature}><th scope="row">{AI_FEATURE_LABEL[feature]}</th><td>{line.accepted}</td><td>{line.corrected}</td><td>{line.rejected}</td><td>{line.reverted}</td></tr>
          })}</tbody>
        </table>}
  </section>
}

function EventsSection({ companyId, namespace, routeEntryGeneration }: {
  readonly companyId: string
  readonly namespace: string | null
  readonly routeEntryGeneration: number
}) {
  const [eventType, setEventType] = useState<AiEventType>('ai.request.refused')
  const [cursors, setCursors] = useState<readonly string[]>([])
  const cursor = cursors.length === 0 ? null : cursors[cursors.length - 1]
  const query = useBoundaryRead([namespace, routeEntryGeneration, 'ai-events', eventType, cursor], namespace !== null,
    (signal) => fetchAiSecurityEvents(companyId, eventType, cursor, signal))
  const state = stateOf<AiSecurityEventPage>(query.isPending, query.data)
  return <section aria-labelledby="ai-events" data-testid="ai-events">
    <h2 id="ai-events">{T.eventsHeading}</h2>
    <p className={styles.muted}>{T.eventsIntro}</p>
    <Field label={T.eventType}>{(control) => <select {...control} value={eventType}
      onChange={(event) => { setEventType(event.target.value as AiEventType); setCursors([]) }}>
      {AI_EVENT_TYPES.map((type) => <option key={type} value={type}>{AI_EVENT_TYPE_LABEL[type]}</option>)}
    </select>}</Field>
    {state.kind !== 'ready'
      ? <StateBlock state={state} subject={T.eventsSubject} onRetry={() => void query.refetch()} />
      : state.data.items.length === 0 ? <p>{T.eventsEmpty}</p>
        : <table className={styles.table}>
          <caption>{AI_EVENT_TYPE_LABEL[eventType]}</caption>
          <thead><tr>
            <th scope="col">{T.when}</th><th scope="col">{T.outcome}</th><th scope="col">{T.reasonCode}</th><th scope="col">{T.tool}</th>
            <th scope="col">{T.dataClass}</th><th scope="col">{T.occurrences}</th><th scope="col">{T.correlation}</th>
          </tr></thead>
          <tbody>{state.data.items.map((item) => <tr key={item.id}>
            <td><time dateTime={item.occurredAtUtc}>{instantText(item.occurredAtUtc)}</time></td>
            <td>{item.outcome}</td><td>{item.reasonCode ?? '—'}</td>
            <td>{item.aiToolId === null ? '—' : `${item.aiToolId}${item.aiToolVersion === null ? '' : ` (${item.aiToolVersion})`}`}</td>
            <td>{item.aiDataClass ?? '—'}</td><td>{item.occurrenceCount}</td><td>{item.correlationId}</td>
          </tr>)}</tbody>
        </table>}
    {state.kind === 'ready' && state.data.nextCursor !== null
      ? <Button type="button" onClick={() => setCursors((current) => [...current, state.data.nextCursor as string])}>{T.more}</Button>
      : null}
    {cursors.length > 0 ? <Button type="button" onClick={() => setCursors([])}>{T.firstPage}</Button> : null}
  </section>
}
