import { runtimeParameterSource } from './RuntimeParametersForm'
import { api } from '../api'
import type { Metric } from '../types'
import { SqlPreviewButton } from './SqlPreviewButton'

export function MetricSqlButton({
  tenantId,
  entityId,
  metric,
  disabled,
}: {
  tenantId: string
  entityId: string | null
  metric: Pick<Metric, 'name' | 'aggregate' | 'fieldId' | 'filter' | 'sql'>
  disabled?: boolean
}) {
  return (
    <SqlPreviewButton
      name={metric.name}
      filter={runtimeParameterSource(metric)}
      disabled={disabled}
      load={(parameters) => api.metricSql(tenantId, entityId, metric, parameters)}
    />
  )
}
