import { test, expect } from './fixtures'
import { expectButtonIcons } from './button-icon-assertions'

test('global navigation follows the entire Entities section', async ({ page, api }) => {
  api.collections['/api/entities'].forEach((entity, index) => {
    entity.pinnedOrder = index
  })
  await page.goto('/metrics')
  await expect(page.getByRole('heading', { name: 'Global metrics' })).toBeVisible()
  await expect(page.locator('aside a')).toHaveText([
    'Manage tenants',
    'Dashboards',
    'Entities',
    'Customers',
    'Companies',
    'Views',
    'Reports',
    'Metrics',
    'Alerts',
  ])
  await page.locator('aside').getByRole('link', { name: 'Reports', exact: true }).click()
  await expect(page).toHaveURL(/\/reports$/)
  await expect(page.getByRole('heading', { name: 'Global reports' })).toBeVisible()
})

test('global metrics: SQL-only create, preview, parameters, edit, JSON and delete', async ({ page, api }) => {
  await page.goto('/metrics')
  await expect(page.getByRole('button', { name: 'Builder', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Generate from Builder' })).toHaveCount(0)
  await page.getByLabel('Name', { exact: true }).fill('Global count')
  await page.getByRole('textbox', { name: 'SQL query', exact: true }).fill('SELECT @take AS value')
  await page.getByRole('button', { name: 'Validate SQL' }).click()
  await expect(page.getByText('SQL is valid.', { exact: true })).toBeVisible()
  await expectButtonIcons(page)
  await page.getByRole('button', { name: 'Save metric', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Edit Global count', exact: true })).toBeVisible()
  expect(api.collections['/api/metrics'][0]).toMatchObject({ entityId: null, sql: { enabled: true } })
  expect(api.collections['/api/entities/customer/metrics']).toHaveLength(1)
  await page.reload()
  await page.getByRole('button', { name: 'Refresh Global count', exact: true }).click()
  await page.locator('.modal').getByRole('textbox').fill('12')
  await page.locator('.modal button[type=submit]').click()
  await expect(page.getByText('42', { exact: true })).toBeVisible()
  expect(api.requests.find((request) => request.path === '/api/metrics/created-0/evaluate')?.body.parameters).toEqual({
    take: '12',
  })
  await page.getByRole('button', { name: 'Edit Global count', exact: true }).click()
  await expect(page.getByRole('textbox', { name: 'SQL query', exact: true })).toHaveValue('SELECT @take AS value')
  await page.getByRole('button', { name: 'View SQL' }).click()
  await expect(page.locator('.modal pre').first()).toContainText('Customer')
  await page.locator('.modal').getByRole('button', { name: 'Close', exact: true }).click()
  await page.getByRole('button', { name: 'Edit JSON' }).click()
  await expect(page.getByRole('textbox', { name: 'JSON', exact: true })).toHaveValue(/SELECT @take AS value/)
  await page.locator('.modal').getByRole('button', { name: 'Cancel', exact: true }).click()
  await page.getByLabel('Name', { exact: true }).fill('Renamed global metric')
  await page.getByRole('button', { name: 'Save metric', exact: true }).click()
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: 'Delete Renamed global metric', exact: true }).click()
  await expect.poll(() => api.collections['/api/metrics'].length).toBe(0)
})

test('global reports: SQL-only create, reopen, preview and export', async ({ page, api }) => {
  await page.goto('/reports/new')
  await expect(page.getByRole('button', { name: 'Builder', exact: true })).toHaveCount(0)
  await page.getByLabel('Name', { exact: true }).fill('Global summary')
  await page.getByRole('textbox', { name: 'SQL query', exact: true }).fill('SELECT COUNT(*) AS count FROM Customer')
  await page.getByRole('button', { name: 'Save report', exact: true }).click()
  await expect(page).toHaveURL(/\/reports\/created-0$/)
  await page.reload()
  await expect(page.getByRole('textbox', { name: 'SQL query', exact: true })).toHaveValue(
    'SELECT COUNT(*) AS count FROM Customer',
  )
  await page.getByRole('button', { name: 'Preview', exact: true }).click()
  await expect(page.getByRole('cell', { name: '42', exact: true })).toBeVisible()
  await expectButtonIcons(page)
  expect(api.collections['/api/reports'][0].entityId).toBeNull()
  await page.goto('/reports')
  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: 'CSV', exact: true }).click()
  expect((await download).suggestedFilename()).toMatch(/\.csv$/)
  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  await expect.poll(() => api.collections['/api/reports'].length).toBe(0)
})

test('global definitions work without any entities', async ({ page, api }) => {
  api.collections['/api/entities'] = []
  await page.goto('/metrics')
  await expect(page.getByRole('textbox', { name: 'SQL query', exact: true })).toBeVisible()
  await page.getByLabel('Name', { exact: true }).fill('Constant')
  await page.getByRole('textbox', { name: 'SQL query', exact: true }).fill('SELECT 1 AS value')
  await page.getByRole('button', { name: 'Save metric', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Edit Constant', exact: true })).toBeVisible()
})

test('dashboard library includes global SQL metrics and evaluates them without an entity', async ({ page, api }) => {
  api.collections['/api/metrics'] = [
    {
      id: 'global-count',
      entityId: null,
      name: 'Global count',
      aggregate: 'Count',
      sql: { enabled: true, text: 'SELECT 1 AS value', parameters: [] },
    },
  ]
  await page.goto('/dashboards/overview')
  await page.getByRole('button', { name: 'Add cards', exact: true }).click()
  await page
    .locator('.dashboard-library-group')
    .filter({ has: page.getByRole('heading', { name: 'Global', exact: true }) })
    .getByRole('button', { name: /Global count/ })
    .click()
  await page.getByRole('button', { name: 'Close dialog' }).click()
  await expect(page.locator('.react-grid-item').getByText('42', { exact: true })).toBeVisible()
  expect(api.collections['/api/dashboards'][0].definition.items[0].entityId).toBeNull()
  expect(api.requests.some((request) => request.path === '/api/metrics/global-count/evaluate')).toBe(true)
  await page.reload()
  await expect(page.locator('.react-grid-item').getByText('42', { exact: true })).toBeVisible()
})
