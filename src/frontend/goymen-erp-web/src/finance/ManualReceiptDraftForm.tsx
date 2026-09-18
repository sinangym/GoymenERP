/**
 * FS1 — prepare (save) a manual incoming bank receipt DRAFT.
 *
 * A `Succeeded` DraftSave is draft persistence only (#258 README), so every success text
 * here says "para etkisi yok". Recording money is a different command on the detail
 * screen with its own key.
 *
 * Where each value comes from:
 *  - bank accounts: the FS0 reader; only active, receipt-usable accounts are offered;
 *  - party + company account: the entry context (no Finance party picker exists);
 *  - currency, effective money profile (reference + approved scale) and the Cari label: the
 *    published draft read context (#261) for EXACTLY the chosen bank, party, account and
 *    transaction date. Changing the bank or the date is a new context; until its own answer
 *    arrives nothing can be saved, so a profile read for another date is never sent.
 *
 * Without a ready context the form stays closed and says why — it never invents a profile,
 * a currency code or a scale.
 */

import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { StateBlock } from '../shell/StateBlock'
import { useShellDispatch } from '../shell/shellContext'
import { decodeBankAccountPage, type SourceDetail } from './contract'
import { draftContextRequestFor, partyLabelText } from './draftContext'
import { useDraftContext, useFinanceRead, useReceiptCommand } from './financePort'
import {
  BANK_ACCOUNTS_SUBJECT,
  BANK_ACCOUNT_PLACEHOLDER,
  DRAFT_CONTEXT_RETRY,
  DRAFT_CONTEXT_TEXT,
  DRAFT_SAVED,
  FIELD_ERROR,
  FIELD_HINT,
  FIELD_LABEL,
  NO_USABLE_BANK_ACCOUNT,
  PARTY_LABEL,
  PARTY_LABEL_PENDING,
  SAVE_DRAFT,
  amountLabelFor,
  scaleHintFor,
} from './financeText'
import {
  buildDraftSaveRequest,
  emptyManualReceiptFields,
  fieldsFromDraft,
  type FieldErrors,
  type ManualReceiptFields,
  type ReceiptParty,
} from './manualReceiptForm'
import { isFrozen } from './receiptCommand'
import { CommandAttemptPanel } from './CommandAttemptPanel'
import styles from './Finance.module.css'

const FIELD_ORDER: readonly (keyof ManualReceiptFields)[] = [
  'treasuryAccountId',
  'amount',
  'occurredOn',
  'valueOn',
  'bankReference',
  'description',
]

export const FINANCE_DRAFT_DIRTY_SCOPE = 'finance.manual-receipt.draft'

const RETRYABLE_CONTEXT = new Set(['unavailable', 'network-error', 'malformed'])

export function ManualReceiptDraftForm({
  party,
  draft,
  onSaved,
}: {
  readonly party: ReceiptParty
  readonly draft?: SourceDetail
  readonly onSaved?: (detail: SourceDetail) => void
}) {
  const banks = useFinanceRead('bank-accounts', [], (port, scope, signal) => port.listBankAccounts(scope, signal), decodeBankAccountPage)
  const command = useReceiptCommand('draft-save', draft?.source.id ?? 'new')
  const [initialFields] = useState<ManualReceiptFields>(() => (draft ? fieldsFromDraft(draft) : emptyManualReceiptFields()))
  const [fields, setFields] = useState<ManualReceiptFields>(initialFields)
  const [errors, setErrors] = useState<FieldErrors>({})
  const refs = useRef<Partial<Record<keyof ManualReceiptFields, HTMLElement | null>>>({})

  const contextRequest = draftContextRequestFor(fields.treasuryAccountId, party, fields.occurredOn)
  const draftContext = useDraftContext(contextRequest)
  const context = draftContext.state.kind === 'ready' ? draftContext.state.context : null

  const frozen = isFrozen(command.state)
  // A saved CREATE is done: submitting the same form again would be a second draft.
  const closedAfterCreate = draft === undefined && command.state.phase === 'succeeded'

  useEffect(() => {
    if (command.state.phase === 'succeeded') onSaved?.(command.state.resource)
  }, [command.state, onSaved])

  // Unsaved edits and an unresolved key are dirty work: a company switch must ask first
  // (ERP-000K §13.1), through the shared shell gate — not a local flag.
  const edited = command.state.phase !== 'succeeded' && FIELD_ORDER.some((name) => fields[name] !== initialFields[name])
  const dirty = frozen || edited
  const shellDispatch = useShellDispatch()
  useEffect(() => {
    shellDispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: FINANCE_DRAFT_DIRTY_SCOPE })
  }, [dirty, shellDispatch])
  useEffect(() => () => shellDispatch({ type: 'DIRTY_CLEAR', scope: FINANCE_DRAFT_DIRTY_SCOPE }), [shellDispatch])

  if (banks.state.kind !== 'ready') {
    return <StateBlock state={banks.state} subject={BANK_ACCOUNTS_SUBJECT} onRetry={banks.refetch} />
  }
  const usable = banks.state.data.items.filter((account) => account.isActive && account.isUsableForReceipt)

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    // The context must belong to the CURRENT selection; the query key guarantees that a ready
    // context is for these exact values, and a changed selection has no ready context yet.
    if (context === null || frozen || closedAfterCreate) return
    const built = buildDraftSaveRequest(fields, {
      party,
      identity: draft ? { draftId: draft.source.id, expectedVersion: draft.source.version } : { draftId: null, expectedVersion: null },
      profile: context.currency.profile.reference,
      // The context's own bank view is authoritative for currency and eligibility.
      bankAccounts: [context.bankAccount],
      scale: context.currency.profile.scale,
    })
    if (!built.ok) {
      setErrors(built.errors)
      const first = FIELD_ORDER.find((name) => built.errors[name] !== undefined)
      if (first !== undefined) refs.current[first]?.focus()
      return
    }
    setErrors({})
    command.submit({ operation: 'draft-save', body: built.request })
  }

  const hintFor = (name: keyof ManualReceiptFields): string | null => {
    if (name === 'amount' && context !== null) return `${FIELD_HINT.amount ?? ''} ${scaleHintFor(context.currency.profile.scale)}`.trim()
    return FIELD_HINT[name] ?? null
  }

  const bind = (name: keyof ManualReceiptFields) => ({
    id: `finance-${name}`,
    ref: (element: HTMLElement | null) => {
      refs.current[name] = element
    },
    value: fields[name],
    disabled: frozen,
    'aria-invalid': errors[name] !== undefined,
    'aria-describedby': [hintFor(name) ? `finance-${name}-hint` : null, errors[name] ? `finance-${name}-error` : null]
      .filter(Boolean)
      .join(' ') || undefined,
    onChange: (event: { target: { value: string } }) => setFields((current) => ({ ...current, [name]: event.target.value })),
  })

  const row = (name: keyof ManualReceiptFields, control: ReactNode, label: string = FIELD_LABEL[name]) => {
    const hint = hintFor(name)
    return (
      <div className={styles.field} key={name}>
        <label htmlFor={`finance-${name}`}>{label}</label>
        {control}
        {hint ? (
          <p id={`finance-${name}-hint`} className={styles.hint}>
            {hint}
          </p>
        ) : null}
        {errors[name] ? (
          <p id={`finance-${name}-error`} className={styles.error}>
            {FIELD_ERROR[errors[name]]}
          </p>
        ) : null}
      </div>
    )
  }

  const contextKind = draftContext.state.kind
  return (
    <form className={styles.form} onSubmit={onSubmit} noValidate aria-busy={command.state.phase === 'submitting'}>
      {usable.length === 0 ? (
        <p className={styles.notice} role="status">
          {NO_USABLE_BANK_ACCOUNT}
        </p>
      ) : null}

      <div className={styles.field}>
        <span className={styles.term} id="finance-party-label">
          {PARTY_LABEL}
        </span>
        <p aria-labelledby="finance-party-label" data-party-label={context === null ? 'pending' : 'ready'}>
          {context === null ? PARTY_LABEL_PENDING : partyLabelText(context.party)}
        </p>
      </div>

      {row(
        'treasuryAccountId',
        <select {...bind('treasuryAccountId')}>
          <option value="">{BANK_ACCOUNT_PLACEHOLDER}</option>
          {usable.map((account) => (
            <option key={account.id} value={account.id}>
              {account.displayName}
            </option>
          ))}
        </select>,
      )}
      {row('occurredOn', <input {...bind('occurredOn')} type="date" />)}
      {row('amount', <input {...bind('amount')} inputMode="decimal" autoComplete="off" />, amountLabelFor(context?.currency.displayCode ?? null))}
      {row('valueOn', <input {...bind('valueOn')} type="date" />)}
      {row('bankReference', <input {...bind('bankReference')} autoComplete="off" />)}
      {row('description', <textarea {...bind('description')} rows={3} />)}

      {contextKind === 'ready' ? null : (
        <div className={styles.notice} role="status" data-draft-context={contextKind}>
          <p>{DRAFT_CONTEXT_TEXT[contextKind]}</p>
          {RETRYABLE_CONTEXT.has(contextKind) ? (
            <button type="button" onClick={draftContext.refetch}>
              {DRAFT_CONTEXT_RETRY}
            </button>
          ) : null}
        </div>
      )}

      <CommandAttemptPanel command={command} />
      {command.state.phase === 'succeeded' ? (
        <p className={styles.notice} role="status">
          {DRAFT_SAVED}
        </p>
      ) : null}

      <div className={styles.actions}>
        <button type="submit" disabled={context === null || frozen || closedAfterCreate || !command.available}>
          {SAVE_DRAFT}
        </button>
      </div>
    </form>
  )
}
