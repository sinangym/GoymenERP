/**
 * DEC-FIN-016 first release — the customer cheque / promissory note portfolio on F-02.
 *
 * - `CustomerInstrumentReceiveForm`: receive a customer's cheque or note into the portfolio, entered from the same
 *   Cari account link as a receipt draft (party and company account come from the entry; Finance has no picker).
 *   Receiving has NO money effect.
 * - `CustomerInstrumentPortfolio`: the company's instruments, newest received first, and one instrument's detail
 *   with its history and the next steps the SERVER offers (`availableActions`: the state machine intersected with the
 *   actor's fresh grants — the screen invents none). Collection is the one money effect: it records one receipt
 *   source, which F-02 opens and F-03 distributes like any receipt.
 *
 * Commands keep their Idempotency-Key until a definite answer arrives, so a retry after "no answer" can never make a
 * second collection; the server answers a replay with the first result. Amounts are shown only as disclosed.
 */

import { useQueryClient } from '@tanstack/react-query'
import { useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Link } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { portfolioInstrumentPath, receiptDetailPath } from './capabilities'
import { decodeBankAccountPage, type BankAccountView } from './contract'
import { WithCurrencyCodes, currencyShown, type CurrencyCodes } from './currencyCodes'
import {
  decodeCustomerInstrumentCommandResult,
  decodeCustomerInstrumentDetail,
  decodeCustomerInstrumentPage,
  type CustomerInstrumentAction,
  type CustomerInstrumentCommandResult,
  type CustomerInstrumentDetail,
  type CustomerInstrumentKind,
  type CustomerInstrumentView,
} from './customerInstrumentContract'
import {
  ACTION_FORM,
  CLOSE_INSTRUMENT,
  COLLECTED_NOTICE,
  COLUMN,
  FACT,
  FORM,
  INSTRUMENT_SUBJECT,
  ISSUE_TEXT,
  KIND_LABEL,
  MESSAGE,
  MOVEMENTS_TITLE,
  MOVEMENT_RECEIVED,
  NONE,
  NOT_YET,
  NOT_YET_TITLE,
  OPEN_COLLECTION_SOURCE,
  OPEN_INSTRUMENT,
  PORTFOLIO_EMPTY,
  PORTFOLIO_NOTICE,
  PORTFOLIO_SUBJECT,
  PORTFOLIO_TITLE,
  PORTFOLIO_UNAVAILABLE,
  STATUS_LABEL,
  actionLabel,
} from './customerInstrumentText'
import { isDateOnly } from './dateOnly'
import { parseAmountInput } from './decimalText'
import { useFinanceBinding, useFinancePort, useFinanceRead } from './financePort'
import { NEXT_PAGE, PREVIOUS_PAGE } from './financeText'
import type { FinanceReceiptPort, FinanceScope, PortReply } from './port'
import { amountShown, amountText } from './sourceView'
import styles from './Finance.module.css'

type Succeeded = Extract<CustomerInstrumentCommandResult, { outcome: 'Succeeded' }>
type Outcome = { readonly kind: 'done'; readonly result: Succeeded } | { readonly kind: 'message'; readonly text: string }
type Send = (port: FinanceReceiptPort, scope: FinanceScope, key: string) => Promise<PortReply>

function issueText(result: CustomerInstrumentCommandResult): string {
  const texts = result.issues.map((issue) => ISSUE_TEXT[issue.code] ?? issue.code)
  return texts.length === 0 ? MESSAGE.refused : `${MESSAGE.refused}: ${texts.join(' ')}`
}

/**
 * One command at a time with a key that survives an unknown outcome. A definite answer (success or refusal) retires
 * the key, so the next submit is a new attempt; no answer keeps it, so a resend is the same attempt (ADR-011).
 */
function useInstrumentCommand() {
  const port = useFinancePort()
  const bound = useFinanceBinding()
  const queryClient = useQueryClient()
  const key = useRef<string | null>(null)
  const [busy, setBusy] = useState(false)

  const send = async (call: Send): Promise<Outcome> => {
    if (port === null || bound === null) return { kind: 'message', text: MESSAGE.unavailable }
    const companyId = bound.binding.companyId
    key.current ??= globalThis.crypto.randomUUID()
    setBusy(true)
    let reply: PortReply
    try {
      reply = await call(port, { companyId }, key.current)
    } catch {
      reply = { kind: 'no-reply' }
    } finally {
      setBusy(false)
    }
    if (reply.kind === 'no-reply') return { kind: 'message', text: MESSAGE.unknown }
    const decoded = decodeCustomerInstrumentCommandResult(reply.body, companyId)
    if (!decoded.ok) return { kind: 'message', text: MESSAGE.unknown }
    key.current = null
    const result = decoded.value
    await queryClient.invalidateQueries({
      predicate: (query) => query.queryKey[1] === 'finance'
        && (query.queryKey[2] === 'customer-instruments' || query.queryKey[2] === 'customer-instrument'),
    })
    switch (result.outcome) {
      case 'Succeeded':
        return { kind: 'done', result }
      case 'Denied':
        return { kind: 'message', text: MESSAGE.denied }
      case 'Unavailable':
        return { kind: 'message', text: MESSAGE.unavailable }
      case 'VersionConflict':
      case 'IdempotencyConflict':
        return { kind: 'message', text: MESSAGE.conflict }
      default:
        return { kind: 'message', text: issueText(result) }
    }
  }

  return { send, busy, ready: port !== null && bound !== null }
}

function useBankAccounts() {
  return useFinanceRead('bank-accounts', [], (port, scope, signal) => port.listBankAccounts(scope, signal),
    decodeBankAccountPage)
}

function usableBanks(banks: readonly BankAccountView[], currencyId: string | null): readonly BankAccountView[] {
  return banks.filter((bank) => bank.isActive && bank.isUsableForReceipt
    && (currencyId === null || bank.currencyId === currencyId))
}

function Field({ id, label, children }: { readonly id: string; readonly label: string; readonly children: ReactNode }) {
  return (
    <div className={styles.field}>
      <label htmlFor={id}>{label}</label>
      {children}
    </div>
  )
}

function TextField({
  id,
  label,
  value,
  onChange,
  type = 'text',
}: {
  readonly id: string
  readonly label: string
  readonly value: string
  readonly onChange: (value: string) => void
  readonly type?: 'text' | 'date'
}) {
  return (
    <Field id={id} label={label}>
      <input id={id} type={type} value={value} onChange={(event) => onChange(event.target.value)} />
    </Field>
  )
}

function Fact({ term, children }: { readonly term: string; readonly children: ReactNode }) {
  return (
    <div>
      <dt>{term}</dt>
      <dd>{children}</dd>
    </div>
  )
}

function InstrumentFacts({ instrument, codes }: { readonly instrument: CustomerInstrumentView; readonly codes: CurrencyCodes }) {
  const amount = amountShown(instrument.amount)
  const bank = [instrument.bankName, instrument.branchName].filter((part) => part !== null).join(' / ')
  return (
    <dl className={styles.facts}>
      <Fact term={FACT.kind}>{KIND_LABEL[instrument.kind]}</Fact>
      <Fact term={FACT.serial}>{instrument.serialNumber}</Fact>
      <Fact term={FACT.drawer}>{instrument.drawer}</Fact>
      <Fact term={FACT.bank}>{bank === '' ? NONE : bank}</Fact>
      <Fact term={FACT.dueOn}>{instrument.dueOn}</Fact>
      <Fact term={FACT.receivedOn}>{instrument.receivedOn}</Fact>
      <div>
        <dt>{FACT.amount}</dt>
        <dd data-amount-kind={amount.kind}>{amountText(amount)}</dd>
      </div>
      <Fact term={FACT.currency}>{currencyShown(instrument.currencyId, codes)}</Fact>
      <Fact term={FACT.status}>{STATUS_LABEL[instrument.status]}</Fact>
      <Fact term={FACT.statusChangedOn}>{instrument.statusChangedOn}</Fact>
      {instrument.description === null ? null : <Fact term={FACT.description}>{instrument.description}</Fact>}
    </dl>
  )
}

// ---------------------------------------------------------------------------------
// Receive
// ---------------------------------------------------------------------------------

export function CustomerInstrumentReceiveForm({
  party,
}: {
  readonly party: { readonly partyId: string; readonly companyAccountId: string }
}) {
  const command = useInstrumentCommand()
  const banks = useBankAccounts()
  const [kind, setKind] = useState<CustomerInstrumentKind>('Cheque')
  const [serial, setSerial] = useState('')
  const [drawer, setDrawer] = useState('')
  const [bankName, setBankName] = useState('')
  const [branchName, setBranchName] = useState('')
  const [dueOn, setDueOn] = useState('')
  const [receivedOn, setReceivedOn] = useState('')
  const [amount, setAmount] = useState('')
  const [currencyId, setCurrencyId] = useState('')
  const [description, setDescription] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const [saved, setSaved] = useState<CustomerInstrumentView | null>(null)

  const currencies = banks.state.kind === 'ready'
    ? [...new Set(usableBanks(banks.state.data.items, null).map((bank) => bank.currencyId))]
    : []

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (command.busy) return
    const parsed = parseAmountInput(amount)
    const optional = (value: string) => (value.trim() === '' ? null : value.trim())
    if (!parsed.ok || serial.trim() === '' || drawer.trim() === '' || (kind === 'Cheque' && bankName.trim() === '')
      || !isDateOnly(dueOn) || !isDateOnly(receivedOn) || currencyId === '') {
      setMessage(MESSAGE.invalid)
      return
    }
    const request = {
      kind,
      partyId: party.partyId,
      companyAccountId: party.companyAccountId,
      serialNumber: serial.trim(),
      drawer: drawer.trim(),
      bankName: optional(bankName),
      branchName: optional(branchName),
      dueOn,
      receivedOn,
      money: { amount: parsed.wire, currencyId },
      description: optional(description),
    }
    const outcome = await command.send((port, scope, key) => port.receiveCustomerInstrument(scope, key, request))
    if (outcome.kind === 'done') {
      setSaved(outcome.result.resource)
      setMessage(null)
    } else {
      setMessage(outcome.text)
    }
  }

  if (saved !== null) {
    return (
      <section aria-labelledby="finance-instrument-saved-title" className={styles.form}>
        <h2 id="finance-instrument-saved-title">{FORM.title}</h2>
        <p role="status">{FORM.saved}</p>
        <WithCurrencyCodes>{(codes) => <InstrumentFacts instrument={saved} codes={codes} />}</WithCurrencyCodes>
        <p>
          <Link to={portfolioInstrumentPath(saved.id)}>{FORM.openSaved}</Link>
        </p>
      </section>
    )
  }

  return (
    <section aria-labelledby="finance-instrument-form-title">
      <h2 id="finance-instrument-form-title">{FORM.title}</h2>
      <p className={styles.notice}>{FORM.notice}</p>
      <form className={styles.form} onSubmit={(event) => void submit(event)} noValidate>
        <Field id="instrument-kind" label={FORM.kind}>
          <select id="instrument-kind" value={kind}
            onChange={(event) => setKind(event.target.value === 'PromissoryNote' ? 'PromissoryNote' : 'Cheque')}>
            <option value="Cheque">{KIND_LABEL.Cheque}</option>
            <option value="PromissoryNote">{KIND_LABEL.PromissoryNote}</option>
          </select>
        </Field>
        <TextField id="instrument-serial" label={FORM.serial} value={serial} onChange={setSerial} />
        <TextField id="instrument-drawer" label={FORM.drawer} value={drawer} onChange={setDrawer} />
        <TextField id="instrument-bank" label={kind === 'Cheque' ? FORM.bankName : FORM.bankNameNote}
          value={bankName} onChange={setBankName} />
        <TextField id="instrument-branch" label={FORM.branchName} value={branchName} onChange={setBranchName} />
        <TextField id="instrument-due" label={FORM.dueOn} value={dueOn} onChange={setDueOn} type="date" />
        <TextField id="instrument-received" label={FORM.receivedOn} value={receivedOn} onChange={setReceivedOn} type="date" />
        <TextField id="instrument-amount" label={FORM.amount} value={amount} onChange={setAmount} />
        <WithCurrencyCodes>
          {(codes) => (
            <Field id="instrument-currency" label={FORM.currency}>
              <select id="instrument-currency" value={currencyId} onChange={(event) => setCurrencyId(event.target.value)}
                disabled={currencies.length === 0}>
                <option value="">{FORM.currencyPlaceholder}</option>
                {currencies.map((id) => <option key={id} value={id}>{currencyShown(id, codes)}</option>)}
              </select>
              {banks.state.kind === 'ready' && currencies.length === 0
                ? <p className={styles.hint}>{FORM.noCurrency}</p>
                : null}
            </Field>
          )}
        </WithCurrencyCodes>
        <TextField id="instrument-description" label={FORM.description} value={description} onChange={setDescription} />
        <div className={styles.actions}>
          <button type="submit" disabled={command.busy || !command.ready}>
            {command.busy ? FORM.submitting : FORM.submit}
          </button>
        </div>
      </form>
      {message === null ? null : <p role="status">{message}</p>}
    </section>
  )
}

// ---------------------------------------------------------------------------------
// Portfolio
// ---------------------------------------------------------------------------------

export function CustomerInstrumentPortfolio({
  selectedId,
  onSelect,
}: {
  readonly selectedId: string | null
  readonly onSelect: (instrumentId: string | null) => void
}) {
  const [cursors, setCursors] = useState<readonly (string | null)[]>([null])
  const cursor = cursors[cursors.length - 1]
  const list = useFinanceRead('customer-instruments', [cursor],
    (port, scope, signal) => port.listCustomerInstruments(scope, { cursor }, signal), decodeCustomerInstrumentPage)

  // An actor without the portfolio grant sees no portfolio at all (the receipts above are theirs to use).
  if (list.state.kind === 'forbidden') return null

  const body = (() => {
    if (list.state.kind === 'service-unavailable') return <p role="status">{PORTFOLIO_UNAVAILABLE}</p>
    if (list.state.kind !== 'ready') {
      return <StateBlock state={list.state} subject={PORTFOLIO_SUBJECT} onRetry={list.refetch} />
    }
    const page = list.state.data
    if (page.items.length === 0 && cursors.length === 1) return <p role="status">{PORTFOLIO_EMPTY}</p>
    return (
      <WithCurrencyCodes>
        {(codes) => (
          <div className={styles.list}>
            <table>
              <thead>
                <tr>
                  <th scope="col">{COLUMN.serial}</th>
                  <th scope="col">{COLUMN.kind}</th>
                  <th scope="col">{COLUMN.drawer}</th>
                  <th scope="col">{COLUMN.dueOn}</th>
                  <th scope="col">{COLUMN.amount}</th>
                  <th scope="col">{COLUMN.currency}</th>
                  <th scope="col">{COLUMN.status}</th>
                  <th scope="col">{COLUMN.actions}</th>
                </tr>
              </thead>
              <tbody>
                {page.items.map((item) => {
                  const amount = amountShown(item.amount)
                  return (
                    <tr key={item.id} data-instrument-status={item.status}>
                      <th scope="row">{item.serialNumber}</th>
                      <td>{KIND_LABEL[item.kind]}</td>
                      <td>{item.drawer}</td>
                      <td>{item.dueOn}</td>
                      <td data-amount-kind={amount.kind}>{amountText(amount)}</td>
                      <td>{currencyShown(item.currencyId, codes)}</td>
                      <td>{STATUS_LABEL[item.status]}</td>
                      <td>
                        <button type="button" onClick={() => onSelect(item.id)}
                          aria-label={`${OPEN_INSTRUMENT}: ${item.serialNumber}`}>
                          {OPEN_INSTRUMENT}
                        </button>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
            <div className={styles.actions}>
              {cursors.length > 1 ? (
                <button type="button" onClick={() => setCursors((trail) => trail.slice(0, -1))}>{PREVIOUS_PAGE}</button>
              ) : null}
              {page.nextCursor !== null ? (
                <button type="button" onClick={() => setCursors((trail) => [...trail, page.nextCursor])}>{NEXT_PAGE}</button>
              ) : null}
            </div>
          </div>
        )}
      </WithCurrencyCodes>
    )
  })()

  return (
    <section aria-labelledby="finance-portfolio-title">
      <h2 id="finance-portfolio-title">{PORTFOLIO_TITLE}</h2>
      <p className={styles.notice}>{PORTFOLIO_NOTICE}</p>
      {body}
      {selectedId === null ? null : (
        <CustomerInstrumentDetailPanel key={selectedId} instrumentId={selectedId} onClose={() => onSelect(null)} />
      )}
    </section>
  )
}

function CustomerInstrumentDetailPanel({
  instrumentId,
  onClose,
}: {
  readonly instrumentId: string
  readonly onClose: () => void
}) {
  const detail = useFinanceRead('customer-instrument', [instrumentId],
    (port, scope, signal) => port.customerInstrumentDetail(scope, instrumentId, signal),
    (resource, companyId) => {
      const decoded = decodeCustomerInstrumentDetail(resource, companyId)
      return decoded.ok && decoded.value.instrument.id !== instrumentId ? { ok: false } : decoded
    })

  if (detail.state.kind !== 'ready') {
    return <StateBlock state={detail.state} subject={INSTRUMENT_SUBJECT} onRetry={detail.refetch} />
  }
  const { instrument } = detail.state.data
  const title = `${KIND_LABEL[instrument.kind]} · ${instrument.serialNumber}`
  return (
    <section aria-label={title} className={styles.detail} data-instrument-status={instrument.status}>
      <h3>{title}</h3>
      <WithCurrencyCodes>{(codes) => <InstrumentFacts instrument={instrument} codes={codes} />}</WithCurrencyCodes>
      {instrument.collectionSourceId === null ? null : (
        <>
          <p role="note">{COLLECTED_NOTICE}</p>
          <p>
            <Link to={receiptDetailPath(instrument.collectionSourceId)}>{OPEN_COLLECTION_SOURCE}</Link>
          </p>
        </>
      )}
      <Movements detail={detail.state.data} />
      <InstrumentActions detail={detail.state.data} />
      <details>
        <summary>{NOT_YET_TITLE}</summary>
        <ul>
          {NOT_YET.map((line) => <li key={line}>{line}</li>)}
        </ul>
      </details>
      <div className={styles.actions}>
        <button type="button" onClick={onClose}>{CLOSE_INSTRUMENT}</button>
      </div>
    </section>
  )
}

function Movements({ detail }: { readonly detail: CustomerInstrumentDetail }) {
  return (
    <>
      <h4>{MOVEMENTS_TITLE}</h4>
      <ol aria-label={MOVEMENTS_TITLE}>
        {detail.movements.map((movement) => (
          <li key={movement.version}>
            {movement.occurredOn}
            {' — '}
            {movement.fromStatus === null
              ? MOVEMENT_RECEIVED
              : `${STATUS_LABEL[movement.fromStatus]} → ${STATUS_LABEL[movement.toStatus]}`}
            {movement.note === null ? null : ` (${movement.note})`}
          </li>
        ))}
      </ol>
    </>
  )
}

function InstrumentActions({ detail }: { readonly detail: CustomerInstrumentDetail }) {
  const [open, setOpen] = useState<CustomerInstrumentAction | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const { instrument, availableActions } = detail
  if (availableActions.length === 0 && message === null) return null
  return (
    <div className={styles.command}>
      <div className={styles.actions}>
        {availableActions.map((action) => (
          <button key={action} type="button" aria-pressed={open === action}
            onClick={() => { setOpen(action); setMessage(null) }}>
            {actionLabel(action, instrument.kind)}
          </button>
        ))}
      </div>
      {open === null || !availableActions.includes(open) ? null : (
        <ActionForm key={`${open}-${instrument.version}`} action={open} instrument={instrument}
          onDone={(text) => { setOpen(null); setMessage(text) }} onCancel={() => setOpen(null)} />
      )}
      {message === null ? null : <p role="status">{message}</p>}
    </div>
  )
}

function ActionForm({
  action,
  instrument,
  onDone,
  onCancel,
}: {
  readonly action: CustomerInstrumentAction
  readonly instrument: CustomerInstrumentView
  readonly onDone: (message: string) => void
  readonly onCancel: () => void
}) {
  const command = useInstrumentCommand()
  const banks = useBankAccounts()
  const needsBank = action === 'DepositForCollection' || action === 'Collect'
  // A cheque at the bank is collected into THAT bank account; the server refuses any other.
  const lockedBank = action === 'Collect' ? instrument.treasuryAccountId : null
  const [date, setDate] = useState('')
  const [valueOn, setValueOn] = useState('')
  const [bank, setBank] = useState(lockedBank ?? '')
  const [note, setNote] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const options = banks.state.kind === 'ready' ? usableBanks(banks.state.data.items, instrument.currencyId) : []

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (command.busy) return
    if (!isDateOnly(date) || (needsBank && bank === '') || (valueOn !== '' && !isDateOnly(valueOn))) {
      setMessage(MESSAGE.invalid)
      return
    }
    const outcome = action === 'Collect'
      ? await command.send((port, scope, key) => port.collectCustomerInstrument(scope, key, {
          instrumentId: instrument.id,
          expectedVersion: instrument.version,
          treasuryAccountId: bank,
          collectedOn: date,
          valueOn: valueOn === '' ? null : valueOn,
        }))
      : await command.send((port, scope, key) => port.changeCustomerInstrumentStatus(scope, key, {
          instrumentId: instrument.id,
          expectedVersion: instrument.version,
          transition: action,
          occurredOn: date,
          treasuryAccountId: action === 'DepositForCollection' ? bank : null,
          note: note.trim() === '' ? null : note.trim(),
        }))
    if (outcome.kind === 'done') onDone(`${actionLabel(action, instrument.kind)}: ${MESSAGE.done}`)
    else setMessage(outcome.text)
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} noValidate
      aria-label={actionLabel(action, instrument.kind)}>
      <TextField id="instrument-action-date" label={action === 'Collect' ? ACTION_FORM.collectedOn : ACTION_FORM.date}
        value={date} onChange={setDate} type="date" />
      {needsBank ? (
        <Field id="instrument-action-bank" label={ACTION_FORM.bank}>
          <select id="instrument-action-bank" value={bank} disabled={lockedBank !== null}
            onChange={(event) => setBank(event.target.value)}>
            <option value="">{ACTION_FORM.bankPlaceholder}</option>
            {options.map((account) => <option key={account.id} value={account.id}>{account.displayName}</option>)}
          </select>
        </Field>
      ) : null}
      {action === 'Collect' ? (
        <TextField id="instrument-action-value" label={ACTION_FORM.valueOn} value={valueOn} onChange={setValueOn} type="date" />
      ) : (
        <TextField id="instrument-action-note" label={ACTION_FORM.note} value={note} onChange={setNote} />
      )}
      <div className={styles.actions}>
        <button type="submit" disabled={command.busy || !command.ready}>{ACTION_FORM.confirm}</button>
        <button type="button" onClick={onCancel}>{ACTION_FORM.cancel}</button>
      </div>
      {message === null ? null : <p role="status">{message}</p>}
    </form>
  )
}
