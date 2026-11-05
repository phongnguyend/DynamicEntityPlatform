import { test, expect } from './fixtures'
import { expectButtonIcons } from './button-icon-assertions'

test('global alerts: prefixed metrics, required parameters, edit, history, test and delete', async ({ page, api }) => {
  api.collections['/api/metrics'].push({
    id: 'global-count',
    entityId: null,
    name: 'Customer count',
    aggregate: 'Count',
    sql: { enabled: true, text: 'SELECT @minimum', parameters: [{ parameter: 'minimum', dataType: 'Integer' }] },
  })
  await page.goto('/alerts')
  await expect(page.getByRole('heading', { name: 'Global alerts' })).toBeVisible()
  const selector = page.getByRole('combobox', { name: 'Metric', exact: true })
  await expect(selector.locator('option')).toHaveText([
    'Select a metric',
    'Companies: Customer count',
    'Customers: Customer count',
    'Global: Customer count',
  ])
  await selector.selectOption('global-count')
  await page.getByLabel('Name', { exact: true }).fill('Global count alert')
  await expect(page.getByRole('button', { name: 'Save alert', exact: true })).toBeDisabled()
  await page.getByLabel('minimum (required)').fill('5')
  await expectButtonIcons(page)
  await page.getByRole('button', { name: 'Save alert', exact: true }).click()
  await expect(page.getByText('Global count alert', { exact: true })).toBeVisible()
  expect(api.collections['/api/alerts'][0]).toMatchObject({
    entityId: null,
    metricId: 'global-count',
    parameters: { minimum: '5' },
  })
  await page.reload()
  await page.getByRole('button', { name: 'Edit', exact: true }).click()
  await expect(selector).toHaveValue('global-count')
  await expect(page.getByLabel('minimum (required)')).toHaveValue('5')
  await selector.selectOption('count-customer')
  await expect(page.getByLabel('minimum (required)')).toHaveCount(0)
  await page.getByRole('button', { name: 'Save alert', exact: true }).click()
  await expect.poll(() => api.collections['/api/alerts'][0]?.metricId).toBe('count-customer')
  expect(api.collections['/api/alerts'][0].parameters).toEqual({})
  expect(api.collections['/api/entities/customer/alerts']).toHaveLength(0)
  await page.getByRole('button', { name: 'Test now', exact: true }).click()
  await expect.poll(() => api.requests.some((request) => request.path === '/api/alerts/created-0/test')).toBe(true)
  await expect(page.getByRole('heading', { name: 'History', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'History', exact: true }).click()
  await expectButtonIcons(page)
  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  await expect(page.getByText('Global count alert', { exact: true })).toHaveCount(0)
  expect(api.collections['/api/alerts']).toHaveLength(0)
})

test('global alerts can use a global metric when no entities exist', async ({ page, api }) => {
  api.collections['/api/entities'] = []
  api.collections['/api/metrics'].push({
    id: 'constant',
    entityId: null,
    name: 'Constant',
    aggregate: 'Count',
    sql: { enabled: true, text: 'SELECT 1', parameters: [] },
  })
  await page.goto('/alerts')
  await page.getByLabel('Name', { exact: true }).fill('Constant alert')
  await page.getByRole('combobox', { name: 'Metric', exact: true }).selectOption({ label: 'Global: Constant' })
  await page.getByRole('button', { name: 'Save alert', exact: true }).click()
  await expect(page.getByText('Constant alert', { exact: true })).toBeVisible()
})

test('entity alerts show scoped metric names without a prefix', async ({ page }) => {
  await page.goto('/entities/customer/alerts')
  await expect(page.getByRole('combobox', { name: 'Metric', exact: true }).locator('option')).toHaveText([
    'Select a metric',
    'Customer count',
  ])
})
