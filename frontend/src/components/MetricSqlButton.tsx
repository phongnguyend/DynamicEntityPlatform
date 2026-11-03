import { api } from '../api'
import type { Metric } from '../types'
import { SqlPreviewButton } from './SqlPreviewButton'

export function MetricSqlButton({ tenantId, entityId, metric, disabled }: {
  tenantId: string; entityId: string; metric: Pick<Metric, 'name' | 'aggregate' | 'fieldId' | 'filter'>; disabled?: boolean
}) {
  return <SqlPreviewButton name={metric.name} filter={metric.filter} disabled={disabled}
    load={parameters => api.metricSql(tenantId, entityId, metric, parameters)} />
}
