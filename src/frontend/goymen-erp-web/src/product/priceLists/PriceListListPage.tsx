/**
 * U-05 list: the committed company's price lists, most recently changed first, with the version that prices today
 * and any open draft (the server's derived facts; nothing is recomputed here). A row opens the list.
 */

import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { useShellState } from '../../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../../shell/shellState'
import { StateBlock } from '../../shell/StateBlock'
import { classifyOutcome } from '../../shell/uiState'
import { useAuthBoundary } from '../../shell/useAuthBoundary'
import { fetchPriceLists } from './api'
import { PRICE_LIST_CAPABILITY, PRICE_LIST_ROUTE, priceListDetailPath } from './capabilities'
import { formatDate } from './contract'
import { PRICE_LIST_TEXT as T } from './priceListText'
import styles from './PriceListPages.module.css'

export function PriceListListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: ['price-lists', namespace ?? 'no-context', shell.routeEntryGeneration, 'list'],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPriceLists(companyId as string, null, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const outcome = query.data
  return (
    <section className={styles.page} aria-labelledby="price-list-list-heading">
      <header className={styles.header}>
        <div>
          <h1 id="price-list-list-heading" className={styles.title}>{T.listHeading}</h1>
          <p className={styles.muted}>{T.listDescription}</p>
        </div>
        {hasCapability(shell, PRICE_LIST_CAPABILITY.create)
          ? <Link className={styles.primaryLink} to={PRICE_LIST_ROUTE.create} data-testid="price-list-new">{T.newList}</Link>
          : null}
      </header>
      {outcome === undefined || outcome.type !== 'ok-single' ? (
        <StateBlock state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)} subject="fiyat listeleri"
          onRetry={() => void query.refetch()} />
      ) : outcome.data.items.length === 0 ? (
        <p className={styles.muted} data-testid="price-list-empty">{T.emptyList}</p>
      ) : (
        <div className={styles.tableWrap}>
          <table className={styles.table} data-testid="price-list-table">
            <thead>
              <tr>
                <th scope="col">{T.columns.name}</th>
                <th scope="col">{T.columns.currency}</th>
                <th scope="col">{T.columns.active}</th>
                <th scope="col">{T.columns.draft}</th>
                <th scope="col" className={styles.numeric}>{T.columns.versions}</th>
              </tr>
            </thead>
            <tbody>
              {outcome.data.items.map((item) => (
                <tr key={item.id}>
                  <td>
                    {hasCapability(shell, PRICE_LIST_CAPABILITY.detail)
                      ? <Link to={priceListDetailPath(item.id)}>{item.name}</Link>
                      : item.name}
                  </td>
                  <td>{item.currencyCode}</td>
                  <td>{item.activeVersionNo === null || item.activeValidFrom === null ? T.none
                    : T.activeFrom(item.activeVersionNo, formatDate(item.activeValidFrom))}</td>
                  <td>{item.draftVersionNo === null ? T.none : T.draftVersion(item.draftVersionNo)}</td>
                  <td className={styles.numeric}>{item.versionCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
