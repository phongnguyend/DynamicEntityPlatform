import { useState, type FormEvent } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { api } from './api'
import { EntityDesigner } from './components/EntityDesigner'
import { EntityWorkspace } from './components/EntityWorkspace'

const tenantStorageKey = 'dynamic-data.tenant-id'

export default function App() {
  const [tenantId, setTenantId] = useState(() => localStorage.getItem(tenantStorageKey) ?? '')
  const [tenantName, setTenantName] = useState('')
  const [existingTenantId, setExistingTenantId] = useState('')
  const [selectedId, setSelectedId] = useState('')
  const tenants = useQuery({
    queryKey: ['tenants'], queryFn: api.tenants, enabled: !tenantId,
  })
  const createTenant = useMutation({
    mutationFn: () => api.createTenant(tenantName),
    onSuccess: tenant => { localStorage.setItem(tenantStorageKey, tenant.id); setTenantId(tenant.id) },
  })
  const entities = useQuery({
    queryKey: ['entities', tenantId], queryFn: () => api.entities(tenantId), enabled: Boolean(tenantId),
  })
  const selected = entities.data?.find(entity => entity.id === selectedId) ?? entities.data?.[0]

  function provision(event: FormEvent) { event.preventDefault(); createTenant.mutate() }
  function openTenant() {
    if (!existingTenantId) return
    localStorage.setItem(tenantStorageKey, existingTenantId)
    setTenantId(existingTenantId)
  }
  function changeTenant() { localStorage.removeItem(tenantStorageKey); setTenantId(''); setSelectedId('') }

  if (!tenantId) return <main className="onboarding"><section className="hero"><p className="eyebrow">Dynamic Entity Platform</p>
    <h1>Shape the data around your work.</h1><p>Open an existing tenant, or create an isolated workspace whose metadata drives every form and list.</p>
    <div className="tenant-picker">
      <h2>Open a workspace</h2>
      {tenants.isLoading ? <p className="empty">Loading tenants…</p> : tenants.error ? <p className="error">{tenants.error.message}</p> : tenants.data?.length ?
        <div className="tenant-select"><select aria-label="Existing tenant" value={existingTenantId} onChange={event => setExistingTenantId(event.target.value)}>
          <option value="">Select a tenant</option>
          {tenants.data.map(tenant => <option key={tenant.id} value={tenant.id} disabled={tenant.status !== 'Active'}>
            {tenant.name}{tenant.status === 'Active' ? '' : ` (${tenant.status})`}
          </option>)}
        </select><button type="button" disabled={!existingTenantId} onClick={openTenant}>Open workspace</button></div> :
        <p className="empty">No existing tenants yet.</p>}
    </div>
    <div className="create-workspace"><h2>Create a workspace</h2>
      <form onSubmit={provision}><input required placeholder="Organization name" value={tenantName} onChange={e => setTenantName(e.target.value)} />
        <button disabled={createTenant.isPending}>{createTenant.isPending ? 'Provisioning…' : 'Create workspace'}</button></form>
      {createTenant.error && <p className="error">{createTenant.error.message}</p>}</div></section></main>

  return <div className="shell"><aside><div className="brand"><span className="mark">D</span><strong>Dynamic Data</strong></div>
    <p className="nav-label">Entities</p><nav>{entities.data?.map(entity => <button key={entity.id}
      className={entity.id === selected?.id ? 'active' : ''} onClick={() => setSelectedId(entity.id)}>{entity.displayName}</button>)}</nav>
    <button className="tenant" onClick={changeTenant}>Change tenant<br /><small>{tenantId.slice(0, 8)}…</small></button></aside>
    <main>{entities.isLoading ? <p>Loading workspace…</p> : entities.error ? <p className="error">{entities.error.message}</p> :
      selected ? <EntityWorkspace tenantId={tenantId} entity={selected} /> : <div className="first-entity"><EntityDesigner tenantId={tenantId} /></div>}</main></div>
}
