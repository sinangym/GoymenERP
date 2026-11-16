/**
 * BS-9 (2026-11-11): the "Ebat/kalınlık" tab of the product on the SHARED card (the same in every company; a holder of the name
 * in the selected company changes it for all) — the input the U-08 unit conversion reads. A product without a row has
 * "kaynak yok": the conversion makes no estimate. The density is the company Settings value and the card stores none.
 * Without `product.dimension.manage` (granted to no role here) the server answers 403 and the panel shows nothing; an ended
 * session (401), a missing card (404), an unwired provider (503) and a failed read each say so instead of "girilmemiş".
 * The form checks the shape only to help the person; the server and the database hold the rule. URN-D16 (STK-S37R): the kg/m is
 * not an input; the panel shows the acting company's table value with its source label and table version, or that the table
 * holds none, and reads the server's refusal of a profile the table does not hold.
 */

import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  DIMENSION_MEASURES,
  DIMENSION_PROBLEM,
  PRODUCT_SECTION_KINDS,
  SECTION_MEASURES,
  SECTION_TABLE_WEIGHT,
  fetchDimension,
  parseMeasure,
  setDimension,
  type DimensionMeasure,
  type DimensionMeasures,
  type ProductDimensionView,
  type ProductSectionKind,
} from './dimensionApi'
import { DIMENSION_TEXT, MEASURE_LABEL, SECTION_KIND_LABEL } from './dimensionText'
import styles from './ProductDetailPage.module.css'

const HEADING_ID = 'product-dimension-heading'

function Heading() {
  return <h2 id={HEADING_ID}>{DIMENSION_TEXT.heading}</h2>
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
          {DIMENSION_MEASURES.filter((measure) => view.measures[measure] !== null).map((measure) => (
            <div key={measure}>
              <dt>{MEASURE_LABEL[measure]}</dt>
              <dd>{String(view.measures[measure])}</dd>
            </div>
          ))}
          {view.tableWeight !== null ? (
            <>
              <div>
                <dt>{DIMENSION_TEXT.kgPerMetreLabel}</dt>
                <dd data-testid="product-dimension-kg-per-metre">{String(view.tableWeight.kgPerMetre)}</dd>
              </div>
              <div>
                <dt>{DIMENSION_TEXT.kgPerMetreSource}</dt>
                <dd>{view.tableWeight.sourceLabel}</dd>
              </div>
              <div>
                <dt>{DIMENSION_TEXT.kgPerMetreTableVersion}</dt>
                <dd>{view.tableWeight.tableVersion}</dd>
              </div>
            </>
          ) : SECTION_TABLE_WEIGHT[view.sectionKind] === 'none' ? null : (
            <div>
              <dt>{DIMENSION_TEXT.kgPerMetreLabel}</dt>
              <dd data-testid="product-dimension-kg-per-metre-missing">
                {SECTION_TABLE_WEIGHT[view.sectionKind] === 'required'
                  ? DIMENSION_TEXT.kgPerMetreMissing
                  : DIMENSION_TEXT.kgPerMetreTubeMissing}
              </dd>
            </div>
          )}
        </dl>
      )}
      <p className={styles.note}>{DIMENSION_TEXT.shared}</p>
      {view.changedInAnotherCompany ? <p className={styles.note}>{DIMENSION_TEXT.changedElsewhere}</p> : null}
      <p className={styles.note}>{DIMENSION_TEXT.note}</p>
      <p className={styles.note}>{DIMENSION_TEXT.weightNote}</p>
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

type Draft = Record<DimensionMeasure, string>

function draftOf(view: ProductDimensionView): Draft {
  return Object.fromEntries(
    DIMENSION_MEASURES.map((measure) => [measure, view.measures[measure] === null ? '' : String(view.measures[measure])]),
  ) as Draft
}

type Checked = { readonly ok: true; readonly measures: DimensionMeasures } | { readonly ok: false; readonly error: string }

/** The kind's measures from the draft; the others are sent as null, whatever was typed before the kind changed. */
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
  const wall = measures.wallThicknessMm
  const outers = kind === 'RoundTube' ? [measures.outerDiameterMm] : kind === 'Profile' ? [measures.widthMm, measures.heightMm] : []
  if (wall !== null && outers.some((outer) => outer !== null && wall + wall > outer)) {
    return { ok: false, error: DIMENSION_TEXT.wallTooThick }
  }
  return { ok: true, measures }
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
    const body = { expectedVersion: view.version, sectionKind: kind, ...checked.measures, reason: reason.trim() }
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
            : outcome.code === DIMENSION_PROBLEM.weightSourceMissing ? DIMENSION_TEXT.weightSourceMissing
              : outcome.code === DIMENSION_PROBLEM.weightSourceConflict ? DIMENSION_TEXT.weightSourceConflict
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
      {kind === ''
        ? null
        : DIMENSION_MEASURES.filter((measure) => SECTION_MEASURES[kind][measure] !== 'forbidden').map((measure) => (
          <label key={measure}>
            {MEASURE_LABEL[measure]}
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
      {kind === 'RoundTube' ? <p className={styles.note}>{DIMENSION_TEXT.tubeKgNote}</p> : null}
      {kind === 'Profile' ? <p className={styles.note}>{DIMENSION_TEXT.profileWeightNote}</p> : null}
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
