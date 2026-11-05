import { test, expect } from './fixtures'

test('dashboards: create, rename, reopen, and delete', async ({ page, api }) => {
  await page.goto('/dashboards')
  await page.getByRole('button', { name: 'New dashboard', exact: true }).click()
  await page.getByLabel('Name', { exact: true }).fill('Sales')
  await page.getByRole('button', { name: 'Create dashboard', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Sales', exact: true })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Sales', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Edit', exact: true }).click()
  await page.getByLabel('Name', { exact: true }).fill('Sales overview')
  await page.getByRole('button', { name: 'Save changes', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Sales overview', exact: true })).toBeVisible()
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  await expect(page).toHaveURL(/\/dashboards$/)
  expect(api.collections['/api/dashboards']).toHaveLength(1)
})

test('dashboards: duplicate names and backdrop dismissal', async ({ page }) => {
  await page.goto('/dashboards')
  await page.getByRole('button', { name: 'New dashboard', exact: true }).click()
  await page.getByLabel('Name', { exact: true }).fill('Overview')
  await expect(page.getByRole('button', { name: 'Create dashboard', exact: true })).toBeDisabled()
  await page.mouse.click(2, 2)
  await expect(page.locator('.modal')).toBeVisible()
  await page.getByRole('button', { name: 'Cancel', exact: true }).click()
  await expect(page.locator('.modal')).toHaveCount(0)
})

test('dashboards: configure a dashboard parameter and persist after refresh', async ({ page, api }) => {
  await page.goto('/dashboards/overview')
  await page.getByRole('button', { name: 'Parameters (0)', exact: true }).click()
  await page.getByRole('button', { name: 'Add parameter', exact: true }).click()
  await page.getByLabel('Name', { exact: true }).fill('customerName')
  await page.getByLabel('Display label').fill('Customer')
  await page.getByLabel('Set a value').check()
  await page.getByLabel('Value', { exact: true }).fill('Alice')
  await page.getByRole('button', { name: 'Apply parameter', exact: true }).click()
  await page.getByRole('button', { name: 'Save changes', exact: true }).click()
  await expect.poll(() => api.collections['/api/dashboards'][0].definition.parameters[0]?.value).toBe('Alice')
  await page.reload()
  await page.getByRole('button', { name: 'Parameters (1)', exact: true }).click()
  await expect(page.getByText('Customer', { exact: true })).toBeVisible()
})

test('dashboards: cards and layout survive reload', async ({ page, api }) => {
  api.collections['/api/dashboards'][0].definition = {
    items: [{ kind: 'metric', entityId: 'customer', id: 'count-customer' }],
    layouts: { lg: [{ i: 'metric:customer:count-customer', x: 3, y: 0, w: 3, h: 4 }] },
  }
  await page.setViewportSize({ width: 1700, height: 1000 })
  await page.goto('/dashboards/overview')
  const card = page.locator('.react-grid-item').filter({ hasText: 'Customer count' })
  await expect(card.getByText('42', { exact: true })).toBeVisible()
  const before = await card.boundingBox()
  await page.reload()
  await expect(card.getByText('42', { exact: true })).toBeVisible()
  await expect
    .poll(async () => {
      const after = await card.boundingBox()
      return Math.abs(after!.x - before!.x) + Math.abs(after!.width - before!.width)
    })
    .toBeLessThan(2)
  await page.getByRole('button', { name: 'Remove from dashboard' }).click()
  await expect(card).toHaveCount(0)
  await expect.poll(() => api.collections['/api/dashboards'][0].definition.items.length).toBe(0)
})
