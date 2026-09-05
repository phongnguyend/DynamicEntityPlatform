import { useQuery } from '@tanstack/react-query'
import { api } from '../api'

export function useEntityContext(tenantId: string, entityId: string) {
  const entities = useQuery({ queryKey: ['entities', tenantId], queryFn: () => api.entities(tenantId), enabled: Boolean(tenantId) })
  const fields = useQuery({ queryKey: ['fields', tenantId, entityId], queryFn: () => api.fields(tenantId, entityId), enabled: Boolean(entityId) })
  const entity = entities.data?.find(item => item.id === entityId)
  return {
    entity, fields: fields.data ?? [],
    isLoading: entities.isLoading || fields.isLoading,
    error: entities.error ?? fields.error,
  }
}
