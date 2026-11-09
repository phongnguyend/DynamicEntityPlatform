import { test, expect } from './fixtures'
import { expectButtonIcons } from './button-icon-assertions'

test('global views: SQL create, read-only results, column settings, reload, edit and delete', async ({ page, api }) => {
  await page.goto('/views')
  await page.getByRole('link', { name: 'New view', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Builder', exact: true })).toHaveCount(0)
  await page.getByLabel('Name', { exact: true }).fill('All customers')
  await page.getByRole('textbox', { name: 'SQL query', exact: true }).fill('SELECT FirstName AS name FROM Customer')
  await page.getByRole('button', { name: 'Validate SQL' }).click()
  await expect(page.getByText('SQL is valid.', { exact: true })).toBeVisible()
  await expectButtonIcons(page)
  await page.getByRole('button', { name: 'Save view', exact: true }).click()
  await expect(page).toHaveURL(/\/views\/created-0$/)
  await expect(page.getByRole('cell', { name: 'Alice', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: /Edit record|Delete record|New record/ })).toHaveCount(0)
  await page.getByRole('button', { name: 'Column settings', exact: true }).click()
  await expectButtonIcons(page)
  const columnRows = page.locator('.modal .field-order-list li')
  await columnRows.filter({ hasText: 'Count' }).dragTo(columnRows.filter({ hasText: 'Name' }))
  await expect(columnRows.locator('strong')).toHaveText(['Count', 'Name'])
  await page.getByRole('checkbox', { name: 'Name', exact: true }).uncheck()
  await page.getByRole('button', { name: 'Apply settings', exact: true }).click()
  await expect(page.getByRole('columnheader')).toHaveText(['Count'])
  await page.reload()
  await expect(page.getByRole('columnheader')).toHaveText(['Count'])
  expect(api.collections['/api/views'][0].definition).toMatchObject({
    columnOrder: ['count', 'name'],
    hiddenColumns: ['name'],
  })
  await page.getByRole('button', { name: 'View SQL', exact: true }).click()
  await expect(page.locator('.modal pre').first()).toContainText('Customer')
  await page.getByRole('button', { name: 'Database names', exact: true }).click()
  await expect(page.locator('.modal pre').first()).toContainText('dbo')
  await page.locator('.modal').getByRole('button', { name: 'Close', exact: true }).click()
  await page.getByRole('button', { name: 'View JSON', exact: true }).click()
  await expect(page.locator('.modal pre')).toContainText('columnOrder')
  await page.locator('.modal').getByRole('button', { name: 'Close dialog', exact: true }).click()
  await page.getByRole('link', { name: 'Edit view', exact: true }).click()
  await page.getByLabel('Name', { exact: true }).fill('Renamed view')
  await page.getByRole('button', { name: 'Save view', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Renamed view', exact: true })).toBeVisible()
  await page.getByRole('link', { name: 'Views', exact: true }).last().click()
  await expect(page.getByText('Renamed view', { exact: true })).toBeVisible()
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  await expect(page.getByText('Renamed view', { exact: true })).toHaveCount(0)
  expect(api.collections['/api/views']).toHaveLength(0)
  expect(api.collections['/api/entities/customer/views']).toHaveLength(1)
})

test('global view runtime parameters are prompted and passed to execution', async ({ page, api }) => {
  api.collections['/api/views'].push({
    id: 'parameterized',
    name: 'By name',
    definition: {
      rowLimit: 50,
      sql: { enabled: true, text: 'SELECT @name AS name', parameters: [{ parameter: 'name', type: 'Text' }] },
    },
  })
  await page.goto('/views/parameterized')
  await expect(page.getByText('Run this view to provide its runtime parameter values.')).toBeVisible()
  expect(api.requests.filter((request) => request.path.endsWith('/run'))).toHaveLength(0)
  await page.getByRole('button', { name: 'Run view', exact: true }).click()
  await page.locator('.modal').getByLabel('name', { exact: true }).fill('Alice')
  await page.locator('.modal').getByRole('button', { name: 'Run view', exact: true }).click()
  await expect(page.getByRole('cell', { name: 'Alice', exact: true })).toBeVisible()
  expect(api.requests.find((request) => request.path === '/api/views/parameterized/run')?.body).toEqual({
    parameters: { name: 'Alice' },
  })
  await page.getByRole('button', { name: 'Run view', exact: true }).click()
  await expect(page.locator('.modal').getByLabel('name', { exact: true })).toHaveValue('Alice')
})

test('global view editor previews explicitly and supports JSON and empty-entity workspaces', async ({ page, api }) => {
  api.collections['/api/entities'] = []
  await page.goto('/views/new')
  await page.getByLabel('Name', { exact: true }).fill('Constant')
  await page.getByRole('textbox', { name: 'SQL query', exact: true }).fill('SELECT 1 AS value')
  expect(api.requests.filter((request) => request.path.endsWith('/preview'))).toHaveLength(0)
  await page.getByRole('button', { name: 'Preview results', exact: true }).click()
  await expect(page.getByRole('cell', { name: 'Alice', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Edit JSON', exact: true }).click()
  await page
    .getByRole('textbox', { name: 'JSON', exact: true })
    .fill(JSON.stringify({ sql: { enabled: true, text: 'SELECT 2 AS value', parameters: [] }, rowLimit: 20 }))
  await page.getByRole('button', { name: 'Save JSON', exact: true }).click()
  await expect(page.getByRole('textbox', { name: 'SQL query', exact: true })).toHaveValue('SELECT 2 AS value')
  await expect(page.getByText('The SQL has changed. Preview again to update these results.')).toBeVisible()
  await page.getByRole('button', { name: 'Save view', exact: true }).click()
  await expect(page).toHaveURL(/\/views\/created-0$/)
  expect(api.collections['/api/views'][0].definition.rowLimit).toBe(20)
})

test('global view errors are visible and missing views do not run SQL', async ({ page, api }) => {
  await page.route('**/api/views/missing', (route) =>
    route.fulfill({ status: 404, json: { title: 'Global view was not found.' } }),
  )
  await page.goto('/views/missing')
  await expect(page.getByRole('alert')).toContainText('Global view was not found.')
  expect(api.requests.filter((request) => request.path.endsWith('/run'))).toHaveLength(0)
})
