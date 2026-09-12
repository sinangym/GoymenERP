/**
 * C-06 — the selected company's operational contact and address editor.
 *
 * SCOPE, and it is the point of the screen: everything here belongs to ONE
 * `CompanyPartyAccount`. The shared Party identity (legal name, official
 * identifiers, registry ids, IBAN) is not editable from this route at all — those go
 * through the CARI-DEC-004 proposal with a separate approver, and
 * `contactEditModel` refuses them at the payload boundary so a mixed edit can never
 * be announced as one "everything was saved".
 *
 * WHAT THE SAVE SENDS. Only what changed. An omitted property is retained, an
 * explicit `null` clears an optional scalar, and a collection nobody touched is left
 * out entirely — never `null` (the server rejects that) and never `[]`, which would
 * read as an instruction it does not carry. A row that is not sent is NOT deleted:
 * passivity is the explicit `deactivate` operation on the row the user chose.
 *
 * VERSIONS. One fence for the whole graph: the ACCOUNT version from the read this
 * form is seeded on, a canonical Int64 decimal string that is never parsed. There is
 * no per-child expected version. Every terminal outcome ADOPTS a fresh read before
 * the next write can go out — a success re-seeds the whole draft, a conflict adopts
 * the new version while KEEPING the draft for review — so the fence can never be one
 * the server has already moved past.
 *
 * AFTER A 204 the response carries no body, so nothing on screen comes from the
 * request: the draft is re-seeded from a fresh authorized GET, and only then does the
 * screen say the values it shows are what the record holds. If that read fails, the
 * screen says so and offers a READ retry — it never lets a second write go out on top
 * of a state nobody has confirmed.
 *
 * THE 400 HAS NO FIELD MAP. `validation_failed` arrives as ProblemDetails with a
 * localized `detail` and no structured field/index errors, so this screen never parses
 * it into field messages: the server's rejection is shown as a summary and the draft
 * is kept intact. The per-field messages below are the client's OWN checks, and they
 * are labelled as such.
 *
 * UNSAVED WORK IS DEFENDED WHETHER OR NOT IT IS VALID. The dirty scope, the enabled
 * Save and the switch warning follow `dirty` (the user altered something), never
 * `hasChanges` (something is sendable). An edit that is currently invalid is still
 * work that would be lost.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import {
  API_BASE,
  contractReadSeq,
  isCommandContractReady,
  type ContractContext,
} from './clientContract'
import { PROBLEM_CODE, problemCodeOf } from './contract'
import { fetchContactInformation, updateContactInformation } from './contactInformationApi'
import {
  ADDRESS_FIELD_RULES,
  ADDRESS_PURPOSE_LABEL,
  CHANNEL_FIELD_RULES,
  CHANNEL_KIND_LABEL,
  CONTACT_CHANGES_SPEC,
  CONTACT_FIELD_RULES,
  CONTACT_SCALAR_RULES,
  refineChannelRules,
  refineScalarRules,
  type ContactInformationView,
  type OperationalAddress,
  type OperationalContact,
} from './contactInformationContract'
import { repeatsUnresolvedAttempt } from './commandAttempt'
import {
  buildChanges,
  generateRowId,
  trimToNull,
  type EditedRow,
  type FieldProblem,
  type FieldRule,
  type OriginalRow,
  type RowIntent,
  type RowProblem,
} from './contactEditModel'
import { CARI_CONTACT_TEXT as TEXT } from './cariCommandText'
import { CommandAttemptNotice } from './CommandAttemptNotice'
import { useCommandAttempt } from './useCommandAttempt'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useCompanyDirectory } from '../shell/useShellOrchestrator'
import { StateBlock } from '../shell/StateBlock'
import styles from './CariContactEditPage.module.css'

/** The dirty scope this editor registers with the shell (spec §4.2). */
export const CARI_CONTACT_DIRTY_SCOPE = 'cari.contact-edit'

const SCALAR_FIELDS = ['phone', 'phoneExtension', 'email', 'website'] as const
const ADDRESS_FIELDS = ['purpose', 'countryCode', 'line1', 'line2', 'city', 'district', 'postalCode'] as const
const CONTACT_FIELDS = ['displayName', 'jobTitle', 'purpose'] as const
const CHANNEL_FIELDS = ['kind', 'value', 'extension'] as const

/** See CariListPage: a contract refusal is about this client, never about the record. */
const CONTRACT_PROBLEM_CODES = new Set<string>([
  PROBLEM_CODE.clientContractRequired,
  PROBLEM_CODE.clientContractUnsupported,
])

/**
 * What a settled attempt owes the screen before another write may go out.
 *
 * `full` — a success: the record is whatever the server now says, so the draft is
 * rebuilt from the fresh read.
 * `fence` — a refusal that moved the record (a version conflict): the new version is
 * adopted so the next attempt is fenced on it, and the DRAFT IS KEPT so the user can
 * review their own edits against what the record has become. Overwriting their work
 * unreviewed would be the other half of the same bug.
 */
type PendingAdopt = 'full' | 'fence' | null

interface RowState {
  readonly id: string
  readonly values: Record<string, string>
  /** Only `deactivate`/`reactivate` are explicit; an edit is derived from the values. */
  readonly intent: RowIntent
  readonly isNew: boolean
  /** As the read reported it. A new row has no server state yet. */
  readonly isActive: boolean
  readonly channels?: readonly RowState[]
}

interface FormState {
  readonly scalars: Record<string, string>
  readonly addresses: readonly RowState[]
  readonly contacts: readonly RowState[]
}

const EMPTY_FORM: FormState = { scalars: {}, addresses: [], contacts: [] }

function asInput(value: string | null): string {
  return value ?? ''
}

function addressRow(address: OperationalAddress): RowState {
  return {
    id: address.id,
    isNew: false,
    isActive: address.isActive,
    intent: 'unchanged',
    values: {
      purpose: address.purpose,
      countryCode: address.countryCode,
      line1: address.line1,
      line2: asInput(address.line2),
      city: asInput(address.city),
      district: asInput(address.district),
      postalCode: asInput(address.postalCode),
    },
  }
}

function contactRow(contact: OperationalContact): RowState {
  return {
    id: contact.id,
    isNew: false,
    isActive: contact.isActive,
    intent: 'unchanged',
    values: {
      displayName: contact.displayName,
      jobTitle: asInput(contact.jobTitle),
      purpose: contact.purpose,
    },
    channels: contact.channels.map((channel) => ({
      id: channel.id,
      isNew: false,
      isActive: channel.isActive,
      intent: 'unchanged' as RowIntent,
      values: { kind: channel.kind, value: channel.value, extension: asInput(channel.extension) },
    })),
  }
}

function formFromView(view: ContactInformationView): FormState {
  const info = view.information
  return {
    scalars: {
      phone: asInput(info.phone),
      phoneExtension: asInput(info.phoneExtension),
      email: asInput(info.email),
      website: asInput(info.website),
    },
    addresses: info.addresses.map(addressRow),
    contacts: info.contacts.map(contactRow),
  }
}

/** The read's own values, as the patch builder compares against. */
function originalFromView(view: ContactInformationView) {
  const info = view.information
  const addresses: Record<string, OriginalRow> = {}
  for (const a of info.addresses) {
    addresses[a.id] = {
      values: {
        purpose: a.purpose,
        countryCode: a.countryCode,
        line1: a.line1,
        line2: a.line2,
        city: a.city,
        district: a.district,
        postalCode: a.postalCode,
      },
    }
  }
  const contacts: Record<string, OriginalRow> = {}
  for (const c of info.contacts) {
    const children: Record<string, OriginalRow> = {}
    for (const ch of c.channels) {
      children[ch.id] = { values: { kind: ch.kind, value: ch.value, extension: ch.extension } }
    }
    contacts[c.id] = {
      values: { displayName: c.displayName, jobTitle: c.jobTitle, purpose: c.purpose },
      children,
    }
  }
  return {
    scalars: {
      phone: info.phone,
      phoneExtension: info.phoneExtension,
      email: info.email,
      website: info.website,
    },
    collections: { addresses, contacts },
  }
}

/**
 * ONE field, across three versions of the truth: what the draft was seeded on (base),
 * what the user has in front of them (mine), and what the record says now (theirs).
 *
 * The user's value wins ONLY where they actually changed it. Everywhere else the fresh
 * server value is taken — and that is the whole point: keeping the entire old draft over
 * a new baseline silently re-asserted every field the draft still held, including ones
 * the user never touched. A concurrent editor's corrected phone number was overwritten by
 * the stale value this form happened to be holding, on a save the user believed was about
 * their e-mail.
 *
 * "CHANGED" MEANS WHAT THE PATCH MEANS BY IT, which is why the comparison is canonical
 * and not raw. The patch builder compares trimmed values, so typing ` 111 ` over `111`
 * sends nothing — but a raw comparison here read that as intent, kept the stale `111`,
 * and the next save then overwrote the concurrent `222` with it. The mirror case is
 * worse: whitespace typed into an empty field is no change at all, yet raw it looked like
 * one, and the save would have sent `null` and CLEARED whatever value had arrived in the
 * meantime. Both are the same mistake — two different ideas of "the user changed this" —
 * and the fix is to have only one.
 */
function mergeValues(
  base: Readonly<Record<string, string>>,
  mine: Readonly<Record<string, string>>,
  theirs: Readonly<Record<string, string>>,
): Record<string, string> {
  const merged: Record<string, string> = { ...theirs }
  for (const key of Object.keys(mine)) {
    if (trimToNull(mine[key] ?? '') !== trimToNull(base[key] ?? '')) merged[key] = mine[key]
  }
  return merged
}

/**
 * The same rule for a collection, and for the channels under a contact.
 *
 * Order and existence come from the SERVER: a row somebody else added appears, and a row
 * this draft holds that the record no longer returns is dropped, because the read is the
 * only authority on what exists. What survives from the draft is the user's own work —
 * their field edits, their explicitly marked status operations, and the rows they added,
 * which the server has never seen and cannot speak for.
 */
function mergeRows(
  base: readonly RowState[],
  mine: readonly RowState[],
  theirs: readonly RowState[],
): RowState[] {
  const baseById = new Map(base.map((row) => [row.id, row]))
  const mineById = new Map(mine.map((row) => [row.id, row]))
  const merged = theirs.map((theirRow) => {
    const myRow = mineById.get(theirRow.id)
    if (myRow === undefined) return theirRow
    const baseRow = baseById.get(theirRow.id)
    return {
      ...theirRow,
      // The status operation the user chose is their intent, and it outlives the refresh.
      intent: myRow.intent,
      values: mergeValues(baseRow?.values ?? {}, myRow.values, theirRow.values),
      ...(theirRow.channels === undefined && myRow.channels === undefined
        ? {}
        : {
            channels: mergeRows(baseRow?.channels ?? [], myRow.channels ?? [], theirRow.channels ?? []),
          }),
    }
  })
  // Rows the user added are not in the server's answer yet; they are still their work.
  return [...merged, ...mine.filter((row) => row.isNew)]
}

function mergeDraft(base: ContactInformationView, mine: FormState, theirs: ContactInformationView): FormState {
  const baseForm = formFromView(base)
  const theirsForm = formFromView(theirs)
  return {
    scalars: mergeValues(baseForm.scalars, mine.scalars, theirsForm.scalars),
    addresses: mergeRows(baseForm.addresses, mine.addresses, theirsForm.addresses),
    contacts: mergeRows(baseForm.contacts, mine.contacts, theirsForm.contacts),
  }
}

function toEditedRow(row: RowState): EditedRow {
  return {
    id: row.id,
    intent: row.intent,
    values: row.values,
    ...(row.channels === undefined ? {} : { children: row.channels.map(toEditedRow) }),
  }
}

/**
 * Does this row carry field work the server has not seen?
 *
 * Used to keep a status operation and a field edit from colliding. A status operation
 * carries the identity and nothing else — the contract says so — so a row that is both
 * edited and marked passive would have had its edit silently dropped on save. Rather
 * than choosing for the user, the screen closes one of the two while the other stands.
 */
function rowHasFieldWork(
  originals: Readonly<Record<string, OriginalRow>>,
  row: RowState,
  fields: readonly string[],
): boolean {
  const before = originals[row.id]
  if (before === undefined) return Object.values(row.values).some((raw) => trimToNull(raw) !== null)
  for (const key of fields) {
    if (trimToNull(row.values[key] ?? '') !== (before.values[key] ?? null)) return true
  }
  for (const channel of row.channels ?? []) {
    const beforeChannel = before.children?.[channel.id]
    if (beforeChannel === undefined) return true
    for (const key of CHANNEL_FIELDS) {
      if (trimToNull(channel.values[key] ?? '') !== (beforeChannel.values[key] ?? null)) return true
    }
  }
  return false
}

/**
 * The client's OWN message for a problem it detected itself. None of these comes from
 * the server, and the two are never mixed: a server rejection is shown as its own
 * summary, because `validation_failed` carries no field map to place.
 */
function problemText(problem: FieldProblem | RowProblem, rule?: FieldRule): string {
  const p = TEXT.problem
  switch (problem) {
    case 'required':
      return p.required
    case 'too-long':
      return rule?.maxLength === undefined ? p.tooLongUnknown : p.tooLong(rule.maxLength)
    case 'email-format':
      return p.emailFormat
    case 'url-unsupported':
      return p.urlUnsupported
    case 'not-allowed':
      return p.notAllowed
    case 'country-format':
      return p.countryFormat
    case 'not-clearable':
      return p.notClearable
    case 'critical-scope':
      return p.criticalScope
    case 'unknown-field':
      return p.unknownField
    case 'row-identity':
      return p.rowIdentity
    case 'row-empty':
      return p.rowEmpty
    case 'immutable':
      return p.immutable
    case 'not-applicable':
      return p.notApplicable
    case 'control-characters':
      return p.controlCharacters
    case 'row-duplicate':
      return p.rowDuplicate
    case 'status-needs-existing':
      return p.statusNeedsExisting
    case 'too-many-operations':
      return p.tooManyOperations
    default:
      return p.required
  }
}

function labelFor(field: string, collection: 'address' | 'contact' | 'channel' | 'scalar'): string {
  if (collection === 'contact' && field === 'purpose') return TEXT.field.contactPurpose
  return (TEXT.field as Record<string, string>)[field] ?? field
}

function inputModeFor(rule: FieldRule): 'text' | 'tel' | 'email' | 'url' {
  if (rule.kind === 'phone') return 'tel'
  if (rule.kind === 'email') return 'email'
  if (rule.kind === 'url') return 'url'
  return 'text'
}

/**
 * Remounted on every context change, exactly like C-01 and C-03: a draft prepared in
 * one company must never be carried into another, and the company is in this screen's
 * own heading.
 */
export function CariContactEditPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { partyId } = useParams()
  return <ContactEditor key={`${namespace ?? 'no-context'}::${partyId ?? ''}`} namespace={namespace} />
}

function ContactEditor({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const dispatch = useShellDispatch()
  const entryGeneration = shell.routeEntryGeneration
  const { partyId } = useParams()
  const boundary = useAuthBoundary()
  const directory = useCompanyDirectory()
  const queryClient = useQueryClient()

  const activeCompanyId = shell.activeContext?.companyId ?? null
  const scopeMode = shell.activeContext?.scopeMode ?? 'single'
  const singleCompanyContext = scopeMode === 'single' && activeCompanyId !== null
  const activeCompany = useMemo(
    () => directory.find((c) => c.id === activeCompanyId) ?? null,
    [directory, activeCompanyId],
  )

  const contractContext: ContractContext | null = useMemo(
    () => (namespace === null ? null : { origin: API_BASE, namespace }),
    [namespace],
  )

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', entryGeneration, 'counterparty-contact', partyId ?? ''],
    enabled: namespace !== null && singleCompanyContext && (partyId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchContactInformation(
        partyId ?? '',
        activeCompanyId ?? '',
        signal,
        contractContext,
      )
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<ContactInformationView>

  /**
   * The read the form stands on. Kept separate from the latest read on purpose: the
   * fence and the "what changed" comparison must both be against the values the user
   * was actually shown, not against something that arrived underneath them.
   */
  const [seeded, setSeeded] = useState<ContactInformationView | null>(null)
  const [form, setForm] = useState<FormState>(EMPTY_FORM)
  const [showErrors, setShowErrors] = useState(false)
  const [summaryFocusRequest, setSummaryFocusRequest] = useState(0)
  const [pendingAdopt, setPendingAdopt] = useState<PendingAdopt>(null)
  /** The version a save was confirmed at, so the notice outlives the attempt reset. */
  const [savedVersion, setSavedVersion] = useState<string | null>(null)
  /** Set when a conflict refresh adopted a new version and kept the draft. */
  const [refreshedVersion, setRefreshedVersion] = useState<string | null>(null)

  const summaryRef = useRef<HTMLDivElement | null>(null)
  const unresolvedRef = useRef<HTMLDivElement | null>(null)

  const view = state.kind === 'ready' ? state.data : null

  /**
   * The base the draft stands on, readable from the adopt effect without making that
   * effect depend on every keystroke.
   */
  const seededRef = useRef<ContactInformationView | null>(null)
  useEffect(() => {
    seededRef.current = seeded
  }, [seeded])

  const attempt = useCommandAttempt<Record<string, unknown>, null>()
  const completed = attempt.state.phase === 'completed' ? attempt.state : null
  const frozen = attempt.state.phase === 'submitting' || attempt.state.phase === 'unknown'

  /**
   * Seeding the draft from the server's answer, once.
   *
   * This is the legitimate use of an effect: the read is the external system, and the
   * draft cannot be derived during render because from the first keystroke it is the
   * user's, not the server's. It seeds only while nothing is seeded yet, so a refetch
   * landing underneath an edit never overwrites what the user typed.
   */
  useEffect(() => {
    if (view === null || seeded !== null) return
    // oxlint-disable-next-line react/set-state-in-effect -- server-triggered seed, once per read
    setSeeded(view)
    setForm(formFromView(view))
  }, [view, seeded])

  const original = useMemo(
    () =>
      seeded === null
        ? { scalars: {}, collections: { addresses: {}, contacts: {} } }
        : originalFromView(seeded),
    [seeded],
  )
  const edited = useMemo(
    () => ({
      scalars: form.scalars,
      collections: {
        addresses: form.addresses.map(toEditedRow),
        contacts: form.contacts.map(toEditedRow),
      },
    }),
    [form],
  )
  const body = useMemo(() => buildChanges(CONTACT_CHANGES_SPEC, original, edited), [original, edited])

  const operation = `counterparty.contact-update:${partyId ?? ''}`
  const payload = useMemo(
    () => ({ expectedVersion: seeded?.version ?? '', changes: body.changes }),
    [seeded, body.changes],
  )
  /**
   * The abandoned-but-unresolved attempt whose payload the form still holds.
   *
   * Without this the recovery disappeared: after an unknown outcome was abandoned, the
   * notice unmounted, the unchanged payload was refused as `unchanged` with nothing on
   * screen to explain it, and the same-key retry — the only way to resolve a write that
   * may already have landed — was unreachable.
   */
  const repeatsUnresolved = repeatsUnresolvedAttempt(attempt.state, operation, payload)

  /**
   * Unsaved work is unsaved work, valid or not: `dirty`, never `hasChanges`. A frozen
   * attempt counts too, because a write may be in flight.
   */
  const dirty = frozen || body.dirty
  useEffect(() => {
    dispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: CARI_CONTACT_DIRTY_SCOPE })
  }, [dirty, dispatch])

  /**
   * A completion is applied once per snapshot, replay included. The record's new state
   * is NOT taken from the command — a 204 has no body — so the cached read is dropped
   * and the form waits to be re-seeded from a fresh authorized GET.
   */
  const completedKeyRef = useRef<string | null>(null)
  useEffect(() => {
    if (completed === null || completedKeyRef.current === completed.snapshot.key) return
    completedKeyRef.current = completed.snapshot.key
    // oxlint-disable-next-line react/set-state-in-effect -- reconciles a definite server outcome, once per snapshot
    setPendingAdopt('full')
    setRefreshedVersion(null)
    if (namespace !== null) {
      void queryClient.invalidateQueries({ predicate: (q) => q.queryKey[0] === namespace })
    }
  }, [completed, namespace, queryClient])

  /**
   * Adopting the fresh read, and RELEASING the attempt.
   *
   * Releasing is the part that was missing: a completed attempt can begin no new one, so
   * after the first successful save the form re-seeded, went quiet, and then refused
   * every further edit — Save enabled, submit silently blocked, and the screen calling
   * itself clean. The attempt is closed here, once the fresh read it depends on has
   * actually landed.
   */
  useEffect(() => {
    if (pendingAdopt === null || query.isFetching || view === null) return
    if (pendingAdopt === 'full') {
      // oxlint-disable-next-line react/set-state-in-effect -- completes the server-triggered reseed above
      setSeeded(view)
      setForm(formFromView(view))
      setShowErrors(false)
      setSavedVersion(view.version)
    } else {
      /**
       * The fence moves to what the record now IS, and the draft is merged onto it: the
       * user's own edits are kept, and every field they did not touch takes the fresh
       * server value. Keeping the whole old draft over a new baseline is what re-asserted
       * a concurrent editor's field as if this user had asked for it.
       */
      const base = seededRef.current
      // oxlint-disable-next-line react/set-state-in-effect -- completes the server-triggered refence above
      setSeeded(view)
      setForm((current) => (base === null ? formFromView(view) : mergeDraft(base, current, view)))
      setRefreshedVersion(view.version)
      setSavedVersion(null)
    }
    setPendingAdopt(null)
    attempt.abandon()
  }, [pendingAdopt, query.isFetching, view, attempt])

  useEffect(() => {
    if (summaryFocusRequest === 0) return
    // The summary exists only after the render this request caused, which is why the
    // focus cannot be moved from the submit handler: it ran before the box was mounted
    // and the focus stayed on the Save button.
    summaryRef.current?.focus()
  }, [summaryFocusRequest])

  const setScalar = useCallback((fieldName: string, value: string) => {
    setForm((current) => ({ ...current, scalars: { ...current.scalars, [fieldName]: value } }))
  }, [])

  const setRowValue = useCallback(
    (collection: 'addresses' | 'contacts', index: number, fieldName: string, value: string) => {
      setForm((current) => ({
        ...current,
        [collection]: current[collection].map((row, i) =>
          i === index ? { ...row, values: { ...row.values, [fieldName]: value } } : row,
        ),
      }))
    },
    [],
  )

  const setChannelValue = useCallback(
    (contactIndex: number, channelIndex: number, fieldName: string, value: string) => {
      setForm((current) => ({
        ...current,
        contacts: current.contacts.map((row, i) =>
          i !== contactIndex
            ? row
            : {
                ...row,
                channels: (row.channels ?? []).map((channel, j) =>
                  j === channelIndex
                    ? { ...channel, values: { ...channel.values, [fieldName]: value } }
                    : channel,
                ),
              },
        ),
      }))
    },
    [],
  )

  const toggleRowState = useCallback((collection: 'addresses' | 'contacts', index: number) => {
    setForm((current) => ({
      ...current,
      [collection]: current[collection].map((row, i) => {
        if (i !== index) return row
        // Passivity is chosen, and it is reversible before the save: the toggle returns
        // to `unchanged`, which sends nothing at all for this row.
        if (row.intent === 'deactivate' || row.intent === 'reactivate') {
          return { ...row, intent: 'unchanged' as RowIntent }
        }
        return { ...row, intent: (row.isActive ? 'deactivate' : 'reactivate') as RowIntent }
      }),
    }))
  }, [])

  /**
   * Dropping a row the user just added.
   *
   * Only ever a NEW row. An existing row leaving the editor is not an instruction — the
   * contract has no delete and omission retains — so for those the offer is
   * deactivate/reactivate. Without this, an added-and-abandoned empty row could not be
   * removed at all and its `row-empty` refusal held the whole section hostage.
   */
  const removeRow = useCallback((collection: 'addresses' | 'contacts', index: number) => {
    setForm((current) => ({
      ...current,
      [collection]: current[collection].filter((row, i) => !(i === index && row.isNew)),
    }))
  }, [])

  const removeChannel = useCallback((contactIndex: number, channelIndex: number) => {
    setForm((current) => ({
      ...current,
      contacts: current.contacts.map((row, i) =>
        i !== contactIndex
          ? row
          : { ...row, channels: (row.channels ?? []).filter((c, j) => !(j === channelIndex && c.isNew)) },
      ),
    }))
  }, [])

  /**
   * A new row's id is minted HERE, when the row appears — never at submit time. The id
   * is part of the payload, and a same-key retry has to reproduce it exactly or the
   * server sees a different request under a spent key.
   */
  const addAddress = useCallback(() => {
    setForm((current) => ({
      ...current,
      addresses: [
        ...current.addresses,
        {
          id: generateRowId(),
          isNew: true,
          isActive: true,
          intent: 'upsert',
          values: Object.fromEntries(ADDRESS_FIELDS.map((f) => [f, ''])),
        },
      ],
    }))
  }, [])

  const addContact = useCallback(() => {
    setForm((current) => ({
      ...current,
      contacts: [
        ...current.contacts,
        {
          id: generateRowId(),
          isNew: true,
          isActive: true,
          intent: 'upsert',
          values: Object.fromEntries(CONTACT_FIELDS.map((f) => [f, ''])),
          channels: [],
        },
      ],
    }))
  }, [])

  const addChannel = useCallback((contactIndex: number) => {
    setForm((current) => ({
      ...current,
      contacts: current.contacts.map((row, i) =>
        i !== contactIndex
          ? row
          : {
              ...row,
              channels: [
                ...(row.channels ?? []),
                {
                  id: generateRowId(),
                  isNew: true,
                  isActive: true,
                  intent: 'upsert' as RowIntent,
                  values: Object.fromEntries(CHANNEL_FIELDS.map((f) => [f, ''])),
                },
              ],
            },
      ),
    }))
  }, [])

  const submit = () => {
    if (frozen || pendingAdopt !== null || seeded === null) return
    if (activeCompanyId === null || namespace === null) return
    if (!body.dirty) return
    if (Object.keys(body.problems).length > 0) {
      // Client-invalid: nothing is sent and NO key is minted. The summary takes focus via
      // the effect above, so the refusal is perceivable without sight.
      setShowErrors(true)
      setSummaryFocusRequest((count) => count + 1)
      return
    }
    if (!body.hasChanges) return
    setShowErrors(false)
    const changes = body.changes
    const companyId = activeCompanyId
    const expectedVersion = seeded.version
    const target = partyId ?? ''
    const outcome = attempt.submit(operation, { expectedVersion, changes }, (idempotencyKey, signal) =>
      updateContactInformation(
        { companyId, idempotencyKey, contract: { origin: API_BASE, namespace } },
        target,
        expectedVersion,
        changes,
        signal,
      ),
    )
    if (outcome === 'unchanged') unresolvedRef.current?.focus()
  }

  const refreshRecord = () => {
    setPendingAdopt('fence')
    void query.refetch()
  }

  if (!singleCompanyContext) {
    return (
      <section aria-labelledby="cari-contact-heading" className={styles.page}>
        <h1 id="cari-contact-heading">{TEXT.title}</h1>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={TEXT.noSingleCompany} />
      </section>
    )
  }

  const contractRefusal =
    query.data?.type === 'http' &&
    query.data.status === 409 &&
    CONTRACT_PROBLEM_CODES.has(problemCodeOf(query.data.body) ?? '')

  if (contractRefusal) {
    return (
      <section aria-labelledby="cari-contact-heading" className={styles.page}>
        <h1 id="cari-contact-heading">{TEXT.title}</h1>
        <BackLinks partyId={partyId} />
        <div role="alert" data-state="client-contract">
          <p>
            <strong>Uyumlu istemci gerekli</strong>
          </p>
          <p>Bu cari işlemi desteklenen istemci sözleşmesi ile yeniden açılmalıdır. Sayfayı yenileyin.</p>
        </div>
      </section>
    )
  }

  if (seeded === null) {
    return (
      <section aria-labelledby="cari-contact-heading" className={styles.page}>
        {/* The heading stays generic until a record is verified: naming it would confirm
            a record exists to a caller who may not be entitled to know it. */}
        <h1 id="cari-contact-heading">{TEXT.title}</h1>
        <BackLinks partyId={partyId} />
        <StateBlock state={state} subject={TEXT.subject} onRetry={() => void query.refetch()} />
      </section>
    )
  }

  const retainedCount = Object.values(body.retained).reduce((total, ids) => total + ids.length, 0)
  const problemCount = Object.keys(body.problems).length
  /**
   * The read the screen is waiting on did not arrive.
   *
   * This is a READ recovery and nothing else: the draft is kept, the form is closed, and
   * no second write may go out on top of a state nobody has confirmed. Before this, a
   * failed post-save read left the old draft on screen with no feedback at all.
   */
  const adoptReadFailed = pendingAdopt !== null && !query.isFetching && state.kind !== 'ready'
  /**
   * Closed while the pre-retry read is in flight, too.
   *
   * That read exists to license re-sending an attempt whose payload is already frozen. If
   * the form stayed open, an edit made during the wait was not in what went out — and the
   * success that followed re-seeded the draft and erased it. The window is short and the
   * screen says what it is waiting for.
   */
  const formDisabled = frozen || pendingAdopt !== null || attempt.revalidating
  const savedNotice = savedVersion !== null && !body.dirty && pendingAdopt === null

  return (
    <section aria-labelledby="cari-contact-heading" className={styles.page}>
      <h1 id="cari-contact-heading">{TEXT.title}</h1>
      <BackLinks partyId={partyId} />

      <p className={styles.scopeNote}>{TEXT.scopeNote}</p>
      <p className={styles.company}>
        <span className={styles.companyLabel}>{TEXT.activeCompany}:</span>{' '}
        {activeCompany === null ? '—' : `${activeCompany.legalName} (${activeCompany.code})`}
      </p>

      <div aria-live="polite" className={styles.srOnly} data-testid="cari-contact-announcement">
        {savedNotice ? TEXT.announceSaved : ''}
      </div>

      {adoptReadFailed ? (
        <div data-state="read-recovery">
          <p className={styles.retained}>{TEXT.readRecovery}</p>
          <StateBlock state={state} subject={TEXT.subject} onRetry={() => void query.refetch()} />
        </div>
      ) : null}

      {savedNotice ? (
        <div role="status" className={styles.saved} data-state="saved">
          <p className={styles.savedTitle}>{TEXT.savedTitle}</p>
          <p>{TEXT.savedDetail}</p>
        </div>
      ) : null}

      {refreshedVersion !== null ? (
        <div role="status" className={styles.saved} data-state="refreshed">
          <p className={styles.savedTitle}>{TEXT.refreshedTitle}</p>
          <p>{TEXT.refreshedDetail}</p>
        </div>
      ) : null}

      {showErrors && problemCount > 0 ? (
        <div ref={summaryRef} tabIndex={-1} role="alert" className={styles.summary} data-state="form-invalid">
          {TEXT.errorSummary}
        </div>
      ) : null}

      <form
        noValidate
        onSubmit={(event) => {
          event.preventDefault()
          submit()
        }}
      >
        <fieldset className={styles.group} disabled={formDisabled}>
          <legend className={styles.legend}>{TEXT.contactHeading}</legend>
          <div className={styles.grid}>
            {SCALAR_FIELDS.map((fieldName) => (
              <Field
                key={fieldName}
                id={`cari-contact-${fieldName}`}
                label={labelFor(fieldName, 'scalar')}
                rule={refineScalarRules(form.scalars, CONTACT_SCALAR_RULES)[fieldName]}
                value={form.scalars[fieldName] ?? ''}
                problem={showErrors ? body.problems[fieldName] : undefined}
                onChange={(value) => setScalar(fieldName, value)}
              />
            ))}
          </div>
        </fieldset>

        <fieldset className={styles.group} disabled={formDisabled}>
          <legend className={styles.legend}>{TEXT.addressHeading}</legend>
          {form.addresses.length === 0 ? <p className={styles.empty}>{TEXT.noAddresses}</p> : null}
          <CollectionProblem problem={body.problems.addresses} />
          {form.addresses.map((row, index) => (
            <RowBlock
              key={row.id}
              row={row}
              index={index}
              idPrefix={`cari-address-${index}`}
              fields={ADDRESS_FIELDS}
              rules={ADDRESS_FIELD_RULES}
              collection="address"
              collectionProperty="addresses"
              optionLabels={{ purpose: ADDRESS_PURPOSE_LABEL }}
              problems={showErrors ? body.problems : {}}
              problemPrefix={`addresses.${index}`}
              hasFieldWork={rowHasFieldWork(original.collections.addresses, row, ADDRESS_FIELDS)}
              onChange={(fieldName, value) => setRowValue('addresses', index, fieldName, value)}
              onToggleState={() => toggleRowState('addresses', index)}
              onRemove={() => removeRow('addresses', index)}
            />
          ))}
          <button type="button" className={styles.secondary} onClick={addAddress}>
            {TEXT.addAddress}
          </button>
        </fieldset>

        <fieldset className={styles.group} disabled={formDisabled}>
          <legend className={styles.legend}>{TEXT.contactsHeading}</legend>
          {form.contacts.length === 0 ? <p className={styles.empty}>{TEXT.noContacts}</p> : null}
          <CollectionProblem problem={body.problems.contacts} />
          {form.contacts.map((row, index) => {
            const statusMarked = row.intent === 'deactivate' || row.intent === 'reactivate'
            return (
              <div key={row.id} className={styles.nest}>
                <RowBlock
                  row={row}
                  index={index}
                  idPrefix={`cari-contact-row-${index}`}
                  fields={CONTACT_FIELDS}
                  rules={CONTACT_FIELD_RULES}
                  collection="contact"
                  collectionProperty="contacts"
                  optionLabels={{}}
                  problems={showErrors ? body.problems : {}}
                  problemPrefix={`contacts.${index}`}
                  hasFieldWork={rowHasFieldWork(original.collections.contacts, row, CONTACT_FIELDS)}
                  onChange={(fieldName, value) => setRowValue('contacts', index, fieldName, value)}
                  onToggleState={() => toggleRowState('contacts', index)}
                  onRemove={() => removeRow('contacts', index)}
                />
                <div className={styles.channels}>
                  <p className={styles.channelsTitle}>{TEXT.channelsHeading}</p>
                  {(row.channels ?? []).map((channel, channelIndex) => (
                    <div key={channel.id} className={styles.channelRow} data-channel={channel.id}>
                      <div className={styles.grid}>
                        {CHANNEL_FIELDS.map((fieldName) => (
                          <Field
                            key={fieldName}
                            id={`cari-contact-row-${index}-channel-${channelIndex}-${fieldName}`}
                            label={labelFor(fieldName, 'channel')}
                            rule={refineChannelRules(channel.values, CHANNEL_FIELD_RULES)[fieldName]}
                            value={channel.values[fieldName] ?? ''}
                            optionLabel={fieldName === 'kind' ? CHANNEL_KIND_LABEL : undefined}
                            problem={
                              showErrors
                                ? body.problems[`contacts.${index}.channels.${channelIndex}.${fieldName}`]
                                : undefined
                            }
                            /* The kind of a channel the server already holds is fixed by the
                               contract, so the control is closed rather than offering an edit
                               that would be refused. A status operation sends no fields, so
                               while one is marked the channels are closed too. */
                            disabled={statusMarked || (fieldName === 'kind' && !channel.isNew)}
                            onChange={(value) => setChannelValue(index, channelIndex, fieldName, value)}
                          />
                        ))}
                      </div>
                      {channel.isNew ? (
                        <button
                          type="button"
                          className={styles.secondary}
                          onClick={() => removeChannel(index, channelIndex)}
                        >
                          {TEXT.removeRow}
                        </button>
                      ) : null}
                    </div>
                  ))}
                  <button
                    type="button"
                    className={styles.secondary}
                    disabled={statusMarked}
                    onClick={() => addChannel(index)}
                  >
                    {TEXT.addChannel}
                  </button>
                </div>
              </div>
            )
          })}
          <button type="button" className={styles.secondary} onClick={addContact}>
            {TEXT.addContact}
          </button>
        </fieldset>

        {retainedCount > 0 ? <p className={styles.retained}>{TEXT.retainedNote(retainedCount)}</p> : null}

        <CommandAttemptNotice
          state={attempt.state}
          repeatsUnresolved={repeatsUnresolved}
          onRetry={() =>
            attempt.retry(async () => {
              /**
               * A fresh, authorized, acknowledged read in THIS context — not the grant the
               * first send rode on. A refetch can settle from cache or be cancelled and
               * still resolve, so the recorded-read counter has to have MOVED: that is the
               * only evidence a read actually landed.
               */
              const before = contractReadSeq()
              await query.refetch()
              return contractReadSeq() > before && isCommandContractReady(contractContext)
            })
          }
          onAbandon={attempt.abandon}
          onAcknowledge={attempt.abandon}
          unresolvedRef={unresolvedRef}
          extra={
            attempt.state.phase === 'failed' && attempt.state.failure === 'version-conflict' ? (
              <p>
                <button type="button" className={styles.secondary} onClick={refreshRecord}>
                  {TEXT.refreshRecord}
                </button>
              </p>
            ) : undefined
          }
        />

        <div className={styles.actions}>
          {/**
            * Enabled whenever the user has unsaved work, valid or not.
            *
            * A Save that goes grey with no explanation is a worse failure than a refused
            * click: the user cannot tell whether the screen is broken, whether they are
            * unauthorized, or which field is at fault. Clicking shows the summary, moves
            * focus to it and marks every offending field — and still sends nothing and
            * mints no key.
            */}
          <button type="submit" className={styles.primary} disabled={formDisabled || !body.dirty}>
            {frozen ? TEXT.saving : TEXT.save}
          </button>
          {attempt.revalidating ? <span className={styles.hint}>{TEXT.revalidating}</span> : null}
          {!body.dirty && !attempt.revalidating ? (
            <span className={styles.hint}>{TEXT.nothingToSave}</span>
          ) : null}
        </div>
      </form>
    </section>
  )
}

/** A refusal about the whole collection, e.g. more operations than one request carries. */
function CollectionProblem({ problem }: { readonly problem?: FieldProblem | RowProblem }) {
  if (problem === undefined) return null
  return (
    <p role="alert" className={styles.error}>
      {problemText(problem)}
    </p>
  )
}

function BackLinks({ partyId }: { readonly partyId: string | undefined }) {
  return (
    <nav className={styles.back}>
      {/* Only accepted internal route identities: never a raw return URL. */}
      <Link to={`/cari/${encodeURIComponent(partyId ?? '')}`}>{TEXT.backToCard}</Link>
      <Link to="/cari">{TEXT.backToList}</Link>
    </nav>
  )
}

function RowBlock({
  row,
  index,
  idPrefix,
  fields,
  rules,
  collection,
  collectionProperty,
  optionLabels,
  problems,
  problemPrefix,
  hasFieldWork,
  onChange,
  onToggleState,
  onRemove,
}: {
  readonly row: RowState
  readonly index: number
  readonly idPrefix: string
  readonly fields: readonly string[]
  readonly rules: Readonly<Record<string, FieldRule>>
  readonly collection: 'address' | 'contact'
  /** The wire property this row belongs to, so a locator can scope to one collection. */
  readonly collectionProperty: 'addresses' | 'contacts'
  readonly optionLabels: Readonly<Record<string, Record<string, string>>>
  readonly problems: Readonly<Record<string, FieldProblem | RowProblem>>
  readonly problemPrefix: string
  readonly hasFieldWork: boolean
  readonly onChange: (field: string, value: string) => void
  readonly onToggleState: () => void
  readonly onRemove: () => void
}) {
  const rowProblem = problems[problemPrefix]
  const statusMarked = row.intent === 'deactivate' || row.intent === 'reactivate'
  /**
   * The two cannot both be pending on one row.
   *
   * A status operation carries the identity and nothing else, so a row that is edited AND
   * marked passive would have had its edit dropped on save without a word. Rather than
   * choosing for the user, whichever they started closes the other — and says why.
   */
  const statusBlocked = hasFieldWork && !statusMarked
  return (
    <div
      className={styles.row}
      data-collection={collectionProperty}
      data-row={row.id}
      data-row-index={index}
      data-intent={row.intent}
    >
      <div className={styles.rowHead}>
        {row.isNew ? <span className={styles.badge}>{TEXT.newRow}</span> : null}
        {!row.isNew && !row.isActive ? <span className={styles.badge}>{TEXT.passive}</span> : null}
        {row.isNew ? (
          <button type="button" className={styles.secondary} onClick={onRemove}>
            {TEXT.removeRow}
          </button>
        ) : (
          <button type="button" className={styles.secondary} disabled={statusBlocked} onClick={onToggleState}>
            {statusMarked ? TEXT.undoState : row.isActive ? TEXT.deactivate : TEXT.reactivate}
          </button>
        )}
        {row.intent === 'deactivate' ? <span className={styles.mark}>{TEXT.markedDeactivate}</span> : null}
        {row.intent === 'reactivate' ? <span className={styles.mark}>{TEXT.markedReactivate}</span> : null}
        {statusBlocked ? <span className={styles.mark}>{TEXT.statusBlockedByEdits}</span> : null}
        {statusMarked ? <span className={styles.mark}>{TEXT.fieldsFrozenByStatus}</span> : null}
      </div>
      {rowProblem !== undefined ? (
        <p role="alert" className={styles.error}>
          {problemText(rowProblem)}
        </p>
      ) : null}
      <div className={styles.grid}>
        {fields.map((fieldName) => (
          <Field
            key={fieldName}
            id={`${idPrefix}-${fieldName}`}
            label={labelFor(fieldName, collection)}
            rule={rules[fieldName]}
            value={row.values[fieldName] ?? ''}
            optionLabel={optionLabels[fieldName]}
            problem={problems[`${problemPrefix}.${fieldName}`]}
            disabled={statusMarked}
            onChange={(value) => onChange(fieldName, value)}
          />
        ))}
      </div>
    </div>
  )
}

function Field({
  id,
  label,
  rule,
  value,
  problem,
  optionLabel,
  disabled = false,
  onChange,
}: {
  readonly id: string
  readonly label: string
  readonly rule: FieldRule
  readonly value: string
  readonly problem?: FieldProblem | RowProblem
  readonly optionLabel?: Record<string, string>
  readonly disabled?: boolean
  readonly onChange: (value: string) => void
}) {
  const errorId = `${id}-error`
  const described = problem === undefined ? undefined : errorId
  /**
   * A value the server holds that is not in the published set is kept as an option of
   * its own. Dropping it would silently rewrite the record to whatever the select
   * happened to land on the first time the user opened the row.
   */
  const options = rule.options
  const extraOption =
    options !== undefined && value.length > 0 && !options.includes(value) ? value : null

  return (
    <div className={styles.field}>
      <label htmlFor={id}>
        {label}
        {!rule.required ? <span className={styles.optional}> ({TEXT.optional})</span> : null}
      </label>
      {options === undefined ? (
        <input
          id={id}
          type="text"
          inputMode={inputModeFor(rule)}
          value={value}
          disabled={disabled}
          aria-invalid={problem === undefined ? undefined : true}
          aria-describedby={described}
          onChange={(event) => onChange(event.target.value)}
        />
      ) : (
        <select
          id={id}
          value={value}
          disabled={disabled}
          aria-invalid={problem === undefined ? undefined : true}
          aria-describedby={described}
          onChange={(event) => onChange(event.target.value)}
        >
          <option value="">{TEXT.selectPlaceholder}</option>
          {options.map((option) => (
            <option key={option} value={option}>
              {optionLabel?.[option] ?? option}
            </option>
          ))}
          {extraOption === null ? null : (
            <option value={extraOption}>{TEXT.keepCurrent(extraOption)}</option>
          )}
        </select>
      )}
      {problem === undefined ? null : (
        <p id={errorId} className={styles.error}>
          {problemText(problem, rule)}
        </p>
      )}
    </div>
  )
}
