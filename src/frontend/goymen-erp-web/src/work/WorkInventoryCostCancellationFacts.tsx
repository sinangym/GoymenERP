import { WorkInventoryCostBusinessValues } from './WorkInventoryCostFacts'
import { INVENTORY_COST_CANCELLATION_WORK_TEXT as T, type WorkInventoryCostCancellationPreview } from './workInventoryCostCancellationDecision'
import styles from './Work.module.css'

export function WorkInventoryCostCancellationFacts({human}:{readonly human:WorkInventoryCostCancellationPreview}) {
  return <section aria-label={T.title}>
    <h3>{T.title}</h3><p>{T.readOnly}</p>
    <section aria-label={T.original}><h4>{T.original}</h4><WorkInventoryCostBusinessValues value={human.originalProposal}/></section>
    <section aria-label={T.prepared}><h4>{T.prepared}</h4><WorkInventoryCostBusinessValues value={{preparedRecordedAtUtc:human.preparedRecordedAtUtc,...human.preparedCancellation}}/></section>
    <section aria-label={T.current}><h4>{T.current}</h4><WorkInventoryCostBusinessValues value={human.cancellation}/></section>
    <section aria-label={T.source}><h4>{T.source}</h4><dl className={styles.facts}>
      {Object.entries(human.sourceCancellation).filter(([key])=>Object.hasOwn(T.sourceFields,key)).map(([key,value])=><div key={key}>
        <dt>{T.sourceFields[key as keyof typeof T.sourceFields]}</dt><dd style={key==='reason'?{whiteSpace:'pre-wrap'}:undefined}>{key==='documentKind'?T.salesInvoice:value}</dd>
      </div>)}
    </dl></section>
  </section>
}
