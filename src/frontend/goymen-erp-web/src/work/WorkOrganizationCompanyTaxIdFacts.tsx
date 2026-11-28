import { COMPANY_TAX_ID_TEXT as N, taxIdStatusLabel } from '../organization/companyTaxIdText'
import { ORGANIZATION_COMPANY_TAX_ID_WORK_TEXT as T, type WorkOrganizationCompanyTaxIdPreview } from './workOrganizationCompanyTaxIdDecision'
import styles from './Work.module.css'

export function WorkOrganizationCompanyTaxIdFacts({human}:{readonly human:WorkOrganizationCompanyTaxIdPreview}) {
 const r=human.request
 const requestFacts=[
  [T.fields.id,r.id],[N.currentVkn,r.currentVkn??N.noVkn],[N.proposedVkn,r.proposedVkn],[N.reason,r.reason],
  [N.status,taxIdStatusLabel(r.status)],[N.requestedBy,r.requestedBy],[N.requestedAt,r.requestedAtUtc],
  [N.decidedBy,r.decidedBy??N.none],[N.decidedAt,r.decidedAtUtc??N.none],[N.decisionNote,r.decisionNote??N.none]
 ] as const
 return <section aria-label={T.title}><h3>{T.title}</h3><p>{T.readOnly}</p>
  <h4>{T.company}</h4><dl className={styles.facts}>
   <div><dt>{T.fields.companyId}</dt><dd>{human.companyId}</dd></div>
   <div><dt>{T.fields.companyVersion}</dt><dd>{human.companyVersion}</dd></div>
   <div><dt>{T.fields.companyVkn}</dt><dd>{human.companyVkn??N.noVkn}</dd></div>
   <div><dt>{T.fields.companyVersionAtRequest}</dt><dd>{human.companyVersionAtRequest}</dd></div>
   <div><dt>{T.fields.isCurrent}</dt><dd>{human.isCurrent?T.yes:T.no}</dd></div>
  </dl><h4>{T.request}</h4><dl className={styles.facts}>{requestFacts.map(([label,value])=><div key={label}><dt>{label}</dt><dd style={{whiteSpace:'pre-wrap'}}>{value}</dd></div>)}</dl>
  {r.status==='pending'&&!human.isCurrent?<p role="note">{T.stale}</p>:null}
 </section>
}
