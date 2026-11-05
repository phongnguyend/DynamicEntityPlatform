import { test as base, expect } from '@playwright/test'

export const entityId = 'customer'
export const entityPath = `/entities/${entityId}`
const timestamp = '2026-01-01T00:00:00Z'
// These fixtures exercise the real frontend through its HTTP boundary, without a database.
type Row = { id: string; [key: string]: any }

export const test = base.extend<{
  selectedTenant: boolean
  api: { collections: Record<string, Row[]>; requests: { method: string; path: string; body: any }[] }
}>({
  selectedTenant: [true, { option: true }],
  api: [
    async ({ page, selectedTenant }, use) => {
      const entities = ['customer', 'company'].map((id) => ({
        id,
        name: id === 'customer' ? 'Customer' : 'Company',
        displayName: id === 'customer' ? 'Customers' : 'Companies',
        schemaVersion: 1,
        status: 'Active',
        createdAt: timestamp,
        updatedAt: timestamp,
      }))
      const collections: Record<string, Row[]> = {
        '/api/tenants': [
          { id: 'tenant', name: 'Test workspace', status: 'Active', connectionConfigured: true, createdAt: timestamp },
        ],
        '/api/entities': entities,
        '/api/dashboards': [
          {
            id: 'overview',
            name: 'Overview',
            definition: { items: [], layouts: {}, parameters: [] },
            createdAt: timestamp,
            updatedAt: timestamp,
          },
        ],
      }
      for (const entity of entities) {
        const prefix = `/api/entities/${entity.id}`
        collections[`${prefix}/fields`] = [
          {
            id: 'first-name',
            name: 'FirstName',
            displayName: 'First name',
            storageKey: 'FirstName',
            dataType: 'Text',
            isRequired: false,
            isUnique: false,
            isFilterable: true,
            isSortable: true,
            isFacetable: false,
            isSearchable: true,
            sortOrder: 0,
          },
        ]
        collections[`${prefix}/records`] = [
          { id: 'alice', data: { FirstName: 'Alice' }, version: 'AAAA', createdAt: timestamp, updatedAt: timestamp },
        ]
        collections[`${prefix}/views`] = [
          { id: `view-${entity.id}`, entityId: entity.id, name: 'All customers', definition: {} },
        ]
        collections[`${prefix}/metrics`] = [
          {
            id: `count-${entity.id}`,
            entityId: entity.id,
            name: 'Customer count',
            aggregate: 'Count',
            createdAt: timestamp,
            updatedAt: timestamp,
          },
        ]
        collections[`${prefix}/reports`] = [
          {
            id: `report-${entity.id}`,
            entityId: entity.id,
            name: 'Customer report',
            query: { dimensions: [], measures: [{ aggregate: 'Count', alias: 'count' }], limit: 100 },
            visualization: 'Table',
            createdAt: timestamp,
            updatedAt: timestamp,
          },
        ]
        for (const resource of ['alerts', 'webhooks', 'indexes']) {
          collections[`${prefix}/${resource}`] = []
        }
      }
      const requests: { method: string; path: string; body: any }[] = []
      const unexpected: string[] = []
      const errors: string[] = []
      page.on('pageerror', (error) => errors.push(error.message))
      if (selectedTenant) {
        await page.addInitScript(() => localStorage.setItem('dynamic-data.tenant-id', 'tenant'))
      }
      await page.route('**/api/**', async (route) => {
        const request = route.request()
        const path = new URL(request.url()).pathname
        const method = request.method()
        const body = request.headers()['content-type']?.includes('application/json')
          ? request.postDataJSON()
          : undefined
        requests.push({ method, path, body })
        const respond = (json: unknown) => route.fulfill({ json })
        if (path === '/api/entities/customer/imports' && method === 'POST') {
          return respond({ id: 'import', fileName: 'customers.csv', columns: ['FirstName'], status: 'Uploaded' })
        }
        if (path === '/api/imports/import/preview') {
          return respond({ totalRows: 1, validRows: 1, invalidRows: 0, errors: [] })
        }
        if (path === '/api/imports/import/commit') {
          collections['/api/entities/customer/records'].push({
            id: 'imported',
            data: { FirstName: 'Imported' },
            version: 'AAAA',
            createdAt: timestamp,
            updatedAt: timestamp,
          })
          return respond({ importedRows: 1 })
        }
        if (path.endsWith('/sql')) {
          return respond({
            sql: 'SELECT COUNT(*) FROM [dbo].[Records]',
            virtualSql: 'SELECT COUNT(*) FROM [Customer]',
            parameters: [],
            virtualParameters: [],
          })
        }
        if (path.endsWith('/export')) {
          return route.fulfill({ contentType: 'text/csv', body: 'FirstName\nAlice\n' })
        }
        if (/\/metrics\/(preview|[^/]+\/evaluate)$/.test(path)) {
          return respond({ value: 42, evaluatedAt: timestamp })
        }
        if (/\/(reports|analytics)\/(preview|[^/]+\/run)$/.test(path)) {
          return respond({
            columns: [{ key: 'count', label: 'Count', dataType: 'Integer', role: 'Measure' }],
            rows: [{ count: 42 }],
            generatedAt: timestamp,
            truncated: false,
          })
        }
        if (path.endsWith('/history')) {
          return respond({ evaluations: [], notifications: [] })
        }
        if (path.endsWith('/test')) {
          return respond({ id: 'evaluation', state: 'Firing', value: 42, threshold: 1, evaluatedAt: timestamp })
        }
        if (path.endsWith('/query')) {
          return respond({ items: collections[path.replace(/\/query$/, '/records')] })
        }
        if (path === '/api/entities/pins') {
          collections['/api/entities'].forEach((entity) => {
            const order = body.entityIds.indexOf(entity.id)
            entity.pinnedOrder = order < 0 ? null : order
          })
          return respond(collections['/api/entities'])
        }
        let collectionPath = Object.keys(collections).find(
          (key) => path === key || (path.startsWith(key + '/') && !path.slice(key.length + 1).includes('/')),
        )
        if (path.startsWith('/api/views/')) {
          collectionPath = `/api/entities/${entityId}/views`
        }
        const collection = collectionPath && collections[collectionPath]
        if (collection && collectionPath) {
          const id = path === collectionPath ? undefined : path.split('/').at(-1)
          const index = collection.findIndex((item) => item.id === id)
          if (method === 'GET') {
            return respond(id ? collection[index] : path.endsWith('/records') ? { items: collection } : collection)
          }
          if (method === 'POST' && !id) {
            const created = {
              id: `created-${collection.length}`,
              entityId,
              status: 'Active',
              schemaVersion: 1,
              createdAt: timestamp,
              updatedAt: timestamp,
              version: 'AAAA',
              storageKey: body.name,
              indexName: 'IX_Test',
              ...body,
            }
            if (path === '/api/entities') {
              for (const resource of [
                'fields',
                'records',
                'views',
                'metrics',
                'reports',
                'alerts',
                'webhooks',
                'indexes',
              ]) {
                collections[`${path}/${created.id}/${resource}`] = []
              }
            }
            collection.push(created)
            return respond(created)
          }
          if ((method === 'PUT' || method === 'PATCH') && index >= 0) {
            collection[index] = { ...collection[index], ...body }
            return respond(collection[index])
          }
          if (method === 'DELETE' && index >= 0) {
            collection.splice(index, 1)
            return route.fulfill({ status: 204 })
          }
        }
        unexpected.push(`${method} ${path}`)
        await route.fulfill({ status: 501, json: { title: `Unhandled fixture: ${method} ${path}` } })
      })
      await use({ collections, requests })
      expect(unexpected, 'Every API request must have an explicit test fixture').toEqual([])
      expect(errors, 'No uncaught browser errors').toEqual([])
    },
    { auto: true },
  ],
})

export { expect }
