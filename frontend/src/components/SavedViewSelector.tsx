import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api'
import { Save } from 'lucide-react'

export function SavedViewSelector({ tenantId, entityId, definition, onSelect }: {
  tenantId: string; entityId: string; definition: Record<string, unknown> | null
  onSelect: (definition: Record<string, unknown>) => void
}) {
  const client = useQueryClient(); const [name, setName] = useState('')
  const views = useQuery({ queryKey: ['views', tenantId, entityId], queryFn: () => api.views(tenantId, entityId) })
  const save = useMutation({ mutationFn: () => api.createView(tenantId, entityId, name, definition ?? { pageSize: 100 }),
    onSuccess: () => { setName(''); void client.invalidateQueries({ queryKey: ['views', tenantId, entityId] }) } })
  return <div className="saved-views"><select defaultValue="" onChange={event => {
    const view = views.data?.find(item => item.id === event.target.value); if (view) onSelect(view.definition)
  }}><option value="">Saved views…</option>{views.data?.map(view => <option key={view.id} value={view.id}>{view.name}</option>)}</select>
    <input value={name} placeholder="View name" onChange={event => setName(event.target.value)} />
    <button disabled={!name || save.isPending} onClick={() => save.mutate()}><Save />Save view</button></div>
}
