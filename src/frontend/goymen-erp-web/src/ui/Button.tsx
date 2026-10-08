/**
 * GOY-DS-001 Button — a native <button> with the design-system look and nothing else.
 *
 * Every native attribute, ref and event goes to the element untouched: the caller owns
 * `onClick`, `disabled`, `form`, `name`, `value` and the accessible name. `type` is
 * required so no consumer inherits the browser's implicit `submit` by accident; the
 * value it picks is passed through as written. This component adds no retry,
 * idempotency, confirmation or busy policy.
 */

import type { ButtonHTMLAttributes, Ref } from 'react'
import styles from './primitives.module.css'

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger'
export type ButtonSize = 'sm' | 'md'

export type ButtonProps = Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'type'> & {
  readonly type: 'button' | 'submit' | 'reset'
  readonly variant?: ButtonVariant
  readonly size?: ButtonSize
  readonly ref?: Ref<HTMLButtonElement>
}

export function Button({ type, variant = 'secondary', size = 'md', className, ref, ...native }: ButtonProps) {
  const classes = [styles.button, styles[variant], size === 'sm' ? styles.sm : null, className].filter(Boolean).join(' ')
  return <button {...native} ref={ref} type={type} className={classes} />
}
