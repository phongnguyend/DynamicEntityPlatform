import { useState, type FormEvent } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { BrowserRouter, Navigate, NavLink, Route, Routes, useLocation, useMatch, useNavigate, useParams } from 'react-router-dom'
import { ArrowRight, Building2, Database, LayoutDashboard, Plus, TableProperties, Users } from 'lucide-react'
import { api } from './api'
import { EntityDesigner } from './components/EntityDesigner'
import { EntityIcon } from './components/EntityIcon'
import { pinnedEntities } from './pinnedEntities'
import { Modal } from './components/Modal'
import { EntitiesListPage } from './pages/EntitiesListPage'
import { RecordsListPage } from './pages/RecordsListPage'
import { RecordFormPage } from './pages/RecordFormPage'
import { FieldsPage } from './pages/FieldsPage'
import { ReportsPage } from './pages/ReportsPage'
import { ReportBuilderPage } from './pages/ReportBuilderPage'
import { MetricsPage } from './pages/MetricsPage'
import { AlertsPage } from './pages/AlertsPage'
import { WebhooksPage } from './pages/WebhooksPage'
import { ViewsPage } from './pages/ViewsPage'
import { ViewDetailPage } from './pages/ViewDetailPage'
import { TenantsPage } from './pages/TenantsPage'
import { DashboardsPage } from './pages/DashboardsPage'
import { DashboardsListPage } from './pages/DashboardsListPage'

const tenantStorageKey = 'dynamic-data.tenant-id'

export default function App() {
  const [tenantId, setTenantId] = useState(() => localStorage.getItem(tenantStorageKey) ?? '')
  const [tenantName, setTenantName] = useState('')
  const [tenantConnectionString, setTenantConnectionString] = useState('')
  const [existingTenantId, setExistingTenantId] = useState('')
  const tenants = useQuery({
    queryKey: ['tenants'], queryFn: api.tenants, enabled: !tenantId,
  })
  const createTenant = useMutation({
    mutationFn: () => api.createTenant(tenantName, tenantConnectionString),
    onSuccess: tenant => { localStorage.setItem(tenantStorageKey, tenant.id); setTenantId(tenant.id) },
  })
  const isDuplicateName = tenants.data?.some(tenant => tenant.name.trim().toLowerCase() === tenantName.trim().toLowerCase()) ?? false

  function provision(event: FormEvent) { event.preventDefault(); if (isDuplicateName) return; createTenant.mutate() }
  function openTenant() {
    if (!existingTenantId) return
    localStorage.setItem(tenantStorageKey, existingTenantId)
    setTenantId(existingTenantId)
  }
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
        </select><button type="button" disabled={!existingTenantId} onClick={openTenant}>Open workspace <ArrowRight /></button></div> :
        <p className="empty">No existing tenants yet.</p>}
    </div>
    <div className="create-workspace"><h2>Create a workspace</h2>
      <form className="onboarding-create" onSubmit={provision}><input required placeholder="Organization name" value={tenantName} onChange={e => setTenantName(e.target.value)} />
        <textarea required aria-label="Tenant database connection string" placeholder="Connection string for an existing database" value={tenantConnectionString} onChange={e => setTenantConnectionString(e.target.value)} />
        <button disabled={createTenant.isPending || isDuplicateName}><Plus />{createTenant.isPending ? 'Provisioning…' : 'Create workspace'}</button></form>
      <p className="field-note">The database must already exist; the application does not create databases.</p>
      {isDuplicateName && <p className="error">A tenant named "{tenantName}" already exists.</p>}
      {createTenant.error && <p className="error">{createTenant.error.message}</p>}</div></section></main>

  return <BrowserRouter><Shell tenantId={tenantId} onSwitchTenant={next => {
    if (next) localStorage.setItem(tenantStorageKey, next); else localStorage.removeItem(tenantStorageKey)
    setTenantId(next)
  }} /></BrowserRouter>
}

function Shell({ tenantId, onSwitchTenant }: { tenantId: string; onSwitchTenant: (tenantId: string) => void }) {
  const navigate = useNavigate()
  const managingTenants = useLocation().pathname === '/tenants'
  const entities = useQuery({ queryKey: ['entities', tenantId], queryFn: () => api.entities(tenantId), enabled: Boolean(tenantId) })
  const [creatingEntity, setCreatingEntity] = useState(false)
  const pinned = pinnedEntities(entities.data ?? [])
  // The sidebar link targets the records tab, so NavLink's own matching would drop the highlight on
  // every other entity tab. Highlighting is keyed on the entity segment instead, whatever tab is open.
  const openEntityId = useMatch('/entities/:entityId/*')?.params.entityId

  return <div className="shell"><aside><div className="brand"><span className="mark"><Database size={18} /></span><strong>Dynamic Data</strong></div>
    <nav className="admin-nav"><NavLink to="/tenants" className={({ isActive }) => isActive ? 'active' : ''}><Users />Manage tenants</NavLink></nav>
    <nav className="dashboard-nav"><NavLink to="/dashboards" className={({ isActive }) => isActive ? 'active' : ''}><LayoutDashboard />Dashboards</NavLink></nav>
    <nav className="nav-heading"><NavLink end to="/entities" className={({ isActive }) => isActive ? 'nav-label active' : 'nav-label'}><TableProperties />Entities</NavLink></nav>
    <nav className="entity-nav">{pinned.map(entity => <NavLink key={entity.id} to={`/entities/${entity.id}/records`}
      className={entity.id === openEntityId ? 'active' : ''}><EntityIcon icon={entity.icon} />{entity.displayName}</NavLink>)}
      {!pinned.length && !entities.isLoading && <p className="nav-empty">Pin an entity to reach it from here.</p>}</nav>
    </aside>
    <main>{!managingTenants && entities.isLoading ? <p>Loading workspace…</p> : !managingTenants && entities.error ? <p className="error">{entities.error.message}</p> :
      <Routes>
        <Route path="/" element={entities.data?.[0] ? <Navigate to={`/entities/${entities.data[0].id}/records`} replace /> :
          <div className="first-entity"><p className="empty">No entities yet.</p>
            <button type="button" onClick={() => setCreatingEntity(true)}><Building2 />Create an entity</button></div>} />
        <Route path="/entities" element={<EntitiesListPage tenantId={tenantId} entities={entities.data ?? []} onCreateEntity={() => setCreatingEntity(true)} />} />
        <Route path="/entities/:entityId" element={<RedirectToRecords />} />
        <Route path="/entities/:entityId/records" element={<RecordsListPage tenantId={tenantId} />} />
        <Route path="/entities/:entityId/views" element={<ViewsPage tenantId={tenantId} />} />
        <Route path="/entities/:entityId/views/:viewId" element={<ViewDetailPage tenantId={tenantId} />} />
        <Route path="/entities/:entityId/records/new" element={<RecordFormPage tenantId={tenantId} />} />
        <Route path="/entities/:entityId/records/:recordId/edit" element={<RecordFormPage tenantId={tenantId} />} />
        <Route path="/entities/:entityId/fields" element={<FieldsPage tenantId={tenantId} />} />
        <Route path="/entities/:entityId/reports" element={<ReportsPage tenantId={tenantId} />} />
        <Route path="/entities/:entityId/reports/new" element={<ReportBuilderPage tenantId={tenantId} />} />
        <Route path="/entities/:entityId/reports/:reportId" element={<ReportBuilderPage tenantId={tenantId} />} />
        <Route path="/entities/:entityId/metrics" element={<MetricsPage tenantId={tenantId} />} />
        <Route path="/entities/:entityId/alerts" element={<AlertsPage tenantId={tenantId} />} />
        <Route path="/entities/:entityId/webhooks" element={<WebhooksPage tenantId={tenantId} />} />
        <Route path="/dashboards" element={<DashboardsListPage key={tenantId} tenantId={tenantId} />} />
        <Route path="/dashboards/:dashboardId" element={<DashboardsPage tenantId={tenantId} entities={entities.data ?? []} />} />
        <Route path="/tenants" element={<TenantsPage activeTenantId={tenantId} onSwitch={next => { onSwitchTenant(next); if (next) navigate('/') }} />} />
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
