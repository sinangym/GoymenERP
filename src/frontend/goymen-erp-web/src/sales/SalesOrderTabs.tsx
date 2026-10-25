/**
 * S-04 Sipariş 360 — the six LOCKED order-detail tabs (Master §7.2): Satırlar, Tahsis/Kaynak, Gerçekleşme/İrsaliye,
 * Faturalar, Sözleşme/Belgeler, Revizyon/Geçmiş.
 *
 * WAI-ARIA tabs pattern: one tablist, roving tabindex, ←/→/Home/End move and activate, each panel labelled by its tab.
 * Inactive panels stay MOUNTED and hidden, so an in-flight command (an unknown outcome that keeps its idempotency key)
 * survives a tab switch; nothing is re-sent and nothing is re-read just because a tab changed.
 */

import { useId, useRef, type KeyboardEvent, type ReactNode } from 'react'
import { ORDER_TABS, type OrderTab } from './orderTabs'
import { ORDER_TABS_TEXT } from './salesText'
import styles from './SalesDraftDetailPage.module.css'

export function SalesOrderTabs({
  active,
  onChange,
  panels,
}: {
  readonly active: OrderTab
  readonly onChange: (tab: OrderTab) => void
  readonly panels: Readonly<Record<OrderTab, ReactNode>>
}) {
  const baseId = useId()
  const tabRefs = useRef<Partial<Record<OrderTab, HTMLButtonElement | null>>>({})
  const tabId = (tab: OrderTab) => `${baseId}-tab-${tab}`
  const panelId = (tab: OrderTab) => `${baseId}-panel-${tab}`

  function move(event: KeyboardEvent<HTMLButtonElement>, current: OrderTab) {
    const index = ORDER_TABS.indexOf(current)
    let next: OrderTab | null = null
    if (event.key === 'ArrowRight') next = ORDER_TABS[(index + 1) % ORDER_TABS.length]
    else if (event.key === 'ArrowLeft') next = ORDER_TABS[(index - 1 + ORDER_TABS.length) % ORDER_TABS.length]
    else if (event.key === 'Home') next = ORDER_TABS[0]
    else if (event.key === 'End') next = ORDER_TABS[ORDER_TABS.length - 1]
    if (next === null) return
    event.preventDefault()
    onChange(next)
    tabRefs.current[next]?.focus()
  }

  return (
    <div className={styles.tabs} data-testid="sales-order-tabs">
      <div role="tablist" aria-label={ORDER_TABS_TEXT.label} className={styles.tabList}>
        {ORDER_TABS.map((tab) => {
          const selected = tab === active
          return (
            <button
              key={tab}
              ref={(element) => {
                tabRefs.current[tab] = element
              }}
              type="button"
              role="tab"
              id={tabId(tab)}
              aria-selected={selected}
              aria-controls={panelId(tab)}
              tabIndex={selected ? 0 : -1}
              className={styles.tab}
              data-testid={`sales-order-tab-${tab}`}
              onClick={() => onChange(tab)}
              onKeyDown={(event) => move(event, tab)}
            >
              {ORDER_TABS_TEXT.tab[tab]}
            </button>
          )
        })}
      </div>
      {ORDER_TABS.map((tab) => (
        <div
          key={tab}
          role="tabpanel"
          id={panelId(tab)}
          aria-labelledby={tabId(tab)}
          hidden={tab !== active}
          tabIndex={0}
          className={styles.tabPanel}
          data-testid={`sales-order-tabpanel-${tab}`}
        >
          {panels[tab]}
        </div>
      ))}
    </div>
  )
}
