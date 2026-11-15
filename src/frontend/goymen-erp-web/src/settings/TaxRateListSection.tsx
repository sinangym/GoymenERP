/**
 * Ayarlar > Vergi > Oran listeleri — F-029 S9 (SAT-D181, EK26): the company's VAT rate and withholding ratio lists, on the
 * tax master's one name `settings.tax-master.manage` (SAT-D145; no new permission, granted to no role here).
 *
 *  - The lists start EMPTY (SAT-D148): an empty list is shown as "Liste girilmemiş", never a guessed rate; a withholding
 *    ratio without a GİB code is "kod: kaynak yok". The screen never computes a tax: the percents are the server's text.
 *  - Its own read (the section's state never hides the rest of the tax master); every non-ready state is a StateBlock with
 *    a retry where it helps.
 *  - Every change: a whole list version, a mandatory reason, the list's expected version and one Idempotency-Key (kept for
 *    a retry after an unknown outcome). A version conflict closes the form and re-reads. A not-yet-effective version can be
 *    withdrawn.
 */

import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import type { CommandContext, CommandOutcome } from './discountLimitApi'
import { TAX_INPUT, TAX_PROBLEM } from './taxMasterContract'
import { TAX_TEXT, formatDate, taxRefusalText } from './taxMasterText'
import { fetchRateLists, publishRateList, withdrawRateList } from './taxRateListApi'
import {
  parseRateListText,
  rateListText,
  type RateList,
  type RateListEntry,
  type RateLists,
  type RateListVersion,
} from './taxRateListContract'
import { RATE_LIST_LABEL, RATE_LIST_TEXT } from './taxRateListText'
import { useCommandAttempt, type AttemptStatus } from './useCommandAttempt'
import styles from './settings.module.css'

export function TaxRateListSection({ companyId }: { readonly companyId: string }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const key = contextKey(shell)
  const namespace = key === null ? 'no-context' : contextKeyString(key)
  const lists = useQuery({
    queryKey: [namespace, shell.routeEntryGeneration, 'settings-tax-rate-lists'],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchRateLists(companyId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const state = (lists.isPending
    ? { kind: 'initial-loading' }
    : classifyOutcome(lists.data ?? ({ type: 'malformed' } as ViewOutcome<RateLists>))) as RemoteViewState<RateLists>
  return (
    <section aria-labelledby="tax-rate-lists-heading" className={styles.section}>
      <h2 id="tax-rate-lists-heading">{RATE_LIST_TEXT.heading}</h2>
      <p className={styles.muted}>{RATE_LIST_TEXT.intro}</p>
      {state.kind === 'ready' ? (
        <RateListsView lists={state.data} companyId={companyId} onChanged={() => void lists.refetch()} />
      ) : (
        <StateBlock state={state} subject={RATE_LIST_TEXT.subject} onRetry={() => void lists.refetch()} />
      )}
    </section>
  )
}

type Editor =
  | { readonly kind: 'none' }
  | { readonly kind: 'publish'; readonly list: RateList }
  | { readonly kind: 'withdraw'; readonly list: RateList; readonly version: RateListVersion }

function RateListsView({
  lists,
  companyId,
  onChanged,
}: {
  readonly lists: RateLists
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
  const formProps: FormProps = { companyId, onDone: close, onConflict: conflict }

  return (
    <>
      {notice !== null ? (
        <p role="status" className={styles.notice}>
          {notice}
        </p>
      ) : null}
      {lists.lists.map((list) => (
        <div key={list.kind} aria-label={RATE_LIST_LABEL[list.kind]} role="group">
          <h3>{RATE_LIST_LABEL[list.kind]}</h3>
          {list.inForce === null ? <p>{RATE_LIST_TEXT.empty}</p> : <InForceTable list={list} version={list.inForce} />}
          {list.scheduled.length > 0 ? (
            <ul aria-label={RATE_LIST_TEXT.scheduledLabel(RATE_LIST_LABEL[list.kind])} className={styles.plainList}>
              {list.scheduled.map((version) => (
                <li key={version.listVersion}>
                  {TAX_TEXT.scheduledLabel(version.effectiveFrom, RATE_LIST_TEXT.entryCount(version.entries.length))}{' '}
                  <Button
                    type="button"
                    size="sm"
                    variant="danger"
                    aria-label={`${TAX_TEXT.withdraw} — ${RATE_LIST_LABEL[list.kind]} ${formatDate(version.effectiveFrom)}`}
                    onClick={() => setEditor({ kind: 'withdraw', list, version })}
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
            aria-label={`${RATE_LIST_TEXT.publish} — ${RATE_LIST_LABEL[list.kind]}`}
            onClick={() => setEditor({ kind: 'publish', list })}
          >
            {RATE_LIST_TEXT.publish}
          </Button>
        </div>
      ))}

      {editor.kind === 'publish' ? <PublishForm key={editor.list.kind} {...formProps} list={editor.list} /> : null}
      {editor.kind === 'withdraw' ? (
        <WithdrawForm
          key={`${editor.list.kind}:${editor.version.listVersion}`}
          {...formProps}
          list={editor.list}
          version={editor.version}
        />
      ) : null}
    </>
  )
}

function InForceTable({ list, version }: { readonly list: RateList; readonly version: RateListVersion }) {
  const withholding = list.kind === 'WithholdingRatio'
  return (
    <table className={styles.table}>
      <caption>
        {RATE_LIST_TEXT.inForceLabel(RATE_LIST_LABEL[list.kind])} ·{' '}
        {RATE_LIST_TEXT.versionOf(version.listVersion, formatDate(version.effectiveFrom))}
      </caption>
      <thead>
        <tr>
          <th scope="col">{RATE_LIST_TEXT.columnCode}</th>
          <th scope="col">{RATE_LIST_TEXT.columnName}</th>
          <th scope="col">{RATE_LIST_TEXT.columnPercent}</th>
          {withholding ? <th scope="col">{RATE_LIST_TEXT.columnTaxCode}</th> : null}
        </tr>
      </thead>
      <tbody>
        {version.entries.map((entry) => (
          <EntryRow key={entry.code} entry={entry} withholding={withholding} />
        ))}
      </tbody>
    </table>
  )
}

function EntryRow({ entry, withholding }: { readonly entry: RateListEntry; readonly withholding: boolean }) {
  return (
    <tr>
      <th scope="row">
        <code>{entry.code}</code>
      </th>
      <td>{entry.name}</td>
      <td>{RATE_LIST_TEXT.percentOf(entry.percent)}</td>
      {withholding ? <td>{entry.taxCode === null ? RATE_LIST_TEXT.taxCodeMissing : <code>{entry.taxCode}</code>}</td> : null}
    </tr>
  )
}

interface FormProps {
  readonly companyId: string
  readonly onDone: (notice: string | null) => void
  readonly onConflict: () => void
}

function finish(outcome: CommandOutcome<true>, props: FormProps) {
  if (outcome.type === 'applied') props.onDone(outcome.replayed ? TAX_TEXT.savedReplay : TAX_TEXT.saved)
  else if (outcome.type === 'refused' && outcome.code === TAX_PROBLEM.versionConflict) props.onConflict()
}

function Footer({
  status,
  hasPendingRetry,
  onCancel,
}: {
  readonly status: AttemptStatus<true>
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

function PublishForm(props: FormProps & { readonly list: RateList }) {
  const { list } = props
  const [text, setText] = useState(list.inForce === null ? '' : rateListText(list.inForce.entries))
  const [effectiveFrom, setEffectiveFrom] = useState('')
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const entries = parseRateListText(list.kind, text)
  const vat = list.kind === 'VatRate'
  const invalidText = vat ? RATE_LIST_TEXT.vatEntriesInvalid : RATE_LIST_TEXT.withholdingEntriesInvalid
  const entriesError = text.trim().length > 0 && entries === null ? invalidText : undefined

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (entries === null) return
    const body = {
      expectedVersion: list.version,
      entries,
      reason: reason.trim(),
      ...(effectiveFrom.length > 0 ? { effectiveFrom } : {}),
    }
    const context = (idempotencyKey: string): CommandContext => ({ companyId: props.companyId, idempotencyKey })
    finish(
      await attempt.run({ rateList: list.kind, ...body }, (idempotencyKey) =>
        publishRateList(context(idempotencyKey), list.kind, body),
      ),
      props,
    )
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="tax-rate-list-form-heading">
      <h3 id="tax-rate-list-form-heading">
        {RATE_LIST_TEXT.publish} — {RATE_LIST_LABEL[list.kind]}
      </h3>
      <Field
        label={vat ? RATE_LIST_TEXT.vatEntriesLabel : RATE_LIST_TEXT.withholdingEntriesLabel}
        help={vat ? RATE_LIST_TEXT.vatEntriesHelp : RATE_LIST_TEXT.withholdingEntriesHelp}
        error={entriesError}
      >
        {(control) => <textarea {...control} required rows={6} value={text} onChange={(event) => setText(event.target.value)} />}
      </Field>
      <Field label={TAX_TEXT.effectiveFromLabel} help={TAX_TEXT.effectiveFromHelp}>
        {(control) => (
          <input {...control} type="date" value={effectiveFrom} onChange={(event) => setEffectiveFrom(event.target.value)} />
        )}
      </Field>
      <ReasonField value={reason} onChange={setReason} />
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => props.onDone(null)} />
    </form>
  )
}

function WithdrawForm(props: FormProps & { readonly list: RateList; readonly version: RateListVersion }) {
  const { list, version } = props
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()

  async function submit(event: FormEvent) {
    event.preventDefault()
    const body = { expectedVersion: list.version, reason: reason.trim() }
    finish(
      await attempt.run({ rateList: list.kind, listVersion: version.listVersion, ...body }, (idempotencyKey) =>
        withdrawRateList({ companyId: props.companyId, idempotencyKey }, list.kind, version.listVersion, body),
      ),
      props,
    )
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="tax-rate-list-withdraw-heading">
      <h3 id="tax-rate-list-withdraw-heading">
        {TAX_TEXT.withdraw} — {RATE_LIST_LABEL[list.kind]} {formatDate(version.effectiveFrom)}
      </h3>
      <ReasonField value={reason} onChange={setReason} />
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => props.onDone(null)} />
    </form>
  )
}
