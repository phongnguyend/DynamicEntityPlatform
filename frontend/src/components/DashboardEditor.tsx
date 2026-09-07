import { useState, type FormEvent } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { api } from '../api'
import { Modal } from './Modal'
import type { Dashboard } from '../types'

export function DashboardEditor({ tenantId, dashboard, onClose, onSaved }: { tenantId: string; dashboard: Dashboard | null; onClose: () => void; onSaved: (dashboard: Dashboard) => void }) {
  const dashboards = useQuery({ queryKey: ['dashboards', tenantId], queryFn: () => api.dashboards(tenantId) })
  const [name, setName] = useState(dashboard?.name ?? '')
  const trimmedName = name.trim()
  const duplicate = (dashboards.data ?? []).some(value => value.id !== dashboard?.id && value.name.toLocaleLowerCase() === trimmedName.toLocaleLowerCase())
  const save = useMutation({
    mutationFn: () => dashboard
      ? api.updateDashboard(tenantId, dashboard.id, trimmedName, dashboard.definition)
      : api.createDashboard(tenantId, trimmedName, { items: [], layouts: {} }),
    onSuccess: onSaved,
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    if (trimmedName && !duplicate) save.mutate()
  }

  return <Modal title={dashboard ? 'Edit dashboard' : 'Create dashboard'} onClose={onClose}><form className="dynamic-form" onSubmit={submit}>
    <label className="field">Name<input autoFocus required maxLength={80} value={name} onChange={event => setName(event.target.value)} placeholder="e.g. Executive overview" /></label>
    {duplicate && <p className="error">A dashboard with this name already exists.</p>}
    {save.error && <p className="error">{save.error.message}</p>}
    <div className="modal-footer"><button disabled={!trimmedName || duplicate || save.isPending}>{save.isPending ? 'Saving…' : dashboard ? 'Save changes' : 'Create dashboard'}</button><button type="button" className="secondary" onClick={onClose}>Cancel</button></div>
  </form></Modal>
}
