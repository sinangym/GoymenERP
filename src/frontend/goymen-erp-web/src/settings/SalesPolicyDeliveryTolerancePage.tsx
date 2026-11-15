/**
 * Ayarlar > Teslim toleransı — F-029 S4b, SAT-D5 (URN-D2/D3/D4; spec `URUN_F029_POLITIKA_KATALOGLARI_ENVANTER_VE_SPEC_v1`
 * §5.2). Standing on `settings.sales-delivery-tolerances.manage`.
 *
 * The screen shows the company's delivery tolerance timeline (warn / approval / block percents) as the server sends it,
 * offers the one-time baseline transfer while the company still runs on the compiled catalog, and records a new version
 * afterwards: one manager, no second approval, a required reason, a date from today on. It never orders, compares or
 * evaluates the percents — the server checks warn ≤ approval ≤ block and the shipment applies them.
 *
 * Remounted per committed context and route entry; a group (multi-company) context shows the no-company state; a stale
 * refusal (version moved, baseline state changed) closes the form and re-reads; an unknown outcome keeps the same
 * Idempotency-Key for "retry".
 */

import { useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { SETTINGS_CAPABILITY } from './capabilities'
import { isPercentText } from './discountLimitContract'
import {
  fetchDeliveryTolerance,
  recordDeliveryToleranceVersion,
  transferDeliveryToleranceBaseline,
} from './salesPolicyApi'
import { inForceOf, type DeliveryToleranceView } from './salesPolicyContract'
import {
  AttemptFooter,
  BaselineTransferForm,
  EffectiveFromField,
  Frame,
  MetaCells,
  MetaHeaders,
  ReasonField,
  effectiveFromBody,
  isEffectiveFromValid,
  savedNotice,
  staleRefusal,
  stateOf,
  useBoundaryRead,
} from './SalesPolicyParts'
import { POLICY_TEXT, percentLabel } from './salesPolicyText'
import { useCommandAttempt } from './useCommandAttempt'
import styles from './settings.module.css'

export function SalesPolicyDeliveryTolerancePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <ToleranceScreen key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function ToleranceScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const ready = namespace !== null && singleCompany && hasCapability(shell, SETTINGS_CAPABILITY.salesDeliveryTolerance)
  const queryKey = [namespace ?? 'no-context', shell.routeEntryGeneration, 'settings-sales-delivery-tolerance']
  const view = useBoundaryRead(queryKey, ready, (signal) => fetchDeliveryTolerance(companyId ?? '', signal))

  if (!singleCompany) {
    return (
      <Frame title={POLICY_TEXT.toleranceTitle} intro={POLICY_TEXT.toleranceIntro}>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={POLICY_TEXT.toleranceSubject} />
      </Frame>
    )
  }

  if (!ready || companyId === null) {
    return (
      <Frame title={POLICY_TEXT.toleranceTitle} intro={POLICY_TEXT.toleranceIntro}>
        <StateBlock state={{ kind: 'not-found' }} subject={POLICY_TEXT.toleranceSubject} />
      </Frame>
    )
  }

  const state = stateOf<DeliveryToleranceView>(view.isPending, view.data)
  return (
    <Frame title={POLICY_TEXT.toleranceTitle} intro={POLICY_TEXT.toleranceIntro}>
      {state.kind === 'ready' ? (
        <ToleranceSection data={state.data} companyId={companyId} onChanged={() => void view.refetch()} />
      ) : (
        <StateBlock state={state} subject={POLICY_TEXT.toleranceSubject} onRetry={() => void view.refetch()} />
      )}
    </Frame>
  )
}

function ToleranceSection({
  data,
  companyId,
  onChanged,
}: {
  readonly data: DeliveryToleranceView
  readonly companyId: string
  readonly onChanged: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const inForce = inForceOf(data.versions)
  const close = (saved: string | null) => {
    setEditing(false)
    setNotice(saved)
    if (saved !== null) onChanged()
  }
  const stale = (text: string) => {
    setEditing(false)
    setNotice(text)
    onChanged()
  }

  return (
    <section aria-labelledby="tolerance-versions-heading" className={styles.section}>
      {notice !== null ? (
        <p role="status" className={styles.notice}>
          {notice}
        </p>
      ) : null}
      <h2 id="tolerance-versions-heading">{POLICY_TEXT.toleranceTableLabel}</h2>
      <p className={styles.muted}>
        {inForce === null
          ? POLICY_TEXT.toleranceNoneInForce
          : `${POLICY_TEXT.toleranceInForce}: ${POLICY_TEXT.columnWarn} ${percentLabel(inForce.warnPercent)} · ` +
            `${POLICY_TEXT.columnApproval} ${percentLabel(inForce.approvalPercent)} · ` +
            `${POLICY_TEXT.columnBlock} ${percentLabel(inForce.blockPercent)}`}
      </p>
      {data.versions.length === 0 ? (
        <p className={styles.muted}>{POLICY_TEXT.toleranceEmpty}</p>
      ) : (
        <table className={styles.table}>
          <caption>{POLICY_TEXT.toleranceTableLabel}</caption>
          <thead>
            <tr>
              <th scope="col">{POLICY_TEXT.columnVersion}</th>
              <th scope="col">{POLICY_TEXT.columnWarn}</th>
              <th scope="col">{POLICY_TEXT.columnApproval}</th>
              <th scope="col">{POLICY_TEXT.columnBlock}</th>
              <MetaHeaders />
            </tr>
          </thead>
          <tbody>
            {data.versions.map((entry) => (
              <tr key={entry.version}>
                <td>{entry.version}</td>
                <td>{percentLabel(entry.warnPercent)}</td>
                <td>{percentLabel(entry.approvalPercent)}</td>
                <td>{percentLabel(entry.blockPercent)}</td>
                <MetaCells entry={entry} />
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {!data.isBaselineTransferred ? (
        <BaselineTransferForm
          companyId={companyId}
          headingId="tolerance-baseline-heading"
          transfer={(context, body) => transferDeliveryToleranceBaseline(context, body)}
          onApplied={close}
          onStale={stale}
        />
      ) : editing ? (
        <ToleranceForm data={data} companyId={companyId} onDone={close} onStale={stale} />
      ) : (
        <div className={styles.actions}>
          <Button
            type="button"
            variant="primary"
            onClick={() => {
              setNotice(null)
              setEditing(true)
            }}
          >
            {POLICY_TEXT.newVersion}
          </Button>
        </div>
      )}
    </section>
  )
}

function ToleranceForm({
  data,
  companyId,
  onDone,
  onStale,
}: {
  readonly data: DeliveryToleranceView
  readonly companyId: string
  readonly onDone: (notice: string | null) => void
  readonly onStale: (notice: string) => void
}) {
  // The form starts from the version in force today (as the server marked it), never from a value the screen invents.
  const inForce = inForceOf(data.versions)
  const [warnPercent, setWarnPercent] = useState(inForce?.warnPercent ?? '')
  const [approvalPercent, setApprovalPercent] = useState(inForce?.approvalPercent ?? '')
  const [blockPercent, setBlockPercent] = useState(inForce?.blockPercent ?? '')
  const [effectiveFrom, setEffectiveFrom] = useState(data.today)
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const percentsValid = [warnPercent, approvalPercent, blockPercent].every((value) => isPercentText(value.trim()))
  const dateValid = isEffectiveFromValid(effectiveFrom, data.today)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!percentsValid || !dateValid) return
    const body = {
      expectedVersion: data.version,
      warnPercent: warnPercent.trim(),
      approvalPercent: approvalPercent.trim(),
      blockPercent: blockPercent.trim(),
      reason: reason.trim(),
      effectiveFrom: effectiveFromBody(effectiveFrom, data.today),
    }
    const outcome = await attempt.run(body, (idempotencyKey) =>
      recordDeliveryToleranceVersion({ companyId, idempotencyKey }, body),
    )
    if (outcome.type === 'applied') {
      onDone(savedNotice(outcome))
      return
    }
    const stale = staleRefusal(outcome)
    if (stale !== null) onStale(stale)
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="tolerance-edit-heading">
      <h3 id="tolerance-edit-heading">{POLICY_TEXT.newVersion}</h3>
      <p className={styles.muted}>{POLICY_TEXT.oneManagerNote}</p>
      <PercentField label={POLICY_TEXT.warnLabel} value={warnPercent} onChange={setWarnPercent} />
      <PercentField label={POLICY_TEXT.approvalLabel} value={approvalPercent} onChange={setApprovalPercent} />
      <PercentField label={POLICY_TEXT.blockLabel} value={blockPercent} onChange={setBlockPercent} />
      <EffectiveFromField value={effectiveFrom} today={data.today} onChange={setEffectiveFrom} />
      <ReasonField value={reason} onChange={setReason} />
      <AttemptFooter
        status={attempt.status}
        hasPendingRetry={attempt.hasPendingRetry}
        submitLabel={POLICY_TEXT.save}
        onCancel={() => onDone(null)}
      />
    </form>
  )
}

function PercentField({
  label,
  value,
  onChange,
}: {
  readonly label: string
  readonly value: string
  readonly onChange: (value: string) => void
}) {
  const invalid = value.trim().length > 0 && !isPercentText(value.trim())
  return (
    <Field label={label} help={POLICY_TEXT.percentHelp} error={invalid ? POLICY_TEXT.percentError : undefined}>
      {(control) => (
        <input
          {...control}
          type="text"
          inputMode="decimal"
          required
          autoComplete="off"
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
    </Field>
  )
}
