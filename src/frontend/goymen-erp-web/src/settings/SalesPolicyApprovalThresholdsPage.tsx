/**
 * Ayarlar > Onay tutar eşikleri — F-029 S4b, SAT-D6 (URN-D2/D3/D4; spec `URUN_F029_POLITIKA_KATALOGLARI_ENVANTER_VE_SPEC_v1`
 * §5.2). Standing on `settings.sales-approval-thresholds.manage`.
 *
 * One block per currency the server serves: the threshold timeline as sent (enabled + amount, or disabled), the one-time
 * baseline transfer while the company still runs on the compiled catalog, and afterwards a new version per currency — one
 * manager, no second approval, a required reason, a date from today on. The amount stays the lossless text the server and
 * the user write; the screen never compares a document total with it (the sales approval routing does, server-side).
 *
 * The view names a currency by its id only; the screen shows that id as given and does not map it to a code by itself.
 */

import { useState, type FormEvent, type ReactNode } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { SETTINGS_CAPABILITY } from './capabilities'
import {
  fetchApprovalThresholds,
  recordApprovalThresholdVersion,
  transferApprovalThresholdBaseline,
} from './salesPolicyApi'
import {
  SALES_POLICY_INPUT,
  inForceOf,
  isAmountText,
  type ApprovalThresholdCurrency,
  type ApprovalThresholdsView,
} from './salesPolicyContract'
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
import { POLICY_TEXT } from './salesPolicyText'
import { useCommandAttempt } from './useCommandAttempt'
import styles from './settings.module.css'

/** Room for the integer digits, the separator and the fraction digits; the server judges the value itself. */
const AMOUNT_INPUT_MAXIMUM_LENGTH = SALES_POLICY_INPUT.amountMaximumIntegerDigits + 1 + SALES_POLICY_INPUT.amountMaximumScale

export function SalesPolicyApprovalThresholdsPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <ThresholdScreen key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function ThresholdScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const ready = namespace !== null && singleCompany && hasCapability(shell, SETTINGS_CAPABILITY.salesApprovalThresholds)
  const queryKey = [namespace ?? 'no-context', shell.routeEntryGeneration, 'settings-sales-approval-thresholds']
  const view = useBoundaryRead(queryKey, ready, (signal) => fetchApprovalThresholds(companyId ?? '', signal))

  if (!singleCompany) {
    return (
      <Frame title={POLICY_TEXT.thresholdTitle} intro={POLICY_TEXT.thresholdIntro}>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={POLICY_TEXT.thresholdSubject} />
      </Frame>
    )
  }

  if (!ready || companyId === null) {
    return (
      <Frame title={POLICY_TEXT.thresholdTitle} intro={POLICY_TEXT.thresholdIntro}>
        <StateBlock state={{ kind: 'not-found' }} subject={POLICY_TEXT.thresholdSubject} />
      </Frame>
    )
  }

  const state = stateOf<ApprovalThresholdsView>(view.isPending, view.data)
  return (
    <Frame title={POLICY_TEXT.thresholdTitle} intro={POLICY_TEXT.thresholdIntro}>
      {state.kind === 'ready' ? (
        <ThresholdSection data={state.data} companyId={companyId} onChanged={() => void view.refetch()} />
      ) : (
        <StateBlock state={state} subject={POLICY_TEXT.thresholdSubject} onRetry={() => void view.refetch()} />
      )}
    </Frame>
  )
}

function ThresholdSection({
  data,
  companyId,
  onChanged,
}: {
  readonly data: ApprovalThresholdsView
  readonly companyId: string
  readonly onChanged: () => void
}) {
  const [editing, setEditing] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const close = (saved: string | null) => {
    setEditing(null)
    setNotice(saved)
    if (saved !== null) onChanged()
  }
  const stale = (text: string) => {
    setEditing(null)
    setNotice(text)
    onChanged()
  }

  return (
    <>
      {notice !== null ? (
        <p role="status" className={styles.notice}>
          {notice}
        </p>
      ) : null}
      {data.currencies.length === 0 ? <p className={styles.muted}>{POLICY_TEXT.thresholdEmpty}</p> : null}
      {data.currencies.map((currency, index) => (
        <CurrencyBlock
          key={currency.currencyId}
          headingId={`threshold-currency-${index}`}
          currency={currency}
          transferred={data.isBaselineTransferred}
          editing={editing === currency.currencyId}
          onEdit={() => {
            setNotice(null)
            setEditing(currency.currencyId)
          }}
        >
          {editing === currency.currencyId ? (
            <ThresholdForm
              currency={currency}
              today={data.today}
              companyId={companyId}
              onDone={close}
              onStale={stale}
            />
          ) : null}
        </CurrencyBlock>
      ))}
      {!data.isBaselineTransferred ? (
        <section aria-label={POLICY_TEXT.baselineHeading} className={styles.section}>
          <BaselineTransferForm
            companyId={companyId}
            headingId="threshold-baseline-heading"
            transfer={(context, body) => transferApprovalThresholdBaseline(context, body)}
            onApplied={close}
            onStale={stale}
          />
        </section>
      ) : null}
    </>
  )
}

function CurrencyBlock({
  headingId,
  currency,
  transferred,
  editing,
  onEdit,
  children,
}: {
  readonly headingId: string
  readonly currency: ApprovalThresholdCurrency
  readonly transferred: boolean
  readonly editing: boolean
  readonly onEdit: () => void
  readonly children: ReactNode
}) {
  return (
    <section aria-labelledby={headingId} className={styles.section}>
      <h2 id={headingId}>
        {POLICY_TEXT.currencyHeading} <code>{currency.currencyId}</code>
      </h2>
      {currency.versions.length === 0 ? (
        <p className={styles.muted}>{POLICY_TEXT.currencyNoVersions}</p>
      ) : (
        <table className={styles.table}>
          <caption>
            {POLICY_TEXT.currencyTableLabel} <code>{currency.currencyId}</code>
          </caption>
          <thead>
            <tr>
              <th scope="col">{POLICY_TEXT.columnVersion}</th>
              <th scope="col">{POLICY_TEXT.columnEnabled}</th>
              <th scope="col">{POLICY_TEXT.columnAmount}</th>
              <MetaHeaders />
            </tr>
          </thead>
          <tbody>
            {currency.versions.map((entry) => (
              <tr key={entry.version}>
                <td>{entry.version}</td>
                <td>{entry.isEnabled ? POLICY_TEXT.enabled : POLICY_TEXT.disabled}</td>
                <td>{entry.amount ?? POLICY_TEXT.noAmount}</td>
                <MetaCells entry={entry} />
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {transferred && !editing ? (
        <div className={styles.actions}>
          <Button type="button" variant="primary" onClick={onEdit}>
            {POLICY_TEXT.newVersion}
          </Button>
        </div>
      ) : null}
      {children}
    </section>
  )
}

function ThresholdForm({
  currency,
  today,
  companyId,
  onDone,
  onStale,
}: {
  readonly currency: ApprovalThresholdCurrency
  readonly today: string
  readonly companyId: string
  readonly onDone: (notice: string | null) => void
  readonly onStale: (notice: string) => void
}) {
  // The form starts from the version in force today (as the server marked it); with none, from "enabled" and no amount.
  const inForce = inForceOf(currency.versions)
  const [isEnabled, setIsEnabled] = useState(inForce?.isEnabled ?? true)
  const [amount, setAmount] = useState(inForce?.amount ?? '')
  const [effectiveFrom, setEffectiveFrom] = useState(today)
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const amountInvalid = isEnabled && amount.trim().length > 0 && !isAmountText(amount.trim())
  const amountValid = !isEnabled || isAmountText(amount.trim())
  const dateValid = isEffectiveFromValid(effectiveFrom, today)
  const radioName = `threshold-enabled-${currency.currencyId}`

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!amountValid || !dateValid) return
    const body = {
      expectedVersion: currency.version,
      isEnabled,
      amount: isEnabled ? amount.trim() : null,
      reason: reason.trim(),
      effectiveFrom: effectiveFromBody(effectiveFrom, today),
    }
    const outcome = await attempt.run({ currencyId: currency.currencyId, ...body }, (idempotencyKey) =>
      recordApprovalThresholdVersion({ companyId, idempotencyKey }, currency.currencyId, body),
    )
    if (outcome.type === 'applied') {
      onDone(savedNotice(outcome))
      return
    }
    const stale = staleRefusal(outcome)
    if (stale !== null) onStale(stale)
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-label={POLICY_TEXT.newVersion}>
      <p className={styles.muted}>{POLICY_TEXT.oneManagerNote}</p>
      <fieldset className={styles.fieldset}>
        <legend>{POLICY_TEXT.enabledLegend}</legend>
        <label className={styles.choice}>
          <input type="radio" name={radioName} checked={isEnabled} onChange={() => setIsEnabled(true)} />
          {POLICY_TEXT.enabledOn}
        </label>
        <label className={styles.choice}>
          <input type="radio" name={radioName} checked={!isEnabled} onChange={() => setIsEnabled(false)} />
          {POLICY_TEXT.enabledOff}
        </label>
      </fieldset>
      {isEnabled ? (
        <Field label={POLICY_TEXT.amountLabel} help={POLICY_TEXT.amountHelp} error={amountInvalid ? POLICY_TEXT.amountError : undefined}>
          {(control) => (
            <input
              {...control}
              type="text"
              inputMode="decimal"
              required
              autoComplete="off"
              maxLength={AMOUNT_INPUT_MAXIMUM_LENGTH}
              value={amount}
              onChange={(event) => setAmount(event.target.value)}
            />
          )}
        </Field>
      ) : null}
      <EffectiveFromField value={effectiveFrom} today={today} onChange={setEffectiveFrom} />
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
