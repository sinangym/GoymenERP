import { useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { stateOf, useBoundaryRead } from '../settings/SalesPolicyParts'
import { useCommandAttempt } from '../settings/useCommandAttempt'
import { classifyOutcome } from '../shell/uiState'
import { changeTwoFactor, prepareTwoFactor, readTwoFactor, type TwoFactorBody, type TwoFactorEnrollment, type TwoFactorReceipt, type TwoFactorState } from './twoFactorApi'
import { TWO_FACTOR_TEXT as T } from './twoFactorText'
import styles from './identity.module.css'

export function LocalTwoFactorPage() {
  const shell = useShellState(), context = contextKey(shell), namespace = context === null ? null : contextKeyString(context)
  return <Screen key={`${namespace}:${shell.routeEntryGeneration}`} namespace={namespace} />
}
function Screen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState(), company = shell.activeContext?.companyId ?? ''
  const ready = namespace !== null && company !== '' && (shell.activeContext?.scopeMode ?? 'single') === 'single'
  const query = useBoundaryRead([namespace, shell.routeEntryGeneration, 'local-two-factor'], ready, (signal) => readTwoFactor(company, signal))
  const state = stateOf<TwoFactorState>(query.isPending, query.data)
  const [notice, setNotice] = useState(''), [codes, setCodes] = useState<readonly string[] | null>(null)
  return <section className={styles.page}><h1>{T.title}</h1><p>{T.intro}</p><p role="status">{notice}</p>
    {!ready ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} /> : state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
      : <><p>{state.data.enabled ? T.enabled : T.disabled} · {T.count}: {state.data.recoveryCodesRemaining}</p><SecurityForm key={state.data.credentialVersion} company={company} state={state.data}
        finish={(result, replayed) => { setNotice(replayed ? T.replay : T.saved); setCodes(result.recoveryCodes); void query.refetch() }} /></>}
    {codes !== null && <section><h2>{T.codes}</h2><p>{T.codesHelp}</p><pre>{codes.join('\n')}</pre><Button type="button" onClick={() => setCodes(null)}>{T.dismiss}</Button></section>}
  </section>
}
function SecurityForm({ company, state, finish }: { readonly company: string; readonly state: TwoFactorState; readonly finish: (receipt: TwoFactorReceipt, replayed: boolean) => void }) {
  const boundary = useAuthBoundary(), command = useCommandAttempt<TwoFactorReceipt>()
  const [password, setPassword] = useState(''), [code, setCode] = useState('')
  const [enrollment, setEnrollment] = useState<TwoFactorEnrollment | null>(null), [preparing, setPreparing] = useState(false)
  const [error, setError] = useState<ReturnType<typeof classifyOutcome<TwoFactorEnrollment>> | null>(null)
  const [frozen, setFrozen] = useState<{ readonly action: string; readonly body: TwoFactorBody } | null>(null)
  const body: TwoFactorBody = { expectedVersion: state.credentialVersion, currentPassword: password, code: code || null, enrollmentToken: enrollment?.enrollmentToken ?? null }
  const prepare = async (event: FormEvent) => {
    event.preventDefault(); if (!password || preparing) return; setPreparing(true)
    const capture = boundary.capture(), result = await prepareTwoFactor(company, body)
    if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, capture)
    if (result.type === 'ok-single' && result.data.credentialVersion === state.credentialVersion) { setEnrollment(result.data); setError(null) }
    else setError(classifyOutcome(result.type === 'ok-single' ? { type: 'malformed' } : result)); setPreparing(false)
  }
  const send = async (action: string) => {
    if (command.status.kind === 'busy' || (!frozen && (!password || !code))) return
    const payload = frozen ?? { action, body }, capture = boundary.capture()
    const result = await command.run(payload, (key) => changeTwoFactor(company, payload.action, payload.body, key))
    if (result.type === 'unknown') { setFrozen(payload); return }
    setFrozen(null); setPassword(''); setCode('')
    if (result.type === 'applied') { setEnrollment(null); finish(result.data, result.replayed); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, capture)
  }
  const busy = preparing || command.status.kind === 'busy'
  return <section><form onSubmit={(event) => void prepare(event)}><fieldset disabled={busy || frozen !== null}>
    <Field label={T.password}>{(p) => <input {...p} type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} />}</Field>
    {!state.enabled && enrollment === null && <Button type="submit" disabled={!password}>{T.begin}</Button>}
    {enrollment !== null && <><p>{T.setupHelp}</p><p>{T.seed}: <code>{enrollment.secretKey}</code></p><p>{T.uri}: <code>{enrollment.authenticatorUri}</code></p>
      <h2>{T.codes}</h2><p>{T.codesHelp}</p><pre>{enrollment.recoveryCodes.join('\n')}</pre><Button type="button" onClick={() => setEnrollment(null)}>{T.cancel}</Button></>}
    {(state.enabled || enrollment !== null) && <><Field label={T.code}>{(p) => <input {...p} autoComplete="one-time-code" maxLength={64} value={code} onChange={(e) => setCode(e.target.value)} />}</Field>
      {state.enabled ? <><Button type="button" onClick={() => void send('disable')}>{T.disable}</Button><Button type="button" onClick={() => void send('recovery-codes')}>{T.regenerate}</Button></>
        : <Button type="button" onClick={() => void send('enable')}>{T.enable}</Button>}</>}
    </fieldset></form>
    {error !== null && error.kind !== 'ready' && <StateBlock state={error} subject={T.subject} />}
    {command.status.kind === 'refused' && <p role="alert">{T.refused}</p>}{frozen !== null && <><p role="alert">{T.unknown}</p><Button type="button" disabled={busy} onClick={() => void send(frozen.action)}>{T.retry}</Button></>}
  </section>
}
