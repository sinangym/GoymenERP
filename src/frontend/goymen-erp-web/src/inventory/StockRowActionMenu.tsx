import { useRef, type KeyboardEvent } from 'react'
import { Link } from 'react-router'
import { stockRowActionHref, stockRowActionState, type StockRowAction, type StockRowContext } from './stockRowActions'
import styles from './inventory.module.css'

/**
 * AT-063 — one ST-01 row's action disclosure. A list of links, not an ARIA menu: every entry navigates to an existing
 * view with the row's label chosen (an ST-02 panel, or the Manufacturing plan-create screen), carrying the ST-01
 * filter so ST-02's back link returns to the same list. Only the actions the caller already filtered by the server's
 * capabilities and by this row are drawn; with none there is no button.
 */
export function StockRowActionMenu({ row, actions, search, open, onToggle }: {
  readonly row: StockRowContext
  readonly actions: readonly StockRowAction[]
  readonly search: string
  readonly open: boolean
  readonly onToggle: (open: boolean) => void
}) {
  const button = useRef<HTMLButtonElement>(null)
  if (actions.length === 0) return null
  const { labelId, labelCode } = row
  const listId = `stock-row-actions-${labelId}`
  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key !== 'Escape' || !open) return
    event.stopPropagation()
    onToggle(false)
    button.current?.focus()
  }
  return <div className={styles.rowActions} onKeyDown={onKeyDown}>
    <button
      ref={button}
      type="button"
      aria-expanded={open}
      aria-controls={listId}
      aria-label={`${labelCode} işlemleri`}
      onClick={() => onToggle(!open)}
    >İşlemler</button>
    {open
      ? <ul id={listId} className={styles.rowActionList} aria-label={`${labelCode} için işlemler`}>
        {actions.map((action) => <li key={action.id}>
          <Link to={stockRowActionHref(action, row)} state={stockRowActionState(search)}>{action.label}</Link>
        </li>)}
      </ul>
      : null}
  </div>
}
