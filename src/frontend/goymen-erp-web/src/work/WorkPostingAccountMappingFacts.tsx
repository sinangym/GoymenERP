import type { WorkPostingAccountMappingPreview } from './workPostingAccountMappingDecision'
import { WORK_MAPPING_TEXT as T } from './workPostingAccountMappingText'
import { POSTING_MAPPING_TEXT as M } from '../accounting/postingMappingText'

export function WorkPostingAccountMappingFacts({human}:{readonly human:WorkPostingAccountMappingPreview}) {
 const m=human.mapping,p=human.currentActiveAccountPlan
 return <section aria-label={T.title}><h3>{T.title}</h3><dl>
  <dt>{T.company}</dt><dd>{m.companyId}</dd><dt>{T.identity}</dt><dd>{m.mappingVersion}</dd>
  <dt>{T.version}</dt><dd>{m.version}</dd><dt>{T.status}</dt><dd>{T.states[m.status]}</dd><dt>{T.effective}</dt><dd>{m.effectiveFrom}</dd>
  <dt>{T.preparedBy}</dt><dd>{m.preparedByUserId}</dd><dt>{T.preparedAt}</dt><dd>{m.preparedAtUtc}</dd>
  <dt>{T.activatedBy}</dt><dd>{m.activatedByUserId??T.absent}</dd><dt>{T.activatedAt}</dt><dd>{m.activatedAtUtc??T.absent}</dd>
  <dt>{T.activeVersion}</dt><dd>{human.expectedActiveMappingVersion}</dd>
 </dl><h4>{T.preparedPlan}</h4><dl><dt>{T.planVersion}</dt><dd>{human.preparedAccountPlan.sourceVersion}</dd><dt>{T.planHash}</dt><dd>{human.preparedAccountPlan.sourceSha256}</dd></dl>
 <table aria-label={T.rows}><thead><tr><th>{M.purpose}</th><th>{M.key}</th><th>{T.itemType}</th><th>{T.linePurpose}</th><th>{T.isExport}</th><th>{T.vatRateCode}</th><th>{T.accountId}</th><th>{T.code}</th></tr></thead>
 <tbody>{m.rows.map((r,index)=><tr key={index}><td>{M.purposes[r.purpose]}</td><td>{M.keys[r.mappingKey]}</td><td>{r.itemType??T.absent}</td><td>{r.linePurpose??T.absent}</td><td>{r.isExport===null?T.absent:r.isExport?T.yes:T.no}</td><td>{r.vatRateCode??T.absent}</td><td>{r.accountId}</td><td>{r.accountCode}</td></tr>)}</tbody></table>
 <h4>{T.currentPlan}</h4><dl><dt>{T.company}</dt><dd>{p.companyId}</dd><dt>{T.planVersion}</dt><dd>{p.reference.sourceVersion}</dd><dt>{T.planHash}</dt><dd>{p.reference.sourceSha256}</dd></dl>
 <table aria-label={T.accounts}><thead><tr><th>{T.code}</th><th>{T.name}</th><th>{T.accountId}</th></tr></thead><tbody>{p.accounts.map(a=><tr key={a.accountId}><td>{a.code}</td><td>{a.name}</td><td>{a.accountId}</td></tr>)}</tbody></table>
 </section>
}
