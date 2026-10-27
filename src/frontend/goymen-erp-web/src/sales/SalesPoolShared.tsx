/**
 * Pieces the S-01 screens share: the pool tabs, the Cari cell, the attempt notice with its two actions, the form error
 * summary and the lines editor of the request / quote / revision forms. Presentation only; every rule is in ./poolForm.ts
 * and every wire check in ./poolApi.ts.
 */

import { Link, useLocation } from 'react-router'
import type { OrderDraftOptions } from './orderDraftForm'
import { poolLineFieldId, poolLineRowId, priceUnitChoices, withProduct, type PoolFormErrors, type PoolLineValues } from './poolForm'
import { POOL_TEXT } from './poolText'
import { cssId } from './poolView'
import type { PoolOptions } from './poolOptions'
import { SALES_POOL_ROUTE, useSalesPoolCapability } from './poolCapabilities'
import type { PoolAttempt, PoolNotice } from './poolAttempt'
import { CAPABILITY, CAPABILITY_ROUTE } from '../shell/capabilities'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import type { RemoteViewState } from '../shell/uiState'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import styles from './SalesPool.module.css'

const C = POOL_TEXT.common
const F = POOL_TEXT.form

/** Talepler / Teklifler — each tab only when the shell publishes its list. */
export function PoolTabs() {
  const { pathname } = useLocation()
  const canRequests = useSalesPoolCapability('requestsList')
  const canQuotes = useSalesPoolCapability('quotesList')
  const tabs = [
    canRequests ? { to: SALES_POOL_ROUTE.requests, label: POOL_TEXT.tabs.requests } : null,
    canQuotes ? { to: SALES_POOL_ROUTE.quotes, label: POOL_TEXT.tabs.quotes } : null,
  ].filter((tab) => tab !== null)
  if (tabs.length < 2) return null
  return (
    <nav aria-label={POOL_TEXT.tabs.label}>
      <ul className={styles.tabs}>
        {tabs.map((tab) => {
          const current = pathname === tab.to
          return (
            <li key={tab.to}>
              <Link to={tab.to} className={current ? styles.tabCurrent : styles.tab} aria-current={current ? 'page' : undefined}>
                {tab.label}
              </Link>
            </li>
          )
        })}
      </ul>
    </nav>
  )
}

/**
 * The record's party: the request and quote reads carry only its id (no record-bound display), so the id is shown as it
 * is — linked to the Cari card only with that card's own capability. Nothing is looked up elsewhere or guessed.
 */
export function PoolPartyCell({ partyId }: { readonly partyId: string }) {
  const shell = useShellState()
  const canOpen = hasCapability(shell, CAPABILITY.cariPartiesDetail)
  return canOpen ? (
    <Link
      className={`${styles.link} ${styles.identifier}`}
      to={CAPABILITY_ROUTE[CAPABILITY.cariPartiesDetail].replace(':partyId', encodeURIComponent(partyId))}
      aria-label={`${C.partyCard}: ${partyId}`}
    >
      {partyId}
    </Link>
  ) : (
    <span className={styles.identifier}>{partyId}</span>
  )
}

/** The attempt's notice and, while its outcome is unknown, the only two actions: the same snapshot again, or leave it. */
export function PoolAttemptNotice<P, R>({
  attempt,
  notice,
  testId,
}: {
  readonly attempt: PoolAttempt<P, R>
  readonly notice: PoolNotice | null
  readonly testId: string
}) {
  if (notice === null) return null
  return (
    <div className={styles.actions}>
      <p role={notice.role} className={styles.notice} data-testid={testId}>
        {notice.message}
      </p>
      {notice.offerRetry && attempt.state.phase !== 'submitting' ? (
        <>
          <Button type="button" variant="secondary" onClick={attempt.retry}>
            {C.retry}
          </Button>
          <Button type="button" variant="ghost" onClick={attempt.leave}>
            {C.abandon}
          </Button>
        </>
      ) : null}
    </div>
  )
}

/** Every error of the form in DOM order, each linked to its field (or shown as text when no field renders it). */
export function PoolErrorSummary({ errors, labelFor }: { readonly errors: PoolFormErrors; readonly labelFor?: (key: string) => string | null }) {
  const entries = Object.entries(errors)
  if (entries.length === 0) return null
  return (
    <div role="alert" className={styles.notice} data-testid="pool-error-summary" tabIndex={-1}>
      <p className={styles.subtitle}>{F.summary}</p>
      <ul className={styles.errorList}>
        {entries.map(([key, message]) => {
          const label = labelFor?.(key) ?? null
          return (
            <li key={key}>
              <a href={`#${cssId(key)}`}>{label === null ? message : `${label}: ${message}`}</a>
            </li>
          )
        })}
      </ul>
    </div>
  )
}

type ProductOption = OrderDraftOptions['products'][number]

/**
 * The lines of a request (no prices) or a quote / revision (`withPrices`). A line copied from a stored record keeps its
 * product and unit; a new line chooses them from the owner's offer (`products`, null when the offer cannot be read —
 * then no new line can be added and the reason is shown).
 */
export function PoolLinesEditor({
  lines,
  errors,
  withPrices,
  products,
  productsUnavailable,
  disabled,
  onChange,
  onAdd,
  onRemove,
}: {
  readonly lines: readonly PoolLineValues[]
  readonly errors: PoolFormErrors
  readonly withPrices: boolean
  readonly products: readonly ProductOption[] | null
  readonly productsUnavailable: string | null
  readonly disabled: boolean
  readonly onChange: (lineId: string, next: PoolLineValues) => void
  readonly onAdd: (() => void) | null
  readonly onRemove: (lineId: string) => void
}) {
  return (
    <section className={styles.section} aria-labelledby="pool-lines-heading">
      <h2 id="pool-lines-heading" className={styles.subtitle}>
        {C.lines}
      </h2>
      {errors.lines === undefined ? null : (
        <p className={styles.rowError} id={cssId('lines')}>
          {errors.lines}
        </p>
      )}
      {lines.map((line, index) => (
        <LineEditor
          key={line.lineId}
          index={index + 1}
          line={line}
          errors={errors}
          withPrices={withPrices}
          products={products}
          disabled={disabled}
          canRemove={lines.length > 1}
          onChange={(next) => onChange(line.lineId, next)}
          onRemove={() => onRemove(line.lineId)}
        />
      ))}
      {productsUnavailable === null ? null : <p className={styles.muted}>{F.productsUnavailable(productsUnavailable)}</p>}
      {onAdd === null || products === null ? null : (
        <div>
          <Button type="button" variant="secondary" disabled={disabled} onClick={onAdd}>
            {F.addLine}
          </Button>
        </div>
      )}
    </section>
  )
}

function LineEditor({
  index,
  line,
  errors,
  withPrices,
  products,
  disabled,
  canRemove,
  onChange,
  onRemove,
}: {
  readonly index: number
  readonly line: PoolLineValues
  readonly errors: PoolFormErrors
  readonly withPrices: boolean
  readonly products: readonly ProductOption[] | null
  readonly disabled: boolean
  readonly canRemove: boolean
  readonly onChange: (next: PoolLineValues) => void
  readonly onRemove: () => void
}) {
  const id = (field: Parameters<typeof poolLineFieldId>[1]) => cssId(poolLineFieldId(line.lineId, field))
  const error = (field: Parameters<typeof poolLineFieldId>[1]) => errors[poolLineFieldId(line.lineId, field)]
  const headingId = cssId(`line-${line.lineId}-heading`)
  const rowError = errors[poolLineRowId(line.lineId)]
  return (
    <section className={styles.line} aria-labelledby={headingId} data-testid={`pool-line-${index}`}>
      <h3 id={headingId} className={styles.lineTitle}>
        {F.line(index)}
      </h3>
      {rowError === undefined ? null : (
        <p className={styles.rowError} id={cssId(poolLineRowId(line.lineId))}>
          {rowError}
        </p>
      )}
      <div className={styles.grid}>
        {line.fixedProduct ? (
          <div className={styles.wide}>
            <p className={styles.muted}>{F.product}</p>
            <p data-testid={`pool-line-${index}-product`}>{line.productLabel}</p>
            <p className={styles.muted}>{F.fixedProduct}</p>
          </div>
        ) : (
          <Field id={id('productId')} label={F.product} error={error('productId')} className={styles.wide}>
            {(control) => (
              <select
                {...control}
                className={styles.control}
                value={line.productId}
                disabled={disabled || products === null}
                onChange={(event) => onChange(withProduct(line, products?.find((p) => p.productId === event.target.value)))}
              >
                <option value="">{F.productPlaceholder}</option>
                {(products ?? []).map((product) => (
                  <option key={product.productId} value={product.productId}>
                    {product.label}
                  </option>
                ))}
              </select>
            )}
          </Field>
        )}
        <Field id={id('orderUnitId')} label={F.unit} error={error('orderUnitId')}>
          {(control) => (
            <select
              {...control}
              className={styles.control}
              value={line.orderUnitId}
              disabled={disabled || line.fixedProduct}
              onChange={(event) => onChange({ ...line, orderUnitId: event.target.value })}
            >
              <option value="">{F.unitPlaceholder}</option>
              {line.units.map((unit) => (
                <option key={unit.unitId} value={unit.unitId}>
                  {unit.code}
                </option>
              ))}
            </select>
          )}
        </Field>
        <Field id={id('quantity')} label={F.quantity} error={error('quantity')}>
          {(control) => (
            <input
              {...control}
              className={styles.control}
              type="text"
              inputMode="decimal"
              autoComplete="off"
              value={line.quantityText}
              disabled={disabled}
              onChange={(event) => onChange({ ...line, quantityText: event.target.value })}
            />
          )}
        </Field>
        {withPrices ? (
          <>
            <Field id={id('unitPrice')} label={F.unitPrice} error={error('unitPrice')}>
              {(control) => (
                <input
                  {...control}
                  className={styles.control}
                  type="text"
                  inputMode="decimal"
                  autoComplete="off"
                  value={line.unitPriceText}
                  disabled={disabled}
                  onChange={(event) => onChange({ ...line, unitPriceText: event.target.value })}
                />
              )}
            </Field>
            <Field id={id('priceUnitId')} label={F.priceUnit} error={error('priceUnitId')}>
              {(control) => (
                <select
                  {...control}
                  className={styles.control}
                  value={line.priceUnitId}
                  disabled={disabled}
                  onChange={(event) => onChange({ ...line, priceUnitId: event.target.value })}
                >
                  <option value="">{F.priceUnitPlaceholder}</option>
                  {priceUnitChoices(line).map((unit) => (
                    <option key={unit.unitId} value={unit.unitId}>
                      {unit.code}
                    </option>
                  ))}
                </select>
              )}
            </Field>
            <Field id={id('discount')} label={F.discount} error={error('discount')}>
              {(control) => (
                <input
                  {...control}
                  className={styles.control}
                  type="text"
                  inputMode="decimal"
                  autoComplete="off"
                  value={line.discountText}
                  disabled={disabled}
                  onChange={(event) => onChange({ ...line, discountText: event.target.value })}
                />
              )}
            </Field>
          </>
        ) : null}
        <Field id={id('requestedDeliveryDate')} label={F.lineDeliveryDate} error={error('requestedDeliveryDate')}>
          {(control) => (
            <input
              {...control}
              className={styles.control}
              type="date"
              value={line.requestedDeliveryDate}
              disabled={disabled}
              onChange={(event) => onChange({ ...line, requestedDeliveryDate: event.target.value })}
            />
          )}
        </Field>
        <Field id={id('note')} label={F.lineNote} error={error('note')} className={styles.wide}>
          {(control) => (
            <input
              {...control}
              className={styles.control}
              type="text"
              value={line.note}
              disabled={disabled}
              onChange={(event) => onChange({ ...line, note: event.target.value })}
            />
          )}
        </Field>
      </div>
      {canRemove ? (
        <div>
          <Button type="button" variant="ghost" size="sm" disabled={disabled} onClick={onRemove}>
            {F.removeLine(index)}
          </Button>
        </div>
      ) : null}
    </section>
  )
}

/** The list's continuation: the server's cursor, an explicit retry of the SAME cursor on failure, the end when null. */
export function PoolContinuation({
  list,
}: {
  readonly list: {
    readonly nextCursor: string | null
    readonly continuation: 'idle' | 'loading' | RemoteViewState<unknown>
    readonly loadMore: () => Promise<void>
  }
}) {
  const { continuation } = list
  return (
    <div className={styles.actions}>
      {typeof continuation === 'object' ? (
        <div>
          <p className={styles.muted}>{C.nextPageFailed}</p>
          <StateBlock state={continuation} subject="sonraki sayfa" onRetry={() => void list.loadMore()} />
        </div>
      ) : list.nextCursor === null ? (
        <p className={styles.muted}>{C.end}</p>
      ) : (
        <Button
          type="button"
          variant="secondary"
          onClick={() => void list.loadMore()}
          disabled={continuation === 'loading'}
          aria-busy={continuation === 'loading'}
        >
          {continuation === 'loading' ? C.loadingMore : C.loadMore}
        </Button>
      )}
    </div>
  )
}

/** The customer select: the Cari owner's authorized accounts (server search + server cursor), never a default. */
export function PoolCustomerField({
  options,
  partyId,
  companyAccountId,
  error,
  disabled,
  search,
  onSearch,
  onChoose,
}: {
  readonly options: PoolOptions
  readonly partyId: string
  readonly companyAccountId: string
  readonly error: string | undefined
  readonly disabled: boolean
  readonly search: string
  readonly onSearch: (search: string) => void
  readonly onChoose: (partyId: string, companyAccountId: string) => void
}) {
  const value = partyId === '' ? '' : `${partyId}|${companyAccountId}`
  const offer = options.accountsOffer
  return (
    <>
      <Field id={cssId('customerSearch')} label={F.customerSearch}>
        {(control) => (
          <input
            {...control}
            className={styles.control}
            type="search"
            value={search}
            disabled={disabled}
            onChange={(event) => onSearch(event.target.value)}
          />
        )}
      </Field>
      <Field
        id={cssId('customer')}
        label={F.customer}
        error={error}
        help={options.accountsUnavailable === null ? undefined : F.referencesUnavailable(options.accountsUnavailable)}
      >
        {(control) => (
          <select
            {...control}
            className={styles.control}
            value={value}
            disabled={disabled || options.loading}
            onChange={(event) => {
              const [party = '', account = ''] = event.target.value.split('|')
              onChoose(party, account)
            }}
          >
            <option value="">{options.loading ? F.referencesLoading : F.customerPlaceholder}</option>
            {options.accounts.map((account) => (
              <option key={`${account.partyId}|${account.companyAccountId}`} value={`${account.partyId}|${account.companyAccountId}`}>
                {account.label}
              </option>
            ))}
          </select>
        )}
      </Field>
      {offer !== null && offer.nextCursor !== null ? (
        <div>
          <Button type="button" variant="ghost" size="sm" disabled={disabled || offer.loadingMore} onClick={offer.loadMore}>
            {F.customersMore}
          </Button>
        </div>
      ) : null}
    </>
  )
}
