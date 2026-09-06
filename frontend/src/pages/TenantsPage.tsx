import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowRight, Pencil, Plus, Power, Settings } from 'lucide-react'
import { api } from '../api'
import type { Tenant } from '../types'
import { Modal } from '../components/Modal'

export function TenantsPage({ activeTenantId, onSwitch }: { activeTenantId: string; onSwitch: (tenantId: string) => void }) {
  const client = useQueryClient()
  const tenants = useQuery({ queryKey: ['tenants'], queryFn: api.tenants })
  const [editing, setEditing] = useState<Tenant | null | undefined>(undefined)
  const [configuring, setConfiguring] = useState<Tenant | undefined>(undefined)
  const disable = useMutation({
    mutationFn: api.disableTenant,
    onSuccess: (_, tenantId) => {
      void client.invalidateQueries({ queryKey: ['tenants'] })
      if (tenantId === activeTenantId) onSwitch('')
    },
  })

  return <section className="page tenants-page"><header><div><h2>Tenants</h2><p className="empty">Manage workspaces and connect each one to an existing database.</p></div>
    <button type="button" onClick={() => setEditing(null)}><Plus />New tenant</button></header>
    <div className="panel grid-wrap">
      {tenants.isLoading ? <p className="empty">Loading tenants…</p> : tenants.error ? <p className="error">{tenants.error.message}</p> :
        <table><thead><tr><th>Tenant</th><th>Status</th><th>Database</th><th>Created</th><th /></tr></thead>
          <tbody>{tenants.data?.map(tenant => <tr key={tenant.id}><td><strong>{tenant.name}</strong>{tenant.id === activeTenantId && <small className="current-tenant">Current</small>}</td>
            <td><span className={`status status-${tenant.status.toLowerCase()}`}>{tenant.status}</span></td>
            <td>{tenant.connectionConfigured ? tenant.databaseName ?? 'Configured' : 'Not configured'}</td>
            <td>{new Date(tenant.createdAt).toLocaleDateString()}</td><td className="row-actions"><div className="tenant-actions">
              <button type="button" className="link" onClick={() => setEditing(tenant)}><Pencil />Edit</button>
              <button type="button" className="link" onClick={() => setConfiguring(tenant)}><Settings />Configure</button>
              {tenant.status === 'Active' && tenant.id !== activeTenantId && <button type="button" className="link" onClick={() => onSwitch(tenant.id)}><ArrowRight />Switch</button>}
              {tenant.status === 'Active' && <button type="button" className="link danger" disabled={disable.isPending}
                onClick={() => { if (confirm(`Disable ${tenant.name}? Users will no longer be able to open it.`)) disable.mutate(tenant.id) }}><Power />Disable</button>}
            </div></td></tr>)}</tbody></table>}
      {!tenants.isLoading && !tenants.data?.length && <p className="empty">No tenants yet.</p>}
    </div>
    {disable.error && <p className="error">{disable.error.message}</p>}
    {editing !== undefined && <TenantEditor tenant={editing} onClose={() => setEditing(undefined)} onSaved={tenant => {
      setEditing(undefined); void client.invalidateQueries({ queryKey: ['tenants'] }); if (!activeTenantId) onSwitch(tenant.id)
    }} />}
    {configuring && <TenantConnectionEditor tenant={configuring} onClose={() => setConfiguring(undefined)} onSaved={() => {
      setConfiguring(undefined); void client.invalidateQueries({ queryKey: ['tenants'] })
    }} />}
  </section>
}

function TenantEditor({ tenant, onClose, onSaved }: { tenant: Tenant | null; onClose: () => void; onSaved: (tenant: Tenant) => void }) {
  const [name, setName] = useState(tenant?.name ?? '')
  const [connectionString, setConnectionString] = useState('')
  const save = useMutation({ mutationFn: () => tenant ? api.updateTenant(tenant.id, name) : api.createTenant(name, connectionString), onSuccess: onSaved })
  function submit(event: FormEvent) { event.preventDefault(); save.mutate() }
  return <Modal title={tenant ? 'Edit tenant' : 'Create tenant'} onClose={onClose}><form className="dynamic-form" onSubmit={submit}>
    <label className="field">Tenant name<input required maxLength={200} value={name} onChange={event => setName(event.target.value)} /></label>
    {!tenant && <><label className="field">SQL Server connection string<textarea required value={connectionString} onChange={event => setConnectionString(event.target.value)}
      placeholder="Server=…;Database=ExistingTenantDb;User Id=…;Password=…;TrustServerCertificate=true" /></label>
      <p className="field-note">Enter plain text without JSON escaping. The database must already exist.</p></>}
    {save.error && <p className="error">{save.error.message}</p>}<div className="actions"><button disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save tenant'}</button><button type="button" className="secondary" onClick={onClose}>Cancel</button></div>
  </form></Modal>
}

function TenantConnectionEditor({ tenant, onClose, onSaved }: { tenant: Tenant; onClose: () => void; onSaved: (tenant: Tenant) => void }) {
  const [connectionString, setConnectionString] = useState('')
  const save = useMutation({ mutationFn: () => api.configureTenantConnection(tenant.id, connectionString), onSuccess: onSaved })
  function submit(event: FormEvent) { event.preventDefault(); save.mutate() }
  return <Modal title={`Configure ${tenant.name}`} onClose={onClose}><form className="dynamic-form" onSubmit={submit}>
    <label className="field">SQL Server connection string<textarea required value={connectionString} onChange={event => setConnectionString(event.target.value)}
      placeholder="Server=…;Database=ExistingTenantDb;User Id=…;Password=…;TrustServerCertificate=true" /></label>
    <p className="field-note">Enter plain text without JSON escaping. The database must already exist; saving validates the connection and applies pending schema migrations.</p>
    {save.error && <p className="error">{save.error.message}</p>}<div className="actions"><button disabled={save.isPending}>{save.isPending ? 'Configuring…' : 'Save connection'}</button><button type="button" className="secondary" onClick={onClose}>Cancel</button></div>
  </form></Modal>
}
