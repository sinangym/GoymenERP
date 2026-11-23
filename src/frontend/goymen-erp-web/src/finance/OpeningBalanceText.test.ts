import { describe, expect, it } from 'vitest'
import { OPENING_BALANCE_TEXT as T } from './OpeningBalanceText'

describe('opening balance notice (FNS-D84 / FNS-D102)', () => {
  it('K3 P2-2: says a cash or bank opening enters the internal treasury balance, never that it leaves it unchanged', () => {
    expect(T.notice).not.toMatch(/bakiyesini değiştirmez/)
    expect(T.notice).toContain('kasa ve banka açılışı sistemin iç defterindeki kasa veya banka bakiyesine dahil edilir')
    expect(T.notice).toContain('cari açılışı cari ekstrede görünür')
  })
})
