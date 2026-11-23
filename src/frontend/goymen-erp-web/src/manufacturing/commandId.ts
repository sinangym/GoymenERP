/**
 * A new v4-shaped id for a frozen command (its idempotency key or a fact/movement id). Minted once per attempt by the
 * caller; a re-send of an unknown outcome reuses the frozen snapshot and never calls this again.
 */
export function newCommandId(): string {
  const webCrypto = globalThis.crypto
  if (typeof webCrypto?.randomUUID === 'function') return webCrypto.randomUUID()
  // Fallback only where randomUUID is missing; still a v4-shaped id the server accepts.
  const hex = Array.from({ length: 32 }, () => Math.floor(Math.random() * 16).toString(16))
  hex[12] = '4'
  hex[16] = ((parseInt(hex[16], 16) & 0x3) | 0x8).toString(16)
  const s = hex.join('')
  return `${s.slice(0, 8)}-${s.slice(8, 12)}-${s.slice(12, 16)}-${s.slice(16, 20)}-${s.slice(20)}`
}
