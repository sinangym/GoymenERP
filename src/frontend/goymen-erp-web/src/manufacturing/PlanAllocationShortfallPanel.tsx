import type { PlanAllocationShortfall } from './planAllocationShortfall'
import { PLAN_ALLOCATION_SHORTFALL_TEXT as T } from './manufacturingText'
import styles from './OrderDetailPage.module.css'

export function PlanAllocationShortfallPanel({ value }: { readonly value: PlanAllocationShortfall }) {
  return <section aria-label={T.heading} role="alert">
    <h3>{T.heading}</h3><p>{T.help}</p>
    <dl className={styles.card}>
      {[[T.product, value.productId], [T.label, value.labelId], [T.version, value.labelVersion],
        [T.requested, value.requestedKg], [T.available, value.availableKg], [T.missing, value.missingKg]].map(([label, text]) =>
        <div key={label} className={styles.field}><dt>{label}</dt><dd>{text}</dd></div>)}
    </dl>
  </section>
}
