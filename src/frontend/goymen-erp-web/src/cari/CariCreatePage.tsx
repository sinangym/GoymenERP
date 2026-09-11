/**
 * C-02 Yeni Cari — the create SUBSET the accepted backend supports
 * (CARI_C02_LINK_JIT_SPEC_v1 §2.1, §3, §4, §5; screen id `C-02`, route `cari.create`).
 *
 * One submit = one Party + one account in the ACTIVE company. The four fields
 * below are the whole command. Deliberately absent, and forbidden by the spec
 * (§4.1): a code field (server-generated), VKN/TCKN, bank, address, contact,
 * tax/registry, risk, limit, balance, a reason, `externalIntegrationCode`, and any
 * AI suggestion without an owner endpoint. A field with no command behind it is
 * not rendered empty — it is not rendered.
 *
 * The company is shell truth, shown read-only. It is never a form value and
 * nothing here can override the `X-Company-Id` the transport sends (§3.2).
 */

import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { createCounterparty, type CreateCounterpartyPayload } from './commandApi'
import type { CounterpartyMutationResult } from './mutationContract'
import { PARTY_KIND, rolesFromFlags, type PartyKind } from './contract'
import {
  EMPTY_CREATE_FORM,
  LEGAL_NAME_MAX,
  TRADE_NAME_MAX,
  isCreateFormPristine,
  validateCreateForm,
  type CreateFormErrors,
  type CreateFormValues,
} from './createForm'
import { canBeginAttempt, isAttemptFrozen, repeatsUnresolvedAttempt } from './commandAttempt'
import { useCommandAttempt } from './useCommandAttempt'
import { CariRoleFieldset } from './CariRoleFieldset'
import { CommandAttemptNotice } from './CommandAttemptNotice'
import { CariBackLink } from './CariBackLink'
import { CARI_CREATE_TEXT as TEXT, CARI_KIND_LABEL, CARI_ROLE_LABEL } from './cariCommandText'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { API_BASE, isCommandContractReady, type ContractContext } from './clientContract'
import { fetchCounterparties } from './api'
import { CAPABILITY } from '../shell/capabilities'
import { useCompanyDirectory } from '../shell/useShellOrchestrator'
import { StateBlock } from '../shell/StateBlock'
import styles from './CariCreatePage.module.css'

/** The dirty scope this form registers with the shell (spec §4.2). */
export const CARI_CREATE_DIRTY_SCOPE = 'cari.create'

/** Operation id of the create command inside a logical attempt (§4.3). */
const CREATE_OPERATION = 'counterparty.create'

/**
 * Remounted on every context change, exactly like C-01/C-03. The form, its
 * validation state and — above all — its in-memory idempotency attempt belong to
 * ONE actor in ONE company; a new context gets a new, empty form, never a form
 * carried over. `confirming-switch` keeps the same key on purpose: nothing has
 * changed yet, and "Kal" must find the form as it was.
 */
export function CariCreatePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <CariCreateForm key={namespace ?? 'no-context'} namespace={namespace} />
}

function CariCreateForm({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const dispatch = useShellDispatch()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const directory = useCompanyDirectory()
  const attempt = useCommandAttempt<CreateCounterpartyPayload, CounterpartyMutationResult>()

  const activeCompanyId = shell.activeContext?.companyId ?? null
  const singleCompanyContext = shell.activeContext?.scopeMode === 'single' && activeCompanyId !== null
  // The detail is a SEPARATE grant (#182). Success navigates there only when held;
  // otherwise the record is summarised here, with no link the route would refuse.
  const canOpenDetail = hasCapability(shell, CAPABILITY.cariPartiesDetail)
  const activeCompany = useMemo(
    () => directory.find((company) => company.id === activeCompanyId) ?? null,
    [directory, activeCompanyId],
  )
  const companyLabel =
    activeCompany === null ? TEXT.activeCompanyUnknown : `${activeCompany.legalName} (${activeCompany.code})`

  /**
   * THE COMPATIBILITY PROBE (spec §4.2).
   *
   * No Cari write opens until a fresh, successful, decoder-valid Cari GET in THIS
   * context has come back with the server's acknowledgment. This form issues no read
   * of its own otherwise, so without this probe it could only ever have discovered
   * an incompatible server by writing to it — which is the one thing the protocol
   * exists to prevent.
   *
   * A single row is enough and is what the handoff's own example asks for: this is a
   * compatibility question, not a data one. It is entry-scoped like every other Cari
   * read, so returning to the form re-proves compatibility rather than trusting a
   * boolean from a previous visit.
   */
  const contractContext: ContractContext | null = useMemo(
    () => (namespace === null ? null : { origin: API_BASE, namespace }),
    [namespace],
  )
  const probe = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'cari-contract-probe'],
    enabled: namespace !== null && singleCompanyContext,
    queryFn: async ({ signal }) =>
      await fetchCounterparties(
        { mode: 'single', companyId: activeCompanyId },
        { pageSize: 1 },
        signal,
        contractContext,
      ),
  })

  /**
   * Writing is possible only once the probe has actually answered. `probe.isFetching`
   * matters as much as the readiness flag: mid-flight, "not ready" is the question
   * still being asked, not an answer, and showing the incompatibility notice then
   * would accuse a server that has not spoken yet.
   */
  const probeAnswered = !probe.isPending && !probe.isFetching
  const writeReady = isCommandContractReady(contractContext)
  const incompatibleServer = probeAnswered && !writeReady

  const [values, setValues] = useState<CreateFormValues>(EMPTY_CREATE_FORM)
  const [showErrors, setShowErrors] = useState(false)
  // Focus targets: the validation summary (UX-AC-001: it — not the first invalid
  // field — receives focus on an invalid submit) and the abandoned-attempt box.
  // The summary is rendered by the same state change that asks for the focus, so
  // the focus is applied by an effect after that render, via a request counter.
  const summaryRef = useRef<HTMLDivElement>(null)
  const unresolvedRef = useRef<HTMLDivElement>(null)
  const [summaryFocusRequest, setSummaryFocusRequest] = useState(0)

  const phase = attempt.state.phase
  const frozen = isAttemptFrozen(attempt.state)
  /**
   * A new attempt may start from `idle` or from an ABANDONED (unfrozen) one — and
   * only once compatibility has been proven here. `writeReady` is checked again in
   * the transport; this one is so the button never invites a submit that cannot go.
   */
  const canSubmit = canBeginAttempt(attempt.state) && writeReady
  const completed = attempt.state.phase === 'completed' ? attempt.state : null
  const failure = attempt.state.phase === 'failed' ? attempt.state.failure : null

  const validation = validateCreateForm(values)
  const errors: CreateFormErrors = showErrors && !validation.ok ? validation.errors : {}
  const errorList = [errors.kind, errors.legalName, errors.tradeName, errors.roles].filter(
    (message): message is string => message !== undefined,
  )
  // After "İşlemi bırak" the attempt is still unresolved. While the form holds
  // the very payload that was sent, submitting again would be that attempt under
  // a new key (C-004-P1-001); the notice below offers the same-key retry instead.
  const repeatsUnresolved =
    validation.ok && repeatsUnresolvedAttempt(attempt.state, CREATE_OPERATION, validation.payload)

  // Dirty scope (§4.2): from the first user change until success or an explicit
  // reset. A frozen attempt is dirty even if the fields were somehow pristine —
  // the pending write is the thing a silent switch must not carry.
  const dirty = frozen || (completed === null && !isCreateFormPristine(values))
  useEffect(() => {
    dispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: CARI_CREATE_DIRTY_SCOPE })
  }, [dirty, dispatch])
  useEffect(() => () => dispatch({ type: 'DIRTY_CLEAR', scope: CARI_CREATE_DIRTY_SCOPE }), [dispatch])

  function update(patch: Partial<CreateFormValues>) {
    // Editing after a definite failure makes the next submit a NEW business
    // attempt with a new snapshot and key; the old key is never reused for a
    // different payload (§4.3).
    if (failure !== null) attempt.abandon()
    setValues((current) => ({ ...current, ...patch }))
  }

  // UX-AC-001: the validation summary receives focus on an invalid submit. It is
  // rendered by the same state change that requests the focus, so the focus is
  // applied here, after that render. Each field keeps its own error linked by
  // aria-describedby; Tab from the summary reaches the first control to fix.
  useEffect(() => {
    if (summaryFocusRequest > 0) summaryRef.current?.focus()
  }, [summaryFocusRequest])

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    // Double submit, a frozen snapshot or an unacknowledged failure: nothing is sent.
    if (!canSubmit || activeCompanyId === null) return
    const result = validateCreateForm(values)
    if (!result.ok) {
      // Client-invalid: the summary lists every error and takes focus; each control
      // is linked to its own error text. NO key is minted (§4.3).
      setShowErrors(true)
      setSummaryFocusRequest((count) => count + 1)
      return
    }
    setShowErrors(false)
    const payload = result.payload
    const companyId = activeCompanyId
    const outcome = attempt.submit(CREATE_OPERATION, payload, (idempotencyKey, signal) =>
      createCounterparty({ companyId, idempotencyKey, contract: { origin: API_BASE, namespace: namespace ?? '' } }, payload, signal),
    )
    // The unchanged payload of an abandoned, unresolved attempt: nothing was sent
    // and no key was minted. The explanation is already on screen; focus lands on
    // it so the refusal is perceivable without sight (UX-AC-001).
    if (outcome === 'unchanged') unresolvedRef.current?.focus()
  }

  // Completion happens ONCE per snapshot. A replay (`Idempotency-Replayed: true`)
  // settles the same snapshot and therefore reaches the same, single completion:
  // one announcement, one navigation, no second record (§4.4).
  const completedKeyRef = useRef<string | null>(null)
  useEffect(() => {
    if (completed === null || completedKeyRef.current === completed.snapshot.key) return
    completedKeyRef.current = completed.snapshot.key
    // Reads cached under this context predate the new record.
    if (namespace !== null) {
      void queryClient.invalidateQueries({ predicate: (query) => query.queryKey[0] === namespace })
    }
    // The canonical detail route, from the VERIFIED id — never the Location header.
    if (canOpenDetail) void navigate(`/cari/${completed.result.id}`)
  }, [completed, canOpenDetail, namespace, navigate, queryClient])

  const announcement = completed === null ? '' : TEXT.announceCreated(completed.result.code)

  if (!singleCompanyContext) {
    return (
      <section aria-labelledby="cari-create-heading" className={styles.page}>
        <h1 id="cari-create-heading">{TEXT.title}</h1>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={TEXT.noSingleCompany} />
      </section>
    )
  }

  // 403 / 401: the form and anything typed into it are withheld, not just the
  // submit. The boundary has already re-fenced the capability or ended the
  // session; this is the frame before the route closes.
  if (failure === 'forbidden' || failure === 'unauthenticated') {
    return (
      <section aria-labelledby="cari-create-heading" className={styles.page}>
        <h1 id="cari-create-heading">{TEXT.title}</h1>
        <CariBackLink label={TEXT.backToList} />
        <StateBlock state={{ kind: failure }} subject={TEXT.subject} />
      </section>
    )
  }

  if (completed !== null && !canOpenDetail) {
    const record = completed.result
    return (
      <section aria-labelledby="cari-create-heading" className={styles.page}>
        <h1 id="cari-create-heading">{TEXT.title}</h1>
        <CariBackLink label={TEXT.backToList} />
        <div role="status" aria-live="polite" className={styles.announce}>
          {announcement}
        </div>
        <div className={styles.success} data-outcome="created">
          <h2 className={styles.successTitle}>{TEXT.successTitle}</h2>
          <dl className={styles.facts}>
            <Fact label={TEXT.successCode} value={record.code} />
            <Fact label={TEXT.legalName} value={record.legalName} />
            {record.tradeName !== null && record.tradeName !== record.legalName ? (
              <Fact label={TEXT.tradeName} value={record.tradeName} />
            ) : null}
            <Fact label={TEXT.kind} value={CARI_KIND_LABEL[record.kind]} />
            <Fact label={TEXT.successCompany} value={companyLabel} />
            <Fact
              label={TEXT.successRoles}
              value={rolesFromFlags(record.account.roles)
                .map((role) => CARI_ROLE_LABEL[role])
                .join(', ')}
            />
          </dl>
          <p className={styles.successNote}>{TEXT.successNoDetail}</p>
          <button
            type="button"
            className={styles.secondary}
            onClick={() => {
              attempt.abandon()
              setValues(EMPTY_CREATE_FORM)
              setShowErrors(false)
            }}
          >
            {TEXT.createAnother}
          </button>
        </div>
      </section>
    )
  }

  const summaryId = 'cari-create-summary'
  const showSummary = showErrors && errorList.length > 0

  return (
    <section aria-labelledby="cari-create-heading" className={styles.page}>
      <h1 id="cari-create-heading">{TEXT.title}</h1>
      <CariBackLink label={TEXT.backToList} />
      <div role="status" aria-live="polite" className={styles.announce}>
        {announcement}
      </div>

      {/* The server on the other end cannot represent a role-less counterparty, so
          the form is closed rather than left to discover that by writing (spec §4.2).
          Stated only once the probe has answered — mid-flight silence is not a
          verdict. */}
      {incompatibleServer ? (
        <div role="alert" data-state="client-contract" className={styles.contractBlocked}>
          <p>
            <strong>Uyumlu istemci gerekli</strong>
          </p>
          <p>
            Bu cari işlemi desteklenen istemci sözleşmesi ile yeniden açılmalıdır. Sayfayı
            yenileyin; form kapalıdır ve hiçbir istek gönderilmedi.
          </p>
        </div>
      ) : null}

      <form
        className={styles.form}
        noValidate
        onSubmit={onSubmit}
        aria-describedby={showSummary ? summaryId : undefined}
      >
        {/* Shell truth, read-only: company code + legal name from the directory,
            never a raw id, never an input (§4.1). */}
        <div className={styles.readonly}>
          <span className={styles.readonlyLabel}>{TEXT.activeCompany}</span>
          <span className={styles.readonlyValue}>{companyLabel}</span>
        </div>

        {/* Validation summary BEFORE the fields: it takes focus on an invalid
            submit (UX-AC-001), so the controls it lists follow it in tab order. */}
        {showSummary ? (
          <div id={summaryId} ref={summaryRef} tabIndex={-1} role="alert" className={styles.summary}>
            <p>{TEXT.errorSummary}</p>
            <ul>
              {errorList.map((message) => (
                <li key={message}>{message}</li>
              ))}
            </ul>
          </div>
        ) : null}

        {/* Frozen while a request is in flight or its outcome is unknown: the
            submitted snapshot cannot be edited under the same key (§4.3). */}
        <fieldset className={styles.fields} disabled={frozen}>
          <legend className={styles.srOnly}>{TEXT.formLegend}</legend>

          <div className={styles.field}>
            <label htmlFor="cari-create-kind">{TEXT.kind}</label>
            <select
              id="cari-create-kind"
              value={values.kind === '' ? '' : String(values.kind)}
              required
              aria-required="true"
              aria-invalid={errors.kind ? true : undefined}
              aria-describedby={errors.kind ? 'cari-create-kind-error' : undefined}
              onChange={(event) =>
                update({ kind: event.target.value === '' ? '' : (Number(event.target.value) as PartyKind) })
              }
            >
              <option value="">{TEXT.kindPlaceholder}</option>
              <option value={String(PARTY_KIND.LegalEntity)}>{CARI_KIND_LABEL[PARTY_KIND.LegalEntity]}</option>
              <option value={String(PARTY_KIND.NaturalPerson)}>{CARI_KIND_LABEL[PARTY_KIND.NaturalPerson]}</option>
            </select>
            <FieldError id="cari-create-kind-error" message={errors.kind} />
          </div>

          <div className={styles.field}>
            <label htmlFor="cari-create-legal-name">{TEXT.legalName}</label>
            <input
              id="cari-create-legal-name"
              type="text"
              autoComplete="organization"
              value={values.legalName}
              required
              aria-required="true"
              aria-invalid={errors.legalName ? true : undefined}
              aria-describedby={errors.legalName ? 'cari-create-legal-name-error' : 'cari-create-legal-name-hint'}
              onChange={(event) => update({ legalName: event.target.value })}
            />
            <span id="cari-create-legal-name-hint" className={styles.hint}>
              1–{LEGAL_NAME_MAX}
            </span>
            <FieldError id="cari-create-legal-name-error" message={errors.legalName} />
          </div>

          <div className={styles.field}>
            <label htmlFor="cari-create-trade-name">
              {TEXT.tradeName} <span className={styles.optional}>({TEXT.optional})</span>
            </label>
            <input
              id="cari-create-trade-name"
              type="text"
              value={values.tradeName}
              aria-invalid={errors.tradeName ? true : undefined}
              aria-describedby={errors.tradeName ? 'cari-create-trade-name-error' : 'cari-create-trade-name-hint'}
              onChange={(event) => update({ tradeName: event.target.value })}
            />
            <span id="cari-create-trade-name-hint" className={styles.hint}>
              ≤{TRADE_NAME_MAX}
            </span>
            <FieldError id="cari-create-trade-name-error" message={errors.tradeName} />
          </div>

          <CariRoleFieldset
            idPrefix="cari-create"
            legend={TEXT.roles}
            value={values.roles}
            onChange={(roles) => update({ roles })}
            error={errors.roles}
          />
        </fieldset>

        <CommandAttemptNotice
          state={attempt.state}
          onRetry={attempt.retry}
          onAbandon={attempt.abandon}
          onAcknowledge={attempt.abandon}
          repeatsUnresolved={repeatsUnresolved}
          unresolvedRef={unresolvedRef}
        />

        <div className={styles.actions}>
          <button
            type="submit"
            className={styles.primary}
            disabled={!canSubmit}
            aria-busy={phase === 'submitting' || undefined}
          >
            {phase === 'submitting' ? TEXT.submitting : TEXT.submit}
          </button>
        </div>
      </form>
    </section>
  )
}

function FieldError({ id, message }: { readonly id: string; readonly message?: string }) {
  if (!message) return null
  return (
    <p id={id} className={styles.error}>
      {message}
    </p>
  )
}

function Fact({ label, value }: { readonly label: string; readonly value: string }) {
  return (
    <div className={styles.fact}>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  )
}
