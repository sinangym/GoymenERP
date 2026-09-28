/**
 * AT-002 / ERP-000I — "/arama?q=…": the global search results, grouped per owner module.
 *
 *   GET /api/v1/search?q=<term>   (the server fans out to each module's own search source)
 *
 * - The term lives in the URL: Back from an owner screen returns here with the same term, and the results are read
 *   again for this entry (never shown from another context: the read is keyed on the committed context and the route
 *   entry, and the page remounts when either changes).
 * - Only the groups the server answered are shown — a kind the actor may not read has no group, no count and no "more"
 *   (AT-002). No total count is shown anywhere; "more" goes to the owner's list only where it continues the search.
 * - A result opens its owner's detail screen only where the actor's snapshot holds that screen; otherwise it is text.
 * - Price-free: the contract has no price member and the decoder refuses one.
 */

import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { classifyOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { fetchGlobalSearch } from './api'
import { SEARCH_QUERY_PARAM, normalizeTerm, type SearchGroup, type SearchItem } from './searchContract'
import { SEARCH_KIND_SCREENS } from './searchCapabilities'
import { SEARCH_KIND_LABEL, SEARCH_TEXT as T, stateLabel } from './searchText'
import styles from './GlobalSearch.module.css'

export function GlobalSearchPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const [params] = useSearchParams()
  const raw = params.get(SEARCH_QUERY_PARAM) ?? ''
  // Remounted per context AND per term: nothing of one company's or one term's answer survives into another.
  return <SearchResults key={`${namespace ?? 'no-context'}:${raw}`} namespace={namespace} raw={raw} />
}

function SearchResults({ namespace, raw }: { readonly namespace: string | null; readonly raw: string }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const [, setParams] = useSearchParams()
  const term = normalizeTerm(raw)
  const [draft, setDraft] = useState(raw)
  const [invalid, setInvalid] = useState(false)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null

  const read = useQuery({
    // ENTRY-SCOPED (#146/#148, the C-01 rule): a route entry — Back and a reopen included — gets its own cache key, so a
    // revisit is never answered from the previous entry's cache while its authorization is being revalidated.
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'global-search', term],
    enabled: namespace !== null && companyId !== null && term !== null,
    // And belt-and-braces for the same rule: an answer is never served from cache on mount (every entry mounts this
    // component afresh behind the entry gate), and nothing is kept once the page is left.
    staleTime: 0,
    gcTime: 0,
    refetchOnMount: 'always',
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchGlobalSearch(term as string, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  // Only an answer read during THIS mount is shown: until the entry's own read lands the page says it is loading, even if
  // an older answer for the same term is still somewhere in the cache.
  const answer = read.isFetchedAfterMount ? read.data : undefined

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const next = normalizeTerm(draft)
    if (next === null) {
      setInvalid(true)
      return
    }
    setInvalid(false)
    // A new search is a new history entry: Back returns to the previous term's results.
    setParams({ [SEARCH_QUERY_PARAM]: next })
  }

  return (
    <section className={styles.page} aria-labelledby="global-search-heading">
      <h1 id="global-search-heading" className={styles.title}>
        {T.heading}
      </h1>
      <form role="search" aria-label={T.pageLabel} className={styles.form} onSubmit={submit} noValidate>
        <label htmlFor="global-search-page-input">
          {T.pageLabel}
          <input
            id="global-search-page-input"
            type="search"
            value={draft}
            autoComplete="off"
            aria-invalid={invalid || undefined}
            aria-describedby="global-search-page-hint"
            onChange={(event) => setDraft(event.target.value)}
          />
        </label>
        <button type="submit" className={styles.primaryButton}>
          {T.submit}
        </button>
        <p id="global-search-page-hint" className={invalid ? styles.fieldError : styles.muted} role={invalid ? 'alert' : undefined}>
          {invalid ? T.termInvalid : T.termHint}
        </p>
      </form>
      <p className={styles.muted}>{T.scope}</p>
      {term === null ? (
        <p className={styles.muted} data-testid="global-search-prompt">
          {raw.trim() === '' ? T.emptyPrompt : T.termInvalid}
        </p>
      ) : answer === undefined ? (
        <StateBlock state={{ kind: 'initial-loading' }} subject="arama" />
      ) : answer.type !== 'ok-single' ? (
        <StateBlock state={classifyOutcome(answer)} subject="arama" onRetry={() => void read.refetch()} />
      ) : answer.data.groups.length === 0 ? (
        <div data-testid="global-search-no-results">
          <p>{T.noResults}</p>
          <p className={styles.muted}>{T.noResultsHint}</p>
        </div>
      ) : (
        answer.data.groups.map((group) => <Group key={group.kind} group={group} term={term} />)
      )}
    </section>
  )
}

function Group({ group, term }: { readonly group: SearchGroup; readonly term: string }) {
  const shell = useShellState()
  const screens = SEARCH_KIND_SCREENS[group.kind]
  const headingId = `global-search-group-${group.kind.replace(/\./g, '-')}`
  const canOpen = hasCapability(shell, screens.detail)
  const morePath = screens.morePath !== null && hasCapability(shell, screens.list) ? screens.morePath(term) : null
  return (
    <section className={styles.group} aria-labelledby={headingId} data-testid={`global-search-group-${group.kind}`}>
      <h2 id={headingId} className={styles.groupTitle}>
        {SEARCH_KIND_LABEL[group.kind]}
      </h2>
      {group.items.length === 0 ? (
        <p className={styles.muted}>{T.groupEmpty}</p>
      ) : (
        <ul className={styles.list}>
          {group.items.map((item) => (
            <li key={item.id} className={styles.item}>
              <Result item={item} to={canOpen ? screens.detailPath(item.id) : null} />
            </li>
          ))}
        </ul>
      )}
      {group.hasMore ? (
        <p className={styles.more}>
          {T.more}{' '}
          {morePath !== null ? <Link to={morePath}>{T.moreInList}</Link> : T.narrow}
        </p>
      ) : null}
    </section>
  )
}

function Result({ item, to }: { readonly item: SearchItem; readonly to: string | null }) {
  const title = (
    <>
      <span className={styles.code}>{item.code}</span>
      {item.name !== null ? <span className={styles.name}> {item.name}</span> : null}
    </>
  )
  return (
    <>
      {to !== null ? <Link to={to}>{title}</Link> : <span title={T.noDetail}>{title}</span>}
      {item.secondaryName !== null ? <span className={styles.muted}>{item.secondaryName}</span> : null}
      <span className={styles.badge}>{stateLabel(item.state)}</span>
      {item.date !== null ? <span className={styles.muted}>{formatDate(item.date)}</span> : null}
    </>
  )
}

const formatDate = (value: string) => {
  const [year, month, day] = value.split('-')
  return `${day}.${month}.${year}`
}
