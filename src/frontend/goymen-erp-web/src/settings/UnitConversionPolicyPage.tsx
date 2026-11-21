/**
 * Ayarlar > Birim çevrim politikası — F-004, KIM-D37 (`docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK9.md`). Standing
 * on `settings.unit-conversion-policies.manage` (company-wide, not Critical, granted to no role here).
 *
 * There is no seed: an authorized administrator enters every value per company, with a required reason, an expected version
 * and an Idempotency-Key; the server audits it. Until a version is in force the dependent calculations are refused
 * (fail-closed). The decision's suggestions are reminder text only and never pre-fill the form; the coefficient's rounding
 * direction is decided (KLN-D88, half and above away from zero), shown and sent as such, never chosen. The form starts from
 * the version in force, or empty.
 *
 * Remounted per committed context and route entry; a group (multi-company) context shows the no-company state; a version
 * conflict closes the form and re-reads; an unknown outcome keeps the same Idempotency-Key for "retry".
 */

import { useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { SETTINGS_CAPABILITY } from './capabilities'
import type { CommandOutcome } from './discountLimitApi'
import { isDateText } from './discountLimitContract'
import { businessDateLabel } from './discountLimitText'
import { percentLabel, stateLabel } from './salesPolicyText'
import {
  EffectiveFromField,
  Frame,
  effectiveFromBody,
  isEffectiveFromValid,
  stateOf,
  useBoundaryRead,
} from './SalesPolicyParts'
import { fetchUnitConversionPolicy, recordUnitConversionPolicyVersion } from './unitConversionPolicyApi'
import {
  DECIDED_COEFFICIENT_ROUNDING,
  UNIT_CONVERSION_BASES,
  UNIT_CONVERSION_BOUNDARIES,
  UNIT_CONVERSION_POLICY_INPUT,
  UNIT_CONVERSION_POLICY_STALE_CODES,
  UNIT_CONVERSION_ROUNDINGS,
  inForceUnitConversionPolicy,
  isDensityInput,
  isPercentInput,
  parseDecimals,
  type UnitConversionBase,
  type UnitConversionBoundary,
  type UnitConversionPolicyView,
  type UnitConversionRounding,
  type RecordUnitConversionPolicyBody,
} from './unitConversionPolicyContract'
import {
  UNIT_CONVERSION_TEXT as T,
  baseLabel,
  boundaryLabel,
  roundingLabel,
  roundingRuleLabel,
  unitConversionRefusalText,
} from './unitConversionPolicyText'
import { hasCommandFields, useScopedCommandAttempt, type AttemptStatus, type ScopedCommandAttempt } from './useCommandAttempt'
import styles from './settings.module.css'

function policyPayload(value: unknown): value is RecordUnitConversionPolicyBody {
  return hasCommandFields(value, { expectedVersion: 'string', densityGramsPerCubicCentimetre: 'string',
    coefficientDecimals: 'number', coefficientRounding: 'string', documentTotalDecimals: 'number',
    documentTotalRounding: 'string', wholeUnitRounding: 'string', weighbridgeTolerancePercent: 'string',
    toleranceBoundary: 'string', toleranceBase: 'string', reason: 'string', effectiveFrom: 'nullable-string' })
    && /^(0|[1-9][0-9]*)$/.test(value.expectedVersion as string)
    && Number.isInteger(value.coefficientDecimals) && Number.isInteger(value.documentTotalDecimals)
    && value.coefficientRounding === DECIDED_COEFFICIENT_ROUNDING
    && UNIT_CONVERSION_ROUNDINGS.some(entry => entry === value.documentTotalRounding)
    && UNIT_CONVERSION_ROUNDINGS.some(entry => entry === value.wholeUnitRounding)
    && UNIT_CONVERSION_BOUNDARIES.some(entry => entry === value.toleranceBoundary)
    && UNIT_CONVERSION_BASES.some(entry => entry === value.toleranceBase)
    && (value.effectiveFrom === null || isDateText(value.effectiveFrom))
}

export function UnitConversionPolicyPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <PolicyScreen key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}::${shell.sessionBinding ?? ''}`} namespace={namespace} />
}

function PolicyScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const ready = namespace !== null && singleCompany && hasCapability(shell, SETTINGS_CAPABILITY.unitConversionPolicy)
  const queryKey = [namespace ?? 'no-context', shell.routeEntryGeneration, 'settings-unit-conversion-policy']
  const view = useBoundaryRead(queryKey, ready, (signal) => fetchUnitConversionPolicy(companyId ?? '', signal))
  const attempt = useScopedCommandAttempt<RecordUnitConversionPolicyBody, true>('unit-conversion-policy-record',
    (body, idempotencyKey) => recordUnitConversionPolicyVersion({ companyId: companyId ?? '', idempotencyKey }, body), policyPayload)

  if (!singleCompany) {
    return (
      <Frame title={T.title} intro={T.intro}>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
      </Frame>
    )
  }

  if (!ready || companyId === null) {
    return (
      <Frame title={T.title} intro={T.intro}>
        <StateBlock state={{ kind: 'not-found' }} subject={T.subject} />
      </Frame>
    )
  }

  const state = stateOf<UnitConversionPolicyView>(view.isPending, view.data)
  return (
    <Frame title={T.title} intro={T.intro}>
      {state.kind === 'ready' ? (
        <PolicySection data={state.data} attempt={attempt} onChanged={() => void view.refetch()} />
      ) : (
        <StateBlock state={state} subject={T.subject} onRetry={() => void view.refetch()} />
      )}
    </Frame>
  )
}

function PolicySection({
  data,
  attempt,
  onChanged,
}: {
  readonly data: UnitConversionPolicyView
  readonly attempt: ScopedCommandAttempt<RecordUnitConversionPolicyBody, true>
  readonly onChanged: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const inForce = inForceUnitConversionPolicy(data.versions)
  const close = (saved: string | null) => {
    setEditing(false)
    setNotice(saved)
    if (saved !== null) onChanged()
  }
  const stale = (text: string) => {
    setEditing(false)
    setNotice(text)
    onChanged()
  }

  return (
    <section aria-labelledby="unit-conversion-policy-heading" className={styles.section}>
      {notice !== null ? (
        <p role="status" className={styles.notice}>
          {notice}
        </p>
      ) : null}
      <h2 id="unit-conversion-policy-heading">{T.tableLabel}</h2>
      {data.versions.length === 0 ? (
        <p className={styles.muted}>{T.empty}</p>
      ) : (
        <>
          {inForce === null ? <p className={styles.muted}>{T.noneInForce}</p> : null}
          <table className={styles.table}>
            <caption>{T.tableLabel}</caption>
            <thead>
              <tr>
                <th scope="col">{T.columnVersion}</th>
                <th scope="col">{T.columnDensity}</th>
                <th scope="col">{T.columnCoefficient}</th>
                <th scope="col">{T.columnDocumentTotal}</th>
                <th scope="col">{T.columnWholeUnit}</th>
                <th scope="col">{T.columnTolerance}</th>
                <th scope="col">{T.columnFrom}</th>
                <th scope="col">{T.columnThrough}</th>
                <th scope="col">{T.columnState}</th>
                <th scope="col">{T.columnRecorded}</th>
                <th scope="col">{T.columnReason}</th>
              </tr>
            </thead>
            <tbody>
              {data.versions.map((entry) => (
                <tr key={entry.version}>
                  <td>{entry.version}</td>
                  <td>{entry.densityGramsPerCubicCentimetre}</td>
                  <td>{roundingRuleLabel(entry.coefficientDecimals, entry.coefficientRounding)}</td>
                  <td>{roundingRuleLabel(entry.documentTotalDecimals, entry.documentTotalRounding)}</td>
                  <td>{roundingLabel(entry.wholeUnitRounding)}</td>
                  <td>
                    {percentLabel(entry.weighbridgeTolerancePercent)} · {boundaryLabel(entry.toleranceBoundary)} ·{' '}
                    {baseLabel(entry.toleranceBase)}
                  </td>
                  <td>{businessDateLabel(entry.effectiveFrom)}</td>
                  <td>{entry.effectiveThrough === null ? T.openEnded : businessDateLabel(entry.effectiveThrough)}</td>
                  <td>{stateLabel(entry.state)}</td>
                  <td>
                    <code>{entry.recordedBy}</code> · <time dateTime={entry.recordedAtUtc}>{entry.recordedAtUtc}</time>
                  </td>
                  <td>{entry.reason}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
      {editing || attempt.hasPendingRetry ? (
        <PolicyForm data={data} attempt={attempt} onDone={close} onStale={stale} />
      ) : (
        <div className={styles.actions}>
          <Button
            type="button"
            variant="primary"
            onClick={() => {
              setNotice(null)
              setEditing(true)
            }}
          >
            {T.newVersion}
          </Button>
        </div>
      )}
    </section>
  )
}

function PolicyForm({
  data,
  attempt,
  onDone,
  onStale,
}: {
  readonly data: UnitConversionPolicyView
  readonly attempt: ScopedCommandAttempt<RecordUnitConversionPolicyBody, true>
  readonly onDone: (notice: string | null) => void
  readonly onStale: (notice: string) => void
}) {
  // KIM-D37: the form starts from the version in force, or empty; never from the decision's suggestions.
  const base = attempt.pendingPayload ?? inForceUnitConversionPolicy(data.versions)
  const [density, setDensity] = useState(base?.densityGramsPerCubicCentimetre ?? '')
  const [coefficientDecimalsText, setCoefficientDecimalsText] = useState(
    base === null ? '' : String(base.coefficientDecimals),
  )
  const [documentTotalDecimalsText, setDocumentTotalDecimalsText] = useState(
    base === null ? '' : String(base.documentTotalDecimals),
  )
  const [documentTotalRounding, setDocumentTotalRounding] = useState<UnitConversionRounding | null>(
    base?.documentTotalRounding ?? null,
  )
  const [wholeUnitRounding, setWholeUnitRounding] = useState<UnitConversionRounding | null>(
    base?.wholeUnitRounding ?? null,
  )
  const [tolerance, setTolerance] = useState(base?.weighbridgeTolerancePercent ?? '')
  const [toleranceBoundary, setToleranceBoundary] = useState<UnitConversionBoundary | null>(
    base?.toleranceBoundary ?? null,
  )
  const [toleranceBase, setToleranceBase] = useState<UnitConversionBase | null>(base?.toleranceBase ?? null)
  const [effectiveFrom, setEffectiveFrom] = useState(attempt.pendingPayload?.effectiveFrom ?? data.today)
  const [reason, setReason] = useState(attempt.pendingPayload?.reason ?? '')
  const densityText = density.trim()
  const toleranceText = tolerance.trim()
  const coefficientDecimals = parseDecimals(coefficientDecimalsText)
  const documentTotalDecimals = parseDecimals(documentTotalDecimalsText)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (attempt.hasPendingRetry) {
      const outcome = await attempt.retry()
      if (outcome.type === 'applied') onDone(outcome.replayed ? T.savedReplay : T.saved)
      else {
        const stale = staleRefusal(outcome)
        if (stale !== null) onStale(stale)
      }
      return
    }
    if (
      !isDensityInput(densityText) ||
      !isPercentInput(toleranceText) ||
      coefficientDecimals === null ||
      documentTotalDecimals === null ||
      documentTotalRounding === null ||
      wholeUnitRounding === null ||
      toleranceBoundary === null ||
      toleranceBase === null ||
      !isEffectiveFromValid(effectiveFrom, data.today)
    ) {
      return
    }
    const body = {
      expectedVersion: data.version,
      densityGramsPerCubicCentimetre: densityText,
      coefficientDecimals,
      coefficientRounding: DECIDED_COEFFICIENT_ROUNDING,
      documentTotalDecimals,
      documentTotalRounding,
      wholeUnitRounding,
      weighbridgeTolerancePercent: toleranceText,
      toleranceBoundary,
      toleranceBase,
      reason: reason.trim(),
      effectiveFrom: effectiveFromBody(effectiveFrom, data.today),
    }
    const outcome = await attempt.run(body)
    if (outcome.type === 'applied') {
      onDone(outcome.replayed ? T.savedReplay : T.saved)
      return
    }
    const stale = staleRefusal(outcome)
    if (stale !== null) onStale(stale)
  }

  return (
    <form
      className={styles.form}
      onSubmit={(event) => void submit(event)}
      aria-labelledby="unit-conversion-policy-edit-heading"
    >
      <h3 id="unit-conversion-policy-edit-heading">{T.newVersion}</h3>
      <fieldset className={styles.commandFields} disabled={attempt.status.kind === 'busy' || attempt.hasPendingRetry}>
        <div className={styles.muted}>
          <p>{T.hintsHeading}</p>
          <ul aria-label={T.hintsHeading}>
            <li>{T.hintDensity}</li>
            <li>{T.hintCoefficientDecimals}</li>
            <li>{T.hintCoefficientRounding}</li>
            <li>{T.hintWholeUnit}</li>
            <li>{T.hintDocumentTotal}</li>
            <li>{T.hintTolerance}</li>
          </ul>
        </div>
        <Field
          label={T.densityLabel}
          help={T.densityHelp}
          error={densityText.length > 0 && !isDensityInput(densityText) ? T.densityError : undefined}
        >
          {(control) => (
            <input
              {...control}
              type="text"
              inputMode="decimal"
              required
              autoComplete="off"
              value={density}
              onChange={(event) => setDensity(event.target.value)}
            />
          )}
        </Field>
        <Field
          label={T.coefficientDecimalsLabel}
          help={T.decimalsHelp}
          error={coefficientDecimalsText.trim().length > 0 && coefficientDecimals === null ? T.decimalsError : undefined}
        >
          {(control) => (
            <input
              {...control}
              type="text"
              inputMode="numeric"
              required
              autoComplete="off"
              value={coefficientDecimalsText}
              onChange={(event) => setCoefficientDecimalsText(event.target.value)}
            />
          )}
        </Field>
        <fieldset className={styles.fieldset}>
          <legend>{T.coefficientRoundingLegend}</legend>
          <p>{roundingLabel(DECIDED_COEFFICIENT_ROUNDING)}</p>
          <p className={styles.muted}>{T.coefficientRoundingDecided}</p>
        </fieldset>
        <Field
          label={T.documentTotalDecimalsLabel}
          help={T.decimalsHelp}
          error={
            documentTotalDecimalsText.trim().length > 0 && documentTotalDecimals === null ? T.decimalsError : undefined
          }
        >
          {(control) => (
            <input
              {...control}
              type="text"
              inputMode="numeric"
              required
              autoComplete="off"
              value={documentTotalDecimalsText}
              onChange={(event) => setDocumentTotalDecimalsText(event.target.value)}
            />
          )}
        </Field>
        <RoundingChoice
          name="unit-conversion-document-total-rounding"
          legend={T.documentTotalRoundingLegend}
          value={documentTotalRounding}
          onChange={setDocumentTotalRounding}
        />
        <RoundingChoice
          name="unit-conversion-whole-unit-rounding"
          legend={T.wholeUnitRoundingLegend}
          value={wholeUnitRounding}
          onChange={setWholeUnitRounding}
        />
        <Field
          label={T.toleranceLabel}
          help={T.toleranceHelp}
          error={toleranceText.length > 0 && !isPercentInput(toleranceText) ? T.toleranceError : undefined}
        >
          {(control) => (
            <input
              {...control}
              type="text"
              inputMode="decimal"
              required
              autoComplete="off"
              value={tolerance}
              onChange={(event) => setTolerance(event.target.value)}
            />
          )}
        </Field>
        <fieldset className={styles.fieldset}>
          <legend>{T.boundaryLegend}</legend>
          {UNIT_CONVERSION_BOUNDARIES.map((boundary) => (
            <label key={boundary} className={styles.choice}>
              <input
                type="radio"
                name="unit-conversion-tolerance-boundary"
                required
                checked={toleranceBoundary === boundary}
                onChange={() => setToleranceBoundary(boundary)}
              />
              {boundaryLabel(boundary)}
            </label>
          ))}
        </fieldset>
        <fieldset className={styles.fieldset}>
          <legend>{T.baseLegend}</legend>
          {UNIT_CONVERSION_BASES.map((entry) => (
            <label key={entry} className={styles.choice}>
              <input
                type="radio"
                name="unit-conversion-tolerance-base"
                required
                checked={toleranceBase === entry}
                onChange={() => setToleranceBase(entry)}
              />
              {baseLabel(entry)}
            </label>
          ))}
        </fieldset>
        <EffectiveFromField value={effectiveFrom} today={data.today} onChange={setEffectiveFrom} />
        <Field label={T.reasonLabel} help={T.reasonHelp}>
          {(control) => (
            <textarea
              {...control}
              required
              rows={3}
              maxLength={UNIT_CONVERSION_POLICY_INPUT.reasonMaximumLength}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
            />
          )}
        </Field>
      </fieldset>
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => onDone(null)} />
    </form>
  )
}

function RoundingChoice({
  name,
  legend,
  value,
  onChange,
}: {
  readonly name: string
  readonly legend: string
  readonly value: UnitConversionRounding | null
  readonly onChange: (value: UnitConversionRounding) => void
}) {
  return (
    <fieldset className={styles.fieldset}>
      <legend>{legend}</legend>
      {UNIT_CONVERSION_ROUNDINGS.map((mode) => (
        <label key={mode} className={styles.choice}>
          <input type="radio" name={name} required checked={value === mode} onChange={() => onChange(mode)} />
          {roundingLabel(mode)}
        </label>
      ))}
    </fieldset>
  )
}

/** A refusal after which the screen's copy is stale (the version moved): close and re-read. */
function staleRefusal<D>(outcome: CommandOutcome<D>): string | null {
  if (outcome.type !== 'refused' || outcome.code === null) return null
  return UNIT_CONVERSION_POLICY_STALE_CODES.includes(outcome.code)
    ? unitConversionRefusalText(outcome.status, outcome.code)
    : null
}

function Footer<D>({
  status,
  hasPendingRetry,
  onCancel,
}: {
  readonly status: AttemptStatus<D>
  readonly hasPendingRetry: boolean
  readonly onCancel: () => void
}) {
  return (
    <>
      {status.kind === 'refused' ? (
        <p role="alert" className={styles.error}>
          {unitConversionRefusalText(status.status, status.code)}
        </p>
      ) : null}
      {status.kind === 'unknown' ? (
        <p role="alert" className={styles.error}>
          {T.unknownOutcome}
        </p>
      ) : null}
      <div className={styles.actions}>
        <Button type="submit" variant="primary" disabled={status.kind === 'busy'} aria-busy={status.kind === 'busy'}>
          {status.kind === 'busy' ? T.saving : hasPendingRetry ? T.retrySame : T.save}
        </Button>
        <Button type="button" variant="ghost" onClick={onCancel} disabled={status.kind === 'busy' || hasPendingRetry}>
          {T.cancel}
        </Button>
      </div>
    </>
  )
}
