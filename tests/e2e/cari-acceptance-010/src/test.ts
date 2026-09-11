/** `test` with automatic per-test diagnostics; specs import it instead of '@playwright/test'. */
import { test as base, expect } from '@playwright/test'
import { Diagnostics } from './diagnostics'

export const test = base.extend<{ diagnostics: Diagnostics }>({
  diagnostics: [async ({ page }, use, testInfo) => {
    const diagnostics = Diagnostics.attach(page)
    await use(diagnostics)
    diagnostics.flush(testInfo)
  }, { auto: true }],
})
export { expect }
