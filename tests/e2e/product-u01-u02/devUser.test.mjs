/**
 * Target regression for the L-lane development user (`src/devUser.ts`): the compiled
 * `import.meta.env.VITE_DEV_USER` is exactly the bound actor; the T lane gets nothing;
 * an empty, missing or unprintable actor is refused rather than defaulted. Synthetic;
 * no browser, no server — nothing here is acceptance evidence.
 *
 *   node --test tests/e2e/product-u01-u02/devUser.test.mjs
 */
import assert from 'node:assert/strict'
import { test } from 'node:test'
import { DEV_USER_DEFINE_KEY, DevUserError, devUserDefine } from './src/devUser.ts'

test('the L lane compiles exactly the bound actor into the bundle', () => {
  const define = devUserDefine({ actor: 'group-bootstrap-admin' })
  assert.deepEqual(define, { [DEV_USER_DEFINE_KEY]: '"group-bootstrap-admin"' })
  // The value is a string literal for the bundle, not the bare name.
  assert.equal(JSON.parse(define[DEV_USER_DEFINE_KEY]), 'group-bootstrap-admin')
})

test('the T lane (no binding) compiles nothing: the controlled lane keeps the app default and never reaches an API', () => {
  assert.deepEqual(devUserDefine(null), {})
})

for (const [label, binding] of [['missing', {}], ['empty', { actor: '' }], ['blank', { actor: '   ' }], ['not a string', { actor: 42 }], ['with a newline', { actor: 'a\nb' }], ['with a quote', { actor: 'a"b' }]]) {
  test(`an actor that is ${label} is refused (ACTOR) — the bundle must not fall back to a default user`, () => {
    assert.throws(() => devUserDefine(binding), (e) => e instanceof DevUserError && e.code === 'ACTOR')
  })
}
