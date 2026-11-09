import { test, expect, entityPath } from './fixtures'

test.describe('onboarding', () => {
  test.use({ selectedTenant: false })

  test('select an existing tenant', async ({ page }) => {
    await page.goto('/tenants')
    await page.getByRole('combobox', { name: 'Existing tenant' }).selectOption('tenant')
    await page.getByRole('button', { name: 'Open workspace' }).click()
    await expect(page.getByRole('heading', { name: 'Tenants', exact: true })).toBeVisible()
    expect(await page.evaluate(() => localStorage.getItem('dynamic-data.tenant-id'))).toBe('tenant')
  })
})

test('fields: edit an existing field', async ({ page, api }) => {
  await page.goto(`${entityPath}/fields`)
  await page.locator('.field-manager').getByRole('button', { name: 'Edit', exact: true }).click()
  await page.locator('.modal').getByLabel('Display name', { exact: true }).fill('Given name')
  await page.getByRole('button', { name: 'Save field' }).click()
  await expect.poll(() => api.collections['/api/entities/customer/fields'][0].displayName).toBe('Given name')
  await expect(page.locator('.field-list').getByText('Given name', { exact: true })).toBeVisible()
})

test('indexes: create and remove an index', async ({ page, api }) => {
  await page.goto(`${entityPath}/fields`)
  await page.getByRole('button', { name: 'Create index', exact: true }).click()
  await page
    .locator('.modal')
    .getByRole('checkbox', { name: /First name/ })
    .check()
  await page.locator('.modal').getByRole('button', { name: 'Create index', exact: true }).click()
  await expect(page.getByText('IX_Test', { exact: true })).toBeVisible()
  expect(
    api.requests.find((request) => request.method === 'POST' && request.path.endsWith('/indexes'))?.body.columns,
  ).toEqual([{ fieldId: 'first-name', descending: false }])
  await page.locator('.index-list').getByRole('button', { name: 'Remove', exact: true }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Remove index', exact: true }).click()
  await expect(page.getByText('No indexes have been created.')).toBeVisible()
})

test('records: filter submits the selected condition and clear restores the list', async ({ page, api }) => {
  await page.goto(`${entityPath}/records`)
  await page.getByRole('button', { name: 'First name', exact: true }).click()
  await page.getByPlaceholder('Value', { exact: true }).fill('Alice')
  await page.getByRole('button', { name: 'Apply', exact: true }).click()
  await expect
    .poll(() => api.requests.find((request) => request.path.endsWith('/query'))?.body.filter.conditions)
    .toEqual([{ fieldId: 'first-name', operator: 'Equal', value: 'Alice' }])
  await page.getByRole('button', { name: 'First name', exact: true }).click()
  await page.getByRole('button', { name: 'Clear', exact: true }).click()
  await expect(page.getByRole('cell', { name: 'Alice', exact: true })).toBeVisible()
  await expect(page.getByPlaceholder('Value', { exact: true })).toHaveCount(0)
})

test('imports: upload, map, validate, commit, and refresh records', async ({ page, api }) => {
  await page.goto(`${entityPath}/records`)
  await page.getByRole('button', { name: 'Import', exact: true }).click()
  await page
    .locator('input[type=file]')
    .setInputFiles({ name: 'customers.csv', mimeType: 'text/csv', buffer: Buffer.from('FirstName\nImported\n') })
  await expect(page.locator('.mapping select')).toHaveValue('first-name')
  await page.getByRole('button', { name: 'Validate preview', exact: true }).click()
  await page
    .locator('.modal')
    .getByRole('button', { name: /Import 1/ })
    .click()
  await expect(page.getByText('1 record imported', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Done', exact: true }).click()
  await expect(page.getByRole('cell', { name: 'Imported', exact: true })).toBeVisible()
  expect(api.requests.find((request) => request.path === '/api/imports/import/preview')?.body).toEqual({
    mappings: [{ sourceColumn: 'FirstName', targetFieldId: 'first-name' }],
  })
})

test('JSON editor rejects invalid JSON, saves valid edits, and scrolls inside modal body', async ({ page, api }) => {
  await page.goto(`${entityPath}/metrics`)
  await page.getByRole('button', { name: 'Edit JSON', exact: true }).click()
  const editor = page.getByRole('textbox', { name: 'JSON', exact: true })
  await editor.fill('{ invalid')
  await page.getByRole('button', { name: 'Save JSON', exact: true }).click()
  await expect(page.locator('.modal .error')).toBeVisible()
  expect(api.requests.filter((request) => request.method === 'POST' && request.path.endsWith('/metrics'))).toHaveLength(
    0,
  )
  await editor.fill(JSON.stringify({ name: 'JSON count', aggregate: 'Count' }, null, 2))
  expect(await page.locator('.modal-body').evaluate((element) => getComputedStyle(element).overflowY)).toMatch(
    /auto|scroll/,
  )
  await page.getByRole('button', { name: 'Save JSON', exact: true }).click()
  await expect(page.locator('.modal')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Edit JSON count', exact: true })).toBeVisible()
})

test('reports: CSV export asks for runtime parameter values', async ({ page, api }) => {
  api.collections['/api/entities/customer/reports'][0].query.filter = {
    fieldId: 'first-name',
    operator: 'Equal',
    value: { parameter: 'customerName' },
  }
  await page.goto(`${entityPath}/reports`)
  await page.getByRole('button', { name: 'CSV', exact: true }).click()
  await page.locator('.modal').getByRole('textbox').fill('Alice')
  const download = page.waitForEvent('download')
  await page.locator('.modal button[type=submit]').click()
  await download
  expect(api.requests.find((request) => request.path.endsWith('/export'))?.body.parameters).toEqual({
    customerName: 'Alice',
  })
})

test('dashboard cards: bind runtime input to a dashboard parameter', async ({ page, api }) => {
  api.collections['/api/entities/customer/metrics'][0].filter = {
    fieldId: 'first-name',
    operator: 'Equal',
    value: { parameter: 'customerName' },
  }
  api.collections['/api/dashboards'][0].definition = {
    items: [{ kind: 'metric', entityId: 'customer', id: 'count-customer' }],
    layouts: {},
    parameters: [{ id: 'customer-param', name: 'customer', label: 'Customer', type: 'text', value: 'Alice' }],
  }
  await page.goto('/dashboards/overview')
  await page.getByRole('button', { name: 'Configure runtime parameters', exact: true }).click()
  await page.getByRole('combobox', { name: 'Value source for customerName' }).selectOption('dashboard')
  await page.getByRole('combobox', { name: 'Dashboard parameter for customerName' }).selectOption('customer-param')
  await page.getByRole('button', { name: 'Save parameters', exact: true }).click()
  await expect(page.locator('.react-grid-item').getByText('42', { exact: true })).toBeVisible()
  expect(api.collections['/api/dashboards'][0].definition.items[0].parameterBindings).toEqual({
    customerName: 'customer-param',
  })
  expect(api.requests.find((request) => request.path.endsWith('/evaluate'))?.body.parameters).toEqual({
    customerName: 'Alice',
  })
  await page.reload()
  await expect(page.locator('.react-grid-item').getByText('42', { exact: true })).toBeVisible()
})

test('dashboard library: add a metric card', async ({ page, api }) => {
  await page.goto('/dashboards/overview')
  await page.getByRole('button', { name: 'Add cards', exact: true }).click()
  await page
    .locator('.dashboard-library-group')
    .filter({ has: page.getByRole('heading', { name: 'Customers', exact: true }) })
    .getByRole('button', { name: /Customer count/ })
    .click()
  await expect
    .poll(() => api.collections['/api/dashboards'][0].definition.items)
    .toEqual([{ kind: 'metric', entityId: 'customer', id: 'count-customer' }])
  await page.locator('.modal').getByRole('button', { name: 'Close dialog' }).click()
  await expect(page.locator('.react-grid-item').getByText('42', { exact: true })).toBeVisible()
})
