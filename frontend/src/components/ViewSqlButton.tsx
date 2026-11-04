import { api } from '../api'
import { SqlPreviewButton } from './SqlPreviewButton'

export function toRecordQuery(definition: Record<string, unknown>) {
  const { columnSettings: _columnSettings, color: _color, ...query } = definition
  return query
}

export function ViewSqlButton({
  tenantId,
  entityId,
  name,
  definition,
  className,
}: {
  tenantId: string
  entityId: string
  name: string
  definition: Record<string, unknown>
  className?: string
}) {
  return (
    <SqlPreviewButton
      name={name}
      filter={undefined}
      className={className}
      description="Preview the record query and its bound parameters without executing it. Column visibility and color are display settings and do not change the SQL."
      load={() => api.viewSql(tenantId, entityId, toRecordQuery(definition))}
    />
  )
}
