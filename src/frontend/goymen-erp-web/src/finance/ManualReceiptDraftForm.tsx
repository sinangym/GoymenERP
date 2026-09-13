/**
 * FS1 — prepare (save) a manual incoming bank receipt DRAFT.
 *
 * A `Succeeded` DraftSave is draft persistence only (#258 README), so every success text
 * here says "para etkisi yok". Recording money is a different command on the detail
 * screen with its own key.
 *
 * Bank accounts come from the FS0 reader; only active, receipt-usable accounts of the
 * current company are offered. Party/account arrive from the entry context (no Finance
 * party reader exists). The profile reference is required by the contract; without one
 * a NEW draft cannot be saved and the form says so instead of inventing one.
 */

import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { StateBlock } from '../shell/StateBlock'
import { useShellDispatch } from '../shell/shellContext'
import { decodeBankAccountPage, type ProfileReference, type SourceDetail } from './contract'
import { useFinanceRead, useReceiptCommand } from './financePort'
import {
  BANK_ACCOUNTS_SUBJECT,
  BANK_ACCOUNT_PLACEHOLDER,
  DRAFT_SAVED,
  FIELD_ERROR,
  FIELD_HINT,
  FIELD_LABEL,
  NO_USABLE_BANK_ACCOUNT,
  PROFILE_MISSING,
  SAVE_DRAFT,
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

export function ManualReceiptDraftForm({
  party,
  profile,
  draft,
  onSaved,
}: {
  readonly party: ReceiptParty
  /** Server-supplied. `null` closes a create; an update uses the draft's own profile. */
  readonly profile: ProfileReference | null
  readonly draft?: SourceDetail
  readonly onSaved?: (detail: SourceDetail) => void
}) {
  const banks = useFinanceRead('bank-accounts', [], (port, scope, signal) => port.listBankAccounts(scope, signal), decodeBankAccountPage)
  const command = useReceiptCommand('draft-save', draft?.source.id ?? 'new')
  const [initialFields] = useState<ManualReceiptFields>(() => (draft ? fieldsFromDraft(draft) : emptyManualReceiptFields()))
  const [fields, setFields] = useState<ManualReceiptFields>(initialFields)
  const [errors, setErrors] = useState<FieldErrors>({})
  const refs = useRef<Partial<Record<keyof ManualReceiptFields, HTMLElement | null>>>({})

  const effectiveProfile = draft?.profile ?? profile
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
    if (effectiveProfile === null || frozen || closedAfterCreate) return
    const built = buildDraftSaveRequest(fields, {
      party,
      identity: draft ? { draftId: draft.source.id, expectedVersion: draft.source.version } : { draftId: null, expectedVersion: null },
      profile: effectiveProfile,
      bankAccounts: usable,
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

  const bind = (name: keyof ManualReceiptFields) => ({
    id: `finance-${name}`,
    ref: (element: HTMLElement | null) => {
      refs.current[name] = element
    },
    value: fields[name],
    disabled: frozen,
    'aria-invalid': errors[name] !== undefined,
    'aria-describedby': [FIELD_HINT[name] ? `finance-${name}-hint` : null, errors[name] ? `finance-${name}-error` : null]
      .filter(Boolean)
      .join(' ') || undefined,
    onChange: (event: { target: { value: string } }) => setFields((current) => ({ ...current, [name]: event.target.value })),
  })

  const row = (name: keyof ManualReceiptFields, control: ReactNode) => (
    <div className={styles.field} key={name}>
      <label htmlFor={`finance-${name}`}>{FIELD_LABEL[name]}</label>
      {control}
      {FIELD_HINT[name] ? (
        <p id={`finance-${name}-hint`} className={styles.hint}>
          {FIELD_HINT[name]}
        </p>
      ) : null}
      {errors[name] ? (
        <p id={`finance-${name}-error`} className={styles.error}>
          {FIELD_ERROR[errors[name]]}
        </p>
      ) : null}
    </div>
  )

  return (
    <form className={styles.form} onSubmit={onSubmit} noValidate aria-busy={command.state.phase === 'submitting'}>
      {effectiveProfile === null ? (
        <p className={styles.notice} role="status">
          {PROFILE_MISSING}
        </p>
      ) : null}
      {usable.length === 0 ? (
        <p className={styles.notice} role="status">
          {NO_USABLE_BANK_ACCOUNT}
        </p>
      ) : null}
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
      {row('amount', <input {...bind('amount')} inputMode="decimal" autoComplete="off" />)}
      {row('occurredOn', <input {...bind('occurredOn')} type="date" />)}
      {row('valueOn', <input {...bind('valueOn')} type="date" />)}
      {row('bankReference', <input {...bind('bankReference')} autoComplete="off" />)}
      {row('description', <textarea {...bind('description')} rows={3} />)}

      <CommandAttemptPanel command={command} />
      {command.state.phase === 'succeeded' ? (
        <p className={styles.notice} role="status">
          {DRAFT_SAVED}
        </p>
      ) : null}

      <div className={styles.actions}>
        <button type="submit" disabled={effectiveProfile === null || frozen || closedAfterCreate || !command.available}>
          {SAVE_DRAFT}
        </button>
      </div>
    </form>
  )
}
