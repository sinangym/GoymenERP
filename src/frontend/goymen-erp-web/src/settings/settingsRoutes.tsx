import { SalesContractDiscountLimitPage } from './SalesContractDiscountLimitPage'
import { CollateralTypesPage } from './CollateralTypesPage'
import { FinanceApprovalPoliciesPage, CashCountDifferencePoliciesPage } from './FinancePoliciesPage'
import { BankInstitutionsPage, CollateralRatesPage } from './CollateralPoliciesPage'
/**
 * The Settings route table, spread into `shell/router.tsx` (before the catch-all). Each entry is wrapped in
 * `CapabilityRoute`: an ungranted capability mounts nothing and renders the same neutral not-found block any unknown
 * path gets, and calls nothing.
 */

import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { SETTINGS_CAPABILITY, SETTINGS_ROUTE } from './capabilities'
import { ExchangeRatesPage } from './ExchangeRatesPage'
import { LoginRateLimitPage } from './LoginRateLimitPage'
import { ReferenceListPage } from './ReferenceListPage'
import { SalesDiscountLimitsPage } from './SalesDiscountLimitsPage'
import { SalesPolicyApprovalThresholdsPage } from './SalesPolicyApprovalThresholdsPage'
import { SalesPolicyDeliveryTolerancePage } from './SalesPolicyDeliveryTolerancePage'
import { TaxMasterPage } from './TaxMasterPage'
import { UnitConversionPolicyPage } from './UnitConversionPolicyPage'
import { UnitProfilePage } from './UnitProfilePage'
import { MoneyProfilePage } from './MoneyProfilePage'
import { WorkCenterCapacityPage } from './WorkCenterCapacityPage'
import { ManufacturingProfilePage } from './ManufacturingProfilePage'
import { FinanceOpeningPeriodPage } from './FinanceOpeningPeriodPage'
import { OperationalPolicyPage } from './OperationalPolicyPage'
import { DocumentNumberSeriesPage } from './DocumentNumberSeriesPage'
import { PurchaseInvoiceVariancePolicyPage } from './PurchaseInvoiceVariancePolicyPage'
import { WarehouseTransferScalePage } from './WarehouseTransferScalePage'
import { SubcontractTaxDefaultPage } from './SubcontractTaxDefaultPage'

export const settingsRoutes: RouteObject[] = [
  { path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.financePolicies].slice(1), element: <CapabilityRoute capability={SETTINGS_CAPABILITY.financePolicies}><FinanceApprovalPoliciesPage /></CapabilityRoute> },
  { path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.cashDifferencePolicies].slice(1), element: <CapabilityRoute capability={SETTINGS_CAPABILITY.cashDifferencePolicies}><CashCountDifferencePoliciesPage /></CapabilityRoute> },
  { path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.bankInstitutions].slice(1), element: <CapabilityRoute capability={SETTINGS_CAPABILITY.bankInstitutions}><BankInstitutionsPage /></CapabilityRoute> },
  { path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.collateralRatePolicies].slice(1), element: <CapabilityRoute capability={SETTINGS_CAPABILITY.collateralRatePolicies}><CollateralRatesPage /></CapabilityRoute> },
  { path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.salesSubcontractTax].slice(1), element: <CapabilityRoute capability={SETTINGS_CAPABILITY.salesSubcontractTax}><SubcontractTaxDefaultPage side="sales" /></CapabilityRoute> },
  { path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.purchasingSubcontractTax].slice(1), element: <CapabilityRoute capability={SETTINGS_CAPABILITY.purchasingSubcontractTax}><SubcontractTaxDefaultPage side="purchasing" /></CapabilityRoute> },
  { path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.warehouseTransferScale].slice(1), element: <CapabilityRoute capability={SETTINGS_CAPABILITY.warehouseTransferScale}><WarehouseTransferScalePage /></CapabilityRoute> },
  { path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.purchaseInvoiceVariance].slice(1), element: <CapabilityRoute capability={SETTINGS_CAPABILITY.purchaseInvoiceVariance}><PurchaseInvoiceVariancePolicyPage /></CapabilityRoute> },
  { path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.documentNumberSeries].slice(1), element: <CapabilityRoute capability={SETTINGS_CAPABILITY.documentNumberSeries}><DocumentNumberSeriesPage /></CapabilityRoute> },
  { path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.manufacturingScrapCost].slice(1), element: <CapabilityRoute capability={SETTINGS_CAPABILITY.manufacturingScrapCost}><OperationalPolicyPage kind="scrap-cost" /></CapabilityRoute> },
  { path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.notificationRetention].slice(1), element: <CapabilityRoute capability={SETTINGS_CAPABILITY.notificationRetention}><OperationalPolicyPage kind="notification-retention" /></CapabilityRoute> },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.salesContractDiscountLimit].slice(1),
    element: <CapabilityRoute capability={SETTINGS_CAPABILITY.salesContractDiscountLimit}><SalesContractDiscountLimitPage /></CapabilityRoute>,
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.collateralTypes].slice(1),
    element: <CapabilityRoute capability={SETTINGS_CAPABILITY.collateralTypes}><CollateralTypesPage /></CapabilityRoute>,
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.financeOpeningPeriod].slice(1),
    element: <CapabilityRoute capability={SETTINGS_CAPABILITY.financeOpeningPeriod}><FinanceOpeningPeriodPage /></CapabilityRoute>,
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.manufacturingParameters].slice(1),
    element: <CapabilityRoute capability={SETTINGS_CAPABILITY.manufacturingParameters}><ManufacturingProfilePage kind="parameters" /></CapabilityRoute>,
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.manufacturingTolerances].slice(1),
    element: <CapabilityRoute capability={SETTINGS_CAPABILITY.manufacturingTolerances}><ManufacturingProfilePage kind="tolerances" /></CapabilityRoute>,
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.moneyProfiles].slice(1),
    element: <CapabilityRoute capability={SETTINGS_CAPABILITY.moneyProfiles}><MoneyProfilePage /></CapabilityRoute>,
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.salesDiscountLimits].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.salesDiscountLimits}>
        <SalesDiscountLimitsPage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.taxMaster].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.taxMaster}>
        <TaxMasterPage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.exchangeRates].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.exchangeRates}>
        <ExchangeRatesPage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.salesDeliveryTolerance].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.salesDeliveryTolerance}>
        <SalesPolicyDeliveryTolerancePage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.salesApprovalThresholds].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.salesApprovalThresholds}>
        <SalesPolicyApprovalThresholdsPage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.loginRateLimit].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.loginRateLimit}>
        <LoginRateLimitPage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.unitConversionPolicy].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.unitConversionPolicy}>
        <UnitConversionPolicyPage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.unitProfiles].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.unitProfiles}>
        <UnitProfilePage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.workCenterCapacity].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.workCenterCapacity}>
        <WorkCenterCapacityPage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.purchasingReferenceLists].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.purchasingReferenceLists}>
        <ReferenceListPage group="purchasing" />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.productionReferenceLists].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.productionReferenceLists}>
        <ReferenceListPage group="production" />
      </CapabilityRoute>
    ),
  },
]
