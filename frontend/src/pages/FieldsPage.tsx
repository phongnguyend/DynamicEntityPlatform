import { useParams } from 'react-router-dom'
import { EntityDesigner } from '../components/EntityDesigner'
import { FieldManager } from '../components/FieldManager'
import { EntityTabs } from '../components/EntityTabs'
import { useEntityContext } from './useEntityContext'

export function FieldsPage({ tenantId }: { tenantId: string }) {
  const { entityId = '' } = useParams()
  const { entity, fields, isLoading, error } = useEntityContext(tenantId, entityId)

  if (isLoading) return <p>Loading…</p>
  if (error) return <p className="error">{error.message}</p>
  if (!entity) return <p className="error">Entity not found.</p>

  return <section className="page">
    <header><div><p className="eyebrow">Entity</p><h2>{entity.displayName}</h2><p>{entity.description}</p></div>
      <span className="pill">Schema v{entity.schemaVersion}</span></header>
    <EntityTabs entityId={entityId} />
    <div className="workspace-grid">
      <div className="panel"><EntityDesigner tenantId={tenantId} entity={entity} /></div>
      <div className="panel wide"><FieldManager tenantId={tenantId} entityId={entityId} fields={fields} /></div>
    </div>
  </section>
}
