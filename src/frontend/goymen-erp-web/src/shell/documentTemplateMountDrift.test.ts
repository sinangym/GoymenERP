import type { ReactElement } from 'react'
import { matchRoutes } from 'react-router'
import { describe, expect, it } from 'vitest'
import { CAPABILITY, CAPABILITY_ROUTE } from './capabilities'
import { documentBrandingRoutes } from './documentBrandingRoutes'
import { NAV_CAPABILITIES, NAV_GROUPS, NAV_PURPOSE_CAPABILITIES } from './navigation'

describe('document template workspace purpose gateways', () => {
  it.each([
    [CAPABILITY.documentsSalesTemplatePrepare, CAPABILITY.documentsSalesTemplatePublish],
    [CAPABILITY.documentsInventoryTemplatePrepare, CAPABILITY.documentsInventoryTemplatePublish],
    [CAPABILITY.documentsLogisticsTemplatePrepare, CAPABILITY.documentsLogisticsTemplatePublish],
  ] as const)('keeps one navigation route for independently granted %s and %s', (prepare, publish) => {
    const path = CAPABILITY_ROUTE[prepare]
    expect(CAPABILITY_ROUTE[publish]).toBe(path)
    expect(NAV_CAPABILITIES.filter((entry) => entry === prepare)).toHaveLength(1)
    expect(NAV_CAPABILITIES).not.toContain(publish)
    expect(NAV_GROUPS.filter((group) => group.entries.includes(prepare)).map((group) => group.id)).toEqual(['settings'])
    expect(NAV_PURPOSE_CAPABILITIES[prepare]).toEqual([prepare, publish])
    const route = matchRoutes(documentBrandingRoutes, path)?.at(-1)?.route
    expect(route?.path).toBe(path.slice(1))
    const gate = route?.element as ReactElement<{ capability: readonly string[] }>
    expect(gate.props.capability).toEqual([prepare, publish])
  })

  it('does not turn branding management into template preparation or publishing', () => {
    const path = CAPABILITY_ROUTE[CAPABILITY.documentsBrandingManage]
    const route = matchRoutes(documentBrandingRoutes, path)?.at(-1)?.route
    const gate = route?.element as ReactElement<{ capability: string }>
    expect(gate.props.capability).toBe(CAPABILITY.documentsBrandingManage)
    expect(NAV_PURPOSE_CAPABILITIES[CAPABILITY.documentsBrandingManage]).toBeUndefined()
    expect(documentBrandingRoutes).toHaveLength(4)
  })
})
