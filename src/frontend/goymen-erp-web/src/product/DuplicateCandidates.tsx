/**
 * Spec PRODUCT-U01-U02-JIT-001 §10.2/§15 — the server's read-only duplicate preview, shown as a WARNING.
 *
 * The server answers one decided rule (`exactName`: the same name after the write path's trim, under the
 * name column's Turkish case-insensitive collation, every lifecycle, the card itself left out). §10.2 locks
 * what a match means: a code/alias collision is a hard block (the server mints the code and the database
 * refuses a second holder), while a name match only warns and lets the person compare — it never blocks a
 * save or an approval and never merges. So this panel:
 *  - lists the other cards with a link to each (when the actor may open a card), code, name and lifecycle;
 *  - never disables a save or an approval, and says so; a failed check says so too and blocks nothing;
 *  - computes no similarity of its own: which technical attributes define "the same product" and any
 *    fuzzy rule are undecided (NEEDS-SINAN), so nothing here guesses one.
 *
 * The query side (fresh on every mount) is ./duplicateQuery.ts.
 */

import { Link } from 'react-router'
import { productDetailPath } from './capabilities'
import { lifecycleLabel, PRODUCT_DUPLICATE_TEXT } from './productText'
import type { DuplicateOutcome } from './duplicateQuery'
import styles from './DuplicateCandidates.module.css'

export function DuplicateCandidatesPanel({
  outcome,
  canOpenDetail,
  id,
  notice,
}: {
  readonly outcome: DuplicateOutcome | undefined
  readonly canOpenDetail: boolean
  /** For an `aria-describedby` from the field or control the warning is about. */
  readonly id: string
  /** An extra line above the result (e.g. "the list changed before approval"). */
  readonly notice?: string | null
}) {
  let body: React.ReactNode
  if (outcome === undefined || outcome.type === 'fence-stale') {
    body = <p className={styles.line}>{PRODUCT_DUPLICATE_TEXT.checking}</p>
  } else if (outcome.type === 'ok-single') {
    const { exactName, hasMore } = outcome.data
    body =
      exactName.length === 0 ? (
        <p className={styles.line}>{PRODUCT_DUPLICATE_TEXT.none}</p>
      ) : (
        <>
          <p className={styles.title}>{PRODUCT_DUPLICATE_TEXT.foundTitle}</p>
          <p className={styles.line}>{PRODUCT_DUPLICATE_TEXT.foundDetail}</p>
          <ul className={styles.list} data-testid="product-duplicate-candidates">
            {exactName.map((item) => (
              <li key={item.id}>
                {canOpenDetail ? (
                  <Link to={productDetailPath(item.id)}>{item.code}</Link>
                ) : (
                  <span>{item.code}</span>
                )}{' '}
                {item.name} — {lifecycleLabel(item.lifecycle)}
              </li>
            ))}
          </ul>
          {hasMore ? <p className={styles.line}>{PRODUCT_DUPLICATE_TEXT.more}</p> : null}
        </>
      )
  } else {
    body = <p className={styles.line}>{PRODUCT_DUPLICATE_TEXT.failed}</p>
  }
  const found = outcome?.type === 'ok-single' && outcome.data.exactName.length > 0
  return (
    <section
      id={id}
      className={found ? `${styles.panel} ${styles.found}` : styles.panel}
      aria-label={PRODUCT_DUPLICATE_TEXT.heading}
      data-testid="product-duplicate-check"
    >
      {/* A polite live region already on the page: the answer is announced without taking focus. */}
      <div aria-live="polite">
        {notice ? (
          <p className={styles.title} role="alert">
            {notice}
          </p>
        ) : null}
        {body}
      </div>
      <p className={styles.rule}>{PRODUCT_DUPLICATE_TEXT.rule}</p>
    </section>
  )
}
