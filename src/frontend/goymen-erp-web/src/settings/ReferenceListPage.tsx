/**
 * Ayarlar > Masraf ve hizmet tipleri and Ayarlar > Boya rengi ve hurda türü — API-98, B-14 (ALS-D79, BS-10; KIM-D49,
 * KIM-D51). The purchasing group (expense type, service type) stands on `settings.reference-lists.manage`, the production
 * group (paint color, scrap kind) on `settings.production-reference-lists.manage`; both company-wide, not Critical and
 * granted to no role here (the grant is a human gate).
 *
 * Every list starts empty: nothing is seeded and the screen suggests no entry. Satış fasonu is an operation type, not a
 * service type, and the operation type list is not opened. Adding an entry takes a code (fixed for good), a name and a
 * required reason; a new version changes the name or the active flag against the entry's expected version. Entries are
 * never deleted: an entry no longer used is made inactive. Each write carries an Idempotency-Key and is audited server-side.
 *
 * Remounted per committed context and route entry; a group (multi-company) context shows the no-company state; a version
 * conflict or a vanished entry closes the form and re-reads; an unknown outcome keeps the same Idempotency-Key for "retry".
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
import { createReferenceListEntry, fetchReferenceList, recordReferenceListEntryVersion } from './referenceListApi'
import {
  REFERENCE_LIST_INPUT,
  REFERENCE_LIST_STALE_CODES,
  isReferenceListCode,
  normalizeReferenceListName,
  type ReferenceListEntry,
  type ReferenceListId,
  type ReferenceListView,
} from './referenceListContract'
import {
  REFERENCE_LIST_GROUP_TEXT,
  REFERENCE_LIST_LABEL,
  REFERENCE_LIST_TEXT as T,
  referenceListRefusalText,
} from './referenceListText'
import { Frame, stateOf, useBoundaryRead } from './SalesPolicyParts'
import { useCommandAttempt, type AttemptStatus } from './useCommandAttempt'
import styles from './settings.module.css'

export type ReferenceListGroup = 'purchasing' | 'production'

/** The lists of each screen and the one server name it stands on. */
export const REFERENCE_LIST_GROUP: Record<
  ReferenceListGroup,
  { readonly capability: string; readonly lists: readonly ReferenceListId[] }
> = {
  purchasing: { capability: SETTINGS_CAPABILITY.purchasingReferenceLists, lists: ['expenseType', 'serviceType'] },
  production: { capability: SETTINGS_CAPABILITY.productionReferenceLists, lists: ['paintColor', 'scrapKind'] },
}

export function ReferenceListPage({ group }: { readonly group: ReferenceListGroup }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return (
    <ReferenceListScreen
      key={`${group}::${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`}
      group={group}
      namespace={namespace}
    />
  )
}

function ReferenceListScreen({ group, namespace }: { readonly group: ReferenceListGroup; readonly namespace: string | null }) {
  const shell = useShellState()
  const text = REFERENCE_LIST_GROUP_TEXT[group]
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const ready = namespace !== null && singleCompany && hasCapability(shell, REFERENCE_LIST_GROUP[group].capability)

  if (!singleCompany) {
    return (
      <Frame title={text.title} intro={text.intro}>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={text.subject} />
      </Frame>
    )
  }

  if (!ready || companyId === null || namespace === null) {
    return (
      <Frame title={text.title} intro={text.intro}>
        <StateBlock state={{ kind: 'not-found' }} subject={text.subject} />
      </Frame>
    )
  }

  return (
    <Frame title={text.title} intro={text.intro}>
      {REFERENCE_LIST_GROUP[group].lists.map((list) => (
        <ListSection
          key={list}
          list={list}
          companyId={companyId}
          scope={[namespace, shell.routeEntryGeneration]}
        />
      ))}
    </Frame>
  )
}

function ListSection({
  list,
  companyId,
  scope,
}: {
  readonly list: ReferenceListId
  readonly companyId: string
  readonly scope: readonly (string | number)[]
}) {
  const label = REFERENCE_LIST_LABEL[list]
  const read = useBoundaryRead([...scope, 'settings-reference-list', list], true, (signal) =>
    fetchReferenceList(companyId, list, signal),
  )
  const state = stateOf<ReferenceListView>(read.isPending, read.data)
  const headingId = `reference-list-${list}-heading`
  return (
    <section aria-labelledby={headingId} className={styles.section}>
      <h2 id={headingId}>{label}</h2>
      {state.kind !== 'ready' ? (
        <StateBlock state={state} subject={label.toLocaleLowerCase('tr-TR')} onRetry={() => void read.refetch()} />
      ) : (
        <ListBody list={list} view={state.data} companyId={companyId} onChanged={() => void read.refetch()} />
      )}
    </section>
  )
}

type Editing = { readonly mode: 'add' } | { readonly mode: 'edit'; readonly entryId: string } | null

function ListBody({
  list,
  view,
  companyId,
  onChanged,
}: {
  readonly list: ReferenceListId
  readonly view: ReferenceListView
  readonly companyId: string
  readonly onChanged: () => void
}) {
  const [editing, setEditing] = useState<Editing>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const label = REFERENCE_LIST_LABEL[list]
  const editedEntry =
    editing?.mode === 'edit' ? (view.entries.find((entry) => entry.id === editing.entryId) ?? null) : null
  const finish = (text: string | null) => {
    setEditing(null)
    setNotice(text)
    if (text !== null) onChanged()
  }

  return (
    <>
      {notice !== null ? (
        <p role="status" className={styles.notice}>
          {notice}
        </p>
      ) : null}
      {view.entries.length === 0 ? (
        <p className={styles.muted}>{T.empty}</p>
      ) : (
        <table className={styles.table}>
          <caption>{label}</caption>
          <thead>
            <tr>
              <th scope="col">{T.columnCode}</th>
              <th scope="col">{T.columnName}</th>
              <th scope="col">{T.columnStatus}</th>
              <th scope="col">{T.columnVersion}</th>
              <th scope="col">{T.columnRecorded}</th>
              <th scope="col">{T.columnActions}</th>
            </tr>
          </thead>
          <tbody>
            {view.entries.map((entry) => (
              <tr key={entry.id}>
                <td>{entry.code}</td>
                <td>{entry.name}</td>
                <td>{entry.active ? T.active : T.inactive}</td>
                <td>{entry.version}</td>
                <td>
                  <code>{entry.recordedBy}</code> · <time dateTime={entry.recordedAtUtc}>{entry.recordedAtUtc}</time>
                </td>
                <td>
                  <Button
                    type="button"
                    variant="ghost"
                    aria-label={`${entry.code}: ${T.edit}`}
                    aria-expanded={editing?.mode === 'edit' && editing.entryId === entry.id}
                    onClick={() => {
                      setNotice(null)
                      setEditing({ mode: 'edit', entryId: entry.id })
                    }}
                  >
                    {T.edit}
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {editing?.mode === 'add' ? (
        <AddForm list={list} companyId={companyId} onDone={finish} />
      ) : editedEntry !== null ? (
        <EditForm key={`${editedEntry.id}::${editedEntry.version}`} list={list} entry={editedEntry} companyId={companyId} onDone={finish} />
      ) : (
        <div className={styles.actions}>
          <Button
            type="button"
            variant="primary"
            onClick={() => {
              setNotice(null)
              setEditing({ mode: 'add' })
            }}
          >
            {T.add}
          </Button>
        </div>
      )}
    </>
  )
}

function AddForm({
  list,
  companyId,
  onDone,
}: {
  readonly list: ReferenceListId
  readonly companyId: string
  readonly onDone: (notice: string | null) => void
}) {
  // Empty fields: the screen never proposes a code or a name (nothing seeded, KIM-D49).
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const normalizedName = normalizeReferenceListName(name)
  const formId = `reference-list-${list}-add`

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!isReferenceListCode(code) || normalizedName === null) return
    const body = { code, name: normalizedName, reason: reason.trim() }
    const outcome = await attempt.run(body, (idempotencyKey) =>
      createReferenceListEntry({ companyId, idempotencyKey }, list, body),
    )
    if (outcome.type === 'applied') onDone(outcome.replayed ? T.savedReplay : T.saved)
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby={`${formId}-heading`}>
      <h3 id={`${formId}-heading`}>{T.addHeading(REFERENCE_LIST_LABEL[list])}</h3>
      <p className={styles.muted}>{T.singleDeciderNote}</p>
      <Field label={T.codeLabel} help={T.codeHelp} error={code.length > 0 && !isReferenceListCode(code) ? T.codeError : undefined}>
        {(control) => (
          <input
            {...control}
            type="text"
            required
            autoComplete="off"
            maxLength={REFERENCE_LIST_INPUT.codeMaximumLength}
            value={code}
            onChange={(event) => setCode(event.target.value)}
          />
        )}
      </Field>
      <NameField value={name} onChange={setName} />
      <ReasonInput value={reason} onChange={setReason} />
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => onDone(null)} />
    </form>
  )
}

function EditForm({
  list,
  entry,
  companyId,
  onDone,
}: {
  readonly list: ReferenceListId
  readonly entry: ReferenceListEntry
  readonly companyId: string
  readonly onDone: (notice: string | null) => void
}) {
  // The form starts from the stored latest version, never from an invented value.
  const [name, setName] = useState(entry.name)
  const [active, setActive] = useState(entry.active)
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const normalizedName = normalizeReferenceListName(name)
  const formId = `reference-list-${list}-edit-${entry.id}`

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (normalizedName === null) return
    const body = { expectedVersion: entry.version, name: normalizedName, active, reason: reason.trim() }
    const outcome = await attempt.run(body, (idempotencyKey) =>
      recordReferenceListEntryVersion({ companyId, idempotencyKey }, list, entry.id, body),
    )
    if (outcome.type === 'applied') {
      onDone(outcome.replayed ? T.savedReplay : T.saved)
      return
    }
    const stale = staleRefusal(outcome)
    if (stale !== null) onDone(stale)
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby={`${formId}-heading`}>
      <h3 id={`${formId}-heading`}>{T.editHeading(entry.code)}</h3>
      <p className={styles.muted}>{T.singleDeciderNote}</p>
      <NameField value={name} onChange={setName} />
      <label className={styles.choice}>
        <input type="checkbox" checked={active} onChange={(event) => setActive(event.target.checked)} />
        {T.activeLabel}
      </label>
      <ReasonInput value={reason} onChange={setReason} />
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => onDone(null)} />
    </form>
  )
}

function NameField({ value, onChange }: { readonly value: string; readonly onChange: (value: string) => void }) {
  return (
    <Field
      label={T.nameLabel}
      help={T.nameHelp}
      error={value.length > 0 && normalizeReferenceListName(value) === null ? T.nameError : undefined}
    >
      {(control) => (
        <input
          {...control}
          type="text"
          required
          autoComplete="off"
          maxLength={REFERENCE_LIST_INPUT.nameMaximumLength}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
    </Field>
  )
}

function ReasonInput({ value, onChange }: { readonly value: string; readonly onChange: (value: string) => void }) {
  return (
    <Field label={T.reasonLabel} help={T.reasonHelp}>
      {(control) => (
        <textarea
          {...control}
          required
          rows={3}
          maxLength={REFERENCE_LIST_INPUT.reasonMaximumLength}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
    </Field>
  )
}

/** A refusal after which the screen's copy is stale (the version moved, the entry vanished): close and re-read. */
function staleRefusal<D>(outcome: CommandOutcome<D>): string | null {
  if (outcome.type !== 'refused' || outcome.code === null) return null
  return REFERENCE_LIST_STALE_CODES.includes(outcome.code) ? referenceListRefusalText(outcome.status, outcome.code) : null
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
          {referenceListRefusalText(status.status, status.code)}
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
          {status.kind === 'busy' ? T.saving : hasPendingRetry ? T.retrySame : T.save}
        </Button>
        <Button type="button" variant="ghost" onClick={onCancel} disabled={status.kind === 'busy'}>
          {T.cancel}
        </Button>
      </div>
    </>
  )
}
