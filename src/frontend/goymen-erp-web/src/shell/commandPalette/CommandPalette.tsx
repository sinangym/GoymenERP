/**
 * W1-B (DALGA_PLANI_v2 1.3, YOL-D4 (d)) — the shell's command palette (Ctrl+K, ⌘K on a Mac).
 *
 * It offers only what the committed server capability snapshot grants (`paletteCommands.ts`): the sidebar's screens,
 * the granted "Yeni …" screens, a hand-off to the existing global search, and this actor's last ten screens in this
 * company. It reads nothing from the network itself and runs no command: choosing an option only opens a screen, whose
 * own route gate and backend authorize again.
 *
 * Accessibility: a modal dialog holding one `combobox` input that owns a `listbox`; the options are never focusable,
 * the active one is announced through `aria-activedescendant`. Arrow keys move (wrapping), Enter opens, Escape closes,
 * Tab stays in the input (the dialog has nothing else to reach but the close button, which a pointer can use). The
 * entrance animation runs only without `prefers-reduced-motion`.
 */

import { useEffect, useId, useMemo, useRef, useState, useSyncExternalStore, type KeyboardEvent } from 'react'
import { useNavigate } from 'react-router'
import { useShellState } from '../shellContext'
import { capabilityFailClosed } from '../shellState'
import { filterCommands, grantedCommands, searchCommand, type PaletteCommand } from './paletteCommands'
import { PALETTE_CREATE_LABEL, PALETTE_TEXT as T } from './paletteText'
import { RECENT_LIMIT, readRecent, type StorageAccessor } from './recentScreens'
import { paletteRecentKey } from './useRecentScreens'
import styles from './CommandPalette.module.css'

type SectionId = 'recent' | 'screen' | 'create' | 'search'

interface Section {
  readonly id: SectionId
  readonly label: string
  readonly commands: readonly PaletteCommand[]
}

const SECTION_LABEL: Readonly<Record<SectionId, string>> = {
  recent: T.groupRecent,
  screen: T.groupScreens,
  create: T.groupCreate,
  search: T.groupSearch,
}

const KIND_TEXT: Readonly<Record<PaletteCommand['kind'], string>> = {
  screen: T.kindScreen,
  create: T.kindCreate,
  search: T.kindSearch,
}

function subscribeOnline(onChange: () => void): () => void {
  window.addEventListener('online', onChange)
  window.addEventListener('offline', onChange)
  return () => {
    window.removeEventListener('online', onChange)
    window.removeEventListener('offline', onChange)
  }
}

const readOnline = () => (typeof navigator === 'undefined' ? true : navigator.onLine !== false)

export function CommandPalette({
  storage,
  onClose,
  onNavigate,
}: {
  readonly storage: StorageAccessor
  /** Closes the palette without navigating (Escape, close button, backdrop). */
  readonly onClose: () => void
  /** Called after an option opened its screen. */
  readonly onNavigate: () => void
}) {
  const state = useShellState()
  const navigate = useNavigate()
  const baseId = useId()
  const inputRef = useRef<HTMLInputElement>(null)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const online = useSyncExternalStore(subscribeOnline, readOnline, () => true)

  const pending = capabilityFailClosed(state)
  const commands = useMemo(() => grantedCommands(state, PALETTE_CREATE_LABEL), [state])
  // Read once per opening: the tracker writes while the palette is closed, and a stored id is shown only while it is
  // still one of the granted commands — a revoked or tampered entry is simply not there.
  const [recentIds] = useState(() => readRecent(storage, paletteRecentKey(state)))

  const sections = useMemo<Section[]>(() => {
    if (query.trim() === '') {
      const byId = new Map(commands.map((command) => [command.id, command]))
      const recent = recentIds.flatMap((id) => byId.get(id) ?? []).slice(0, RECENT_LIMIT)
      return [
        { id: 'recent', label: SECTION_LABEL.recent, commands: recent },
        { id: 'screen', label: SECTION_LABEL.screen, commands: commands.filter((command) => command.kind === 'screen') },
        { id: 'create', label: SECTION_LABEL.create, commands: commands.filter((command) => command.kind === 'create') },
      ]
    }
    const matched = filterCommands(commands, query)
    const search = searchCommand(state, query, T.searchFor)
    return [
      { id: 'screen', label: SECTION_LABEL.screen, commands: matched.filter((command) => command.kind === 'screen') },
      { id: 'create', label: SECTION_LABEL.create, commands: matched.filter((command) => command.kind === 'create') },
      { id: 'search', label: SECTION_LABEL.search, commands: search === null ? [] : [search] },
    ]
  }, [commands, recentIds, query, state])

  const visible = sections.filter((section) => section.commands.length > 0)
  const options = visible.flatMap((section) =>
    section.commands.map((command, index) => ({ command, domId: `${baseId}-${section.id}-${index}` })),
  )
  const activeIndex = options.length === 0 ? -1 : Math.min(active, options.length - 1)
  const activeOption = activeIndex >= 0 ? options[activeIndex] : undefined

  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  useEffect(() => {
    if (activeOption === undefined) return
    const element = document.getElementById(activeOption.domId)
    if (typeof element?.scrollIntoView === 'function') element.scrollIntoView({ block: 'nearest' })
  }, [activeOption])

  const open = (command: PaletteCommand) => {
    navigate(command.path)
    onNavigate()
  }

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault()
        if (options.length > 0) setActive((activeIndex + 1) % options.length)
        return
      case 'ArrowUp':
        event.preventDefault()
        if (options.length > 0) setActive((activeIndex - 1 + options.length) % options.length)
        return
      case 'Enter':
        event.preventDefault()
        if (activeOption !== undefined) open(activeOption.command)
        return
      case 'Escape':
        event.preventDefault()
        event.stopPropagation()
        onClose()
        return
      case 'Tab':
        // The dialog is modal and its only keyboard stop is this input.
        event.preventDefault()
        return
      default:
    }
  }

  const listId = `${baseId}-list`
  const statusId = `${baseId}-status`
  const message = pending
    ? T.capabilitiesPending
    : options.length > 0
      ? null
      : query.trim() === ''
        ? T.noCommands
        : T.noMatch

  let position = 0
  return (
    <>
      <div className={styles.backdrop} aria-hidden="true" onClick={onClose} data-testid="command-palette-backdrop" />
      <div className={styles.dialog} role="dialog" aria-modal="true" aria-label={T.dialog}>
        <div className={styles.header}>
          <label htmlFor={`${baseId}-input`} className={styles.visuallyHidden}>
            {T.inputLabel}
          </label>
          <input
            id={`${baseId}-input`}
            ref={inputRef}
            className={styles.input}
            type="text"
            role="combobox"
            aria-expanded={options.length > 0}
            aria-controls={listId}
            aria-autocomplete="list"
            aria-activedescendant={activeOption?.domId}
            aria-describedby={statusId}
            autoComplete="off"
            spellCheck={false}
            placeholder={T.inputPlaceholder}
            value={query}
            onChange={(event) => {
              setQuery(event.target.value)
              setActive(0)
            }}
            onKeyDown={onKeyDown}
          />
          <button type="button" tabIndex={-1} onClick={onClose}>
            {T.close}
          </button>
        </div>
        <p id={statusId} role="status" className={styles.visuallyHidden}>
          {pending ? T.capabilitiesPending : T.resultCount(options.length)}
        </p>
        {!online ? <p className={styles.message}>{T.offline}</p> : null}
        <ul id={listId} role="listbox" aria-label={T.listLabel} className={styles.list}>
          {visible.map((section) => (
            <li key={section.id} role="presentation">
              <div id={`${baseId}-${section.id}-label`} className={styles.groupLabel} aria-hidden="true">
                {section.label}
              </div>
              <ul role="group" aria-labelledby={`${baseId}-${section.id}-label`} className={styles.groupList}>
                {section.commands.map((command) => {
                  const index = position++
                  const option = options[index]
                  const selected = index === activeIndex
                  return (
                    <li
                      key={option.domId}
                      id={option.domId}
                      role="option"
                      aria-selected={selected}
                      data-command-id={command.id}
                      className={selected ? styles.optionActive : styles.option}
                      onMouseMove={() => {
                        if (!selected) setActive(index)
                      }}
                      onMouseDown={(event) => event.preventDefault()}
                      onClick={() => open(command)}
                    >
                      <span>{command.label}</span>
                      <span className={styles.optionContext}>
                        {command.kind === 'search' ? T.searchHint : command.context}
                        <span className={styles.visuallyHidden}>{`, ${KIND_TEXT[command.kind]}`}</span>
                      </span>
                    </li>
                  )
                })}
              </ul>
            </li>
          ))}
        </ul>
        {message !== null ? <p className={styles.message}>{message}</p> : null}
        <p className={styles.footer}>{T.footer}</p>
      </div>
    </>
  )
}
