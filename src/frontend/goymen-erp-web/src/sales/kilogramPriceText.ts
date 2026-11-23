import type { KilogramPrice } from './kilogramPriceContract'
import { KILOGRAM_PRICE_TEXT as T } from './salesText'

/**
 * A server kg price as text: the value per kg in the document currency (`currency`: its code, or the neutral text when the code
 * cannot be read), or why it is not shown — never a figure the screen computed.
 */
export function kilogramPriceText(price: KilogramPrice, currency: string): string {
  return price.state === 'Shown' && price.value !== null ? T.perKg(price.value, currency) : T.state[price.state]
}
