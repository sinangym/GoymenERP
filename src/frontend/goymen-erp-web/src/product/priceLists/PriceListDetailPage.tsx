/**
 * U-05 one price list: its context (currency, Sales channel, VAT excluded), every version with the server's derived
 * status (Taslak / Planlı / Geçerli / Geçmiş) and its lines. Commands, each one idempotent attempt behind its own
 * control: revise the draft version (edit), publish it (publish — its own grant), open a new version from the latest
 * published prices (edit). After a recorded command, and after a refusal that says the record moved (409), the list is
 * re-read: what the screen shows is always the server's stored list.
 */

import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { useShellState } from '../../shell/shellContext'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../../shell/shellState'
import { StateBlock } from '../../shell/StateBlock'
import { classifyOutcome } from '../../shell/uiState'
import { useAuthBoundary } from '../../shell/useAuthBoundary'
import { fetchPriceList } from './api'
import { PRICE_LIST_CAPABILITY, PRICE_LIST_ROUTE } from './capabilities'
import { formatDate, formatMoneyText, isWireDate, todayText, type PriceListVersion, type PriceListView } from './contract'
import { PriceListLinesEditor } from './PriceListLinesEditor'
import { buildLines, rowsFromLines, type PriceListLineRow } from './priceListLines'
import { isPriceListAttemptFrozen, livePriceListSnapshot, newIdentity } from './priceListAttempt'
import { PRICE_LIST_TEXT as T, VERSION_STATUS_TEXT } from './priceListText'
import { attemptNotice, fieldErrorTexts, usePriceListCommand } from './usePriceListCommand'
import styles from './PriceListPages.module.css'

export function PriceListDetailPage() {
  const shell = useShellState()
  const { priceListId } = useParams()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const queryKey = ['price-lists', namespace ?? 'no-context', shell.routeEntryGeneration, 'detail', priceListId ?? '']
  const query = useQuery({
    queryKey,
    enabled: namespace !== null && companyId !== null && (priceListId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPriceList(priceListId as string, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const backLink = hasCapability(shell, PRICE_LIST_CAPABILITY.list)
    ? <Link className={styles.backLink} to={PRICE_LIST_ROUTE.list}>← {T.backToList}</Link>
    : null
  const outcome = query.data
  if (outcome === undefined || outcome.type !== 'ok-single') {
    return (
      <section className={styles.page} aria-label={T.detailHeading}>
        {backLink}
        <StateBlock state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)} subject="fiyat listesi"
          onRetry={() => void query.refetch()} />
      </section>
    )
  }

  const view = outcome.data
  const reread = () => void queryClient.invalidateQueries({ queryKey: ['price-lists'] })
  const draft = view.versions.find((version) => version.state === 'Draft') ?? null
  return (
    <article className={styles.page} aria-labelledby="price-list-detail-heading">
      {backLink}
      <header className={styles.header}>
        <div>
          <h1 id="price-list-detail-heading" className={styles.title} data-testid="price-list-name">{view.name}</h1>
          <p className={styles.muted}>{T.contextNote}</p>
        </div>
      </header>
      <dl className={styles.facts}>
        <div><dt>{T.columns.currency}</dt><dd data-testid="price-list-currency">{view.currencyCode}</dd></div>
        <div><dt>{T.channel}</dt><dd>{view.channel === 'Sales' ? T.channelSales : view.channel}</dd></div>
        <div><dt>{T.vatBasis}</dt><dd>{view.vatBasis === 'ExcludingVat' ? T.vatExcluded : view.vatBasis}</dd></div>
        <div><dt>{T.columns.active}</dt><dd data-testid="price-list-active">{view.activeVersionNo === null ? T.none : T.versionTitle(view.activeVersionNo)}</dd></div>
        <div><dt>{T.version}</dt><dd data-testid="price-list-version">{view.version}</dd></div>
      </dl>

      {companyId === null ? null : (
        <>
          {draft !== null && hasCapability(shell, PRICE_LIST_CAPABILITY.edit)
            ? <DraftEditor key={`draft:${view.id}:${draft.versionNo}`} shell={shell} view={view} draft={draft} companyId={companyId} onChanged={reread} />
            : null}
          {draft !== null && hasCapability(shell, PRICE_LIST_CAPABILITY.publish)
            ? <PublishCommand key={`publish:${view.id}:${draft.versionNo}`} shell={shell} view={view} draft={draft} onChanged={reread} />
            : null}
          {draft === null && view.versions.some((version) => version.state === 'Published') && hasCapability(shell, PRICE_LIST_CAPABILITY.edit)
            ? <StartVersionCommand key={`start:${view.id}:${view.versions.length}`} shell={shell} view={view} onChanged={reread} />
            : null}
        </>
      )}

      <section className={styles.section} aria-labelledby="price-list-versions-heading">
        <h2 id="price-list-versions-heading">{T.versionsHeading}</h2>
        {[...view.versions].reverse().map((version) => <VersionView key={version.versionId} version={version} />)}
      </section>
    </article>
  )
}

function VersionView({ version }: { readonly version: PriceListVersion }) {
  return (
    <div className={styles.version} data-testid={`price-list-version-${version.versionNo}`}>
      <div className={styles.versionHeader}>
        <h3 className={styles.lineTitle}>{T.versionTitle(version.versionNo)}</h3>
        <span className={styles.badge} data-testid={`price-list-version-${version.versionNo}-status`}>{VERSION_STATUS_TEXT[version.status]}</span>
        <span className={styles.muted}>{T.validity(formatDate(version.validFrom), version.validThrough === null ? null : formatDate(version.validThrough))}</span>
      </div>
      {version.publishedBy !== null && version.publishedAtUtc !== null
        ? <p className={styles.muted}>{T.publishedBy(version.publishedBy, new Date(version.publishedAtUtc).toLocaleString('tr-TR'))}</p>
        : null}
      {version.lines.length === 0 ? <p className={styles.muted}>{T.noLines}</p> : (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">{T.product}</th>
                <th scope="col">{T.priceUnit}</th>
                <th scope="col">{T.quantityTier}</th>
                <th scope="col" className={styles.numeric}>{T.unitPrice}</th>
                <th scope="col" className={styles.numeric}>{T.floorUnitPrice}</th>
              </tr>
            </thead>
            <tbody>
              {version.lines.map((line) => (
                <tr key={line.lineId}>
                  <td>{line.productCode === null ? line.productId : `${line.productCode} · ${line.productName ?? ''}`}</td>
                  <td className={styles.identifier}>{line.priceUnitId}</td>
                  <td data-testid="price-list-line-quantity-tier">{T.quantityRange(line.lowerQuantity.replace('.', ','), line.upperQuantity?.replace('.', ',') ?? null)}</td>
                  <td className={styles.numeric} data-testid="price-list-line-price">{formatMoneyText(line.unitPrice)}</td>
                  <td className={styles.numeric}>{line.floorUnitPrice === null ? T.none : formatMoneyText(line.floorUnitPrice)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

function CommandNotice({ attempt, success, retry, abandon }: {
  readonly attempt: ReturnType<typeof usePriceListCommand>['attempt']
  readonly success: string
  readonly retry: () => void
  readonly abandon: () => void
}) {
  const notice = attemptNotice(attempt, success)
  return (
    <>
      {notice === null ? null : (
        <p role={notice.tone === 'error' ? 'alert' : 'status'} className={styles.notice} data-tone={notice.tone} data-testid="price-list-notice">
          {notice.text}
        </p>
      )}
      {fieldErrorTexts(attempt).map((text) => <p key={text} className={styles.fieldError}>{text}</p>)}
      {attempt.phase !== 'sending' && livePriceListSnapshot(attempt) !== null ? (
        <div className={styles.actions}>
          <button type="button" className={styles.secondaryButton} onClick={retry}>{T.notice.retry}</button>
          <button type="button" className={styles.linkButton} onClick={abandon}>{T.notice.abandon}</button>
        </div>
      ) : null}
    </>
  )
}

function DraftEditor({ shell, view, draft, companyId, onChanged }: {
  readonly shell: ShellState
  readonly view: PriceListView
  readonly draft: PriceListVersion
  readonly companyId: string
  readonly onChanged: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(view.name)
  const [validFrom, setValidFrom] = useState(draft.validFrom)
  const [validThrough, setValidThrough] = useState(draft.validThrough ?? '')
  const [rows, setRows] = useState<PriceListLineRow[]>(() => rowsFromLines(draft.lines))
  const [localError, setLocalError] = useState<string | null>(null)
  const command = usePriceListCommand(shell, `product.price-list.revise:${view.id}`, () => {
    setEditing(false)
    onChanged()
  }, onChanged)
  const frozen = isPriceListAttemptFrozen(command.attempt)

  function start() {
    setName(view.name)
    setValidFrom(draft.validFrom)
    setValidThrough(draft.validThrough ?? '')
    setRows(rowsFromLines(draft.lines))
    setLocalError(null)
    setEditing(true)
  }

  function save() {
    setLocalError(null)
    if (name.trim() === '') return setLocalError(T.localErrors.name)
    if (!isWireDate(validFrom)) return setLocalError(T.localErrors.validFrom)
    const lines = buildLines(rows)
    if (!lines.ok) return setLocalError(lines.message)
    const result = command.submit({
      kind: 'revise-draft',
      priceListId: view.id,
      payload: { expectedVersion: view.version, name: name.trim(), validFrom, validThrough: validThrough === '' ? null : validThrough, lines: lines.lines },
    })
    if (result === 'unresolved-other-payload') setLocalError(T.notice.blockedOther)
  }

  return (
    <section className={styles.section} aria-label={T.editDraft} data-testid="price-list-draft-editor">
      {!editing ? (
        <div className={styles.actions}>
          <button type="button" className={styles.secondaryButton} onClick={start} data-testid="price-list-edit">{T.editDraft}</button>
        </div>
      ) : (
        <>
          <fieldset className={styles.fieldset} disabled={frozen}>
            <legend>{T.versionTitle(draft.versionNo)}</legend>
            <div className={styles.grid}>
              <label htmlFor="price-list-edit-name">{T.name}</label>
              <input id="price-list-edit-name" value={name} maxLength={200} onChange={(event) => setName(event.target.value)} />
              <label htmlFor="price-list-edit-valid-from">{T.validFrom}</label>
              <input id="price-list-edit-valid-from" type="date" value={validFrom} onChange={(event) => setValidFrom(event.target.value)} />
              <label htmlFor="price-list-edit-valid-through">{T.validThrough}</label>
              <input id="price-list-edit-valid-through" type="date" value={validThrough} onChange={(event) => setValidThrough(event.target.value)} />
            </div>
          </fieldset>
          <PriceListLinesEditor rows={rows} onChange={setRows} companyId={companyId} disabled={frozen} />
          {localError === null ? null : <p role="alert" className={styles.notice} data-tone="error">{localError}</p>}
          <div className={styles.actions}>
            <button type="button" className={styles.primaryButton} onClick={save} disabled={frozen} data-testid="price-list-save-draft">{T.saveDraft}</button>
            <button type="button" className={styles.linkButton} onClick={() => setEditing(false)} disabled={frozen}>{T.cancelEdit}</button>
          </div>
        </>
      )}
      <CommandNotice attempt={command.attempt} success={T.notice.saved} retry={command.retry} abandon={command.abandon} />
    </section>
  )
}

function PublishCommand({ shell, view, draft, onChanged }: {
  readonly shell: ShellState
  readonly view: PriceListView
  readonly draft: PriceListVersion
  readonly onChanged: () => void
}) {
  const command = usePriceListCommand(shell, `product.price-list.publish:${view.id}`, onChanged, onChanged)
  const frozen = isPriceListAttemptFrozen(command.attempt)
  return (
    <section className={styles.section} aria-label={T.publish}>
      <p className={styles.muted}>{T.publishNote}</p>
      <div className={styles.actions}>
        <button type="button" className={styles.primaryButton} disabled={frozen} data-testid="price-list-publish"
          onClick={() => command.submit({ kind: 'publish', priceListId: view.id, payload: { expectedVersion: view.version, versionNo: draft.versionNo } })}>
          {T.publish} ({T.versionTitle(draft.versionNo)})
        </button>
      </div>
      <CommandNotice attempt={command.attempt} success={T.notice.published} retry={command.retry} abandon={command.abandon} />
    </section>
  )
}

function StartVersionCommand({ shell, view, onChanged }: {
  readonly shell: ShellState
  readonly view: PriceListView
  readonly onChanged: () => void
}) {
  const [versionId] = useState(() => newIdentity())
  const [validFrom, setValidFrom] = useState(() => todayText())
  const [validThrough, setValidThrough] = useState('')
  const [localError, setLocalError] = useState<string | null>(null)
  const command = usePriceListCommand(shell, `product.price-list.start:${view.id}`, onChanged, onChanged)
  const frozen = isPriceListAttemptFrozen(command.attempt)

  function start() {
    setLocalError(null)
    if (!isWireDate(validFrom)) return setLocalError(T.localErrors.validFrom)
    const result = command.submit({
      kind: 'start-version',
      priceListId: view.id,
      payload: { expectedVersion: view.version, versionId, validFrom, validThrough: validThrough === '' ? null : validThrough },
    })
    if (result === 'unresolved-other-payload') setLocalError(T.notice.blockedOther)
  }

  return (
    <section className={styles.section} aria-label={T.startVersion}>
      <p className={styles.muted}>{T.startVersionNote}</p>
      <fieldset className={styles.fieldset} disabled={frozen}>
        <div className={styles.grid}>
          <label htmlFor="price-list-next-valid-from">{T.validFrom}</label>
          <input id="price-list-next-valid-from" type="date" value={validFrom} onChange={(event) => setValidFrom(event.target.value)} />
          <label htmlFor="price-list-next-valid-through">{T.validThrough}</label>
          <input id="price-list-next-valid-through" type="date" value={validThrough} onChange={(event) => setValidThrough(event.target.value)} />
        </div>
      </fieldset>
      {localError === null ? null : <p role="alert" className={styles.notice} data-tone="error">{localError}</p>}
      <div className={styles.actions}>
        <button type="button" className={styles.secondaryButton} onClick={start} disabled={frozen} data-testid="price-list-start-version">{T.startVersion}</button>
      </div>
      <CommandNotice attempt={command.attempt} success={T.notice.started} retry={command.retry} abandon={command.abandon} />
    </section>
  )
}
