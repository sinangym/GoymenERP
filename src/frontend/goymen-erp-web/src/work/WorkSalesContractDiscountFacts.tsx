import type {WorkSalesContractDiscountPreview} from './workSalesContractDiscountDecision'
import {SALES_CONTRACT_DISCOUNT_WORK_TEXT as T} from './workSalesContractDiscountText'
import styles from './Work.module.css'

type Row = readonly [string, string | number | boolean | null]
function Facts({rows}: {readonly rows: readonly Row[]}) {
  return <dl className={styles.facts}>{rows.map(([label, value]) => <div key={label}><dt>{label}</dt><dd style={{whiteSpace: 'pre-wrap'}}>{value === null ? T.none : typeof value === 'boolean' ? value ? T.yes : T.no : String(value)}</dd></div>)}</dl>
}
export function WorkSalesContractDiscountFacts({human: h}: {readonly human: WorkSalesContractDiscountPreview}) {
  const c = h.contract, f = T.fields
  const amount = (v: string | null) => c.amountDisclosure === 'Withheld' ? T.withheld : v
  return <section aria-label={T.title}><h3>{T.title}</h3><p>{T.readOnly}</p>
    <Facts rows={[[f.companyId, h.companyId], [f.id, c.id], [f.contractNo, c.contractNo], [f.partyId, c.partyId], [f.companyAccountId, c.companyAccountId], [f.currencyId, c.currencyId], [f.moneyScale, c.moneyScale], [f.state, T.states[c.state]], [f.version, c.version], [f.validFrom, c.validFrom], [f.validThrough, c.validThrough], [f.amountDisclosure, c.amountDisclosure === 'Withheld' ? T.withheld : T.yes], [f.plannedTotalAmount, amount(c.plannedTotalAmount)], [f.clauses, c.clauses], [f.discountApprovalRequired, c.discountApprovalRequired], [f.discountLimitDefined, c.discountLimitDefined], [f.discountLimitPercent, amount(c.discountLimitPercent)], [f.isInEffectToday, c.isInEffectToday], [f.createdBy, c.createdBy], [f.createdAtUtc, c.createdAtUtc], [f.decidedBy, c.decidedBy], [f.decidedAtUtc, c.decidedAtUtc], [f.rejectionReason, c.rejectionReason], [f.approvalSupported, h.approvalSupported], [f.rejectionSupported, h.rejectionSupported]]}/>
    <div className={styles.tableWrap} role="region" aria-label={T.installments} tabIndex={0}><table className={styles.table}><caption>{T.installments}</caption><thead><tr><th scope="col">{f.installmentNo}</th><th scope="col">{f.dueDate}</th><th scope="col">{f.amount}</th></tr></thead><tbody>{c.installments.map(x => <tr key={x.installmentNo}><td>{x.installmentNo}</td><td>{x.dueDate}</td><td>{amount(x.amount)}</td></tr>)}</tbody></table></div>
    <div className={styles.tableWrap} role="region" aria-label={T.rules} tabIndex={0}><table className={styles.table}><caption>{T.rules}</caption><thead><tr><th scope="col">{f.ruleNo}</th><th scope="col">{f.kind}</th><th scope="col">{f.label}</th><th scope="col">{f.ratePercent}</th></tr></thead><tbody>{c.discountRules.map(x => <tr key={x.ruleNo}><td>{x.ruleNo}</td><td>{T.kinds[x.kind]}</td><td>{x.label}</td><td>{amount(x.ratePercent)}</td></tr>)}</tbody></table></div>
  </section>
}
