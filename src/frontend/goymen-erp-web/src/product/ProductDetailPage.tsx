/**
 * U-01 Ürün 360 — card, pinned to `GET /api/v1/products/{id}` (`ProductCardView`).
 *
 * Shows exactly what the card carries: the product core, the reference block and
 * the server's completeness verdict.
 *
 * WHAT IT REFUSES TO DO:
 *  - resolve a reference id itself. The labels come from the SERVER, on the
 *    projection; a client-side lookup would be the fan-out §19.5 forbids. When a
 *    label is null while the id is present, that says the catalogue could not be
 *    read — a different fact from "nothing assigned", and shown differently;
 *  - present `NotEvaluated` completeness as complete or activatable. README states
 *    that rule explicitly, and an unknown evaluation token is not coerced either.
 *
 * Not pulled in, per §9: physical stock/lot, price, accounting detail.
 */

import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { fetchProductCard } from './api'
import { PRODUCT_ROLE, UNIT_ROLE, isComplete, type ProductCard } from './contract'
import {
  COMPLETENESS_LABEL,
  COMPLETENESS_UNKNOWN_LABEL,
  PENDING_REFERENCE_NOTE,
  PRODUCT_DETAIL_TEXT,
  ROLE_LABEL,
  ROLE_PROVENANCE_LABEL,
  UNASSIGNED,
  UNIT_ROLE_LABEL,
  labelOrUnavailable,
  lifecycleLabel,
  lifecycleMeaning,
} from './productText'
import {
  PRODUCT_CAPABILITY,
  PRODUCT_CAPABILITY_ROUTE,
  productEditPath,
} from './capabilities'
import { classifyOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './ProductDetailPage.module.css'

function formatInstant(value: string): string {
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return value
  return parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

export function ProductDetailPage() {
  const shell = useShellState()
  const { productId } = useParams()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const origin = shell.activeContext?.companyId ?? null
  const boundary = useAuthBoundary()
  const entryGeneration = shell.routeEntryGeneration
  const canReturnToList = hasCapability(shell, PRODUCT_CAPABILITY.productsList)

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', entryGeneration, 'product-card', productId ?? ''],
    enabled: namespace !== null && origin !== null && productId !== undefined && productId.length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchProductCard(productId as string, { companyId: origin as string }, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const backLink = canReturnToList ? (
    <Link
      className={styles.backLink}
      to={PRODUCT_CAPABILITY_ROUTE[PRODUCT_CAPABILITY.productsList]}
    >
      ← {PRODUCT_DETAIL_TEXT.backToList}
    </Link>
  ) : null

  const outcome = query.data
  if (outcome === undefined || outcome.type !== 'ok-single') {
    return (
      <section className={styles.page} aria-label={PRODUCT_DETAIL_TEXT.identityHeading}>
        {backLink}
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject="ürün"
          onRetry={() => void query.refetch()}
        />
      </section>
    )
  }

  const card = outcome.data
  /**
   * The way into basic edit, gated on its OWN capability AND on the record being a
   * Draft — the domain refuses to basic-edit anything else (`lifecycle_conflict`),
   * so offering the control elsewhere would be offering a refusal.
   */
  const canEdit =
    hasCapability(shell, PRODUCT_CAPABILITY.productsEdit) &&
    card.product.lifecycle.known &&
    card.product.lifecycle.value === 'Draft'

  return (
    <ProductCardView
      card={card}
      backLink={backLink}
      editLink={
        canEdit ? (
          <Link className={styles.editLink} to={productEditPath(card.product.id)}>
            {PRODUCT_DETAIL_TEXT.edit}
          </Link>
        ) : null
      }
    />
  )
}

function ProductCardView({
  card,
  backLink,
  editLink,
}: {
  readonly card: ProductCard
  readonly backLink: React.ReactNode
  readonly editLink: React.ReactNode
}) {
  const { product, references } = card
  // Single source: the card's own `completeness` is a compatibility projection of
  // this one, so reading the product's removes any chance of the two disagreeing.
  const completeness = product.completeness

  return (
    <article className={styles.page} aria-labelledby="product-detail-heading">
      {backLink}

      <header className={styles.header}>
        <div>
          <p className={styles.code}>{product.code}</p>
          <h1 id="product-detail-heading" className={styles.title}>
            {product.name}
          </h1>
        </div>
        {/*
          WHERE AN ACTIVATE / DEACTIVATE CONTROL WOULD GO, and the rule it must
          follow. There is none today: the contract includes no activation command
          ("No activation command is included") and §6.6 keeps it fail-closed.

          When one is added it must be CLOSED for a lifecycle this build could not
          read, with the reason said out loud — an action whose meaning depends on
          the current state cannot be offered against a state we declined to
          interpret.
        */}
        <div className={styles.lifecycle}>
          {editLink}
          <span
            className={styles.badge}
            data-lifecycle={product.lifecycle.known ? product.lifecycle.value : 'unknown'}
          >
            {lifecycleLabel(product.lifecycle)}
          </span>
          <span className={styles.lifecycleMeaning}>{lifecycleMeaning(product.lifecycle)}</span>
        </div>
      </header>

      {/* Only for a KNOWN Inactive: an unrecognised status must not inherit this claim. */}
      {product.lifecycle.known && product.lifecycle.value === 'Inactive' ? (
        <p className={styles.inactiveBanner} role="status">
          Bu ürün her iki şirkette de yeni kullanıma kapalıdır. Geçmiş kayıtlar görünür kalır.
        </p>
      ) : null}

      <section className={styles.section} aria-labelledby="product-roles-heading">
        <h2 id="product-roles-heading">{PRODUCT_DETAIL_TEXT.rolesHeading}</h2>
        {/*
          One row per role with its provenance, NOT collapsed into a yes/no column.
          An unevaluated role must not be shown as `false`, and a derived role must
          be distinguishable from one a person typed — only one of the two is safe
          to overwrite by hand.
        */}
        <table className={styles.roleTable}>
          <caption>{PRODUCT_DETAIL_TEXT.rolesHeading}</caption>
          <thead>
            <tr>
              <th scope="col">Rol</th>
              <th scope="col">Durum</th>
              <th scope="col">Kaynak</th>
            </tr>
          </thead>
          <tbody>
            {PRODUCT_ROLE.map((role) => {
              const state = product.roles[role]
              return (
                <tr key={role}>
                  <th scope="row">{ROLE_LABEL[role]}</th>
                  <td data-testid={`product-role-${role}`}>
                    {state.value === null ? '—' : state.value ? 'Evet' : 'Hayır'}
                  </td>
                  <td>{ROLE_PROVENANCE_LABEL[state.provenance]}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </section>

      <section className={styles.section} aria-labelledby="product-completeness-heading">
        <h2 id="product-completeness-heading">{PRODUCT_DETAIL_TEXT.completenessHeading}</h2>
        {/*
          README: "NotEvaluated must not be rendered as complete or activatable." An
          unknown token is not coerced either — it reads as unknown, with its raw
          value, so the report that reaches the contract owner is actionable.
        */}
        <p className={styles.value} data-testid="product-completeness">
          {completeness.evaluation === null
            ? `${COMPLETENESS_UNKNOWN_LABEL}: ${completeness.rawEvaluation}`
            : COMPLETENESS_LABEL[completeness.evaluation]}
        </p>
        {isComplete(completeness) ? null : (
          <>
            <p className={styles.note}>{PRODUCT_DETAIL_TEXT.missingFields}</p>
            {completeness.missingFields.length === 0 ? (
              <p className={styles.empty}>{PRODUCT_DETAIL_TEXT.noMissingFields}</p>
            ) : (
              <ul className={styles.missingList} data-testid="product-missing-fields">
                {completeness.missingFields.map((field) => (
                  <li key={field}>{field}</li>
                ))}
              </ul>
            )}
          </>
        )}
      </section>

      <section className={styles.section} aria-labelledby="product-references-heading">
        <h2 id="product-references-heading">{PRODUCT_DETAIL_TEXT.referencesHeading}</h2>
        {/*
          Server-resolved labels, never a client lookup. A null label beside a
          present id means the catalogue could not be read; a null id means nothing
          is assigned — and the two read differently on purpose.
        */}
        <dl className={styles.definitionList}>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.groupId}</dt>
            <dd data-testid="product-group">
              {labelOrUnavailable(product.labels.group, product.groupId)}
            </dd>
          </div>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.classId}</dt>
            <dd data-testid="product-class">
              {labelOrUnavailable(product.labels.class, product.classId)}
            </dd>
          </div>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.familyId}</dt>
            <dd data-testid="product-family">
              {labelOrUnavailable(product.labels.family, product.familyId)}
            </dd>
          </div>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.primaryStockUnit}</dt>
            <dd data-testid="product-primary-stock-unit">
              {product.labels.primaryStockUnit ?? UNASSIGNED}
            </dd>
          </div>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.templateVersion}</dt>
            {/* Carried verbatim: a master change must not rewrite what a record meant. */}
            <dd>{references.templateVersion ?? UNASSIGNED}</dd>
          </div>
          {UNIT_ROLE.map((role) => (
            <div key={role}>
              <dt>{UNIT_ROLE_LABEL[role]}</dt>
              {/*
                An unassigned unit shows "atanmadı" and never falls back to the stock
                unit. DEC-PRD-006 keeps the six roles separate; a fallback would be
                the screen inventing a conversion.
              */}
              <dd className={styles.identifier} data-testid={`product-unit-${role}`}>
                {references.units[role] ?? UNASSIGNED}
              </dd>
            </div>
          ))}
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.trackingPolicy}</dt>
            <dd className={styles.identifier}>{references.trackingPolicyId ?? UNASSIGNED}</dd>
          </div>
        </dl>
        {/*
          DEC-PRD-003: the card holds the tracking POLICY reference; the lot, the
          origin and the quality/defect status belong to the physical label record.
          Without this an empty policy reads as "this product has no lots".
        */}
        <p className={styles.note}>{PRODUCT_DETAIL_TEXT.trackingPolicyNote}</p>
        <p className={styles.note}>{PENDING_REFERENCE_NOTE}</p>
      </section>

      <section className={styles.section} aria-labelledby="product-audit-heading">
        <h2 id="product-audit-heading">{PRODUCT_DETAIL_TEXT.auditHeading}</h2>
        <dl className={styles.definitionList}>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.createdAt}</dt>
            <dd>{formatInstant(product.createdAtUtc)}</dd>
          </div>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.updatedAt}</dt>
            <dd>{formatInstant(product.updatedAtUtc)}</dd>
          </div>
          <div>
            <dt>Sürüm</dt>
            <dd>{product.version}</dd>
          </div>
        </dl>
      </section>
    </article>
  )
}
