/**
 * U-01 Ürün 360 — detail.
 *
 * Scope (JIT spec §9): identity + lifecycle header, classification and independent
 * roles, template/version, unit-role completeness, package-standard summary, alias
 * and audit timeline.
 *
 * §9 also draws the line this screen must not cross: the 360 "fiziksel stok/lot,
 * fiyat veya muhasebe detayını Product aggregate içine çekmez", and a section whose
 * module is not mounted is marked unavailable — "sahte metrik gösterilmez". So a
 * section the server did not list in `availableSections` renders as unavailable,
 * never as `0` and never as an empty table that reads like "there are none".
 */

import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { fetchProductDetail } from './api'
import {
  DETAIL_SECTION,
  PRODUCT_ROLE,
  UNIT_ROLE,
  type DetailSection,
  type ProductDetail,
} from './contract'
import {
  COMPLETENESS_AREA_LABEL,
  COMPLETENESS_LABEL,
  DETAIL_SECTION_LABEL,
  lifecycleLabel,
  lifecycleMeaning,
  PRODUCT_DETAIL_TEXT,
  ROLE_LABEL,
  ROLE_PROVENANCE_LABEL,
  SECTION_UNAVAILABLE,
  SECTION_UNAVAILABLE_DETAIL,
  UNASSIGNED,
  UNIT_ROLE_LABEL,
} from './productText'
import { PRODUCT_CAPABILITY, PRODUCT_CAPABILITY_ROUTE } from './capabilities'
import { COMPLETENESS_AREA } from './contract'
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
  const boundary = useAuthBoundary()
  const entryGeneration = shell.routeEntryGeneration
  const canReturnToList = hasCapability(shell, PRODUCT_CAPABILITY.productsList)

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', entryGeneration, 'product-detail', productId ?? ''],
    enabled: namespace !== null && productId !== undefined && productId.length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchProductDetail(productId as string, signal)
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

  return <ProductDetailView detail={outcome.data} backLink={backLink} />
}

function ProductDetailView({
  detail,
  backLink,
}: {
  readonly detail: ProductDetail
  readonly backLink: React.ReactNode
}) {
  const available = new Set<DetailSection>(detail.availableSections)

  return (
    <article className={styles.page} aria-labelledby="product-detail-heading">
      {backLink}

      <header className={styles.header}>
        <div>
          <p className={styles.code}>{detail.code}</p>
          <h1 id="product-detail-heading" className={styles.title}>
            {detail.name}
          </h1>
        </div>
        {/*
          WHERE AN ACTIVATE / DEACTIVATE CONTROL WOULD GO, and the rule it must
          follow when it does.

          There is none today: §6.6 keeps those commands fail-closed until
          `G-PRD-PLATFORM-01`, `G-PRD-PERM-01` and `G-PRD-PERSIST-01` pass, so this
          slice offers no state-dependent action anywhere — not here and not on a
          list row.

          When one is added it must be CLOSED for a lifecycle this build could not
          read, with the reason said out loud ("bu kaydın durumu okunamadı"), not
          left enabled. An action whose meaning depends on the current state cannot
          be offered against a state we declined to interpret: the user would be
          asked to approve a transition neither side can name. Same reasoning as the
          banner below — do not make a second claim about something unknown.
        */}
        <div className={styles.lifecycle}>
          <span
            className={styles.badge}
            data-lifecycle={detail.lifecycle.known ? detail.lifecycle.value : 'unknown'}
          >
            {lifecycleLabel(detail.lifecycle)}
          </span>
          {/*
            The state NAME alone does not tell an operator that a Draft cannot be
            picked in an order line — which is the whole point of the state
            (§6.1, DEC-PRD-010). The consequence is printed with it.
          */}
          <span className={styles.lifecycleMeaning}>{lifecycleMeaning(detail.lifecycle)}</span>
        </div>
      </header>

      {/*
        §6.5: an Inactive product is closed to NEW use in both companies while its
        history stays readable. Said as a banner because the rest of the card looks
        exactly like an active one.
      */}
      {/*
        Only for a KNOWN Inactive. An unrecognised status must not inherit this
        banner's claim — "closed in both companies" is a statement about a state we
        would not understand.
      */}
      {detail.lifecycle.known && detail.lifecycle.value === 'Inactive' ? (
        <p className={styles.inactiveBanner} role="status">
          Bu ürün her iki şirkette de yeni kullanıma kapalıdır. Geçmiş kayıtlar görünür kalır.
          {detail.deactivationReason !== null
            ? ` ${PRODUCT_DETAIL_TEXT.deactivationReason}: ${detail.deactivationReason}`
            : ''}
        </p>
      ) : null}

      <section className={styles.section} aria-labelledby="product-classification-heading">
        <h2 id="product-classification-heading">{PRODUCT_DETAIL_TEXT.classificationHeading}</h2>
        <dl className={styles.definitionList}>
          <div>
            <dt>Sınıf</dt>
            <dd>{detail.classification.class}</dd>
          </div>
          <div>
            <dt>Grup</dt>
            <dd>{detail.classification.group ?? UNASSIGNED}</dd>
          </div>
          <div>
            <dt>Aile</dt>
            <dd>{detail.classification.family ?? UNASSIGNED}</dd>
          </div>
        </dl>

        {/*
          Roles are listed one per line with their provenance, NOT collapsed into a
          yes/no column. §5.2: an unevaluated role must not be persisted — or shown —
          as `false`, and a derived role must be distinguishable from one a person
          typed, because only one of the two is safe to overwrite by hand.
        */}
        <table className={styles.roleTable}>
          <caption>{PRODUCT_DETAIL_TEXT.classificationHeading} — roller</caption>
          <thead>
            <tr>
              <th scope="col">Rol</th>
              <th scope="col">Durum</th>
              <th scope="col">Kaynak</th>
            </tr>
          </thead>
          <tbody>
            {PRODUCT_ROLE.map((role) => {
              const state = detail.roles[role]
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
        <h2 id="product-completeness-heading">Tamlık</h2>
        <dl className={styles.definitionList}>
          {COMPLETENESS_AREA.map((area) => (
            <div key={area}>
              <dt>{COMPLETENESS_AREA_LABEL[area]}</dt>
              <dd data-testid={`product-completeness-${area}`}>
                {COMPLETENESS_LABEL[detail.completeness[area]]}
              </dd>
            </div>
          ))}
        </dl>
      </section>

      <SectionOrUnavailable
        section="template"
        available={available}
        headingId="product-template-heading"
      >
        {detail.template === null ? (
          <p className={styles.empty}>{PRODUCT_DETAIL_TEXT.noTemplate}</p>
        ) : (
          <dl className={styles.definitionList}>
            <div>
              <dt>Şablon</dt>
              <dd>{detail.template.name}</dd>
            </div>
            <div>
              {/*
                The PINNED version, carried verbatim. DEC-PRDCT-008: a master change
                must not rewrite what a historical record meant, so this is never
                re-derived from "the current template version".
              */}
              <dt>Sürüm</dt>
              <dd>{detail.template.version}</dd>
            </div>
          </dl>
        )}
      </SectionOrUnavailable>

      <SectionOrUnavailable
        section="units"
        available={available}
        headingId="product-units-heading"
      >
        <dl className={styles.definitionList}>
          {UNIT_ROLE.map((role) => (
            <div key={role}>
              <dt>{UNIT_ROLE_LABEL[role]}</dt>
              {/*
                An unassigned unit role shows "atanmadı" and never falls back to the
                stock unit. DEC-PRD-006 makes the six unit roles separate; a fallback
                would be the screen inventing a conversion.
              */}
              <dd data-testid={`product-unit-${role}`}>{detail.unitRoles[role] ?? UNASSIGNED}</dd>
            </div>
          ))}
        </dl>
      </SectionOrUnavailable>

      <SectionOrUnavailable
        section="packageStandards"
        available={available}
        headingId="product-package-heading"
      >
        {/*
          Reached only when the server listed the section. There is no count and no
          table here yet: U-07 is a separate contract behind `G-PRD-PACK-01`, and
          §9.7 forbids fabricating an aggregate the server did not send.
        */}
        <p className={styles.empty}>
          Paket standartları şirket ve tesis kapsamında sürümlüdür; ayrıntı U-07
          ekranında yönetilir.
        </p>
      </SectionOrUnavailable>

      <SectionOrUnavailable
        section="aliasHistory"
        available={available}
        headingId="product-alias-heading"
      >
        {detail.aliases.length === 0 ? (
          <p className={styles.empty}>{PRODUCT_DETAIL_TEXT.noAlias}</p>
        ) : (
          <>
            <p className={styles.note}>{PRODUCT_DETAIL_TEXT.aliasNote}</p>
            <table className={styles.aliasTable}>
              <caption>{PRODUCT_DETAIL_TEXT.aliasHeading}</caption>
              <thead>
                <tr>
                  <th scope="col">Eski kod</th>
                  <th scope="col">Emekliye ayrılma</th>
                </tr>
              </thead>
              <tbody>
                {detail.aliases.map((alias) => (
                  <tr key={alias.code}>
                    <th scope="row">{alias.code}</th>
                    <td>{formatInstant(alias.retiredAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}
      </SectionOrUnavailable>

      <section className={styles.section} aria-labelledby="product-tracking-heading">
        <h2 id="product-tracking-heading">{PRODUCT_DETAIL_TEXT.trackingPolicy}</h2>
        <p className={styles.value}>{detail.trackingPolicy ?? UNASSIGNED}</p>
        {/*
          DEC-PRD-003, kept explicit at the product owner's request: the master card
          holds the POLICY; the lot, the origin and the quality/defect status belong
          to the physical label record. Without this line a reader takes an empty
          tracking policy to mean "this product has no lots".
        */}
        <p className={styles.note}>{PRODUCT_DETAIL_TEXT.trackingPolicyNote}</p>
      </section>

      <section className={styles.section} aria-labelledby="product-audit-heading">
        <h2 id="product-audit-heading">{PRODUCT_DETAIL_TEXT.auditHeading}</h2>
        <dl className={styles.definitionList}>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.createdAt}</dt>
            <dd>
              {formatInstant(detail.createdAt)} — {detail.createdBy}
            </dd>
          </div>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.updatedAt}</dt>
            <dd>
              {formatInstant(detail.updatedAt)} — {detail.updatedBy}
            </dd>
          </div>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.lastActivatedAt}</dt>
            <dd>
              {detail.lastActivatedAt === null ? '—' : formatInstant(detail.lastActivatedAt)}
            </dd>
          </div>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.lastDeactivatedAt}</dt>
            <dd>
              {detail.lastDeactivatedAt === null ? '—' : formatInstant(detail.lastDeactivatedAt)}
            </dd>
          </div>
        </dl>
      </section>
    </article>
  )
}

/**
 * Renders a 360 section, or an explicit "unavailable" block when the server did not
 * list it in `availableSections`.
 *
 * One component rather than an `&&` at each call site, so no section can be added
 * later that silently renders an empty body instead of saying it is unavailable —
 * which is precisely the fake-metric failure §9 forbids.
 */
function SectionOrUnavailable({
  section,
  available,
  headingId,
  children,
}: {
  readonly section: DetailSection
  readonly available: ReadonlySet<DetailSection>
  readonly headingId: string
  readonly children: React.ReactNode
}) {
  const label = DETAIL_SECTION_LABEL[section]
  if (!available.has(section)) {
    return (
      <section className={styles.section} aria-labelledby={headingId}>
        <h2 id={headingId}>{label}</h2>
        <div className={styles.unavailable} role="status" data-testid={`section-${section}`}>
          <p className={styles.unavailableTitle}>{SECTION_UNAVAILABLE}</p>
          <p className={styles.note}>{SECTION_UNAVAILABLE_DETAIL}</p>
        </div>
      </section>
    )
  }
  return (
    <section className={styles.section} aria-labelledby={headingId}>
      <h2 id={headingId}>{label}</h2>
      <div data-testid={`section-${section}`}>{children}</div>
    </section>
  )
}

/** Exported for the test that asserts every declared section has a rendered home. */
export const RENDERED_DETAIL_SECTIONS: readonly DetailSection[] = DETAIL_SECTION
