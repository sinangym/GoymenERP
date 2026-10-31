/**
 * Ayarlar > Vergi — MOD-03 tax master (SAT-D73/D74/D82/D83, EK12; SAT-D93/D110, EK13), on the one name
 * `settings.tax-master.manage` (Muhasebe/Finans + Admin personas; granted to no role here — a human gate).
 *
 *  - VAT classes and their dated rates; withholding classes × buyer categories with their dated withholding (or an explicit
 *    "not applied"); the UBL-TR exemption and withholding code lists as dated versions.
 *  - Nothing is seeded and the screen never computes a tax: a value the server does not answer is shown as "Kaynak yok".
 *  - Every change: one person, a mandatory reason, the expected version, one Idempotency-Key (kept for a retry after an
 *    unknown outcome). A version conflict closes the form and re-reads. A not-yet-effective record can be withdrawn.
 *  - Remounted per committed context and route entry; a group (multi-company) context shows the no-company state.
 */

import { useMemo, useState, type FormEvent, type ReactNode } from 'react'
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
  createTaxCategory,
  fetchTaxMaster,
  publishCodeList,
  recordVatRate,
  recordWithholdingRate,
  withdrawCodeList,
  withdrawRateRecord,
} from './taxMasterApi'
import {
  CATEGORY_KINDS,
  TAX_INPUT,
  TAX_PROBLEM,
  isCategoryCode,
  isListCode,
  isRatePercentText,
  normalizeTaxCode,
  parseCodeListText,
  type CategoryKind,
  type TaxCategory,
  type TaxCodeList,
  type TaxMaster,
  type TaxRateRecord,
  type TaxRateSeries,
} from './taxMasterContract'
import { CATEGORY_KIND_LABEL, CODE_LIST_LABEL, TAX_TEXT, formatDate, taxRefusalText } from './taxMasterText'
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
  | { readonly kind: 'category' }
  | { readonly kind: 'vat'; readonly vatClass: TaxCategory }
  | { readonly kind: 'withholding'; readonly withholdingClass: TaxCategory; readonly buyer: TaxCategory }
  | { readonly kind: 'withdraw-rate'; readonly owner: TaxCategory; readonly record: TaxRateRecord }
  | { readonly kind: 'list'; readonly list: TaxCodeList }
  | { readonly kind: 'withdraw-list'; readonly list: TaxCodeList; readonly listVersion: string }

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
  const byKind = useMemo(() => {
    const groups: Record<CategoryKind, TaxCategory[]> = { VatClass: [], WithholdingClass: [], BuyerCategory: [] }
    for (const category of master.categories) groups[category.kind].push(category)
    return groups
  }, [master.categories])
  const series = (kind: 'Vat' | 'Withholding', classId: string, buyerId: string | null): TaxRateSeries | undefined =>
    master.rateSeries.find((item) => item.kind === kind && item.classId === classId && item.buyerCategoryId === buyerId)
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
      {master.categories.length === 0 ? <p className={styles.muted}>{TAX_TEXT.emptyMaster}</p> : null}

      <section aria-labelledby="tax-vat-heading" className={styles.section}>
        <h2 id="tax-vat-heading">{TAX_TEXT.vatHeading}</h2>
        {byKind.VatClass.length === 0 ? (
          <p className={styles.muted}>{TAX_TEXT.noCategoriesOfKind}</p>
        ) : (
          <table aria-label={TAX_TEXT.vatTableLabel} className={styles.table}>
            <thead>
              <tr>
                <th scope="col">{TAX_TEXT.columnCode}</th>
                <th scope="col">{TAX_TEXT.columnName}</th>
                <th scope="col">{TAX_TEXT.columnInForce}</th>
                <th scope="col">{TAX_TEXT.columnScheduled}</th>
                <th scope="col">{TAX_TEXT.columnActions}</th>
              </tr>
            </thead>
            <tbody>
              {byKind.VatClass.map((vatClass) => {
                const rates = series('Vat', vatClass.id, null)
                return (
                  <tr key={vatClass.id}>
                    <td>
                      <code>{vatClass.code}</code>
                    </td>
                    <td>{vatClass.name}</td>
                    <td>{rates?.inForce ? recordLabel(rates.inForce) : TAX_TEXT.noSource}</td>
                    <td>
                      <ScheduledList
                        label={vatClass.code}
                        records={rates?.scheduled ?? []}
                        onWithdraw={(record) => setEditor({ kind: 'withdraw-rate', owner: vatClass, record })}
                      />
                    </td>
                    <td>
                      <Button
                        type="button"
                        size="sm"
                        aria-label={`${TAX_TEXT.recordRate} — ${vatClass.code}`}
                        onClick={() => setEditor({ kind: 'vat', vatClass })}
                      >
                        {TAX_TEXT.recordRate}
                      </Button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}
      </section>

      <section aria-labelledby="tax-withholding-heading" className={styles.section}>
        <h2 id="tax-withholding-heading">{TAX_TEXT.withholdingHeading}</h2>
        {byKind.WithholdingClass.length === 0 || byKind.BuyerCategory.length === 0 ? (
          <p className={styles.muted}>{TAX_TEXT.withholdingNeedsCategories}</p>
        ) : (
          <table aria-label={TAX_TEXT.withholdingTableLabel} className={styles.table}>
            <thead>
              <tr>
                <th scope="col">{TAX_TEXT.columnClass}</th>
                <th scope="col">{TAX_TEXT.columnBuyer}</th>
                <th scope="col">{TAX_TEXT.columnInForce}</th>
                <th scope="col">{TAX_TEXT.columnScheduled}</th>
                <th scope="col">{TAX_TEXT.columnActions}</th>
              </tr>
            </thead>
            <tbody>
              {byKind.WithholdingClass.flatMap((withholdingClass) =>
                byKind.BuyerCategory.map((buyer) => {
                  const rates = series('Withholding', withholdingClass.id, buyer.id)
                  const label = `${withholdingClass.code} / ${buyer.code}`
                  return (
                    <tr key={`${withholdingClass.id}:${buyer.id}`}>
                      <td>
                        <code>{withholdingClass.code}</code>
                      </td>
                      <td>
                        <code>{buyer.code}</code>
                      </td>
                      <td>{rates?.inForce ? recordLabel(rates.inForce) : TAX_TEXT.noSource}</td>
                      <td>
                        <ScheduledList
                          label={label}
                          records={rates?.scheduled ?? []}
                          onWithdraw={(record) => setEditor({ kind: 'withdraw-rate', owner: withholdingClass, record })}
                        />
                      </td>
                      <td>
                        <Button
                          type="button"
                          size="sm"
                          aria-label={`${TAX_TEXT.recordWithholding} — ${label}`}
                          onClick={() => setEditor({ kind: 'withholding', withholdingClass, buyer })}
                        >
                          {TAX_TEXT.recordWithholding}
                        </Button>
                      </td>
                    </tr>
                  )
                }),
              )}
            </tbody>
          </table>
        )}
      </section>

      <section aria-labelledby="tax-buyer-heading" className={styles.section}>
        <h2 id="tax-buyer-heading">{TAX_TEXT.buyerHeading}</h2>
        {byKind.BuyerCategory.length === 0 ? (
          <p className={styles.muted}>{TAX_TEXT.noCategoriesOfKind}</p>
        ) : (
          <ul className={styles.plainList}>
            {byKind.BuyerCategory.map((buyer) => (
              <li key={buyer.id}>
                <code>{buyer.code}</code> — {buyer.name}
              </li>
            ))}
          </ul>
        )}
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

      {editor.kind === 'none' ? (
        <Button type="button" variant="primary" onClick={() => setEditor({ kind: 'category' })}>
          {TAX_TEXT.newCategory}
        </Button>
      ) : null}
      {editor.kind === 'category' ? <CategoryForm {...formProps} /> : null}
      {editor.kind === 'vat' ? <VatRateForm key={editor.vatClass.id} {...formProps} vatClass={editor.vatClass} /> : null}
      {editor.kind === 'withholding' ? (
        <WithholdingForm
          key={`${editor.withholdingClass.id}:${editor.buyer.id}`}
          {...formProps}
          withholdingClass={editor.withholdingClass}
          buyer={editor.buyer}
        />
      ) : null}
      {editor.kind === 'withdraw-rate' ? (
        <WithdrawForm
          key={editor.record.recordId}
          {...formProps}
          heading={`${TAX_TEXT.withdraw} — ${editor.owner.code} ${formatDate(editor.record.effectiveFrom)}`}
          execute={(context, reason) =>
            withdrawRateRecord(context, editor.record.recordId, { expectedVersion: editor.owner.version, reason })
          }
          fingerprint={{ record: editor.record.recordId, version: editor.owner.version }}
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

function recordLabel(record: TaxRateRecord): string {
  if (!record.applies) return TAX_TEXT.notApplied
  return record.withholdingCode === null
    ? TAX_TEXT.rateLabelOf(record.ratePercent ?? '')
    : TAX_TEXT.withholdingLabelOf(record.ratePercent ?? '', record.withholdingCode)
}

function ScheduledList({
  label,
  records,
  onWithdraw,
}: {
  readonly label: string
  readonly records: readonly TaxRateRecord[]
  readonly onWithdraw: (record: TaxRateRecord) => void
}) {
  if (records.length === 0) return <>{TAX_TEXT.noScheduled}</>
  return (
    <ul aria-label={`${TAX_TEXT.columnScheduled} — ${label}`} className={styles.plainList}>
      {records.map((record) => (
        <li key={record.recordId}>
          {TAX_TEXT.scheduledLabel(record.effectiveFrom, recordLabel(record))}{' '}
          <Button
            type="button"
            size="sm"
            variant="danger"
            aria-label={`${TAX_TEXT.withdraw} — ${label} ${formatDate(record.effectiveFrom)}`}
            onClick={() => onWithdraw(record)}
          >
            {TAX_TEXT.withdraw}
          </Button>
        </li>
      ))}
    </ul>
  )
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

function CategoryForm(props: FormProps) {
  const [kind, setKind] = useState<CategoryKind>('VatClass')
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<{ readonly id: string; readonly version: string }>()
  const normalized = normalizeTaxCode(code)
  const codeError = code.trim().length > 0 && !isCategoryCode(normalized) ? TAX_TEXT.codeHelp : undefined

  async function submit(event: FormEvent) {
    event.preventDefault()
    const body = { kind, code: normalized, name: name.trim(), reason: reason.trim() }
    finish(await attempt.run(body, (idempotencyKey) => createTaxCategory({ companyId: props.companyId, idempotencyKey }, body)), props)
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="tax-category-heading">
      <h3 id="tax-category-heading">{TAX_TEXT.newCategory}</h3>
      <Field label={TAX_TEXT.kindLabel}>
        {(control) => (
          <select {...control} value={kind} onChange={(event) => setKind(event.target.value as CategoryKind)}>
            {CATEGORY_KINDS.map((option) => (
              <option key={option} value={option}>
                {CATEGORY_KIND_LABEL[option]}
              </option>
            ))}
          </select>
        )}
      </Field>
      <Field label={TAX_TEXT.codeLabel} help={TAX_TEXT.codeHelp} error={codeError}>
        {(control) => (
          <input
            {...control}
            type="text"
            required
            autoComplete="off"
            maxLength={TAX_INPUT.categoryCodeMaximumLength}
            value={code}
            onChange={(event) => setCode(event.target.value)}
          />
        )}
      </Field>
      <Field label={TAX_TEXT.nameLabel}>
        {(control) => (
          <input
            {...control}
            type="text"
            required
            maxLength={TAX_INPUT.categoryNameMaximumLength}
            value={name}
            onChange={(event) => setName(event.target.value)}
          />
        )}
      </Field>
      <ReasonField value={reason} onChange={setReason} />
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => props.onDone(null)} />
    </form>
  )
}

function VatRateForm(props: FormProps & { readonly vatClass: TaxCategory }) {
  const [rate, setRate] = useState('')
  const [effectiveFrom, setEffectiveFrom] = useState('')
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const rateError = rate.length > 0 && !isRatePercentText(rate) ? TAX_TEXT.rateHelp : undefined

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!isRatePercentText(rate)) return
    const body = {
      expectedVersion: props.vatClass.version,
      ratePercent: rate,
      reason: reason.trim(),
      ...(effectiveFrom.length > 0 ? { effectiveFrom } : {}),
    }
    finish(
      await attempt.run({ classId: props.vatClass.id, ...body }, (idempotencyKey) =>
        recordVatRate({ companyId: props.companyId, idempotencyKey }, props.vatClass.id, body),
      ),
      props,
    )
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="tax-vat-form-heading">
      <h3 id="tax-vat-form-heading">
        {TAX_TEXT.recordRate} — <code>{props.vatClass.code}</code>
      </h3>
      <Field label={TAX_TEXT.rateLabel} help={TAX_TEXT.rateHelp} error={rateError}>
        {(control) => (
          <input {...control} type="text" inputMode="decimal" required value={rate} onChange={(event) => setRate(event.target.value.trim())} />
        )}
      </Field>
      <EffectiveFromField value={effectiveFrom} onChange={setEffectiveFrom} />
      <ReasonField value={reason} onChange={setReason} />
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => props.onDone(null)} />
    </form>
  )
}

function WithholdingForm(props: FormProps & { readonly withholdingClass: TaxCategory; readonly buyer: TaxCategory }) {
  const [applies, setApplies] = useState(true)
  const [rate, setRate] = useState('')
  const [code, setCode] = useState('')
  const [effectiveFrom, setEffectiveFrom] = useState('')
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const normalizedCode = normalizeTaxCode(code)
  const rateValid = isRatePercentText(rate) && Number(rate) > 0
  const rateError = applies && rate.length > 0 && !rateValid ? TAX_TEXT.withholdingRateHelp : undefined

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (applies && (!rateValid || !isListCode(normalizedCode))) return
    const body = {
      expectedVersion: props.withholdingClass.version,
      buyerCategoryId: props.buyer.id,
      applies,
      ratePercent: applies ? rate : null,
      withholdingCode: applies ? normalizedCode : null,
      reason: reason.trim(),
      ...(effectiveFrom.length > 0 ? { effectiveFrom } : {}),
    }
    finish(
      await attempt.run({ classId: props.withholdingClass.id, ...body }, (idempotencyKey) =>
        recordWithholdingRate({ companyId: props.companyId, idempotencyKey }, props.withholdingClass.id, body),
      ),
      props,
    )
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="tax-withholding-form-heading">
      <h3 id="tax-withholding-form-heading">
        {TAX_TEXT.recordWithholding} — <code>{props.withholdingClass.code}</code> / <code>{props.buyer.code}</code>
      </h3>
      <fieldset>
        <legend>{TAX_TEXT.buyerLabel}: {props.buyer.name}</legend>
        <label>
          <input type="radio" name="tax-applies" checked={applies} onChange={() => setApplies(true)} /> {TAX_TEXT.appliesLabel}
        </label>
        <label>
          <input type="radio" name="tax-applies" checked={!applies} onChange={() => setApplies(false)} /> {TAX_TEXT.notAppliedLabel}
        </label>
      </fieldset>
      {applies ? (
        <>
          <Field label={TAX_TEXT.rateLabel} help={TAX_TEXT.withholdingRateHelp} error={rateError}>
            {(control) => (
              <input {...control} type="text" inputMode="decimal" required value={rate} onChange={(event) => setRate(event.target.value.trim())} />
            )}
          </Field>
          <Field label={TAX_TEXT.withholdingCodeLabel} help={TAX_TEXT.withholdingCodeHelp}>
            {(control) => (
              <input
                {...control}
                type="text"
                required
                autoComplete="off"
                maxLength={TAX_INPUT.listCodeMaximumLength}
                value={code}
                onChange={(event) => setCode(event.target.value)}
              />
            )}
          </Field>
        </>
      ) : null}
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
