/**
 * AT-002 — the shell toolbar's global search box. It renders ONLY when the committed capability snapshot carries at
 * least one searchable owner's list screen (ERP-000K §16.1: no mock result, suggestion, recent or count — this box shows
 * none, it only navigates). Submitting a valid term opens `/arama?q=…`: the query lives in the URL, so Back from an owner
 * screen returns to the same results. The box reads nothing itself.
 *
 * The toolbar input is a plain text field inside a `search` landmark, and its button is named "Genel aramada ara" (the
 * visible "Ara" is part of that name): a page's OWN search box and "Ara" button stay the only `searchbox` / "Ara" on it.
 *
 * Mounted by the shell owner in `src/shell/AppShell.tsx`'s toolbar (see searchRoutes.tsx for the exact handoff).
 */

import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { normalizeTerm } from './searchContract'
import { SEARCHABLE_LIST_CAPABILITIES, searchPath } from './searchCapabilities'
import { SEARCH_TEXT as T } from './searchText'
import styles from './GlobalSearch.module.css'

export function GlobalSearchBox() {
  const shell = useShellState()
  const navigate = useNavigate()
  const [value, setValue] = useState('')
  const [invalid, setInvalid] = useState(false)

  if (!SEARCHABLE_LIST_CAPABILITIES.some((capability) => hasCapability(shell, capability))) return null

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const term = normalizeTerm(value)
    if (term === null) {
      setInvalid(true)
      return
    }
    setInvalid(false)
    navigate(searchPath(term))
  }

  return (
    <form role="search" aria-label={T.boxLabel} className={styles.box} onSubmit={submit} noValidate>
      <label htmlFor="global-search-input" className={styles.visuallyHidden}>
        {T.boxLabel}
      </label>
      <input
        id="global-search-input"
        className={styles.boxInput}
        type="text"
        inputMode="search"
        enterKeyHint="search"
        value={value}
        placeholder={T.boxPlaceholder}
        autoComplete="off"
        aria-invalid={invalid || undefined}
        aria-describedby={invalid ? 'global-search-error' : undefined}
        onChange={(event) => {
          setValue(event.target.value)
          if (invalid) setInvalid(false)
        }}
      />
      <button type="submit" className={styles.boxButton} aria-label={T.boxSubmit}>
        {T.submit}
      </button>
      {invalid ? (
        <span id="global-search-error" role="alert" className={styles.boxError}>
          {T.termInvalid}
        </span>
      ) : null}
    </form>
  )
}
