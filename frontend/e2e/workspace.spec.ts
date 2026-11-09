import { test, expect, entityPath } from './fixtures'

test('tenants: create and rename a workspace', async ({ page, api }) => {
  await page.goto('/tenants')
  await page.getByRole('button', { name: 'New tenant' }).click()
  await page.getByLabel('Tenant name', { exact: true }).fill('New workspace')
  await page.getByLabel('SQL Server connection string').fill('Server=fixture;Database=fixture')
  await page.getByRole('button', { name: 'Save tenant' }).click()
  const row = page.getByRole('row').filter({ hasText: 'New workspace' })
  await expect(row).toBeVisible()
  await row.getByRole('button', { name: 'Edit', exact: true }).click()
  await page.getByLabel('Tenant name', { exact: true }).fill('Renamed workspace')
  await page.getByRole('button', { name: 'Save tenant' }).click()
  await expect(page.getByRole('row').filter({ hasText: 'Renamed workspace' })).toBeVisible()
  expect(api.collections['/api/tenants']).toHaveLength(2)
})

test('entities: create and pin an entity', async ({ page, api }) => {
  await page.goto('/entities')
  await page.getByRole('button', { name: 'New entity', exact: true }).click()
  await page.getByLabel('Entity name').fill('Orders')
  await page.getByLabel('Display name').fill('Orders')
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect.poll(() => api.collections['/api/entities'].some((item) => item.name === 'Orders')).toBe(true)
  await page.goto('/entities')
  await page.getByTitle('Pin Orders to the sidebar').click()
  await expect.poll(() => api.collections['/api/entities'].find((item) => item.name === 'Orders')?.pinnedOrder).toBe(0)
})

test('fields: create a choice field with behavior flags', async ({ page, api }) => {
  await page.goto(`${entityPath}/fields`)
  await page.getByLabel('Field name').fill('Status')
  await page.getByLabel('Display name', { exact: true }).fill('Status')
  await page.getByRole('combobox', { name: 'Data type', exact: true }).selectOption('Choice')
  await page.getByLabel('Choices (one per line)').fill('Active\nInactive')
  await page.getByLabel('Required', { exact: true }).check()
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect
    .poll(() => api.collections['/api/entities/customer/fields'].find((item) => item.name === 'Status'))
    .toMatchObject({ isRequired: true, configuration: { values: ['Active', 'Inactive'] } })
  await expect(page.locator('.field-list').getByText('Status', { exact: true })).toBeVisible()
})

test('records: create, edit, and delete a record', async ({ page, api }) => {
  await page.goto(`${entityPath}/records/new`)
  await page.getByLabel('First name').fill('Bob')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  const row = page.getByRole('row').filter({ hasText: 'Bob' })
  await expect(row).toBeVisible()
  await row.getByRole('button', { name: 'Edit record', exact: true }).click()
  await page.getByLabel('First name').fill('Bobby')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByRole('row').filter({ hasText: 'Bobby' })).toBeVisible()
  await page
    .getByRole('row')
    .filter({ hasText: 'Bobby' })
    .getByRole('button', { name: 'Delete record', exact: true })
    .click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Delete record', exact: true }).click()
  await expect(page.getByRole('cell', { name: 'Bobby', exact: true })).toHaveCount(0)
  expect(api.collections['/api/entities/customer/records']).toHaveLength(1)
})

test('records: CSV export and import dialog', async ({ page }) => {
  await page.goto(`${entityPath}/records`)
  await expect(page.getByRole('heading', { name: 'Customers', exact: true })).toBeVisible()
  await expect(page.locator('.page > header')).not.toContainText('Schema v')
  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Export CSV' }).click()
  expect((await download).suggestedFilename()).toBe('Customer.csv')
  await page.getByRole('button', { name: 'Import', exact: true }).click()
  await expect(page.locator('.modal')).toBeVisible()
  await expect(page.locator('input[type=file]')).toHaveCount(1)
})

test('views: rename, inspect JSON, open, and delete', async ({ page, api }) => {
  await page.goto(`${entityPath}/views`)
  await page.getByRole('button', { name: 'Edit', exact: true }).click()
  await page.getByLabel('Name', { exact: true }).fill('Renamed view')
  await page.getByRole('button', { name: 'Save view', exact: true }).click()
  await expect(page.getByText('Renamed view', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'View JSON' }).click()
  await expect(page.locator('.modal').locator('.highlighted-code')).toBeVisible()
  await page.locator('.modal').getByRole('button', { name: 'Close', exact: true }).last().click()
  await page.getByRole('link', { name: 'Open', exact: true }).click()
  await expect(page.getByRole('cell', { name: 'Alice' })).toBeVisible()
  await page.getByRole('button', { name: 'Column Settings', exact: true }).click()
  const visibility = page.locator('.modal').getByRole('checkbox').first()
  await visibility.uncheck()
  await expect(page.locator('.modal .column-hidden')).toContainText('Hidden')
  await page.locator('.modal').getByRole('button', { name: 'Cancel', exact: true }).click()
  await page.getByRole('button', { name: 'Column Settings', exact: true }).click()
  await expect(visibility).toBeChecked()
  await page.locator('.modal').getByRole('button', { name: 'Apply settings', exact: true }).click()
  await page.goto(`${entityPath}/views`)
  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Delete view', exact: true }).click()
  await expect(page.getByText('Renamed view', { exact: true })).toHaveCount(0)
  expect(
    api.requests
      .filter((request) => request.method === 'PATCH' || request.method === 'DELETE')
      .map((request) => request.path),
  ).toEqual(['/api/entities/customer/views/view-customer', '/api/entities/customer/views/view-customer'])
  expect(api.collections['/api/views']).toHaveLength(0)
})

test('API errors are visible to users', async ({ page }) => {
  await page.route('**/api/entities/customer/records?*', (route) =>
    route.fulfill({ status: 500, json: { title: 'Records unavailable' } }),
  )
  await page.goto(`${entityPath}/records`)
  await expect(page.getByText('Records unavailable', { exact: true })).toBeVisible({ timeout: 15000 })
})
