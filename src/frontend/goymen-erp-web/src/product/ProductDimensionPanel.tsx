/**
 * BS-9 (2026-11-11): the "Ebat/kalınlık" tab of the product on the SHARED card (the same in every company; a holder of the name
 * in the selected company changes it for all) — the input the U-08 unit conversion reads. A product without a row has
 * "kaynak yok": the conversion makes no estimate. The density is the company Settings value and the card stores none.
 * Without `product.dimension.manage` (granted to no role here) the server answers 403 and the panel shows nothing; an ended
 * session (401), a missing card (404), an unwired provider (503) and a failed read each say so instead of "girilmemiş".
 * The form checks the shape only to help the person; the server and the database hold the rule.
 * KLN-D48 (STK-S37): the new section families; a structural profile names its series and size and shows the kg/m read from the
 * company's standard weight table with its source and version, or "kaynak yok" — it is never typed here (KLN-D54).
 */

import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  DIMENSION_MEASURES,
  DIMENSION_PROBLEM,
  DIMENSION_TEXTS,
  PRODUCT_SECTION_KINDS,
  PROFILE_SERIES,
  PROFILE_SIZE_MAXIMUM_LENGTH,
  SECTION_MEASURES,
  SURFACE_FINISH_MAXIMUM_LENGTH,
  fetchDimension,
  isProfileSize,
  isSurfaceFinish,
  parseMeasure,
  setDimension,
  type DimensionField,
  type DimensionMeasure,
  type DimensionMeasures,
  type DimensionTexts,
  type ProductDimensionView,
  type ProductSectionKind,
} from './dimensionApi'
import {
  DIMENSION_TEXT,
  KG_SOURCE_LABEL,
  MEASURE_LABEL,
  MEASURE_LABEL_FOR_KIND,
  SECTION_KIND_LABEL,
} from './dimensionText'
import styles from './ProductDetailPage.module.css'

const HEADING_ID = 'product-dimension-heading'

function Heading() {
  return <h2 id={HEADING_ID}>{DIMENSION_TEXT.heading}</h2>
}

function measureLabel(kind: ProductSectionKind, measure: DimensionMeasure): string {
  return MEASURE_LABEL_FOR_KIND[kind]?.[measure] ?? MEASURE_LABEL[measure]
}

/** A structural profile's kg/m: the table value with its source and version, or "kaynak yok" — never an estimate. */
function StandardWeight({ view }: { readonly view: ProductDimensionView }) {
  const weight = view.weight
  if (weight.source !== 'StandardTable' || weight.standardKgPerMetre === null) {
    return (
      <div data-testid="product-dimension-weight-no-source">
        <dt>{DIMENSION_TEXT.standardKgLabel}</dt>
        <dd>{DIMENSION_TEXT.standardKgNoSource}</dd>
      </div>
    )
  }
  return (
    <>
      <div data-testid="product-dimension-weight-standard">
        <dt>{DIMENSION_TEXT.standardKgLabel}</dt>
        <dd>{String(weight.standardKgPerMetre)}</dd>
      </div>
      <div>
        <dt>{DIMENSION_TEXT.standardKgSource}</dt>
        <dd>{weight.tableSourceLabel ?? KG_SOURCE_LABEL.StandardTable}</dd>
      </div>
      <div>
        <dt>{DIMENSION_TEXT.standardKgTable}</dt>
        <dd>{weight.tableVersion}</dd>
      </div>
    </>
  )
}

export function ProductDimensionPanel({
  companyId,
  productId,
  queryNamespace,
}: {
  readonly companyId: string
  readonly productId: string
  readonly queryNamespace: string
}) {
  const query = useQuery({
    queryKey: [queryNamespace, 'product-dimension', productId],
    queryFn: ({ signal }) => fetchDimension(companyId, productId, signal),
  })
  const [editing, setEditing] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)

  if (query.isPending) {
    return (
      <section className={styles.section} aria-labelledby={HEADING_ID} aria-busy="true">
        <Heading />
        <p className={styles.note}>{DIMENSION_TEXT.loading}</p>
      </section>
    )
  }

  const outcome = query.data
  if (outcome === undefined || outcome.type === 'forbidden') return null
  if (outcome.type === 'unauthenticated') {
    return (
      <section className={styles.section} aria-labelledby={HEADING_ID} data-testid="product-dimension-unauthenticated">
        <Heading />
        <p role="alert">{DIMENSION_TEXT.unauthenticated}</p>
      </section>
    )
  }

  if (outcome.type !== 'ok') {
    const message = outcome.type === 'not-found' ? DIMENSION_TEXT.notFound
      : outcome.type === 'unavailable' ? DIMENSION_TEXT.unavailable
        : DIMENSION_TEXT.failed
    return (
      <section className={styles.section} aria-labelledby={HEADING_ID} data-testid={`product-dimension-${outcome.type}`}>
        <Heading />
        <p role="alert">{message}</p>
        {outcome.type === 'not-found' ? null : (
          <button type="button" onClick={() => void query.refetch()}>
            {DIMENSION_TEXT.retryRead}
          </button>
        )}
      </section>
    )
  }

  const view = outcome.data
  return (
    <section className={styles.section} aria-labelledby={HEADING_ID}>
      <Heading />
      {notice !== null ? <p role="status">{notice}</p> : null}
      {view.sectionKind === null ? (
        <p data-testid="product-dimension-unset">{DIMENSION_TEXT.unset}</p>
      ) : (
        <dl data-testid="product-dimension-values">
          <div>
            <dt>{DIMENSION_TEXT.kindLabel}</dt>
            <dd>{SECTION_KIND_LABEL[view.sectionKind]}</dd>
          </div>
          {view.texts.profileSeries !== null && view.texts.profileSize !== null ? (
            <div>
              <dt>{DIMENSION_TEXT.profileSeriesLabel}</dt>
              <dd>{`${view.texts.profileSeries} ${view.texts.profileSize}`}</dd>
            </div>
          ) : null}
          {DIMENSION_MEASURES.filter((measure) => view.measures[measure] !== null).map((measure) => (
            <div key={measure}>
              <dt>{measureLabel(view.sectionKind as ProductSectionKind, measure)}</dt>
              <dd>{String(view.measures[measure])}</dd>
            </div>
          ))}
          {view.texts.surfaceFinish !== null ? (
            <div>
              <dt>{DIMENSION_TEXT.surfaceFinishLabel}</dt>
              <dd>{view.texts.surfaceFinish}</dd>
            </div>
          ) : null}
          {view.sectionKind === 'StructuralProfile' ? <StandardWeight view={view} /> : null}
        </dl>
      )}
      <p className={styles.note}>{DIMENSION_TEXT.shared}</p>
      {view.changedInAnotherCompany ? <p className={styles.note}>{DIMENSION_TEXT.changedElsewhere}</p> : null}
      <p className={styles.note}>{DIMENSION_TEXT.note}</p>
      {editing ? (
        <DimensionForm
          companyId={companyId}
          view={view}
          onDone={(saved) => {
            setEditing(false)
            setNotice(saved)
            void query.refetch()
          }}
        />
      ) : (
        <button type="button" onClick={() => setEditing(true)}>
          {DIMENSION_TEXT.change}
        </button>
      )}
    </section>
  )
}

type Draft = Record<DimensionField, string>

function draftOf(view: ProductDimensionView): Draft {
  return {
    ...Object.fromEntries(
      DIMENSION_MEASURES.map((measure) => [measure, view.measures[measure] === null ? '' : String(view.measures[measure])]),
    ),
    ...Object.fromEntries(DIMENSION_TEXTS.map((field) => [field, view.texts[field] ?? ''])),
  } as Draft
}

type Checked =
  | { readonly ok: true; readonly measures: DimensionMeasures; readonly texts: DimensionTexts }
  | { readonly ok: false; readonly error: string }

/** The kind's text fields from the draft (never re-cased or trimmed into something else: a mismatch is refused). */
function checkTexts(kind: ProductSectionKind, draft: Draft): { readonly ok: true; readonly texts: DimensionTexts } | { readonly ok: false; readonly error: string } {
  const texts = {} as Record<(typeof DIMENSION_TEXTS)[number], string | null>
  for (const field of DIMENSION_TEXTS) {
    const presence = SECTION_MEASURES[kind][field]
    const value = draft[field]
    if (presence === 'forbidden' || (value === '' && presence === 'optional')) {
      texts[field] = null
      continue
    }
    if (value === '') return { ok: false, error: DIMENSION_TEXT.measureMissing }
    const valid = field === 'profileSeries' ? PROFILE_SERIES.includes(value as (typeof PROFILE_SERIES)[number])
      : field === 'profileSize' ? isProfileSize(value) : isSurfaceFinish(value)
    if (!valid) return { ok: false, error: field === 'surfaceFinish' ? DIMENSION_TEXT.surfaceInvalid : DIMENSION_TEXT.profileInvalid }
    texts[field] = value
  }
  return { ok: true, texts }
}

/** The geometric fits of the kind (`ProductDimensionShape` fits); null when they hold. */
function fitError(kind: ProductSectionKind, measures: Record<DimensionMeasure, number | null>): string | null {
  const { thicknessMm: thickness, widthMm: width, heightMm: height, wallThicknessMm: wall } = measures
  const tube = kind === 'RoundTube' || kind === 'ErwTube' || kind === 'PaintedErwTube'
  const box = kind === 'Profile' || kind === 'ErwProfile' || kind === 'PaintedErwProfile'
  const outers = tube ? [measures.outerDiameterMm] : box ? [width, height] : []
  if (wall !== null && outers.some((outer) => outer !== null && wall + wall > outer)) return DIMENSION_TEXT.wallTooThick
  if (thickness !== null) {
    const strict = kind === 'EqualAngle' || kind === 'UnequalAngle' || kind === 'TeeBar'
    const bounded = kind === 'FlatBar' || kind === 'NarrowFlatBar'
    const sides = kind === 'UnequalAngle' || kind === 'TeeBar' ? [width, height] : [width]
    if (strict && sides.some((side) => side !== null && thickness >= side)) return DIMENSION_TEXT.thicknessTooLarge
    if (bounded && width !== null && thickness > width) return DIMENSION_TEXT.thicknessTooLarge
  }
  if (kind === 'WeldedMesh' && measures.outerDiameterMm !== null && measures.meshSpacingMm !== null
    && measures.outerDiameterMm >= measures.meshSpacingMm) {
    return DIMENSION_TEXT.wireTooThick
  }
  return null
}

/** The kind's fields from the draft; the others are sent as null, whatever was typed before the kind changed. */
function check(kind: ProductSectionKind, draft: Draft): Checked {
  const measures = {} as Record<DimensionMeasure, number | null>
  for (const measure of DIMENSION_MEASURES) {
    const presence = SECTION_MEASURES[kind][measure]
    if (presence === 'forbidden') {
      measures[measure] = null
      continue
    }
    const parsed = parseMeasure(measure, draft[measure])
    if (!parsed.ok) return { ok: false, error: DIMENSION_TEXT.measureFormat }
    if (parsed.value === null && presence === 'required') return { ok: false, error: DIMENSION_TEXT.measureMissing }
    measures[measure] = parsed.value
  }
  const texts = checkTexts(kind, draft)
  if (!texts.ok) return texts
  const fit = fitError(kind, measures)
  if (fit !== null) return { ok: false, error: fit }
  return { ok: true, measures, texts: texts.texts }
}

function DimensionForm({
  companyId,
  view,
  onDone,
}: {
  readonly companyId: string
  readonly view: ProductDimensionView
  readonly onDone: (notice: string | null) => void
}) {
  const [kind, setKind] = useState<ProductSectionKind | ''>(view.sectionKind ?? '')
  const [draft, setDraft] = useState<Draft>(() => draftOf(view))
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState<{ readonly key: string; readonly fingerprint: string } | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    // An unset product gets no preselected kind: the person chooses one explicitly.
    if (kind === '') return
    const checked = check(kind, draft)
    if (!checked.ok) {
      setError(checked.error)
      return
    }
    const body = { expectedVersion: view.version, sectionKind: kind, ...checked.measures, ...checked.texts, reason: reason.trim() }
    const fingerprint = JSON.stringify(body)
    const key = pending !== null && pending.fingerprint === fingerprint ? pending.key : crypto.randomUUID()
    setBusy(true)
    setError(null)
    const outcome = await setDimension(companyId, key, view.productId, body)
    setBusy(false)
    if (outcome.type === 'unknown') {
      setPending({ key, fingerprint })
      setError(DIMENSION_TEXT.unknownOutcome)
      return
    }

    setPending(null)
    if (outcome.type === 'applied') {
      onDone(DIMENSION_TEXT.saved)
    } else if (outcome.code === DIMENSION_PROBLEM.versionConflict) {
      onDone(DIMENSION_TEXT.conflict)
    } else {
      setError(
        outcome.code === DIMENSION_PROBLEM.reasonRequired ? DIMENSION_TEXT.reasonRequired
          : outcome.code === DIMENSION_PROBLEM.measureInvalid ? DIMENSION_TEXT.measureInvalid
            : DIMENSION_TEXT.refused,
      )
    }
  }

  return (
    <form onSubmit={(event) => void submit(event)} aria-label={DIMENSION_TEXT.formLabel} noValidate>
      <label>
        {DIMENSION_TEXT.kindLabel}
        <select required value={kind} onChange={(event) => setKind(event.target.value as ProductSectionKind | '')}>
          {view.sectionKind === null ? (
            <option value="" disabled>
              {DIMENSION_TEXT.choose}
            </option>
          ) : null}
          {PRODUCT_SECTION_KINDS.map((option) => (
            <option key={option} value={option}>
              {SECTION_KIND_LABEL[option]}
            </option>
          ))}
        </select>
      </label>
      {kind !== '' && SECTION_MEASURES[kind].profileSeries !== 'forbidden' ? (
        <>
          <label>
            {DIMENSION_TEXT.profileSeriesLabel}
            <select
              required
              value={draft.profileSeries}
              onChange={(event) => setDraft((current) => ({ ...current, profileSeries: event.target.value }))}
            >
              <option value="" disabled>
                {DIMENSION_TEXT.chooseSeries}
              </option>
              {PROFILE_SERIES.map((series) => (
                <option key={series} value={series}>
                  {series}
                </option>
              ))}
            </select>
          </label>
          <label>
            {DIMENSION_TEXT.profileSizeLabel}
            <input
              type="text"
              required
              maxLength={PROFILE_SIZE_MAXIMUM_LENGTH}
              aria-describedby="product-dimension-size-hint"
              value={draft.profileSize}
              onChange={(event) => setDraft((current) => ({ ...current, profileSize: event.target.value }))}
            />
          </label>
          <p id="product-dimension-size-hint" className={styles.note}>{DIMENSION_TEXT.profileSizeHint}</p>
        </>
      ) : null}
      {kind === ''
        ? null
        : DIMENSION_MEASURES.filter((measure) => SECTION_MEASURES[kind][measure] !== 'forbidden').map((measure) => (
          <label key={measure}>
            {measureLabel(kind, measure)}
            {SECTION_MEASURES[kind][measure] === 'optional' ? ` ${DIMENSION_TEXT.optional}` : ''}
            <input
              type="text"
              inputMode="decimal"
              required={SECTION_MEASURES[kind][measure] === 'required'}
              value={draft[measure]}
              onChange={(event) => setDraft((current) => ({ ...current, [measure]: event.target.value }))}
            />
          </label>
        ))}
      {kind !== '' && SECTION_MEASURES[kind].surfaceFinish !== 'forbidden' ? (
        <label>
          {DIMENSION_TEXT.surfaceFinishLabel}
          <input
            type="text"
            required={SECTION_MEASURES[kind].surfaceFinish === 'required'}
            maxLength={SURFACE_FINISH_MAXIMUM_LENGTH}
            value={draft.surfaceFinish}
            onChange={(event) => setDraft((current) => ({ ...current, surfaceFinish: event.target.value }))}
          />
        </label>
      ) : null}
      {kind === 'RoundTube' || kind === 'ErwTube' || kind === 'PaintedErwTube'
        ? <p className={styles.note}>{DIMENSION_TEXT.tubeKgNote}</p>
        : null}
      {kind === 'StructuralProfile' ? <p className={styles.note}>{DIMENSION_TEXT.standardKgNote}</p> : null}
      <label>
        {DIMENSION_TEXT.reasonLabel}
        <textarea required rows={2} maxLength={4000} value={reason} onChange={(event) => setReason(event.target.value)} />
      </label>
      {error !== null ? <p role="alert">{error}</p> : null}
      <button type="submit" disabled={busy || kind === ''} aria-busy={busy}>
        {busy ? DIMENSION_TEXT.saving : pending !== null ? DIMENSION_TEXT.retrySame : DIMENSION_TEXT.save}
      </button>
      <button type="button" disabled={busy} onClick={() => onDone(null)}>
        {DIMENSION_TEXT.cancel}
      </button>
    </form>
  )
}
