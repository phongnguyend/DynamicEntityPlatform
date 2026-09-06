export type FieldDataType =
  | 'Text' | 'LongText' | 'Integer' | 'Decimal' | 'Boolean' | 'Date' | 'DateTime'
  | 'Email' | 'Url' | 'Choice' | 'MultiChoice' | 'Lookup'

export interface Tenant { id: string; name: string; status: string; createdAt: string }
export interface Entity {
  id: string; name: string; displayName: string; description?: string
  schemaVersion: number; status: string; createdAt: string; updatedAt: string
}
export interface Field {
  id: string; name: string; displayName: string; storageKey: string; dataType: FieldDataType
  isRequired: boolean; isUnique: boolean; isFilterable: boolean; isSortable: boolean
  isFacetable: boolean; isSearchable: boolean; defaultValue?: unknown
  configuration?: { values?: string[]; [key: string]: unknown }; sortOrder: number
  indexColumnName?: string
}
export interface EntityIndexColumn { fieldId: string; physicalColumnName: string; sortOrder: number; isDescending: boolean }
export interface EntityIndex { id: string; indexName: string; status: string; createdAt: string; columns: EntityIndexColumn[] }
export interface DynamicRecord {
  id: string; data: Record<string, unknown>; createdAt: string; createdBy?: string
  updatedAt: string; updatedBy?: string; version: string
}
export interface RecordPage { items: DynamicRecord[]; nextCursor?: string }
export interface SavedView { id: string; entityId: string; name: string; definition: Record<string, unknown> }
export interface ImportJob { id: string; entityId: string; fileName: string; status: string; columns: string[]; totalRows: number; validRows: number; invalidRows: number }
export interface ImportPreview { totalRows: number; validRows: number; invalidRows: number; errors: Array<{ row: number; fieldId?: string; message: string }> }

export type AggregateFunction = 'Count' | 'CountDistinct' | 'Sum' | 'Average' | 'Min' | 'Max'
export type DateBucket = 'None' | 'Day' | 'Week' | 'Month' | 'Quarter' | 'Year'
export type VisualizationType = 'Table' | 'Number' | 'Bar' | 'Line' | 'Donut'
export interface AnalyticsDimension { fieldId: string; dateBucket: DateBucket; alias: string }
export interface AnalyticsMeasure { aggregate: AggregateFunction; fieldId?: string; alias: string }
export interface AnalyticsSort { alias: string; direction: 'Asc' | 'Desc' }
export interface AnalyticsQuery { filter?: unknown; dimensions: AnalyticsDimension[]; measures: AnalyticsMeasure[]; sort: AnalyticsSort[]; limit: number }
export interface AnalyticsColumn { key: string; label: string; dataType: string; role: 'Dimension' | 'Measure' }
export interface AnalyticsResult { columns: AnalyticsColumn[]; rows: Array<Record<string, unknown>>; generatedAt: string; truncated: boolean }
export interface Report { id: string; entityId: string; name: string; description?: string; query: AnalyticsQuery; visualization: VisualizationType; visualizationConfiguration?: unknown; createdAt: string; updatedAt: string }
export interface MetricFormat { style?: 'integer' | 'decimal' | 'percentage' | 'currency' | 'duration'; decimalPlaces?: number; currencyCode?: string }
export interface Metric { id: string; entityId: string; name: string; description?: string; aggregate: AggregateFunction; fieldId?: string; filter?: unknown; format?: MetricFormat; createdAt: string; updatedAt: string }
export interface MetricEvaluation { value: unknown; evaluatedAt: string }
export type AlertComparisonOperator = 'GreaterThan' | 'GreaterThanOrEqual' | 'LessThan' | 'LessThanOrEqual' | 'Equal' | 'NotEqual'
export type AlertInterval = 'FiveMinutes' | 'Hourly' | 'Daily'
export type AlertState = 'Normal' | 'Firing' | 'Error' | 'Recovered'
export interface Alert { id: string; entityId: string; metricId: string; name: string; comparisonOperator: AlertComparisonOperator; threshold: number; interval: AlertInterval; timezone: string; cooldownSeconds: number; notifyOnRecovery: boolean; isEnabled: boolean; lastState?: AlertState; lastEvaluatedAt?: string; nextEvaluationAt: string; createdAt: string; updatedAt: string }
export interface AlertEvaluation { id: string; alertId: string; value?: unknown; threshold: number; state: AlertState; error?: string; evaluatedAt: string }
export interface AlertNotification { id: string; alertId: string; evaluationId: string; channel: string; status: string; attempts: number; lastError?: string; createdAt: string; deliveredAt?: string }
export interface AlertHistory { evaluations: AlertEvaluation[]; notifications: AlertNotification[] }
export type WebhookEvent = 'RecordCreated' | 'RecordUpdated' | 'RecordDeleted'
export interface WebhookSubscription { id: string; entityId: string; name: string; endpoint: string; events: WebhookEvent[]; isEnabled: boolean; createdAt: string; updatedAt: string }
