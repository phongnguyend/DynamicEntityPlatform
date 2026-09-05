import { useState, type FormEvent } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { BrowserRouter, Navigate, NavLink, Route, Routes, useNavigate, useParams } from 'react-router-dom'
import { api } from './api'
import { EntityDesigner } from './components/EntityDesigner'
import { Modal } from './components/Modal'
import { RecordsListPage } from './pages/RecordsListPage'
import { RecordFormPage } from './pages/RecordFormPage'
import { FieldsPage } from './pages/FieldsPage'

const tenantStorageKey = 'dynamic-data.tenant-id'

export default function App() {
  const [tenantId, setTenantId] = useState(() => localStorage.getItem(tenantStorageKey) ?? '')
  const [tenantName, setTenantName] = useState('')
  const [existingTenantId, setExistingTenantId] = useState('')
  const tenants = useQuery({
    queryKey: ['tenants'], queryFn: api.tenants, enabled: !tenantId,
  })
  const createTenant = useMutation({
    mutationFn: () => api.createTenant(tenantName),
    onSuccess: tenant => { localStorage.setItem(tenantStorageKey, tenant.id); setTenantId(tenant.id) },
  })
  const isDuplicateName = tenants.data?.some(tenant => tenant.name.trim().toLowerCase() === tenantName.trim().toLowerCase()) ?? false

  function provision(event: FormEvent) { event.preventDefault(); if (isDuplicateName) return; createTenant.mutate() }
  function openTenant() {
    if (!existingTenantId) return
    localStorage.setItem(tenantStorageKey, existingTenantId)
    setTenantId(existingTenantId)
  }
  function changeTenant() { localStorage.removeItem(tenantStorageKey); setTenantId('') }

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
        <button disabled={createTenant.isPending || isDuplicateName}>{createTenant.isPending ? 'Provisioning…' : 'Create workspace'}</button></form>
      {isDuplicateName && <p className="error">A tenant named "{tenantName}" already exists.</p>}
      {createTenant.error && <p className="error">{createTenant.error.message}</p>}</div></section></main>

  return <BrowserRouter><Shell tenantId={tenantId} onChangeTenant={changeTenant} /></BrowserRouter>
}

function Shell({ tenantId, onChangeTenant }: { tenantId: string; onChangeTenant: () => void }) {
  const navigate = useNavigate()
  const entities = useQuery({ queryKey: ['entities', tenantId], queryFn: () => api.entities(tenantId), enabled: Boolean(tenantId) })
  const tenants = useQuery({ queryKey: ['tenants'], queryFn: api.tenants })
  const tenantName = tenants.data?.find(tenant => tenant.id === tenantId)?.name ?? tenantId.slice(0, 8) + '…'
  const [creatingEntity, setCreatingEntity] = useState(false)

  return <div className="shell"><aside><div className="brand"><span className="mark">D</span><strong>Dynamic Data</strong></div>
    <p className="nav-label">Entities</p><nav>{entities.data?.map(entity => <NavLink key={entity.id} to={`/entities/${entity.id}/records`}
      className={({ isActive }) => isActive ? 'active' : ''}>{entity.displayName}</NavLink>)}
      <button type="button" onClick={() => setCreatingEntity(true)}>+ New entity</button></nav>
    <button className="tenant" onClick={onChangeTenant}>Change tenant<br /><small>{tenantName}</small></button></aside>
    <main>{entities.isLoading ? <p>Loading workspace…</p> : entities.error ? <p className="error">{entities.error.message}</p> :
      <Routes>
        <Route path="/" element={entities.data?.[0] ? <Navigate to={`/entities/${entities.data[0].id}/records`} replace /> :
          <div className="first-entity"><p className="empty">No entities yet.</p>
            <button type="button" onClick={() => setCreatingEntity(true)}>Create an entity</button></div>} />
        <Route path="/entities/:entityId" element={<RedirectToRecords />} />
        <Route path="/entities/:entityId/records" element={<RecordsListPage tenantId={tenantId} />} />
        <Route path="/entities/:entityId/records/new" element={<RecordFormPage tenantId={tenantId} />} />
        <Route path="/entities/:entityId/records/:recordId/edit" element={<RecordFormPage tenantId={tenantId} />} />
        <Route path="/entities/:entityId/fields" element={<FieldsPage tenantId={tenantId} />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>}
      {creatingEntity && <Modal title="Create an entity" onClose={() => setCreatingEntity(false)}>
        <EntityDesigner tenantId={tenantId} onEntityCreated={entity => { setCreatingEntity(false); navigate(`/entities/${entity.id}/fields`) }} />
      </Modal>}</main></div>
}

function RedirectToRecords() {
  const { entityId } = useParams()
  return <Navigate to={`/entities/${entityId}/records`} replace />
}
