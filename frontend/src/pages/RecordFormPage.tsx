import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api'
import type { DynamicRecord } from '../types'
import { DynamicForm } from '../components/DynamicForm'
import { useEntityContext } from './useEntityContext'

export function RecordFormPage({ tenantId }: { tenantId: string }) {
  const { entityId = '', recordId } = useParams()
  const navigate = useNavigate()
  const location = useLocation()
  const client = useQueryClient()
  const { entity, fields, isLoading, error } = useEntityContext(tenantId, entityId)
  const passedRecord = (location.state as { record?: DynamicRecord } | null)?.record
  const records = useQuery({
    queryKey: ['records', tenantId, entityId, null], queryFn: () => api.records(tenantId, entityId),
    enabled: Boolean(recordId) && !passedRecord,
  })
  const editing = recordId ? passedRecord ?? records.data?.items.find(item => item.id === recordId) : undefined

  const save = useMutation({
    mutationFn: (data: Record<string, unknown>) => editing
      ? api.patchRecord(tenantId, entityId, editing, data)
      : api.createRecord(tenantId, entityId, data),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['records', tenantId, entityId] })
      navigate(`/entities/${entityId}/records`)
    },
  })

  if (isLoading || (recordId && !passedRecord && records.isLoading)) return <p>Loading…</p>
  if (error) return <p className="error">{error.message}</p>
  if (!entity) return <p className="error">Entity not found.</p>
  if (recordId && !editing) return <p className="error">Record not found.</p>

  return <section className="page form-page">
    <header><div><p className="eyebrow">{entity.displayName}</p><h2>{editing ? 'Edit record' : 'New record'}</h2></div></header>
    {fields.length === 0 ? <p className="empty">Add a field before creating records.</p> :
      <DynamicForm fields={fields} record={editing} busy={save.isPending}
        onSubmit={async data => { await save.mutateAsync(data) }}
        onCancel={() => navigate(`/entities/${entityId}/records`)} />}
    {save.error && <p className="error">{save.error.message}</p>}
  </section>
}
