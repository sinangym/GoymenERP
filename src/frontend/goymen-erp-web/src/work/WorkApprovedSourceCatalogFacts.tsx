import type { WorkApprovedSourceCatalogPreview } from './workApprovedSourceCatalogDecision'
import { WORK_CATALOG_TEXT as T } from './workApprovedSourceCatalogText'

export function WorkApprovedSourceCatalogFacts({human}: { readonly human: WorkApprovedSourceCatalogPreview }) {
  const c = human.catalog
  return <section aria-label={T.title}><h3>{T.title}</h3><dl>
    <dt>{T.packageId}</dt><dd>{c.packageId}</dd><dt>{T.companyId}</dt><dd>{c.companyId}</dd>
    <dt>{T.status}</dt><dd>{T.states[c.status]}</dd><dt>{T.version}</dt><dd>{c.version}</dd>
    <dt>{T.catalogVersion}</dt><dd>{c.catalogVersion ?? T.absent}</dd><dt>{T.expectedActiveVersion}</dt><dd>{human.expectedActiveCatalogVersion}</dd>
    <dt>{T.template}</dt><dd>{c.templateVersion}</dd><dt>{T.plan}</dt><dd>{c.accountPlanSourceVersion}</dd>
    <dt>{T.accountsHash}</dt><dd>{c.accountsSourceSha256}</dd><dt>{T.mappingsHash}</dt><dd>{c.postingMappingsSourceSha256}</dd>
    <dt>{T.preparedBy}</dt><dd>{c.preparedByUserId}</dd><dt>{T.preparedAt}</dt><dd>{c.preparedAtUtc}</dd>
    <dt>{T.activatedBy}</dt><dd>{c.activatedByUserId ?? T.absent}</dd><dt>{T.activatedAt}</dt><dd>{c.activatedAtUtc ?? T.absent}</dd>
    <dt>{T.accountCount}</dt><dd>{c.accountCount}</dd><dt>{T.bindingCount}</dt><dd>{c.bindingCount}</dd>
  </dl><h4>{T.accounts}</h4><table aria-label={T.accounts}><thead><tr><th>{T.code}</th><th>{T.name}</th><th>{T.accountId}</th></tr></thead>
    <tbody>{human.package.accounts.map(a=><tr key={a.accountId}><td>{a.code}</td><td>{a.name}</td><td>{a.accountId}</td></tr>)}</tbody></table>
    <h4>{T.bindings}</h4>{human.package.bindings.map((b,index)=><section key={index} aria-label={`${T.sourceId}: ${b.sourceId}`}><dl>
      <dt>{T.sourceId}</dt><dd>{b.sourceId}</dd><dt>{T.sourceKind}</dt><dd>{b.sourceKind}</dd><dt>{T.sourceVersion}</dt><dd>{b.sourceVersion}</dd>
      <dt>{T.profile}</dt><dd>{b.ownerProfileId}</dd><dt>{T.profileVersion}</dt><dd>{b.ownerProfileVersion}</dd>
      <dt>{T.currency}</dt><dd>{b.currencyId}</dd><dt>{T.purpose}</dt><dd>{b.postingPurpose}</dd><dt>{T.rule}</dt><dd>{b.postingRuleVersion}</dd>
    </dl><table aria-label={`${T.mappings}: ${b.sourceId}`}><thead><tr><th>{T.mappingKey}</th><th>{T.side}</th><th>{T.accountId}</th></tr></thead>
      <tbody>{b.mappings.map(m=><tr key={`${m.mappingKey}:${m.side}`}><td>{m.mappingKey}</td><td>{m.side==='Debit'?T.debit:T.credit}</td><td>{m.accountId}</td></tr>)}</tbody></table></section>)}
  </section>
}
