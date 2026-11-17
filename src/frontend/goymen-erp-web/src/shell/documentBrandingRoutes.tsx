import type { RouteObject } from 'react-router'
import { CompanyDocumentBrandingPage, DocumentTemplatePage } from '../documents/DocumentBrandingPage'
import { CapabilityRoute } from './CapabilityRoute'
import { CAPABILITY, CAPABILITY_ROUTE } from './capabilities'

const templates = [
  { module: 1, prepare: CAPABILITY.documentsSalesTemplatePrepare, publish: CAPABILITY.documentsSalesTemplatePublish },
  { module: 2, prepare: CAPABILITY.documentsInventoryTemplatePrepare, publish: CAPABILITY.documentsInventoryTemplatePublish },
  { module: 3, prepare: CAPABILITY.documentsLogisticsTemplatePrepare, publish: CAPABILITY.documentsLogisticsTemplatePublish },
] as const

export const documentBrandingRoutes: RouteObject[] = [
  { path: CAPABILITY_ROUTE[CAPABILITY.documentsBrandingManage].slice(1),
    element: <CapabilityRoute capability={CAPABILITY.documentsBrandingManage}><CompanyDocumentBrandingPage /></CapabilityRoute> },
  ...templates.map(({ module, prepare, publish }) => ({
    path: CAPABILITY_ROUTE[prepare].slice(1),
    element: <CapabilityRoute capability={[prepare, publish]}><DocumentTemplatePage module={module} /></CapabilityRoute>,
  })),
]
