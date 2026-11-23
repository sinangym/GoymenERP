/**
 * Ayarlar > Birim profilleri — F-029 S1/S2 (URUN_F029 spec §5.4(4); URN-D2…D5 of
 * `docs/decisions/SINAN_KARARLARI_2026-11-12_URUN.md`). Standing on `settings.unit-profiles.manage` (company-wide, not
 * Critical, granted to no role here).
 *
 * URN-D2: the profiles are managed on this screen with an effect preview before saving. URN-D3: one authorized manager
 * records, no second approval, every change audited. URN-D4: a new version takes effect from its start (today or later);
 * earlier records keep their values. URN-D5: until the company's one-time baseline transfer the tables show the approved
 * catalog (which still answers every read) and the only action is the transfer, values unchanged.
 *
 * The preview states the values the person typed next to the version in force, as the server sent it; it computes,
 * converts and rounds nothing (the backend applies the scale). Remounted per committed context and route entry; a group
 * (multi-company) context shows the no-company state; a version conflict or a baseline state change closes the form and
 * re-reads; an unknown outcome keeps the same Idempotency-Key for "retry".
 */

import { EditedAttemptNotice } from './EditedAttemptNotice'
import { useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { SETTINGS_CAPABILITY } from './capabilities'
import type { CommandOutcome } from './discountLimitApi'
import { isDateText, isUuid } from './discountLimitContract'
import { businessDateLabel } from './discountLimitText'
import {
  EffectiveFromField,
  Frame,
  MetaCells,
  MetaHeaders,
  effectiveFromBody,
  isEffectiveFromValid,
  stateOf,
  useBoundaryRead,
} from './SalesPolicyParts'
import { POLICY_TEXT } from './salesPolicyText'
import { fetchUnitProfiles, recordUnitProfileVersion, transferUnitProfileBaseline } from './unitProfileApi'
import {
  UNIT_PROFILE_INPUT,
  UNIT_PROFILE_STALE_CODES,
  inForceUnitProfile,
  parseScale,
  type UnitProfileUnit,
  type UnitProfileView,
  type RecordUnitProfileBody,
  type TransferUnitProfileBaselineBody,
} from './unitProfileContract'
import {
  UNIT_PROFILE_TEXT as T,
  activeLabel,
  profileValueLabel,
  scaleHelp,
  unitLabel,
  unitProfileRefusalText,
} from './unitProfileText'
import { hasCommandFields, useScopedCommandAttempt, type AttemptStatus, type ScopedCommandAttempt } from './useCommandAttempt'
import styles from './settings.module.css'

function baselinePayload(value: unknown): value is TransferUnitProfileBaselineBody {
  return hasCommandFields(value, { reason: 'string' })
}

function recordPayload(value: unknown): value is RecordUnitProfileBody {
  return hasCommandFields(value, { expectedVersion: 'string', unitId: 'string', scale: 'number', isActive: 'boolean',
    reason: 'string', effectiveFrom: 'nullable-string' }) && isUuid(value.unitId)
    && /^(0|[1-9][0-9]*)$/.test(value.expectedVersion as string) && Number.isInteger(value.scale)
    && (value.effectiveFrom === null || isDateText(value.effectiveFrom))
}

export function UnitProfilePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <ProfileScreen key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}::${shell.sessionBinding ?? ''}`} namespace={namespace} />
}

function ProfileScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const ready = namespace !== null && singleCompany && hasCapability(shell, SETTINGS_CAPABILITY.unitProfiles)
  const queryKey = [namespace ?? 'no-context', shell.routeEntryGeneration, 'settings-unit-profiles']
  const view = useBoundaryRead(queryKey, ready, (signal) => fetchUnitProfiles(companyId ?? '', signal))
  const baseline = useScopedCommandAttempt<TransferUnitProfileBaselineBody, true>('unit-profile-baseline',
    (body, idempotencyKey) => transferUnitProfileBaseline({ companyId: companyId ?? '', idempotencyKey }, body), baselinePayload)
  const record = useScopedCommandAttempt<RecordUnitProfileBody, true>('unit-profile-record',
    (body, idempotencyKey) => recordUnitProfileVersion({ companyId: companyId ?? '', idempotencyKey }, body), recordPayload)

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

  const state = stateOf<UnitProfileView>(view.isPending, view.data)
  return (
    <Frame title={T.title} intro={T.intro}>
      {state.kind === 'ready' ? (
        <ProfileSection data={state.data} baseline={baseline} record={record} onChanged={() => void view.refetch()} />
      ) : (
        <StateBlock state={state} subject={T.subject} onRetry={() => void view.refetch()} />
      )}
    </Frame>
  )
}

function ProfileSection({
  data,
  baseline,
  record,
  onChanged,
}: {
  readonly data: UnitProfileView
  readonly baseline: ScopedCommandAttempt<TransferUnitProfileBaselineBody, true>
  readonly record: ScopedCommandAttempt<RecordUnitProfileBody, true>
  readonly onChanged: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
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
    <section aria-labelledby="unit-profiles-heading" className={styles.section}>
      {notice !== null ? (
        <p role="status" className={styles.notice}>
          {notice}
        </p>
      ) : null}
      <h2 id="unit-profiles-heading">{T.unitTableLabel}</h2>
      <p className={styles.muted}>{POLICY_TEXT.oneManagerNote}</p>
      {data.units.length === 0 ? <p className={styles.muted}>{T.empty}</p> : null}
      {data.units.map((unit) => (
        <UnitTable key={unit.unitId} unit={unit} />
      ))}
      {baseline.hasPendingRetry || (!data.isBaselineTransferred && !record.hasPendingRetry) ? (
        <BaselineForm attempt={baseline} onApplied={close} onStale={stale} />
      ) : editing || record.hasPendingRetry ? (
        <RecordForm data={data} attempt={record} onDone={close} onStale={stale} />
      ) : (
        <div className={styles.actions}>
          <Button
            type="button"
            variant="primary"
            disabled={data.unitOptions.length === 0}
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

function UnitTable({ unit }: { readonly unit: UnitProfileUnit }) {
  const caption = `${T.unitTableLabel} — ${unitLabel(unit.unitCode, unit.dimensionCode)}`
  return (
    <>
      <h3>
        {T.unitHeading}: {unitLabel(unit.unitCode, unit.dimensionCode)}
        {unit.isKilogram ? <small> ({T.kilogramNote})</small> : null}
      </h3>
      {inForceUnitProfile(unit.versions) === null ? <p className={styles.muted}>{T.noneInForce}</p> : null}
      <table className={styles.table}>
        <caption>{caption}</caption>
        <thead>
          <tr>
            <th scope="col">{T.columnVersion}</th>
            <th scope="col">{T.columnScale}</th>
            <th scope="col">{T.columnActive}</th>
            <MetaHeaders />
          </tr>
        </thead>
        <tbody>
          {unit.versions.map((entry) => (
            <tr key={entry.version}>
              <td>{entry.version}</td>
              <td>{entry.scale}</td>
              <td>{activeLabel(entry.isActive)}</td>
              <MetaCells entry={entry} />
            </tr>
          ))}
        </tbody>
      </table>
    </>
  )
}

/** URN-D5: the one-time transfer of the approved catalog's entries, values unchanged, with its effect stated first. */
function BaselineForm({
  attempt,
  onApplied,
  onStale,
}: {
  readonly attempt: ScopedCommandAttempt<TransferUnitProfileBaselineBody, true>
  readonly onApplied: (notice: string | null) => void
  readonly onStale: (notice: string) => void
}) {
  const [reason, setReason] = useState(attempt.pendingPayload?.reason ?? '')

  async function submit(event: FormEvent) {
    event.preventDefault()
    const body = { reason: reason.trim() }
    const outcome = await (attempt.hasPendingRetry ? attempt.retry() : attempt.run(body))
    if (outcome.type === 'applied') {
      onApplied(outcome.replayed ? T.savedReplay : T.baselineDone)
      return
    }
    const stale = staleRefusal(outcome)
    if (stale !== null) onStale(stale)
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="unit-profiles-baseline">
      <h3 id="unit-profiles-baseline">{T.baselineHeading}</h3>
      <fieldset className={styles.commandFields} disabled={attempt.status.kind === 'busy' || attempt.hasPendingRetry}>
        <p className={styles.muted}>{T.baselinePending}</p>
        <p className={styles.notice}>{T.baselinePreview}</p>
        <ReasonField value={reason} onChange={setReason} />
      </fieldset>
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} submitLabel={T.baselineSubmit} />
    </form>
  )
}

function RecordForm({
  data,
  attempt,
  onDone,
  onStale,
}: {
  readonly data: UnitProfileView
  readonly attempt: ScopedCommandAttempt<RecordUnitProfileBody, true>
  readonly onDone: (notice: string | null) => void
  readonly onStale: (notice: string) => void
}) {
  const [unitId, setUnitId] = useState(attempt.pendingPayload?.unitId ?? '')
  const [scaleText, setScaleText] = useState(attempt.pendingPayload === null ? '' : String(attempt.pendingPayload.scale))
  const [isActive, setIsActive] = useState<boolean | null>(attempt.pendingPayload?.isActive ?? null)
  const [effectiveFrom, setEffectiveFrom] = useState(attempt.pendingPayload?.effectiveFrom ?? data.today)
  const [reason, setReason] = useState(attempt.pendingPayload?.reason ?? '')
  const option = data.unitOptions.find((entry) => entry.unitId === unitId) ?? null
  const unit = data.units.find((entry) => entry.unitId === unitId) ?? null
  const scale = option === null ? null : parseScale(scaleText, option.maximumScale)

  // The form starts from the chosen unit's version in force (or empty); the person changes what should change.
  function chooseUnit(next: string) {
    setUnitId(next)
    const current = inForceUnitProfile(data.units.find((entry) => entry.unitId === next)?.versions ?? [])
    setScaleText(current === null ? '' : String(current.scale))
    setIsActive(current === null ? null : current.isActive)
  }

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
    if (option === null || scale === null || isActive === null || !isEffectiveFromValid(effectiveFrom, data.today)) {
      return
    }
    const body = {
      expectedVersion: unit?.version ?? '0',
      unitId: option.unitId,
      scale,
      isActive,
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

  const current = unit === null ? null : inForceUnitProfile(unit.versions)
  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="unit-profiles-edit-heading">
      <h3 id="unit-profiles-edit-heading">{T.newVersion}</h3>
      <fieldset className={styles.commandFields} disabled={attempt.status.kind === 'busy' || attempt.hasPendingRetry}>
        <Field label={T.unitLabel} help={T.unitHelp}>
          {(control) => (
            <select {...control} required value={unitId} onChange={(event) => chooseUnit(event.target.value)}>
              <option value="">{T.unitPlaceholder}</option>
              {data.unitOptions.map((entry) => (
                <option key={entry.unitId} value={entry.unitId}>
                  {unitLabel(entry.unitCode, entry.dimensionCode)}
                </option>
              ))}
            </select>
          )}
        </Field>
        <Field
          label={T.scaleLabel}
          help={option === null ? undefined : scaleHelp(option.maximumScale)}
          error={option !== null && scaleText.trim().length > 0 && scale === null ? T.scaleError : undefined}
        >
          {(control) => (
            <input
              {...control}
              type="text"
              inputMode="numeric"
              required
              autoComplete="off"
              value={scaleText}
              onChange={(event) => setScaleText(event.target.value)}
            />
          )}
        </Field>
        <fieldset className={styles.fieldset}>
          <legend>{T.activeLegend}</legend>
          <label className={styles.choice}>
            <input
              type="radio"
              name="unit-profile-active"
              required
              checked={isActive === true}
              onChange={() => setIsActive(true)}
            />
            {T.activeOn}
          </label>
          <label className={styles.choice}>
            <input
              type="radio"
              name="unit-profile-active"
              required
              checked={isActive === false}
              onChange={() => setIsActive(false)}
            />
            {T.activeOff}
          </label>
        </fieldset>
        <EffectiveFromField value={effectiveFrom} today={data.today} onChange={setEffectiveFrom} />
        <section aria-label={T.previewHeading} className={styles.notice}>
          <h4>{T.previewHeading}</h4>
          {option === null || scale === null || isActive === null ? (
            <p>{T.previewIncomplete}</p>
          ) : (
            <ul>
              <li>
                {current === null
                  ? T.previewCurrentNone
                  : `${T.previewCurrentPrefix} ${profileValueLabel(current.scale, current.isActive)}`}
              </li>
              <li>
                {T.previewNextPrefix} {profileValueLabel(scale, isActive)}
              </li>
              <li>
                {T.previewFromPrefix}{' '}
                {isEffectiveFromValid(effectiveFrom, data.today) ? businessDateLabel(effectiveFrom) : POLICY_TEXT.notRecorded}
              </li>
              <li>{T.previewHistory}</li>
            </ul>
          )}
        </section>
        <ReasonField value={reason} onChange={setReason} />
      </fieldset>
      <Footer
        status={attempt.status}
        hasPendingRetry={attempt.hasPendingRetry}
        submitLabel={T.save}
        onCancel={() => onDone(null)}
      />
    </form>
  )
}

function ReasonField({ value, onChange }: { readonly value: string; readonly onChange: (value: string) => void }) {
  return (
    <Field label={POLICY_TEXT.reasonLabel} help={POLICY_TEXT.reasonHelp}>
      {(control) => (
        <textarea
          {...control}
          required
          rows={3}
          maxLength={UNIT_PROFILE_INPUT.reasonMaximumLength}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
    </Field>
  )
}

/** A refusal after which the screen's copy is stale (version moved, baseline state changed): close and re-read. */
function staleRefusal<D>(outcome: CommandOutcome<D>): string | null {
  if (outcome.type !== 'refused' || outcome.code === null) return null
  return UNIT_PROFILE_STALE_CODES.includes(outcome.code) ? unitProfileRefusalText(outcome.status, outcome.code) : null
}

function Footer<D>({
  status,
  hasPendingRetry,
  submitLabel,
  onCancel,
}: {
  readonly status: AttemptStatus<D>
  readonly hasPendingRetry: boolean
  readonly submitLabel: string
  readonly onCancel?: () => void
}) {
  return (
    <>
      {status.kind === 'refused' ? (
        <p role="alert" className={styles.error}>
          {unitProfileRefusalText(status.status, status.code)}
        </p>
      ) : null}
      <EditedAttemptNotice status={status} />
      {status.kind === 'unknown' ? (
        <p role="alert" className={styles.error}>
          {T.unknownOutcome}
        </p>
      ) : null}
      <div className={styles.actions}>
        <Button type="submit" variant="primary" disabled={status.kind === 'busy'} aria-busy={status.kind === 'busy'}>
          {status.kind === 'busy' ? T.saving : hasPendingRetry ? T.retrySame : submitLabel}
        </Button>
        {onCancel === undefined ? null : (
          <Button type="button" variant="ghost" onClick={onCancel} disabled={status.kind === 'busy' || hasPendingRetry}>
            {T.cancel}
          </Button>
        )}
      </div>
    </>
  )
}
