import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { formatDecimalTextTr } from '../finance/decimalText'
import { StateBlock } from '../shell/StateBlock'
import { contextKey, contextKeyString } from '../shell/shellState'
import { useShellState } from '../shell/shellContext'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { shortRef } from './accountingText'
import styles from './Accounting.module.css'
import { fetchTrialBalance } from './trialBalanceApi'
import {
  isTrialBalanceGuid,
  isTrialBalanceQuery,
  type TrialBalanceQuery,
  type TrialBalanceReport,
  type TrialBalanceSide,
} from './trialBalanceContract'

const SIDE_LABEL: Record<TrialBalanceSide, string> = {
  Debit: 'Borç',
  Credit: 'Alacak',
  Balanced: 'Dengeli',
}

const amount = (value: string) => formatDecimalTextTr(value) ?? value

export function TrialBalancePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <TrialBalance key={namespace ?? 'no-context'} namespace={namespace} />
}

function TrialBalance({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const [fromDate, setFromDate] = useState('')
  const [throughDate, setThroughDate] = useState('')
  const [currency, setCurrency] = useState('')
  const [submitted, setSubmitted] = useState<TrialBalanceQuery | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [invalid, setInvalid] = useState(false)

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'accounting-trial-balance', submitted, attempt],
    enabled: namespace !== null && singleCompany && submitted !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchTrialBalance(companyId ?? '', submitted!, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    const candidate: TrialBalanceQuery = {
      fromAccountingDate: fromDate,
      throughAccountingDate: throughDate,
      currencyId: currency === '' ? null : currency,
    }
    if (!isTrialBalanceQuery(candidate)) {
      setInvalid(true)
      return
    }
    setInvalid(false)
    setSubmitted(candidate)
    setAttempt((value) => value + 1)
  }

  if (!singleCompany) {
    return <Frame><StateBlock state={{ kind: 'no-authorized-company' }} subject="mizan için tek şirket bağlamı" /></Frame>
  }

  const state = submitted === null
    ? null
    : (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<TrialBalanceReport>

  return (
    <Frame>
      <form className={styles.form} onSubmit={submit} noValidate>
        <label className={styles.control}>
          <span>Başlangıç muhasebe tarihi</span>
          <input type="date" required value={fromDate} onChange={(event) => setFromDate(event.target.value)} />
        </label>
        <label className={styles.control}>
          <span>Bitiş muhasebe tarihi</span>
          <input type="date" required value={throughDate} onChange={(event) => setThroughDate(event.target.value)} />
        </label>
        <label className={styles.control}>
          <span>Para birimi kimliği (isteğe bağlı)</span>
          <input
            value={currency}
            aria-invalid={currency !== '' && !isTrialBalanceGuid(currency)}
            onChange={(event) => setCurrency(event.target.value)}
            placeholder="Tüm para birimleri"
          />
        </label>
        {invalid ? <p role="alert">Tarih aralığını ve para birimi kimliğini kontrol edin.</p> : null}
        <div className={styles.actions}><button type="submit">Mizanı getir</button></div>
      </form>

      {state === null ? <p className={styles.help}>Rapor için iki muhasebe tarihini seçin.</p>
        : state.kind !== 'ready'
          ? <StateBlock state={state} subject="mizan" onRetry={() => void query.refetch()} />
          : <TrialBalanceResult report={state.data} />}
    </Frame>
  )
}

function TrialBalanceResult({ report }: { readonly report: TrialBalanceReport }) {
  if (report.lines.length === 0) {
    return <StateBlock state={{ kind: 'empty-filtered' }} subject="mizan satırı" />
  }

  return (
    <>
      <p className={styles.help}>{report.fromAccountingDate} - {report.throughAccountingDate}; para birimleri çevrilmez veya birbirine netlenmez.</p>
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <thead><tr><th>Hesap referansı</th><th>Para birimi</th><th className={styles.amount}>Borç</th><th className={styles.amount}>Alacak</th><th>Bakiye yönü</th><th className={styles.amount}>Bakiye</th></tr></thead>
          <tbody>{report.lines.map((line) => (
            <tr key={`${line.currencyId}:${line.accountId}`}>
              <td><span className={styles.mono} title={line.accountId}>{shortRef(line.accountId)}</span></td>
              <td><span className={styles.mono} title={line.currencyId}>{shortRef(line.currencyId)}</span></td>
              <td className={styles.amount}>{amount(line.debit)}</td>
              <td className={styles.amount}>{amount(line.credit)}</td>
              <td>{SIDE_LABEL[line.balanceSide]}</td>
              <td className={styles.amount}>{amount(line.balance)}</td>
            </tr>
          ))}</tbody>
          <tfoot>{report.currencyTotals.map((total) => (
            <tr key={total.currencyId}>
              <th colSpan={2} scope="row">Para birimi toplamı <span className={styles.mono} title={total.currencyId}>{shortRef(total.currencyId)}</span></th>
              <td className={styles.amount}>{amount(total.debit)}</td>
              <td className={styles.amount}>{amount(total.credit)}</td>
              <td>{total.isBalanced ? 'Dengeli' : 'Dengesiz'}</td><td />
            </tr>
          ))}</tfoot>
        </table>
      </div>
    </>
  )
}

function Frame({ children }: { readonly children: React.ReactNode }) {
  return <section aria-labelledby="accounting-trial-balance-title" className={styles.page}>
    <h1 id="accounting-trial-balance-title">Mizan</h1>
    <p className={styles.help}>Yalnız kaydedilmiş muhasebe fişlerinin sunucu toplamları gösterilir; bu ekran kayıt üretmez.</p>
    {children}
  </section>
}
