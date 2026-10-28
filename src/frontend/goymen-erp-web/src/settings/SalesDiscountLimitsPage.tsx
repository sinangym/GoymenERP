/**
 * Ayarlar > İskonto limitleri — SAT-D61…D70 (EK10/EK11).
 *
 * Two sections on one screen, both standing on the one name `settings.sales-discount-limits.manage`:
 *  - LIMIT GROUPS of the selected company (separate from roles, SAT-D63): create, rename, deactivate (never delete), and
 *    record the limit — a fixed percent or another group's limit + extra points (SAT-D62). The screen never computes a
 *    limit: "Bugün geçerli limit" is the backend calculator's answer, shown as given.
 *  - PERSON → GROUP binding (SAT-D63/D66): search a person of the company, see their groups in every company the actor
 *    can act in, and write the chosen group CODES to all of them at once (one all-or-nothing server transaction).
 *
 * Rules that shape this file: remounted per committed context and route entry (no company's data under another's label);
 * a group (multi-company) context shows the no-company state; a reason is required for every change (SAT-D68) and the
 * server is the judge of it; a version conflict re-reads; an unknown outcome keeps the same Idempotency-Key for "retry".
 */

import { useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useCompanyDirectory } from '../shell/useShellOrchestrator'
import { SETTINGS_CAPABILITY } from './capabilities'
import {
  createLimitGroup,
  deactivateLimitGroup,
  fetchBinding,
  fetchLimitGroups,
  fetchPeople,
  renameLimitGroup,
  setBinding,
  correctGroupLimit,
  setGroupLimit,
  withdrawGroupLimit,
  type CommandOutcome,
} from './discountLimitApi'
import {
  LIMIT_INPUT,
  LIMIT_KIND,
  LIMIT_PROBLEM,
  isDateText,
  isPercentText,
  isValidCode,
  normalizeCode,
  type Binding,
  type LimitDefinition,
  type LimitGroup,
  type LimitGroupList,
  type LimitKind,
  type Person,
  type PersonList,
} from './discountLimitContract'
import {
  LIMIT_TEXT,
  businessDateLabel,
  definitionLabel,
  percentLabel,
  refusalText,
  scheduledLabel,
} from './discountLimitText'
import { useCommandAttempt, type AttemptStatus } from './useCommandAttempt'
import styles from './settings.module.css'

export function SalesDiscountLimitsPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <LimitScreen key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

/** Runs one read through the auth boundary, so a real 401/403 is reported like everywhere else in the shell. */
function useBoundaryRead<T>(
  queryKey: readonly unknown[],
  enabled: boolean,
  read: (signal: AbortSignal) => Promise<ViewOutcome<T>>,
) {
  const boundary = useAuthBoundary()
  return useQuery({
    queryKey,
    enabled,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await read(signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
}

function stateOf<T>(pending: boolean, data: ViewOutcome<T> | undefined): RemoteViewState<T> {
  return (pending ? { kind: 'initial-loading' } : classifyOutcome(data ?? { type: 'malformed' })) as RemoteViewState<T>
}

function LimitScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const ready = namespace !== null && singleCompany && hasCapability(shell, SETTINGS_CAPABILITY.salesDiscountLimits)
  const groupsKey = [namespace ?? 'no-context', shell.routeEntryGeneration, 'settings-discount-limit-groups']
  const groups = useBoundaryRead(groupsKey, ready, (signal) => fetchLimitGroups(companyId ?? '', signal))

  if (!singleCompany) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={LIMIT_TEXT.contextSubject} />
      </Frame>
    )
  }

  if (!ready || companyId === null) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'not-found' }} subject={LIMIT_TEXT.contextSubject} />
      </Frame>
    )
  }

  const groupsState = stateOf<LimitGroupList>(groups.isPending, groups.data)
  return (
    <Frame>
      <section aria-labelledby="limit-groups-heading" className={styles.section}>
        <h2 id="limit-groups-heading">{LIMIT_TEXT.groupsHeading}</h2>
        {groupsState.kind === 'ready' ? (
          <GroupsSection list={groupsState.data} companyId={companyId} onChanged={() => void groups.refetch()} />
        ) : (
          <StateBlock state={groupsState} subject={LIMIT_TEXT.groupsSubject} onRetry={() => void groups.refetch()} />
        )}
      </section>
      <section aria-labelledby="limit-binding-heading" className={styles.section}>
        <h2 id="limit-binding-heading">{LIMIT_TEXT.bindingHeading}</h2>
        <p className={styles.muted}>{LIMIT_TEXT.bindingIntro}</p>
        {groupsState.kind === 'ready' ? (
          <BindingSection namespace={namespace ?? ''} companyId={companyId} groups={groupsState.data.groups} />
        ) : null}
      </section>
    </Frame>
  )
}

function Frame({ children }: { readonly children: ReactNode }) {
  return (
    <div className={styles.page}>
      <h1>{LIMIT_TEXT.title}</h1>
      <p className={styles.muted}>{LIMIT_TEXT.intro}</p>
      {children}
    </div>
  )
}

type Editor =
  | { readonly kind: 'none' }
  | { readonly kind: 'create' }
  | { readonly kind: 'rename'; readonly group: LimitGroup }
  | { readonly kind: 'limit'; readonly group: LimitGroup }
  | { readonly kind: 'deactivate'; readonly group: LimitGroup }
  | { readonly kind: 'correct'; readonly group: LimitGroup; readonly record: LimitDefinition }
  | { readonly kind: 'withdraw'; readonly group: LimitGroup; readonly record: LimitDefinition }

function GroupsSection({
  list,
  companyId,
  onChanged,
}: {
  readonly list: LimitGroupList
  readonly companyId: string
  readonly onChanged: () => void
}) {
  const [editor, setEditor] = useState<Editor>({ kind: 'none' })
  const [notice, setNotice] = useState<string | null>(null)
  const byId = useMemo(() => new Map(list.groups.map((group) => [group.id, group])), [list.groups])
  const close = (savedNotice: string | null) => {
    setEditor({ kind: 'none' })
    setNotice(savedNotice)
    if (savedNotice !== null) onChanged()
  }
  /** A version conflict closes the form and re-reads, so the next attempt starts from the current record. */
  const conflict = () => {
    setEditor({ kind: 'none' })
    setNotice(refusalText(409, LIMIT_PROBLEM.versionConflict, null, null))
    onChanged()
  }

  return (
    <>
      {notice !== null ? (
        <p role="status" className={styles.notice}>
          {notice}
        </p>
      ) : null}
      {list.groups.length === 0 ? (
        <p className={styles.muted}>{LIMIT_TEXT.noGroups}</p>
      ) : (
        <table aria-label={LIMIT_TEXT.groupsTableLabel} className={styles.table}>
          <thead>
            <tr>
              <th scope="col">{LIMIT_TEXT.columnCode}</th>
              <th scope="col">{LIMIT_TEXT.columnName}</th>
              <th scope="col">{LIMIT_TEXT.columnStatus}</th>
              <th scope="col">{LIMIT_TEXT.columnRecorded}</th>
              <th scope="col">{LIMIT_TEXT.columnEffective}</th>
              <th scope="col">{LIMIT_TEXT.columnScheduled}</th>
              <th scope="col">{LIMIT_TEXT.columnActions}</th>
            </tr>
          </thead>
          <tbody>
            {list.groups.map((group) => (
              <tr key={group.id}>
                <td>
                  <code>{group.code}</code>
                </td>
                <td>{group.name}</td>
                <td>{group.isActive ? LIMIT_TEXT.active : LIMIT_TEXT.passive}</td>
                <td>
                  {group.limit === null
                    ? LIMIT_TEXT.noLimitRecorded
                    : definitionLabel(
                        group.limit,
                        group.limit.linkedGroupId === null ? null : (byId.get(group.limit.linkedGroupId)?.code ?? null),
                      )}
                </td>
                <td>
                  {group.effectiveLimit.state === 'Defined' && group.effectiveLimit.percent !== null
                    ? percentLabel(group.effectiveLimit.percent)
                    : LIMIT_TEXT.noLimit}
                </td>
                <td>
                  {group.scheduled.length === 0 ? (
                    LIMIT_TEXT.noScheduled
                  ) : (
                    <ul aria-label={`${LIMIT_TEXT.scheduledListLabel} — ${group.code}`} className={styles.plainList}>
                      {group.scheduled.map((entry) => (
                        <li key={entry.limit.limitVersion}>
                          <span>
                            {scheduledLabel(
                              entry.limit.effectiveFrom,
                              definitionLabel(
                                entry.limit,
                                entry.limit.linkedGroupId === null
                                  ? null
                                  : (byId.get(entry.limit.linkedGroupId)?.code ?? null),
                              ),
                              entry.effectiveLimit.state === 'Defined' ? entry.effectiveLimit.percent : null,
                            )}
                          </span>
                          {group.isActive ? (
                            <span className={styles.actions}>
                              <Button
                                type="button"
                                size="sm"
                                variant="ghost"
                                aria-label={`${LIMIT_TEXT.correctScheduled} — ${group.code} ${businessDateLabel(entry.limit.effectiveFrom)}`}
                                onClick={() => setEditor({ kind: 'correct', group, record: entry.limit })}
                              >
                                {LIMIT_TEXT.correctScheduled}
                              </Button>
                              <Button
                                type="button"
                                size="sm"
                                variant="danger"
                                aria-label={`${LIMIT_TEXT.withdrawScheduled} — ${group.code} ${businessDateLabel(entry.limit.effectiveFrom)}`}
                                onClick={() => setEditor({ kind: 'withdraw', group, record: entry.limit })}
                              >
                                {LIMIT_TEXT.withdrawScheduled}
                              </Button>
                            </span>
                          ) : null}
                        </li>
                      ))}
                    </ul>
                  )}
                </td>
                <td>
                  {group.isActive ? (
                    <span className={styles.actions}>
                      <Button
                        type="button"
                        size="sm"
                        aria-label={`${LIMIT_TEXT.editLimit} — ${group.code}`}
                        onClick={() => setEditor({ kind: 'limit', group })}
                      >
                        {LIMIT_TEXT.editLimit}
                      </Button>
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        aria-label={`${LIMIT_TEXT.rename} — ${group.code}`}
                        onClick={() => setEditor({ kind: 'rename', group })}
                      >
                        {LIMIT_TEXT.rename}
                      </Button>
                      <Button
                        type="button"
                        size="sm"
                        variant="danger"
                        aria-label={`${LIMIT_TEXT.deactivate} — ${group.code}`}
                        onClick={() => setEditor({ kind: 'deactivate', group })}
                      >
                        {LIMIT_TEXT.deactivate}
                      </Button>
                    </span>
                  ) : null}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {editor.kind === 'none' ? (
        <Button type="button" variant="primary" onClick={() => setEditor({ kind: 'create' })}>
          {LIMIT_TEXT.newGroup}
        </Button>
      ) : null}
      {editor.kind === 'create' ? (
        <CreateGroupForm companyId={companyId} onDone={close} onConflict={conflict} />
      ) : null}
      {editor.kind === 'rename' ? (
        <RenameGroupForm key={editor.group.id} companyId={companyId} group={editor.group} onDone={close} onConflict={conflict} />
      ) : null}
      {editor.kind === 'deactivate' ? (
        <DeactivateGroupForm
          key={editor.group.id}
          companyId={companyId}
          group={editor.group}
          onDone={close}
          onConflict={conflict}
        />
      ) : null}
      {editor.kind === 'limit' ? (
        <LimitForm
          key={editor.group.id}
          companyId={companyId}
          group={editor.group}
          groups={list.groups}
          businessDate={list.businessDate}
          onDone={close}
          onConflict={conflict}
        />
      ) : null}
      {editor.kind === 'correct' ? (
        <LimitForm
          key={`${editor.group.id}:${editor.record.limitVersion}`}
          companyId={companyId}
          group={editor.group}
          groups={list.groups}
          businessDate={list.businessDate}
          correcting={editor.record}
          onDone={close}
          onConflict={conflict}
        />
      ) : null}
      {editor.kind === 'withdraw' ? (
        <WithdrawLimitForm
          key={`${editor.group.id}:${editor.record.limitVersion}`}
          companyId={companyId}
          group={editor.group}
          record={editor.record}
          onDone={close}
          onConflict={conflict}
        />
      ) : null}
    </>
  )
}

interface FormProps {
  readonly companyId: string
  /** Called with a notice after a success, or with null on cancel. */
  readonly onDone: (notice: string | null) => void
  /** A version conflict re-reads the list, so the next attempt starts from the current state. */
  readonly onConflict: () => void
}

/** Shared tail of every command form: the attempt's message, and the submit/retry/cancel controls. */
function AttemptFooter<T>({
  status,
  hasPendingRetry,
  submitLabel,
  onCancel,
  companyLabel,
}: {
  readonly status: AttemptStatus<T>
  readonly hasPendingRetry: boolean
  readonly submitLabel: string
  readonly onCancel: () => void
  readonly companyLabel: (companyId: string | null) => string | null
}) {
  return (
    <>
      {status.kind === 'refused' ? (
        <p role="alert" className={styles.error}>
          {refusalText(status.status, status.code, companyLabel(status.companyId), status.groupCode)}
        </p>
      ) : null}
      {status.kind === 'unknown' ? (
        <p role="alert" className={styles.error}>
          {LIMIT_TEXT.unknownOutcome}
        </p>
      ) : null}
      <div className={styles.actions}>
        <Button type="submit" variant="primary" disabled={status.kind === 'busy'} aria-busy={status.kind === 'busy'}>
          {status.kind === 'busy' ? LIMIT_TEXT.saving : hasPendingRetry ? LIMIT_TEXT.retrySame : submitLabel}
        </Button>
        <Button type="button" variant="ghost" onClick={onCancel} disabled={status.kind === 'busy'}>
          {LIMIT_TEXT.cancel}
        </Button>
      </div>
    </>
  )
}

function noCompanyLabel(): string | null {
  return null
}

function savedNotice<T>(outcome: CommandOutcome<T>): string | null {
  if (outcome.type !== 'applied') return null
  return outcome.replayed ? LIMIT_TEXT.savedReplay : LIMIT_TEXT.saved
}

function isVersionConflict<T>(outcome: CommandOutcome<T>): boolean {
  return outcome.type === 'refused' && outcome.code === LIMIT_PROBLEM.versionConflict
}

function ReasonField({ value, onChange }: { readonly value: string; readonly onChange: (value: string) => void }) {
  return (
    <Field label={LIMIT_TEXT.reasonLabel} help={LIMIT_TEXT.reasonHelp}>
      {(control) => (
        <textarea
          {...control}
          required
          rows={3}
          maxLength={LIMIT_INPUT.reasonMaximumLength}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
    </Field>
  )
}

function CreateGroupForm({ companyId, onDone, onConflict }: FormProps) {
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<{ readonly id: string }>()
  const normalized = normalizeCode(code)
  const codeError = code.trim().length > 0 && !isValidCode(normalized) ? LIMIT_TEXT.codeHelp : undefined

  async function submit(event: FormEvent) {
    event.preventDefault()
    const body = { code: normalized, name: name.trim(), reason: reason.trim() }
    const outcome = await attempt.run(body, (idempotencyKey) => createLimitGroup({ companyId, idempotencyKey }, body))
    if (outcome.type === 'applied') onDone(savedNotice(outcome))
    else if (isVersionConflict(outcome)) onConflict()
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="limit-create-heading">
      <h3 id="limit-create-heading">{LIMIT_TEXT.newGroup}</h3>
      <Field label={LIMIT_TEXT.codeLabel} help={LIMIT_TEXT.codeHelp} error={codeError}>
        {(control) => (
          <input
            {...control}
            type="text"
            required
            autoComplete="off"
            maxLength={LIMIT_INPUT.codeMaximumLength}
            value={code}
            onChange={(event) => setCode(event.target.value)}
          />
        )}
      </Field>
      <Field label={LIMIT_TEXT.nameLabel}>
        {(control) => (
          <input
            {...control}
            type="text"
            required
            maxLength={LIMIT_INPUT.nameMaximumLength}
            value={name}
            onChange={(event) => setName(event.target.value)}
          />
        )}
      </Field>
      <ReasonField value={reason} onChange={setReason} />
      <AttemptFooter
        status={attempt.status}
        hasPendingRetry={attempt.hasPendingRetry}
        submitLabel={LIMIT_TEXT.save}
        onCancel={() => onDone(null)}
        companyLabel={noCompanyLabel}
      />
    </form>
  )
}

function RenameGroupForm({ companyId, group, onDone, onConflict }: FormProps & { readonly group: LimitGroup }) {
  const [name, setName] = useState(group.name)
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()

  async function submit(event: FormEvent) {
    event.preventDefault()
    const body = { expectedVersion: group.version, name: name.trim(), reason: reason.trim() }
    const outcome = await attempt.run({ group: group.id, ...body }, (idempotencyKey) =>
      renameLimitGroup({ companyId, idempotencyKey }, group.id, body),
    )
    if (outcome.type === 'applied') onDone(savedNotice(outcome))
    else if (isVersionConflict(outcome)) onConflict()
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="limit-rename-heading">
      <h3 id="limit-rename-heading">
        {LIMIT_TEXT.rename} — <code>{group.code}</code>
      </h3>
      <Field label={LIMIT_TEXT.nameLabel}>
        {(control) => (
          <input
            {...control}
            type="text"
            required
            maxLength={LIMIT_INPUT.nameMaximumLength}
            value={name}
            onChange={(event) => setName(event.target.value)}
          />
        )}
      </Field>
      <ReasonField value={reason} onChange={setReason} />
      <AttemptFooter
        status={attempt.status}
        hasPendingRetry={attempt.hasPendingRetry}
        submitLabel={LIMIT_TEXT.save}
        onCancel={() => onDone(null)}
        companyLabel={noCompanyLabel}
      />
    </form>
  )
}

function DeactivateGroupForm({ companyId, group, onDone, onConflict }: FormProps & { readonly group: LimitGroup }) {
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()

  async function submit(event: FormEvent) {
    event.preventDefault()
    const body = { expectedVersion: group.version, reason: reason.trim() }
    const outcome = await attempt.run({ group: group.id, ...body }, (idempotencyKey) =>
      deactivateLimitGroup({ companyId, idempotencyKey }, group.id, body),
    )
    if (outcome.type === 'applied') onDone(savedNotice(outcome))
    else if (isVersionConflict(outcome)) onConflict()
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="limit-deactivate-heading">
      <h3 id="limit-deactivate-heading">
        {LIMIT_TEXT.deactivate} — <code>{group.code}</code>
      </h3>
      <p>{LIMIT_TEXT.deactivateConfirm}</p>
      <ReasonField value={reason} onChange={setReason} />
      <AttemptFooter
        status={attempt.status}
        hasPendingRetry={attempt.hasPendingRetry}
        submitLabel={LIMIT_TEXT.deactivate}
        onCancel={() => onDone(null)}
        companyLabel={noCompanyLabel}
      />
    </form>
  )
}

function LimitForm({
  companyId,
  group,
  groups,
  businessDate,
  correcting,
  onDone,
  onConflict,
}: FormProps & {
  readonly group: LimitGroup
  readonly groups: readonly LimitGroup[]
  readonly businessDate: string
  /** SAT-D84: the scheduled record this form corrects (withdrawn + replaced); absent = a new record. */
  readonly correcting?: LimitDefinition
}) {
  // A new record starts from the group's latest record (the last scheduled one, else the one in force) and can start no
  // earlier than today or that record's date (SAT-D78). A correction starts from the corrected record and can start no
  // earlier than today or the scheduled record before it (SAT-D84). The server enforces the same.
  const scheduledBefore = correcting === undefined
    ? group.scheduled
    : group.scheduled.filter((entry) => entry.limit.effectiveFrom < correcting.effectiveFrom)
  const previousScheduled = scheduledBefore.at(-1)?.limit ?? null
  const current = correcting ?? previousScheduled ?? group.limit
  const earliest = previousScheduled?.effectiveFrom ?? businessDate
  const [effectiveFrom, setEffectiveFrom] = useState(correcting?.effectiveFrom ?? earliest)
  const [kind, setKind] = useState<LimitKind>(current?.kind ?? LIMIT_KIND.fixed)
  const [percent, setPercent] = useState(current?.percent ?? '')
  const [linkedGroupId, setLinkedGroupId] = useState(current?.linkedGroupId ?? '')
  const [extraPoints, setExtraPoints] = useState(current?.extraPoints ?? '')
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const candidates = groups.filter((candidate) => candidate.isActive && candidate.id !== group.id)
  const percentError = kind === LIMIT_KIND.fixed && percent.trim().length > 0 && !isPercentText(percent.trim())
  const extraError = kind === LIMIT_KIND.linked && extraPoints.trim().length > 0 && !isPercentText(extraPoints.trim())
  const dateError = !isDateText(effectiveFrom) || effectiveFrom < earliest
  // Today (or, for a correction, the corrected record's own date) travels as "omitted"; another date travels as itself.
  const effectiveFromBody = effectiveFrom === (correcting?.effectiveFrom ?? businessDate) ? null : effectiveFrom

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (dateError) return
    const body =
      kind === LIMIT_KIND.fixed
        ? {
            expectedVersion: group.version,
            kind,
            percent: percent.trim(),
            linkedGroupId: null,
            extraPoints: null,
            reason: reason.trim(),
            effectiveFrom: effectiveFromBody,
          }
        : {
            expectedVersion: group.version,
            kind,
            percent: null,
            linkedGroupId: linkedGroupId.length > 0 ? linkedGroupId : null,
            extraPoints: extraPoints.trim(),
            reason: reason.trim(),
            effectiveFrom: effectiveFromBody,
          }
    const outcome =
      correcting === undefined
        ? await attempt.run({ group: group.id, ...body }, (idempotencyKey) =>
            setGroupLimit({ companyId, idempotencyKey }, group.id, body),
          )
        : await attempt.run({ group: group.id, limitVersion: correcting.limitVersion, ...body }, (idempotencyKey) =>
            correctGroupLimit({ companyId, idempotencyKey }, group.id, { ...body, limitVersion: correcting.limitVersion }),
          )
    if (outcome.type === 'applied') onDone(savedNotice(outcome))
    else if (isVersionConflict(outcome)) onConflict()
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="limit-edit-heading">
      <h3 id="limit-edit-heading">
        {correcting === undefined ? LIMIT_TEXT.editLimit : LIMIT_TEXT.correctHeading} — <code>{group.code}</code>
        {correcting === undefined ? null : ` (${businessDateLabel(correcting.effectiveFrom)})`}
      </h3>
      <p className={styles.muted}>{correcting === undefined ? LIMIT_TEXT.limitHelp : LIMIT_TEXT.correctHelp}</p>
      <fieldset className={styles.fieldset}>
        <legend>{LIMIT_TEXT.kindLegend}</legend>
        <label className={styles.choice}>
          <input
            type="radio"
            name="limit-kind"
            value={LIMIT_KIND.fixed}
            checked={kind === LIMIT_KIND.fixed}
            onChange={() => setKind(LIMIT_KIND.fixed)}
          />
          {LIMIT_TEXT.kindFixed}
        </label>
        <label className={styles.choice}>
          <input
            type="radio"
            name="limit-kind"
            value={LIMIT_KIND.linked}
            checked={kind === LIMIT_KIND.linked}
            onChange={() => setKind(LIMIT_KIND.linked)}
          />
          {LIMIT_TEXT.kindLinked}
        </label>
      </fieldset>
      {kind === LIMIT_KIND.fixed ? (
        <Field label={LIMIT_TEXT.percentLabel} error={percentError ? LIMIT_TEXT.percentLabel : undefined}>
          {(control) => (
            <input
              {...control}
              type="text"
              inputMode="decimal"
              required
              autoComplete="off"
              value={percent}
              onChange={(event) => setPercent(event.target.value)}
            />
          )}
        </Field>
      ) : (
        <>
          <Field label={LIMIT_TEXT.linkedGroupLabel}>
            {(control) => (
              <select {...control} required value={linkedGroupId} onChange={(event) => setLinkedGroupId(event.target.value)}>
                <option value="">{LIMIT_TEXT.linkedGroupPlaceholder}</option>
                {candidates.map((candidate) => (
                  <option key={candidate.id} value={candidate.id}>
                    {candidate.code} — {candidate.name}
                  </option>
                ))}
              </select>
            )}
          </Field>
          <Field label={LIMIT_TEXT.extraPointsLabel} error={extraError ? LIMIT_TEXT.extraPointsLabel : undefined}>
            {(control) => (
              <input
                {...control}
                type="text"
                inputMode="decimal"
                required
                autoComplete="off"
                value={extraPoints}
                onChange={(event) => setExtraPoints(event.target.value)}
              />
            )}
          </Field>
        </>
      )}
      <Field
        label={LIMIT_TEXT.effectiveFromLabel}
        help={`${LIMIT_TEXT.effectiveFromHelp} En erken: ${businessDateLabel(earliest)}.`}
        error={dateError ? LIMIT_TEXT.effectiveFromError : undefined}
      >
        {(control) => (
          <input
            {...control}
            type="date"
            required
            min={earliest}
            value={effectiveFrom}
            onChange={(event) => setEffectiveFrom(event.target.value)}
          />
        )}
      </Field>
      <ReasonField value={reason} onChange={setReason} />
      <AttemptFooter
        status={attempt.status}
        hasPendingRetry={attempt.hasPendingRetry}
        submitLabel={LIMIT_TEXT.save}
        onCancel={() => onDone(null)}
        companyLabel={noCompanyLabel}
      />
    </form>
  )
}

/** SAT-D84: withdraw a scheduled record before its date (reason required; the server refuses one already in force). */
function WithdrawLimitForm({
  companyId,
  group,
  record,
  onDone,
  onConflict,
}: FormProps & { readonly group: LimitGroup; readonly record: LimitDefinition }) {
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()

  async function submit(event: FormEvent) {
    event.preventDefault()
    const body = { expectedVersion: group.version, limitVersion: record.limitVersion, reason: reason.trim() }
    const outcome = await attempt.run({ group: group.id, ...body }, (idempotencyKey) =>
      withdrawGroupLimit({ companyId, idempotencyKey }, group.id, body),
    )
    if (outcome.type === 'applied') onDone(savedNotice(outcome))
    else if (isVersionConflict(outcome)) onConflict()
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="limit-withdraw-heading">
      <h3 id="limit-withdraw-heading">
        {LIMIT_TEXT.withdrawHeading} — <code>{group.code}</code> ({businessDateLabel(record.effectiveFrom)})
      </h3>
      <p>{LIMIT_TEXT.withdrawConfirm}</p>
      <ReasonField value={reason} onChange={setReason} />
      <AttemptFooter
        status={attempt.status}
        hasPendingRetry={attempt.hasPendingRetry}
        submitLabel={LIMIT_TEXT.withdrawScheduled}
        onCancel={() => onDone(null)}
        companyLabel={noCompanyLabel}
      />
    </form>
  )
}

function BindingSection({
  namespace,
  companyId,
  groups,
}: {
  readonly namespace: string
  readonly companyId: string
  readonly groups: readonly LimitGroup[]
}) {
  const [query, setQuery] = useState('')
  const [submittedQuery, setSubmittedQuery] = useState<string | null>(null)
  const [person, setPerson] = useState<Person | null>(null)
  const people = useBoundaryRead(
    [namespace, 'settings-discount-limit-people', submittedQuery],
    submittedQuery !== null,
    (signal) => fetchPeople(companyId, submittedQuery ?? '', signal),
  )
  const peopleState = stateOf<PersonList>(people.isPending, people.data)

  return (
    <>
      <form
        className={styles.searchRow}
        role="search"
        onSubmit={(event) => {
          event.preventDefault()
          setSubmittedQuery(query.trim())
        }}
      >
        <Field label={LIMIT_TEXT.searchLabel} help={LIMIT_TEXT.searchHelp}>
          {(control) => (
            <input
              {...control}
              type="search"
              maxLength={LIMIT_INPUT.peopleQueryMaximumLength}
              value={query}
              onChange={(event) => setQuery(event.target.value)}
            />
          )}
        </Field>
        <Button type="submit">{LIMIT_TEXT.search}</Button>
      </form>
      {submittedQuery === null ? null : peopleState.kind === 'ready' ? (
        peopleState.data.items.length === 0 ? (
          <p className={styles.muted}>{LIMIT_TEXT.noPeople}</p>
        ) : (
          <>
            <ul aria-label={LIMIT_TEXT.peopleListLabel} className={styles.people}>
              {peopleState.data.items.map((item) => (
                <li key={item.userId}>
                  <span>{item.displayName}</span>
                  <Button
                    type="button"
                    size="sm"
                    aria-label={`${LIMIT_TEXT.selectPerson} — ${item.displayName}`}
                    aria-pressed={person?.userId === item.userId}
                    onClick={() => setPerson(item)}
                  >
                    {LIMIT_TEXT.selectPerson}
                  </Button>
                </li>
              ))}
            </ul>
            {peopleState.data.hasMore ? <p className={styles.muted}>{LIMIT_TEXT.morePeople}</p> : null}
          </>
        )
      ) : (
        <StateBlock state={peopleState} subject={LIMIT_TEXT.peopleSubject} onRetry={() => void people.refetch()} />
      )}
      {person === null ? null : (
        <PersonBinding key={person.userId} namespace={namespace} companyId={companyId} person={person} groups={groups} />
      )}
    </>
  )
}

function PersonBinding({
  namespace,
  companyId,
  person,
  groups,
}: {
  readonly namespace: string
  readonly companyId: string
  readonly person: Person
  readonly groups: readonly LimitGroup[]
}) {
  const queryClient = useQueryClient()
  const directory = useCompanyDirectory()
  const bindingKey = [namespace, 'settings-discount-limit-binding', person.userId]
  const binding = useBoundaryRead(bindingKey, true, (signal) => fetchBinding(companyId, person.userId, signal))
  const bindingState = stateOf<Binding>(binding.isPending, binding.data)
  const companyLabel = (id: string | null): string | null => {
    if (id === null) return null
    const company = directory.find((entry) => entry.id === id)
    return company === undefined ? id : `${company.code} — ${company.legalName}`
  }

  return (
    <div className={styles.binding}>
      <h3>
        {LIMIT_TEXT.selectedPerson}: {person.displayName}
      </h3>
      {bindingState.kind === 'ready' ? (
        <BindingForm
          companyId={companyId}
          person={person}
          binding={bindingState.data}
          groups={groups}
          companyLabel={companyLabel}
          onReload={() => void queryClient.invalidateQueries({ queryKey: bindingKey })}
        />
      ) : (
        <StateBlock state={bindingState} subject={LIMIT_TEXT.bindingSubject} onRetry={() => void binding.refetch()} />
      )}
    </div>
  )
}

function BindingForm({
  companyId,
  person,
  binding,
  groups,
  companyLabel,
  onReload,
}: {
  readonly companyId: string
  readonly person: Person
  readonly binding: Binding
  readonly groups: readonly LimitGroup[]
  readonly companyLabel: (companyId: string | null) => string | null
  readonly onReload: () => void
}) {
  const own = binding.companies.find((company) => company.companyId === companyId)
  const [selected, setSelected] = useState<ReadonlySet<string>>(
    () => new Set((own?.groups ?? []).filter((group) => group.isActive).map((group) => group.code)),
  )
  const [reason, setReason] = useState('')
  const [notice, setNotice] = useState<string | null>(null)
  const attempt = useCommandAttempt<true>()
  const active = groups.filter((group) => group.isActive)

  function toggle(code: string, checked: boolean) {
    setSelected((previous) => {
      const next = new Set(previous)
      if (checked) next.add(code)
      else next.delete(code)
      return next
    })
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    setNotice(null)
    const body = {
      groupCodes: [...selected].sort(),
      companies: binding.companies.map((company) => ({ companyId: company.companyId, expectedVersion: company.version })),
      reason: reason.trim(),
    }
    const outcome = await attempt.run({ person: person.userId, ...body }, (idempotencyKey) =>
      setBinding({ companyId, idempotencyKey }, person.userId, body),
    )
    if (outcome.type === 'applied') {
      setNotice(savedNotice(outcome))
      onReload()
    } else if (
      outcome.type === 'refused' &&
      (outcome.code === LIMIT_PROBLEM.versionConflict || outcome.code === LIMIT_PROBLEM.bindingCompaniesMismatch)
    ) {
      onReload()
    }
  }

  return (
    <>
      {notice !== null ? (
        <p role="status" className={styles.notice}>
          {notice}
        </p>
      ) : null}
      <table aria-label={LIMIT_TEXT.bindingTableLabel} className={styles.table}>
        <thead>
          <tr>
            <th scope="col">{LIMIT_TEXT.columnCompany}</th>
            <th scope="col">{LIMIT_TEXT.columnGroups}</th>
          </tr>
        </thead>
        <tbody>
          {binding.companies.map((company) => (
            <tr key={company.companyId}>
              <td>{companyLabel(company.companyId)}</td>
              <td>
                {company.groups.length === 0
                  ? LIMIT_TEXT.noBinding
                  : company.groups
                      .map((group) => (group.isActive ? group.code : `${group.code} (${LIMIT_TEXT.passive})`))
                      .join(', ')}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <form className={styles.form} onSubmit={(event) => void submit(event)} aria-label={LIMIT_TEXT.saveBinding}>
        <fieldset className={styles.fieldset}>
          <legend>{LIMIT_TEXT.groupsLegend}</legend>
          {active.length === 0 ? <p className={styles.muted}>{LIMIT_TEXT.noActiveGroups}</p> : null}
          {active.map((group) => (
            <label key={group.id} className={styles.choice}>
              <input
                type="checkbox"
                checked={selected.has(group.code)}
                onChange={(event) => toggle(group.code, event.target.checked)}
              />
              <code>{group.code}</code> — {group.name}
            </label>
          ))}
        </fieldset>
        <ReasonField value={reason} onChange={setReason} />
        <AttemptFooter
          status={attempt.status}
          hasPendingRetry={attempt.hasPendingRetry}
          submitLabel={LIMIT_TEXT.saveBinding}
          onCancel={() => {
            attempt.reset()
            onReload()
          }}
          companyLabel={companyLabel}
        />
      </form>
    </>
  )
}
