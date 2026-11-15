/**
 * Which way the browser is authenticated, read from the build environment (ID-1F).
 *
 * `VITE_AUTH_MODE=LocalAccount` names the server's local-account cookie scheme: the shell then shows the sign-in
 * screen on a 401 and a sign-out control, and the development identity header is never sent — the server ignores it
 * in that mode, and the client must not pretend it means anything. Any other value (or none) keeps today's behaviour:
 * the development bootstrap header where it was enabled, and the terminal "Oturum sonlandı" state on a 401.
 *
 * Read at call time, not at module load, so a test can switch the mode with `vi.stubEnv`. The server is still the
 * only authority: this flag decides what the client offers, never what the server accepts.
 */

export const LOCAL_ACCOUNT_AUTH_MODE = 'LocalAccount'

export function isLocalAccountMode(): boolean {
  return import.meta.env.VITE_AUTH_MODE === LOCAL_ACCOUNT_AUTH_MODE
}

/** The `X-Development-User` header is sent only outside local-account mode, and only where it was enabled before. */
export function developmentIdentityHeaderEnabled(): boolean {
  if (isLocalAccountMode()) return false
  return import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'
}
