import type { KilogramPrice } from './kilogramPriceContract'
import { KILOGRAM_PRICE_TEXT as T } from './salesText'

/** A server kg price as text: the value per kg, or why it is not shown — never a figure the screen computed. */
export function kilogramPriceText(price: KilogramPrice): string {
  return price.state === 'Shown' && price.value !== null ? T.perKg(price.value) : T.state[price.state]
}
