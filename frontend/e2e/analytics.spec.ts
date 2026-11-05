import { test, expect, entityPath } from './fixtures'

test('metrics: create, evaluate, edit, and delete', async ({ page, api }) => {
  await page.goto(`${entityPath}/metrics`)
  await page.getByLabel('Name', { exact: true }).fill('Total customers')
  await page.getByRole('button', { name: 'Save metric', exact: true }).click()
  await page.getByRole('button', { name: 'Refresh Total customers', exact: true }).click()
  await expect(page.getByText('42', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Edit Total customers', exact: true }).click()
  await page.getByLabel('Name', { exact: true }).fill('Updated count')
  await page.getByRole('button', { name: 'Save metric', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Edit Updated count', exact: true })).toBeVisible()
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: 'Delete Updated count', exact: true }).click()
  await expect.poll(() => api.collections['/api/entities/customer/metrics'].length).toBe(1)
})

test('reports: create, reopen, preview, export, and delete', async ({ page, api }) => {
  await page.goto(`${entityPath}/reports/new`)
  await page.getByLabel('Name', { exact: true }).fill('New summary')
  await page.getByRole('button', { name: 'Save report', exact: true }).click()
  await expect(page).toHaveURL(/reports\/created-/)
  await page.reload()
  await expect(page.getByLabel('Name', { exact: true })).toHaveValue('New summary')
  await expect(page.getByRole('cell', { name: '42', exact: true })).toBeVisible()
  await page.goto(`${entityPath}/reports`)
  const item = page.locator('.definition-list > li').filter({ hasText: 'New summary' })
  const download = page.waitForEvent('download')
  await item.getByRole('button', { name: 'CSV', exact: true }).click()
  expect((await download).suggestedFilename()).toMatch(/\.csv$/)
  page.once('dialog', (dialog) => dialog.accept())
  await item.getByRole('button', { name: 'Delete', exact: true }).click()
  await expect.poll(() => api.collections['/api/entities/customer/reports'].length).toBe(1)
})

test('runtime metric inputs are prompted and sent to the API', async ({ page, api }) => {
  api.collections['/api/entities/customer/metrics'][0].filter = {
    fieldId: 'first-name',
    operator: 'Equal',
    value: { parameter: 'customerName' },
  }
  await page.goto(`${entityPath}/metrics`)
  await page.getByRole('button', { name: 'Refresh Customer count', exact: true }).click()
  const dialog = page.locator('.modal')
  await expect(dialog).toBeVisible()
  await dialog.getByRole('textbox').fill('Alice')
  await dialog.locator('button[type=submit]').click()
  await expect(dialog).toHaveCount(0)
  expect(api.requests.find((request) => request.path.endsWith('/evaluate'))?.body).toEqual({
    parameters: { customerName: 'Alice' },
  })
})

test('alerts: runtime inputs are required before saving', async ({ page, api }) => {
  api.collections['/api/entities/customer/metrics'][0].filter = {
    fieldId: 'first-name',
    operator: 'Equal',
    value: { parameter: 'customerName' },
  }
  await page.goto(`${entityPath}/alerts`)
  await page.getByLabel('Name', { exact: true }).fill('High count')
  await page.getByRole('combobox', { name: 'Metric', exact: true }).selectOption('count-customer')
  await expect(page.getByRole('button', { name: 'Save alert', exact: true })).toBeDisabled()
  await page.getByLabel('customerName (required)').fill('Alice')
  await page.getByRole('button', { name: 'Save alert', exact: true }).click()
  await expect
    .poll(() => api.collections['/api/entities/customer/alerts'][0]?.parameters)
    .toEqual({ customerName: 'Alice' })
  await expect(page.getByText('High count', { exact: true })).toBeVisible()
})

test('webhooks: create, edit, disable, and delete', async ({ page, api }) => {
  await page.goto(`${entityPath}/webhooks`)
  await page.getByLabel('Name', { exact: true }).fill('CRM sync')
  await page.getByLabel('Endpoint', { exact: false }).fill('https://example.com/hook')
  await page.getByLabel('Record updated').check()
  await page.getByRole('button', { name: 'Save webhook', exact: true }).click()
  await expect(page.getByText('CRM sync', { exact: true })).toBeVisible()
  expect(api.collections['/api/entities/customer/webhooks'][0].events).toEqual(['RecordCreated', 'RecordUpdated'])
  await page.getByRole('button', { name: 'Edit', exact: true }).click()
  await page.getByLabel('Name', { exact: true }).fill('CRM updated')
  await page.getByRole('button', { name: 'Save webhook', exact: true }).click()
  await page.getByRole('button', { name: 'Disable', exact: true }).click()
  await expect.poll(() => api.collections['/api/entities/customer/webhooks'][0].isEnabled).toBe(false)
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  await expect.poll(() => api.collections['/api/entities/customer/webhooks'].length).toBe(0)
})
