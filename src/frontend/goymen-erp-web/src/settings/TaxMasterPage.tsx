/**
 * Ayarlar > Vergi — MOD-03 tax master (EK12 SAT-D83; EK13 SAT-D93; EK19 SAT-D133…D139), on the one name
 * `settings.tax-master.manage` (Muhasebe/Finans + Admin personas; granted to no role here — a human gate).
 *
 *  - The default KDV and tevkifat per item type (demir / hurda / hizmet): the reviewed SAT-D139 catalog as the base, a
 *    Settings record over it from its date; the UBL-TR exemption and withholding code lists; the VUK 232 threshold.
 *  - The screen never computes a tax: a value the server does not answer (a code, the threshold) is shown as "Kaynak yok".
 *  - Every change: one person, a mandatory reason, the expected version, one Idempotency-Key (kept for a retry after an
 *    unknown outcome). A version conflict closes the form and re-reads. A not-yet-effective record can be withdrawn.
 *  - Remounted per committed context and route entry; a group (multi-company) context shows the no-company state.
 */

import { useState, type FormEvent, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { SETTINGS_CAPABILITY } from './capabilities'
import type { CommandOutcome } from './discountLimitApi'
import {
  fetchTaxMaster,
  publishCodeList,
  recordItemTypeDefault,
  recordWithholdingThreshold,
  withdrawCodeList,
  withdrawItemTypeDefault,
  withdrawWithholdingThreshold,
} from './taxMasterApi'
import {
  TAX_INPUT,
  TAX_PROBLEM,
  isAmountText,
  isCurrencyCode,
  isListCode,
  isRatePercentText,
  normalizeTaxCode,
  parseCodeListText,
  type TaxCodeList,
  type TaxDefaultRecord,
  type TaxDefaultSeries,
  type TaxMaster,
  type TaxThreshold,
  type TaxThresholdRecord,
} from './taxMasterContract'
import { CODE_LIST_LABEL, ITEM_TYPE_LABEL, TAX_TEXT, formatDate, taxRefusalText } from './taxMasterText'
import { useCommandAttempt, type AttemptStatus } from './useCommandAttempt'
import styles from './settings.module.css'

export function TaxMasterPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <TaxScreen key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function TaxScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const ready = namespace !== null && singleCompany && hasCapability(shell, SETTINGS_CAPABILITY.taxMaster)
  const master = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'settings-tax-master'],
    enabled: ready,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchTaxMaster(companyId ?? '', signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  if (!singleCompany) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={TAX_TEXT.contextSubject} />
      </Frame>
    )
  }

  if (!ready || companyId === null) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'not-found' }} subject={TAX_TEXT.contextSubject} />
      </Frame>
    )
  }

  const state = (master.isPending
    ? { kind: 'initial-loading' }
    : classifyOutcome(master.data ?? ({ type: 'malformed' } as ViewOutcome<TaxMaster>))) as RemoteViewState<TaxMaster>
  return (
    <Frame>
      {state.kind === 'ready' ? (
        <MasterView master={state.data} companyId={companyId} onChanged={() => void master.refetch()} />
      ) : (
        <StateBlock state={state} subject={TAX_TEXT.masterSubject} onRetry={() => void master.refetch()} />
      )}
    </Frame>
  )
}

function Frame({ children }: { readonly children: ReactNode }) {
  return (
    <div className={styles.page}>
      <h1>{TAX_TEXT.title}</h1>
      <p className={styles.muted}>{TAX_TEXT.intro}</p>
      {children}
    </div>
  )
}

type Editor =
  | { readonly kind: 'none' }
  | { readonly kind: 'default'; readonly series: TaxDefaultSeries }
  | { readonly kind: 'withdraw-default'; readonly series: TaxDefaultSeries; readonly record: TaxDefaultRecord }
  | { readonly kind: 'list'; readonly list: TaxCodeList }
  | { readonly kind: 'withdraw-list'; readonly list: TaxCodeList; readonly listVersion: string }
  | { readonly kind: 'threshold'; readonly threshold: TaxThreshold }
  | { readonly kind: 'withdraw-threshold'; readonly threshold: TaxThreshold; readonly record: TaxThresholdRecord }

function MasterView({
  master,
  companyId,
  onChanged,
}: {
  readonly master: TaxMaster
  readonly companyId: string
  readonly onChanged: () => void
}) {
  const [editor, setEditor] = useState<Editor>({ kind: 'none' })
  const [notice, setNotice] = useState<string | null>(null)
  const close = (saved: string | null) => {
    setEditor({ kind: 'none' })
    setNotice(saved)
    if (saved !== null) onChanged()
  }
  const conflict = () => {
    setEditor({ kind: 'none' })
    setNotice(taxRefusalText(409, TAX_PROBLEM.versionConflict))
    onChanged()
  }
  const formProps = { companyId, onDone: close, onConflict: conflict }

  return (
    <>
      <p className={styles.muted}>{TAX_TEXT.businessDate(master.businessDate)}</p>
      {notice !== null ? (
        <p role="status" className={styles.notice}>
          {notice}
        </p>
      ) : null}
      <section aria-labelledby="tax-defaults-heading" className={styles.section}>
        <h2 id="tax-defaults-heading">{TAX_TEXT.defaultsHeading}</h2>
        <table aria-label={TAX_TEXT.defaultsTableLabel} className={styles.table}>
          <thead>
            <tr>
              <th scope="col">{TAX_TEXT.columnItemType}</th>
              <th scope="col">{TAX_TEXT.columnVat}</th>
              <th scope="col">{TAX_TEXT.columnWithholding}</th>
              <th scope="col">{TAX_TEXT.columnSource}</th>
              <th scope="col">{TAX_TEXT.columnScheduled}</th>
              <th scope="col">{TAX_TEXT.columnActions}</th>
            </tr>
          </thead>
          <tbody>
            {master.itemTypeDefaults.map((series) => {
              const label = ITEM_TYPE_LABEL[series.itemType]
              const record = series.inForce
              return (
                <tr key={series.itemType}>
                  <th scope="row">{label}</th>
                  <td>{record === null ? TAX_TEXT.noSource : vatLabel(record)}</td>
                  <td>{record === null ? TAX_TEXT.noSource : withholdingLabel(record)}</td>
                  <td>{record === null ? TAX_TEXT.noSource : sourceLabel(record)}</td>
                  <td>
                    {series.scheduled.length === 0 ? (
                      TAX_TEXT.noScheduled
                    ) : (
                      <ul aria-label={`${TAX_TEXT.columnScheduled} — ${label}`} className={styles.plainList}>
                        {series.scheduled.map((scheduled) => (
                          <li key={scheduled.recordId}>
                            {TAX_TEXT.scheduledLabel(scheduled.effectiveFrom, `${vatLabel(scheduled)}, ${withholdingLabel(scheduled)}`)}{' '}
                            {scheduled.source === 'Record' ? (
                              <Button
                                type="button"
                                size="sm"
                                variant="danger"
                                aria-label={`${TAX_TEXT.withdraw} — ${label} ${formatDate(scheduled.effectiveFrom)}`}
                                onClick={() => setEditor({ kind: 'withdraw-default', series, record: scheduled })}
                              >
                                {TAX_TEXT.withdraw}
                              </Button>
                            ) : null}
                          </li>
                        ))}
                      </ul>
                    )}
                  </td>
                  <td>
                    <Button
                      type="button"
                      size="sm"
                      aria-label={`${TAX_TEXT.changeDefault} — ${label}`}
                      onClick={() => setEditor({ kind: 'default', series })}
                    >
                      {TAX_TEXT.changeDefault}
                    </Button>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </section>

      <section aria-labelledby="tax-threshold-heading" className={styles.section}>
        <h2 id="tax-threshold-heading">{TAX_TEXT.thresholdHeading}</h2>
        <p className={styles.muted}>{TAX_TEXT.thresholdIntro}</p>
        <p aria-label={`${TAX_TEXT.thresholdHeading} — ${TAX_TEXT.columnInForce}`}>
          {master.withholdingThreshold.inForce === null
            ? TAX_TEXT.noSource
            : TAX_TEXT.thresholdLabelOf(master.withholdingThreshold.inForce.amount, master.withholdingThreshold.inForce.currencyCode)}
        </p>
        {master.withholdingThreshold.scheduled.length > 0 ? (
          <ul aria-label={`${TAX_TEXT.thresholdHeading} — ${TAX_TEXT.columnScheduled}`} className={styles.plainList}>
            {master.withholdingThreshold.scheduled.map((record) => (
              <li key={record.recordId}>
                {TAX_TEXT.scheduledLabel(record.effectiveFrom, TAX_TEXT.thresholdLabelOf(record.amount, record.currencyCode))}{' '}
                <Button
                  type="button"
                  size="sm"
                  variant="danger"
                  aria-label={`${TAX_TEXT.withdraw} — ${TAX_TEXT.thresholdHeading} ${formatDate(record.effectiveFrom)}`}
                  onClick={() => setEditor({ kind: 'withdraw-threshold', threshold: master.withholdingThreshold, record })}
                >
                  {TAX_TEXT.withdraw}
                </Button>
              </li>
            ))}
          </ul>
        ) : null}
        <Button
          type="button"
          size="sm"
          aria-label={`${TAX_TEXT.recordThreshold} — ${TAX_TEXT.thresholdHeading}`}
          onClick={() => setEditor({ kind: 'threshold', threshold: master.withholdingThreshold })}
        >
          {TAX_TEXT.recordThreshold}
        </Button>
      </section>

      <section aria-labelledby="tax-lists-heading" className={styles.section}>
        <h2 id="tax-lists-heading">{TAX_TEXT.codeListsHeading}</h2>
        {master.codeLists.map((list) => (
          <div key={list.kind} aria-label={CODE_LIST_LABEL[list.kind]} role="group">
            <h3>{CODE_LIST_LABEL[list.kind]}</h3>
            {list.inForce === null ? (
              <p>{TAX_TEXT.noSource}</p>
            ) : (
              <ul aria-label={`${CODE_LIST_LABEL[list.kind]} — ${TAX_TEXT.columnInForce}`} className={styles.plainList}>
                {list.inForce.entries.map((entry) => (
                  <li key={entry.code}>
                    <code>{entry.code}</code> — {entry.name}
                  </li>
                ))}
              </ul>
            )}
            {list.scheduled.length > 0 ? (
              <ul aria-label={`${CODE_LIST_LABEL[list.kind]} — ${TAX_TEXT.columnScheduled}`} className={styles.plainList}>
                {list.scheduled.map((version) => (
                  <li key={version.listVersion}>
                    {TAX_TEXT.scheduledLabel(version.effectiveFrom, TAX_TEXT.entryCount(version.entries.length))}{' '}
                    <Button
                      type="button"
                      size="sm"
                      variant="danger"
                      aria-label={`${TAX_TEXT.withdraw} — ${CODE_LIST_LABEL[list.kind]} ${formatDate(version.effectiveFrom)}`}
                      onClick={() => setEditor({ kind: 'withdraw-list', list, listVersion: version.listVersion })}
                    >
                      {TAX_TEXT.withdraw}
                    </Button>
                  </li>
                ))}
              </ul>
            ) : null}
            <Button
              type="button"
              size="sm"
              aria-label={`${TAX_TEXT.publishList} — ${CODE_LIST_LABEL[list.kind]}`}
              onClick={() => setEditor({ kind: 'list', list })}
            >
              {TAX_TEXT.publishList}
            </Button>
          </div>
        ))}
      </section>

      {editor.kind === 'default' ? <DefaultForm key={editor.series.itemType} {...formProps} series={editor.series} /> : null}
      {editor.kind === 'withdraw-default' ? (
        <WithdrawForm
          key={editor.record.recordId}
          {...formProps}
          heading={`${TAX_TEXT.withdraw} — ${ITEM_TYPE_LABEL[editor.series.itemType]} ${formatDate(editor.record.effectiveFrom)}`}
          execute={(context, reason) =>
            withdrawItemTypeDefault(context, editor.record.recordId, { expectedVersion: editor.series.version, reason })
          }
          fingerprint={{ record: editor.record.recordId, version: editor.series.version }}
        />
      ) : null}
      {editor.kind === 'threshold' ? <ThresholdForm {...formProps} threshold={editor.threshold} /> : null}
      {editor.kind === 'withdraw-threshold' ? (
        <WithdrawForm
          key={editor.record.recordId}
          {...formProps}
          heading={`${TAX_TEXT.withdraw} — ${TAX_TEXT.thresholdHeading} ${formatDate(editor.record.effectiveFrom)}`}
          execute={(context, reason) =>
            withdrawWithholdingThreshold(context, editor.record.recordId, { expectedVersion: editor.threshold.version, reason })
          }
          fingerprint={{ threshold: editor.record.recordId, version: editor.threshold.version }}
        />
      ) : null}
      {editor.kind === 'list' ? <CodeListForm key={editor.list.kind} {...formProps} list={editor.list} /> : null}
      {editor.kind === 'withdraw-list' ? (
        <WithdrawForm
          key={`${editor.list.kind}:${editor.listVersion}`}
          {...formProps}
          heading={`${TAX_TEXT.withdraw} — ${CODE_LIST_LABEL[editor.list.kind]}`}
          execute={(context, reason) =>
            withdrawCodeList(context, editor.list.kind, editor.listVersion, { expectedVersion: editor.list.version, reason })
          }
          fingerprint={{ list: editor.list.kind, listVersion: editor.listVersion, version: editor.list.version }}
        />
      ) : null}
    </>
  )
}

function vatLabel(record: TaxDefaultRecord): string {
  return record.vatApplies ? TAX_TEXT.rateLabelOf(record.vatPercent ?? '') : TAX_TEXT.vatNone
}

function withholdingLabel(record: TaxDefaultRecord): string {
  if (!record.withholdingApplies) return TAX_TEXT.withholdingNone
  const code = record.withholdingCode === null ? TAX_TEXT.codeMissing : TAX_TEXT.codeOf(record.withholdingCode)
  return `${TAX_TEXT.rateLabelOf(record.withholdingPercent ?? '')} (${code})`
}

function sourceLabel(record: TaxDefaultRecord): string {
  return record.source === 'Catalog' ? TAX_TEXT.sourceCatalog : TAX_TEXT.sourceRecord
}

interface FormProps {
  readonly companyId: string
  readonly onDone: (notice: string | null) => void
  readonly onConflict: () => void
}

function savedNotice<T>(outcome: CommandOutcome<T>): string | null {
  if (outcome.type !== 'applied') return null
  return outcome.replayed ? TAX_TEXT.savedReplay : TAX_TEXT.saved
}

function finish<T>(outcome: CommandOutcome<T>, props: FormProps) {
  if (outcome.type === 'applied') props.onDone(savedNotice(outcome))
  else if (outcome.type === 'refused' && outcome.code === TAX_PROBLEM.versionConflict) props.onConflict()
}

function Footer<T>({
  status,
  hasPendingRetry,
  onCancel,
}: {
  readonly status: AttemptStatus<T>
  readonly hasPendingRetry: boolean
  readonly onCancel: () => void
}) {
  return (
    <>
      {status.kind === 'refused' ? (
        <p role="alert" className={styles.error}>
          {taxRefusalText(status.status, status.code)}
        </p>
      ) : null}
      {status.kind === 'unknown' ? (
        <p role="alert" className={styles.error}>
          {TAX_TEXT.unknownOutcome}
        </p>
      ) : null}
      <div className={styles.actions}>
        <Button type="submit" variant="primary" disabled={status.kind === 'busy'} aria-busy={status.kind === 'busy'}>
          {status.kind === 'busy' ? TAX_TEXT.saving : hasPendingRetry ? TAX_TEXT.retrySame : TAX_TEXT.save}
        </Button>
        <Button type="button" variant="ghost" onClick={onCancel} disabled={status.kind === 'busy'}>
          {TAX_TEXT.cancel}
        </Button>
      </div>
    </>
  )
}

function ReasonField({ value, onChange }: { readonly value: string; readonly onChange: (value: string) => void }) {
  return (
    <Field label={TAX_TEXT.reasonLabel} help={TAX_TEXT.reasonHelp}>
      {(control) => (
        <textarea
          {...control}
          required
          rows={3}
          maxLength={TAX_INPUT.reasonMaximumLength}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
    </Field>
  )
}

function EffectiveFromField({ value, onChange }: { readonly value: string; readonly onChange: (value: string) => void }) {
  return (
    <Field label={TAX_TEXT.effectiveFromLabel} help={TAX_TEXT.effectiveFromHelp}>
      {(control) => <input {...control} type="date" value={value} onChange={(event) => onChange(event.target.value)} />}
    </Field>
  )
}

function DefaultForm(props: FormProps & { readonly series: TaxDefaultSeries }) {
  const current = props.series.inForce
  const [vatApplies, setVatApplies] = useState(current?.vatApplies ?? true)
  const [vatPercent, setVatPercent] = useState(current?.vatPercent ?? '')
  const [exemptionCode, setExemptionCode] = useState(current?.exemptionCode ?? '')
  const [withholdingApplies, setWithholdingApplies] = useState(current?.withholdingApplies ?? false)
  const [withholdingPercent, setWithholdingPercent] = useState(current?.withholdingPercent ?? '')
  const [withholdingCode, setWithholdingCode] = useState(current?.withholdingCode ?? '')
  const [effectiveFrom, setEffectiveFrom] = useState('')
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const vatValid = !vatApplies || isRatePercentText(vatPercent)
  const withholdingValid = !withholdingApplies || (isRatePercentText(withholdingPercent) && Number(withholdingPercent) > 0)
  const normalizedExemption = normalizeTaxCode(exemptionCode)
  const normalizedWithholding = normalizeTaxCode(withholdingCode)
  const codesValid = (normalizedExemption.length === 0 || isListCode(normalizedExemption))
    && (normalizedWithholding.length === 0 || isListCode(normalizedWithholding))

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!vatValid || !withholdingValid || !codesValid) return
    const applyWithholding = vatApplies && withholdingApplies
    const body = {
      expectedVersion: props.series.version,
      vatApplies,
      vatPercent: vatApplies ? vatPercent : null,
      exemptionCode: !vatApplies && normalizedExemption.length > 0 ? normalizedExemption : null,
      withholdingApplies: applyWithholding,
      withholdingPercent: applyWithholding ? withholdingPercent : null,
      withholdingCode: applyWithholding && normalizedWithholding.length > 0 ? normalizedWithholding : null,
      reason: reason.trim(),
      ...(effectiveFrom.length > 0 ? { effectiveFrom } : {}),
    }
    finish(
      await attempt.run({ itemType: props.series.itemType, ...body }, (idempotencyKey) =>
        recordItemTypeDefault({ companyId: props.companyId, idempotencyKey }, props.series.itemType, body),
      ),
      props,
    )
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="tax-default-form-heading">
      <h3 id="tax-default-form-heading">
        {TAX_TEXT.changeDefault} — {ITEM_TYPE_LABEL[props.series.itemType]}
      </h3>
      <fieldset>
        <legend>{TAX_TEXT.columnVat}</legend>
        <label>
          <input type="radio" name="tax-vat" checked={vatApplies} onChange={() => setVatApplies(true)} /> {TAX_TEXT.vatAppliesLabel}
        </label>
        <label>
          <input type="radio" name="tax-vat" checked={!vatApplies} onChange={() => setVatApplies(false)} /> {TAX_TEXT.vatNotAppliedLabel}
        </label>
      </fieldset>
      {vatApplies ? (
        <Field label={TAX_TEXT.vatRateLabel} help={TAX_TEXT.rateHelp} error={vatPercent.length > 0 && !vatValid ? TAX_TEXT.rateHelp : undefined}>
          {(control) => (
            <input {...control} type="text" inputMode="decimal" required value={vatPercent} onChange={(event) => setVatPercent(event.target.value.trim())} />
          )}
        </Field>
      ) : (
        <Field label={TAX_TEXT.exemptionCodeLabel} help={TAX_TEXT.exemptionCodeHelp}>
          {(control) => (
            <input {...control} type="text" autoComplete="off" maxLength={TAX_INPUT.listCodeMaximumLength} value={exemptionCode} onChange={(event) => setExemptionCode(event.target.value)} />
          )}
        </Field>
      )}
      {vatApplies ? (
        <>
          <label>
            <input type="checkbox" checked={withholdingApplies} onChange={(event) => setWithholdingApplies(event.target.checked)} />{' '}
            {TAX_TEXT.withholdingAppliesLabel}
          </label>
          {withholdingApplies ? (
            <>
              <Field
                label={TAX_TEXT.withholdingRateLabel}
                help={TAX_TEXT.withholdingRateHelp}
                error={withholdingPercent.length > 0 && !withholdingValid ? TAX_TEXT.withholdingRateHelp : undefined}
              >
                {(control) => (
                  <input {...control} type="text" inputMode="decimal" required value={withholdingPercent} onChange={(event) => setWithholdingPercent(event.target.value.trim())} />
                )}
              </Field>
              <Field label={TAX_TEXT.withholdingCodeLabel} help={TAX_TEXT.withholdingCodeHelp}>
                {(control) => (
                  <input {...control} type="text" autoComplete="off" maxLength={TAX_INPUT.listCodeMaximumLength} value={withholdingCode} onChange={(event) => setWithholdingCode(event.target.value)} />
                )}
              </Field>
            </>
          ) : null}
        </>
      ) : null}
      <EffectiveFromField value={effectiveFrom} onChange={setEffectiveFrom} />
      <ReasonField value={reason} onChange={setReason} />
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => props.onDone(null)} />
    </form>
  )
}

function ThresholdForm(props: FormProps & { readonly threshold: TaxThreshold }) {
  const [amount, setAmount] = useState('')
  const [currency, setCurrency] = useState('')
  const [effectiveFrom, setEffectiveFrom] = useState('')
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const currencyCode = normalizeTaxCode(currency)
  const amountError = amount.length > 0 && !isAmountText(amount) ? TAX_TEXT.amountHelp : undefined
  const currencyError = currency.trim().length > 0 && !isCurrencyCode(currencyCode) ? TAX_TEXT.currencyHelp : undefined

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!isAmountText(amount) || !isCurrencyCode(currencyCode)) return
    const body = {
      expectedVersion: props.threshold.version,
      amount,
      currencyCode,
      reason: reason.trim(),
      ...(effectiveFrom.length > 0 ? { effectiveFrom } : {}),
    }
    finish(
      await attempt.run(body, (idempotencyKey) => recordWithholdingThreshold({ companyId: props.companyId, idempotencyKey }, body)),
      props,
    )
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="tax-threshold-form-heading">
      <h3 id="tax-threshold-form-heading">{TAX_TEXT.recordThreshold} — {TAX_TEXT.thresholdHeading}</h3>
      <Field label={TAX_TEXT.amountLabel} help={TAX_TEXT.amountHelp} error={amountError}>
        {(control) => (
          <input {...control} type="text" inputMode="decimal" required value={amount} onChange={(event) => setAmount(event.target.value.trim())} />
        )}
      </Field>
      <Field label={TAX_TEXT.currencyLabel} help={TAX_TEXT.currencyHelp} error={currencyError}>
        {(control) => (
          <input {...control} type="text" required maxLength={3} autoComplete="off" value={currency} onChange={(event) => setCurrency(event.target.value)} />
        )}
      </Field>
      <EffectiveFromField value={effectiveFrom} onChange={setEffectiveFrom} />
      <ReasonField value={reason} onChange={setReason} />
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => props.onDone(null)} />
    </form>
  )
}

function CodeListForm(props: FormProps & { readonly list: TaxCodeList }) {
  const [text, setText] = useState(
    props.list.inForce === null ? '' : props.list.inForce.entries.map((entry) => `${entry.code};${entry.name}`).join('\n'),
  )
  const [effectiveFrom, setEffectiveFrom] = useState('')
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const entries = parseCodeListText(text)
  const entriesError = text.trim().length > 0 && entries === null ? TAX_TEXT.entriesInvalid : undefined

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (entries === null) return
    const body = {
      expectedVersion: props.list.version,
      entries,
      reason: reason.trim(),
      ...(effectiveFrom.length > 0 ? { effectiveFrom } : {}),
    }
    finish(
      await attempt.run({ kind: props.list.kind, ...body }, (idempotencyKey) =>
        publishCodeList({ companyId: props.companyId, idempotencyKey }, props.list.kind, body),
      ),
      props,
    )
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="tax-list-form-heading">
      <h3 id="tax-list-form-heading">
        {TAX_TEXT.publishList} — {CODE_LIST_LABEL[props.list.kind]}
      </h3>
      <Field label={TAX_TEXT.entriesLabel} help={TAX_TEXT.entriesHelp} error={entriesError}>
        {(control) => <textarea {...control} required rows={6} value={text} onChange={(event) => setText(event.target.value)} />}
      </Field>
      <EffectiveFromField value={effectiveFrom} onChange={setEffectiveFrom} />
      <ReasonField value={reason} onChange={setReason} />
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => props.onDone(null)} />
    </form>
  )
}

function WithdrawForm(
  props: FormProps & {
    readonly heading: string
    readonly fingerprint: object
    readonly execute: (
      context: { readonly companyId: string; readonly idempotencyKey: string },
      reason: string,
    ) => Promise<CommandOutcome<true>>
  },
) {
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()

  async function submit(event: FormEvent) {
    event.preventDefault()
    const trimmed = reason.trim()
    finish(
      await attempt.run({ ...props.fingerprint, reason: trimmed }, (idempotencyKey) =>
        props.execute({ companyId: props.companyId, idempotencyKey }, trimmed),
      ),
      props,
    )
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="tax-withdraw-heading">
      <h3 id="tax-withdraw-heading">{props.heading}</h3>
      <ReasonField value={reason} onChange={setReason} />
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => props.onDone(null)} />
    </form>
  )
}
