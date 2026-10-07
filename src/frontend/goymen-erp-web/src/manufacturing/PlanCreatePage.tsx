import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useLocation } from 'react-router'
import { fetchWorkCenters } from '../organization/api'
import type { WorkCenterSummary } from '../organization/contract'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { isKilogramText } from './contract'
import { MANUFACTURING_CAPABILITY, orderListPath, parsePlanCreateLabel, planDetailPath } from './capabilities'
import {
  fetchParentRollCandidates,
  PARENT_ROLL_PRESELECT_LIMIT,
  type ParentRollCandidate,
  type ParentRollCandidates,
} from './planReferences'
import {
  savePlan,
  type PlanTargetBasis,
  type SavePlanOutcome,
  type SavePlanRequest,
} from './planCommandApi'
import {
  pendingPlanCreateKey,
  recallPendingPlanCreate,
  releasePendingPlanCreate,
  reservePendingPlanCreate,
  type PendingPlanCreate,
} from './pendingPlanStore'
import { PlanPreviewPanel } from './PlanPreviewPanel'
import { planPreviewInput, usePlanPreview } from './usePlanPreview'
import styles from './PlanPage.module.css'

type ReadState<T> =
  | { readonly type: 'idle' | 'loading' }
  | { readonly type: 'ready'; readonly data: T }
  | { readonly type: 'failed'; readonly message: string }

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly snapshot: PendingPlanCreate }
  | { readonly phase: 'unknown'; readonly snapshot: PendingPlanCreate; readonly message: string }
  | {
      readonly phase: 'failed'
      readonly message: string
      readonly snapshot?: PendingPlanCreate
      readonly unresolvedSince?: string
    }
  | { readonly phase: 'completed'; readonly result: Extract<SavePlanOutcome, { type: 'applied' }> }

/** AT-063: the roll an ST-01 row named, as far as Inventory's candidate read confirmed it. */
type Preselection =
  | { readonly type: 'none' | 'loading' }
  | { readonly type: 'selected'; readonly labelCode: string }
  | { readonly type: 'refused'; readonly labelCode: string; readonly code: string }
  | { readonly type: 'missing'; readonly windowFull: boolean }
  | { readonly type: 'failed'; readonly message: string }

interface PreselectedLabel {
  readonly labelId: string
  readonly labelCode: string
}

interface SelectedInput {
  readonly candidate: ParentRollCandidate
  readonly plannedKg: string
}

interface SlitLineForm {
  readonly id: string
  readonly bandWidthMm: string
  readonly count: string
}

function newId(): string {
  const bytes = new Uint8Array(16)
  globalThis.crypto.getRandomValues(bytes)
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

function readFailure(subject: string, outcome: { readonly type: string; readonly status?: number }): string {
  if (outcome.type === 'http') {
    if (outcome.status === 401 || outcome.status === 403) return `${subject} için yetkiniz artık geçerli değil.`
    return `${subject} kaynağı yanıt vermedi (${outcome.status}).`
  }
  if (outcome.type === 'offline') return 'Çevrimdışısınız.'
  if (outcome.type === 'fence-stale') return 'Şirket veya oturum değişti; eski sonuç kullanılmadı.'
  return outcome.type === 'malformed'
    ? `${subject} kaynağı beklenen sözleşmeyle yanıt vermedi.`
    : `${subject} kaynağına ulaşılamadı.`
}

function commandFailure(outcome: Extract<SavePlanOutcome, { type: 'failed' }>): string {
  if (outcome.status === 401 || outcome.status === 403) return 'Plan kaydetme yetkiniz artık geçerli değil.'
  if (outcome.status === 404) return 'Seçilen ana rulo veya şirket bağlamı artık bulunamıyor.'
  if (outcome.status === 409) return `Seçilen kayıtların sürümü veya yazma durumu değişti (${outcome.code ?? 'conflict'}).`
  if (outcome.status === 422) return `Plan girdisi kabul edilmedi (${outcome.code ?? 'rule_refusal'}).`
  return `Plan kaydedilmedi (${outcome.code ?? outcome.status}).`
}

function positiveKilograms(value: string): boolean {
  return isKilogramText(value) && /[1-9]/.test(value)
}

export function PlanCreatePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const location = useLocation()
  const preselect = parsePlanCreateLabel(location.search)
  return <PlanCreate
    key={`${namespace ?? 'no-context'}:${shell.routeEntryGeneration}:${preselect?.labelId ?? ''}`}
    namespace={namespace}
    preselect={preselect}
  />
}

function PlanCreate({ namespace, preselect }: {
  readonly namespace: string | null
  readonly preselect: PreselectedLabel | null
}) {
  const shell = useShellState()
  const authBoundary = useAuthBoundary()
  const { capture: captureSecurity, report: reportSecurity } = authBoundary
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const storeKey = pendingPlanCreateKey(shell.sessionEpoch, actorId, companyId)
  const recalled = recallPendingPlanCreate(storeKey)
  const [attempt, setAttempt] = useState<Attempt>(() =>
    recalled === null
      ? { phase: 'idle' }
      : { phase: 'unknown', snapshot: recalled, message: 'Önceki gönderimin sonucu bilinmiyor.' },
  )
  const [workCenters, setWorkCenters] = useState<ReadState<readonly WorkCenterSummary[]>>({ type: 'loading' })
  const [workCenterId, setWorkCenterId] = useState('')
  const [search, setSearch] = useState(preselect?.labelCode ?? '')
  const [preselection, setPreselection] = useState<Preselection>({ type: preselect === null ? 'none' : 'loading' })
  const preselectId = preselect?.labelId ?? null
  const preselectCode = preselect?.labelCode ?? ''
  const [candidates, setCandidates] = useState<ReadState<ParentRollCandidates>>({ type: 'idle' })
  const [inputs, setInputs] = useState<readonly SelectedInput[]>([])
  const [targetBasis, setTargetBasis] = useState<'' | PlanTargetBasis>('')
  const [targetKg, setTargetKg] = useState('')
  const [slittingEnabled, setSlittingEnabled] = useState(false)
  const [slitLines, setSlitLines] = useState<readonly SlitLineForm[]>([])
  const [windingTargetKg, setWindingTargetKg] = useState('')
  const [formError, setFormError] = useState<string | null>(null)
  const candidateGeneration = useRef(0)
  const commandGeneration = useRef(0)
  const inFlight = useRef(false)

  useEffect(() => {
    const controller = new AbortController()
    async function read() {
      if (companyId === null) return
      setWorkCenters({ type: 'loading' })
      const security = captureSecurity()
      const outcome = await fetchWorkCenters(companyId, controller.signal)
      if (controller.signal.aborted) return
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        reportSecurity(outcome.status, security)
      }
      setWorkCenters(
        outcome.type === 'ok-single'
          ? { type: 'ready', data: outcome.data }
          : { type: 'failed', message: readFailure('İş merkezi', outcome) },
      )
    }
    void read()
    return () => {
      controller.abort()
      candidateGeneration.current += 1
      commandGeneration.current += 1
    }
  }, [captureSecurity, companyId, namespace, reportSecurity])

  // AT-063 "Üretim planla" from ST-01: the named label is looked up through the SAME Inventory candidate read a manual
  // search uses (its code as the query, the owner's widest window) and selected only when that exact id answers as
  // draft-selectable. Nothing is trusted from the URL; the save still sends the candidate's label version.
  useEffect(() => {
    if (companyId === null || preselectId === null) return
    const controller = new AbortController()
    const generation = ++candidateGeneration.current
    async function read(company: string, labelId: string) {
      setCandidates({ type: 'loading' })
      const security = captureSecurity()
      const outcome = await fetchParentRollCandidates(company, preselectCode, controller.signal, PARENT_ROLL_PRESELECT_LIMIT)
      if (controller.signal.aborted || generation !== candidateGeneration.current) return
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        reportSecurity(outcome.status, security)
      }
      if (outcome.type !== 'ok-single') {
        const message = readFailure('Ana rulo', outcome)
        setCandidates({ type: 'failed', message })
        setPreselection({ type: 'failed', message })
        return
      }
      setCandidates({ type: 'ready', data: outcome.data })
      const found = outcome.data.items.find((item) => item.labelId.toLowerCase() === labelId.toLowerCase())
      if (found === undefined) {
        setPreselection({ type: 'missing', windowFull: outcome.data.searchWindowFull })
        return
      }
      if (!found.draftSelectable) {
        setPreselection({ type: 'refused', labelCode: found.labelCode, code: found.draftRefusalCode ?? 'refused' })
        return
      }
      setInputs((current) => current.some((input) => input.candidate.labelId === found.labelId)
        ? current
        : [...current, { candidate: found, plannedKg: '' }])
      setPreselection({ type: 'selected', labelCode: found.labelCode })
    }
    void read(companyId, preselectId)
    return () => controller.abort()
  }, [captureSecurity, companyId, preselectCode, preselectId, reportSecurity])

  const readCandidates = useCallback(async () => {
    if (companyId === null) return
    const generation = ++candidateGeneration.current
    const controller = new AbortController()
    setCandidates({ type: 'loading' })
    const security = captureSecurity()
    const outcome = await fetchParentRollCandidates(companyId, search, controller.signal)
    if (generation !== candidateGeneration.current) return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      reportSecurity(outcome.status, security)
    }
    setCandidates(
      outcome.type === 'ok-single'
        ? { type: 'ready', data: outcome.data }
        : { type: 'failed', message: readFailure('Ana rulo', outcome) },
    )
  }, [captureSecurity, companyId, reportSecurity, search])

  const send = useCallback(
    async (snapshot: PendingPlanCreate, unresolvedSince?: string) => {
      if (
        companyId === null ||
        actorId === null ||
        snapshot.companyId !== companyId ||
        snapshot.actorId !== actorId ||
        inFlight.current
      ) {
        return
      }
      inFlight.current = true
      const generation = ++commandGeneration.current
      reservePendingPlanCreate(storeKey, snapshot)
      setAttempt({ phase: 'submitting', snapshot })
      const security = captureSecurity()
      const outcome = await savePlan(snapshot.request, {
        companyId: snapshot.companyId,
        idempotencyKey: snapshot.idempotencyKey,
      })
      if (generation !== commandGeneration.current) return
      inFlight.current = false
      if (outcome.type === 'failed' && (outcome.status === 401 || outcome.status === 403)) {
        reportSecurity(outcome.status, security)
      }
      if (outcome.type === 'unknown') {
        setAttempt({
          phase: 'unknown',
          snapshot,
          message: unresolvedSince ?? 'Sonuç bilinmiyor. Aynı içerik ve anahtarla yeniden gönderin.',
        })
        return
      }
      if (outcome.type === 'failed' && unresolvedSince !== undefined) {
        setAttempt({
          phase: 'failed',
          snapshot,
          unresolvedSince,
          message: commandFailure(outcome),
        })
        return
      }
      releasePendingPlanCreate(storeKey, snapshot.idempotencyKey)
      setAttempt(
        outcome.type === 'applied'
          ? { phase: 'completed', result: outcome }
          : { phase: 'failed', message: commandFailure(outcome) },
      )
    },
    [actorId, captureSecurity, companyId, reportSecurity, storeKey],
  )

  // AT-061 / AT-062: the button-less backend preview of what is typed (DEC-PRDCT-005). It writes nothing and never
  // gates the save below.
  const selectedWorkCenter = workCenters.type === 'ready'
    ? workCenters.data.find((workCenter) => workCenter.id === workCenterId)
    : undefined
  const slittingActive = selectedWorkCenter?.kind === 'Slitting' && slittingEnabled
  const previewInput = planPreviewInput(
    workCenterId,
    inputs.map(({ candidate, plannedKg }) => ({
      parentRollId: candidate.labelId,
      label: candidate.labelCode,
      labelVersion: candidate.labelVersion,
      plannedKg,
    })),
    targetBasis,
    targetKg,
    {
      enabled: slittingActive,
      lines: slitLines.map((line, index) => ({ ...line, label: `Bant ${index + 1}` })),
      windingTargetKg,
    },
  )
  const preview = usePlanPreview(companyId, previewInput, authBoundary)

  function labelOf(parentRollId: string): string {
    return inputs.find((input) => input.candidate.labelId === parentRollId)?.candidate.labelCode ?? parentRollId
  }

  function workCenterLabel(id: string): string {
    const found = workCenters.type === 'ready' ? workCenters.data.find((workCenter) => workCenter.id === id) : undefined
    return found === undefined ? id : `${found.code} — ${found.name}`
  }

  function addCandidate(candidate: ParentRollCandidate) {
    if (!candidate.draftSelectable || inputs.some((input) => input.candidate.labelId === candidate.labelId)) return
    setInputs((current) => [...current, { candidate, plannedKg: '' }])
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    if (
      companyId === null ||
      actorId === null ||
      attempt.phase === 'submitting' ||
      attempt.phase === 'unknown' ||
      (attempt.phase === 'failed' && attempt.unresolvedSince !== undefined)
    ) {
      return
    }
    if (workCenterId === '' || inputs.length === 0 || inputs.some((input) => !positiveKilograms(input.plannedKg))) {
      setFormError('İş merkezi, en az bir ana rulo ve her rulo için sıfırdan büyük planlanan kg zorunludur.')
      return
    }
    if ((targetBasis === '') !== (targetKg === '') || (targetKg !== '' && !positiveKilograms(targetKg))) {
      setFormError('Hedef kullanılacaksa türü ve sıfırdan büyük kg değeri birlikte girilmelidir.')
      return
    }
    if (previewInput.type !== 'ready') {
      setFormError(previewInput.reasons.join(' '))
      return
    }
    setFormError(null)
    const request: SavePlanRequest = {
      planId: newId(),
      workCenterId,
      inputs: inputs.map(({ candidate, plannedKg }) => ({
        parentRollId: candidate.labelId,
        plannedKg,
        expectedLabelVersion: candidate.labelVersion,
      })),
      target: targetBasis === '' ? null : { basis: targetBasis, kg: targetKg },
      slittingSpec: previewInput.request.slittingSpec ?? null,
    }
    void send({ actorId, companyId, idempotencyKey: newId(), request })
  }

  if (companyId === null || actorId === null) {
    return <main className={styles.page}><p role="alert">Yetkili tek şirket bağlamı seçilmeden plan oluşturulamaz.</p></main>
  }
  if (attempt.phase === 'completed') {
    const plan = attempt.result.plan
    return (
      <main className={styles.page}>
        <section className={styles.successCard} role="status">
          <p className={styles.eyebrow}>ÜRETİM PLANI</p>
          <h1>Taslak plan kaydedildi</h1>
          <p className={styles.mono}>{plan.planId} · sürüm {plan.version}</p>
          {attempt.result.replayed ? <p>Önceki kayıtlı sonuç yeniden gösterildi.</p> : null}
          <div className={styles.actions}>
            {hasCapability(shell, MANUFACTURING_CAPABILITY.planDetail) ? (
              <Link className={styles.primaryLink} to={planDetailPath(plan.planId)}>
                Formu kapat ve planı yeniden aç
              </Link>
            ) : null}
            <Link className={styles.secondary} to={orderListPath()}>Üretim emirlerine dön</Link>
          </div>
        </section>
      </main>
    )
  }

  const unresolved = attempt.phase === 'unknown' || (attempt.phase === 'failed' && attempt.unresolvedSince !== undefined)
  const frozen = attempt.phase === 'submitting' || unresolved
  const retrySnapshot = attempt.phase === 'unknown' || attempt.phase === 'failed' ? attempt.snapshot : undefined
  const retryReason = attempt.phase === 'unknown' ? attempt.message : attempt.phase === 'failed' ? attempt.unresolvedSince : undefined

  return (
    <main className={styles.page} aria-labelledby="manufacturing-plan-create-heading">
      <header className={styles.header}>
        <div>
          <p className={styles.eyebrow}>ÜRETİM PLANI</p>
          <h1 id="manufacturing-plan-create-heading">Yeni taslak plan</h1>
        </div>
        <Link to={orderListPath()}>← Üretim emirleri</Link>
      </header>
      {unresolved && retrySnapshot !== undefined ? (
        <section className={styles.warningCard} role="alert">
          <p>{attempt.phase === 'failed' ? attempt.message : attempt.message}</p>
          <p>Önceki gönderim kesinleşmeden yeni plan oluşturulamaz.</p>
          <div className={styles.actions}>
            <button type="button" onClick={() => void send(retrySnapshot, retryReason)}>
              Aynı anahtarla yeniden gönder
            </button>
            <Link className={styles.secondary} to={orderListPath()}>Formu kapat</Link>
          </div>
        </section>
      ) : null}
      {attempt.phase === 'failed' && !unresolved ? <p className={styles.error} role="alert">{attempt.message}</p> : null}
      {formError === null ? null : <p className={styles.error} role="alert">{formError}</p>}
      {preselection.type === 'loading' ? <p role="status">Stok pozisyonundan seçilen ana rulo aranıyor…</p> : null}
      {preselection.type === 'selected' ? (
        <p role="status" className={styles.note}>
          Stok pozisyonundan seçilen ana rulo eklendi: {preselection.labelCode}. Planlanan kg’ı girin; seçim kayıtta etiket sürümüyle yeniden doğrulanır.
        </p>
      ) : null}
      {preselection.type === 'refused' ? (
        <p role="alert" className={styles.error}>
          Stok pozisyonundan seçilen etiket ({preselection.labelCode}) taslak plana eklenemez ({preselection.code}).
        </p>
      ) : null}
      {preselection.type === 'missing' ? (
        <p role="alert" className={styles.error}>
          Stok pozisyonundan seçilen etiket ana rulo adayları arasında bulunamadı{preselection.windowFull ? ' (arama penceresi doldu)' : ''}; etiketi elle arayın.
        </p>
      ) : null}
      {preselection.type === 'failed' ? <p role="alert" className={styles.error}>{preselection.message}</p> : null}
      <form className={styles.form} onSubmit={submit} noValidate>
        <fieldset disabled={frozen}>
          <legend>İş merkezi</legend>
          {workCenters.type === 'loading' ? <p role="status">İş merkezleri yükleniyor…</p> : null}
          {workCenters.type === 'failed' ? <p role="alert">{workCenters.message}</p> : null}
          {workCenters.type === 'ready' ? (
            <select aria-label="İş merkezi" value={workCenterId} onChange={(event) => setWorkCenterId(event.target.value)}>
              <option value="">Seçiniz</option>
              {workCenters.data.map((workCenter) => (
                <option key={workCenter.id} value={workCenter.id}>
                  {workCenter.code} — {workCenter.name}{workCenter.isActive ? '' : ' (pasif)'}
                </option>
              ))}
            </select>
          ) : null}
        </fieldset>

        <fieldset disabled={frozen}>
          <legend>Ana rulo adayları</legend>
          <p className={styles.note}>
            Blok dışı fiziksel kg kullanılabilirlik veya rezervasyon garantisi değildir. Seçim, kayıtta etiket sürümüyle yeniden doğrulanır.
          </p>
          <div className={styles.searchRow}>
            <label htmlFor="parent-roll-search">Etiket, ürün veya lot ara</label>
            <input id="parent-roll-search" maxLength={64} value={search} onChange={(event) => setSearch(event.target.value)} />
            <button type="button" onClick={() => void readCandidates()}>Ara</button>
          </div>
          {candidates.type === 'loading' ? <p role="status">Ana rulolar yükleniyor…</p> : null}
          {candidates.type === 'failed' ? <p role="alert">{candidates.message}</p> : null}
          {candidates.type === 'ready' ? (
            <>
              <div className={styles.tableWrap}>
                <table className={styles.table}>
                  <thead><tr><th scope="col">Etiket</th><th scope="col">Ürün / lot</th><th scope="col">Fiziksel kg</th><th scope="col">Belge</th><th scope="col">Seçim</th></tr></thead>
                  <tbody>
                    {candidates.data.items.map((candidate) => {
                      const selected = inputs.some((input) => input.candidate.labelId === candidate.labelId)
                      return (
                        <tr key={candidate.labelId}>
                          <th scope="row">{candidate.labelCode}<div className={styles.mono}>{candidate.labelId}</div></th>
                          <td><span className={styles.mono}>{candidate.productId}</span><br />Lot {candidate.lot.id}</td>
                          <td>{candidate.physicalKg} kg<br /><span className={styles.muted}>Blok dışı: {candidate.unblockedPhysicalKg} kg · Blokeli: {candidate.blockedPhysicalKg} kg</span></td>
                          <td>{candidate.documentStatus}</td>
                          <td>
                            <button type="button" disabled={!candidate.draftSelectable || selected} onClick={() => addCandidate(candidate)}>
                              {selected ? 'Eklendi' : candidate.draftSelectable ? 'Ekle' : 'Seçilemez'}
                            </button>
                            {!candidate.draftSelectable ? <div className={styles.muted}>{candidate.draftRefusalCode}</div> : null}
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
              {candidates.data.searchWindowFull ? <p className={styles.note}>Arama penceresi doldu; sonucu daraltın.</p> : null}
            </>
          ) : null}
        </fieldset>

        <fieldset disabled={frozen}>
          <legend>Planlanan girdiler</legend>
          {inputs.length === 0 ? <p>Henüz ana rulo eklenmedi.</p> : (
            <div className={styles.tableWrap}>
              <table className={styles.table}>
                <thead><tr><th scope="col">Etiket</th><th scope="col">Etiket sürümü</th><th scope="col">Planlanan kg</th><th scope="col">İşlem</th></tr></thead>
                <tbody>
                  {inputs.map((input) => (
                    <tr key={input.candidate.labelId}>
                      <th scope="row">{input.candidate.labelCode}</th>
                      <td>{input.candidate.labelVersion}</td>
                      <td>
                        <input
                          aria-label={`${input.candidate.labelCode} planlanan kg`}
                          inputMode="decimal"
                          value={input.plannedKg}
                          onChange={(event) => setInputs((current) => current.map((row) => row.candidate.labelId === input.candidate.labelId ? { ...row, plannedKg: event.target.value } : row))}
                        />
                      </td>
                      <td><button type="button" onClick={() => setInputs((current) => current.filter((row) => row.candidate.labelId !== input.candidate.labelId))}>Kaldır</button></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </fieldset>

        <fieldset disabled={frozen}>
          <legend>İsteğe bağlı hedef</legend>
          <div className={styles.formGrid}>
            <label htmlFor="plan-target-basis">Hedef türü</label>
            <select id="plan-target-basis" value={targetBasis} onChange={(event) => setTargetBasis(event.target.value as '' | PlanTargetBasis)}>
              <option value="">Hedef yok</option>
              <option value="InputMass">Girdi kütlesi</option>
              <option value="GoodOutputMass">İyi ürün kütlesi</option>
            </select>
            <label htmlFor="plan-target-kg">Hedef kg</label>
            <input id="plan-target-kg" inputMode="decimal" value={targetKg} onChange={(event) => setTargetKg(event.target.value)} />
          </div>
        </fieldset>
        {selectedWorkCenter?.kind === 'Slitting' ? (
          <fieldset disabled={frozen}>
            <legend>Dilme ayrıntısı</legend>
            <label>
              <input
                type="checkbox"
                checked={slittingEnabled}
                onChange={(event) => {
                  const checked = event.target.checked
                  setSlittingEnabled(checked)
                  if (checked && slitLines.length === 0) {
                    setSlitLines([{ id: newId(), bandWidthMm: '', count: '' }])
                  }
                }}
              />{' '}
              Sıralı bant planı ekle
            </label>
            {slittingEnabled ? (
              <>
                <p className={styles.note}>Satırlar kaydedildikleri sırayla korunur. Genişlik mm, adet tam sayıdır.</p>
                <div className={styles.tableWrap}>
                  <table className={styles.table}>
                    <thead><tr><th scope="col">Sıra</th><th scope="col">Bant genişliği (mm)</th><th scope="col">Adet</th><th scope="col">İşlem</th></tr></thead>
                    <tbody>
                      {slitLines.map((line, index) => (
                        <tr key={line.id}>
                          <th scope="row">{index + 1}</th>
                          <td><input aria-label={`Bant ${index + 1} genişliği mm`} inputMode="decimal" value={line.bandWidthMm} onChange={(event) => setSlitLines((current) => current.map((item) => item.id === line.id ? { ...item, bandWidthMm: event.target.value } : item))} /></td>
                          <td><input aria-label={`Bant ${index + 1} adedi`} inputMode="numeric" value={line.count} onChange={(event) => setSlitLines((current) => current.map((item) => item.id === line.id ? { ...item, count: event.target.value } : item))} /></td>
                          <td><button type="button" onClick={() => setSlitLines((current) => current.filter((item) => item.id !== line.id))}>Kaldır</button></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <div className={styles.actions}>
                  <button type="button" onClick={() => setSlitLines((current) => [...current, { id: newId(), bandWidthMm: '', count: '' }])}>Bant satırı ekle</button>
                </div>
                <div className={styles.formGrid}>
                  <label htmlFor="plan-winding-target-kg">İsteğe bağlı sarım hedefi (kg)</label>
                  <input id="plan-winding-target-kg" inputMode="decimal" value={windingTargetKg} onChange={(event) => setWindingTargetKg(event.target.value)} />
                </div>
              </>
            ) : null}
          </fieldset>
        ) : null}
        <PlanPreviewPanel state={preview} labelOf={labelOf} workCenterLabel={workCenterLabel} />
        <div className={styles.actions}>
          <button type="submit" disabled={frozen}>{attempt.phase === 'submitting' ? 'Kaydediliyor…' : 'Taslak planı kaydet'}</button>
        </div>
      </form>
    </main>
  )
}
