/**
 * U-05 list: the committed company's price lists, most recently changed first, with the version that prices today
 * and any open draft (the server's derived facts; nothing is recomputed here). A row opens the list.
 */

import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { useShellState } from '../../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../../shell/shellState'
import { StateBlock } from '../../shell/StateBlock'
import { classifyOutcome } from '../../shell/uiState'
import { useAuthBoundary } from '../../shell/useAuthBoundary'
import { fetchPriceLists } from './api'
import { PRICE_LIST_CAPABILITY, PRICE_LIST_ROUTE, priceListDetailPath } from './capabilities'
import { formatDate, type PriceListSummary } from './contract'
import { PRICE_LIST_TEXT as T } from './priceListText'
import styles from './PriceListPages.module.css'

export function PriceListListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const boundary = useAuthBoundary()
  const listBinding = namespace === null ? null : JSON.stringify([namespace, shell.routeEntryGeneration])
  const [cursorState, setCursorState] = useState<{ readonly binding: string; readonly cursor: string } | null>(null)
  const cursor = listBinding !== null && cursorState?.binding === listBinding ? cursorState.cursor : null
  const [pageState, setPageState] = useState<{
    readonly binding: string
    readonly items: readonly PriceListSummary[]
    readonly nextCursor: string | null
  } | null>(null)
  const page = listBinding !== null && pageState?.binding === listBinding ? pageState : null
  const query = useQuery({
    queryKey: ['price-lists', namespace ?? 'no-context', shell.routeEntryGeneration, 'list', cursor],
    enabled: listBinding !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPriceLists(companyId as string, cursor, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const outcome = query.data
  const nextCursor = page?.nextCursor ?? null
  useEffect(() => {
    if (listBinding === null || outcome?.type !== 'ok-single') return
    setPageState((current) => {
      if (cursor === null || current?.binding !== listBinding) {
        return { binding: listBinding, items: outcome.data.items, nextCursor: outcome.data.nextCursor }
      }
      const known = new Set(current.items.map((item) => item.id.toLowerCase()))
      return {
        binding: listBinding,
        items: [...current.items, ...outcome.data.items.filter((item) => !known.has(item.id.toLowerCase()))],
        nextCursor: outcome.data.nextCursor,
      }
    })
  }, [cursor, listBinding, outcome])

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
      {page === null ? (
        <StateBlock state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)} subject="fiyat listeleri"
          onRetry={() => void query.refetch()} />
      ) : page.items.length === 0 ? (
        <p className={styles.muted} data-testid="price-list-empty">{T.emptyList}</p>
      ) : (
        <>
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
                {page.items.map((item) => (
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
          {cursor !== null && outcome !== undefined && outcome.type !== 'ok-single' ? (
            <StateBlock state={classifyOutcome(outcome)} subject="fiyat listeleri" onRetry={() => void query.refetch()} />
          ) : null}
          {nextCursor === null ? null : (
            <button type="button" className={styles.secondaryButton} data-testid="price-list-more" disabled={query.isFetching}
              onClick={() => setCursorState({ binding: page.binding, cursor: nextCursor })}>
              {T.more}
            </button>
          )}
        </>
      )}
    </section>
  )
}
