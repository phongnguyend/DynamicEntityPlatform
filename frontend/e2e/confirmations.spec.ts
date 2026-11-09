import { test, expect, entityPath } from './fixtures'
import { expectButtonIcons } from './button-icon-assertions'

const cases = [
  ['/dashboards', 'Delete', 'Delete dashboard', '/api/dashboards/overview'],
  ['/dashboards/overview', 'Delete', 'Delete dashboard', '/api/dashboards/overview'],
  [`${entityPath}/records`, 'Delete record', 'Delete record', '/api/entities/customer/records/alice'],
  [`${entityPath}/views/view-customer`, 'Delete record', 'Delete record', '/api/entities/customer/records/alice'],
  [`${entityPath}/views`, 'Delete', 'Delete view', '/api/entities/customer/views/view-customer'],
  ['/views', 'Delete', 'Delete view', '/api/views/global-view'],
  [`${entityPath}/fields`, 'Remove', 'Remove index', '/api/entities/customer/indexes/index'],
  ['/tenants', 'Disable', 'Disable tenant', '/api/tenants/tenant/disable'],
]

for (const [path, triggerName, confirmLabel, endpoint] of cases) {
  test(`confirmation modal: ${path} ${triggerName}`, async ({ page, api }) => {
    api.collections['/api/views'].push({
      id: 'global-view',
      name: 'Customer SQL view',
      definition: { sql: { enabled: true, text: 'SELECT FirstName FROM Customer', parameters: [] }, rowLimit: 100 },
    })
    api.collections['/api/entities/customer/indexes'].push({
      id: 'index',
      indexName: 'IX_Customer',
      status: 'Active',
      columns: [],
    })
    page.on('dialog', async (dialog) => {
      await dialog.dismiss()
      throw new Error('Confirmation must use Modal, not a browser dialog')
    })
    await page.goto(path)
    const trigger = page.getByRole('button', { name: triggerName, exact: true })
    const confirmation = page.getByRole('alertdialog')
    const writes = () => api.requests.filter((request) => request.path === endpoint && request.method !== 'GET')

    await trigger.click()
    await expect(confirmation).toBeVisible()
    await expectButtonIcons(confirmation)
    await expect(confirmation.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused()
    await page.mouse.click(2, 2)
    await expect(confirmation).toBeVisible()
    await confirmation.getByRole('button', { name: confirmLabel, exact: true }).focus()
    await page.keyboard.press('Tab')
    await expect(confirmation.getByRole('button', { name: 'Close dialog' })).toBeFocused()
    await page.keyboard.press('Shift+Tab')
    await expect(confirmation.getByRole('button', { name: confirmLabel, exact: true })).toBeFocused()
    await confirmation.getByRole('button', { name: 'Cancel', exact: true }).click()
    await expect(confirmation).toHaveCount(0)
    await expect(trigger).toBeFocused()
    expect(writes()).toHaveLength(0)

    await trigger.click()
    await page.keyboard.press('Escape')
    await expect(confirmation).toHaveCount(0)
    expect(writes()).toHaveLength(0)
    await trigger.click()
    await confirmation.getByRole('button', { name: 'Close dialog' }).click()
    await expect(confirmation).toHaveCount(0)
    expect(writes()).toHaveLength(0)
    await trigger.click()
    await confirmation.getByRole('button', { name: confirmLabel, exact: true }).click()
    await expect.poll(() => writes().length).toBe(1)
    await expect(confirmation).toHaveCount(0)
  })
}

test('SQL replacement requires modal confirmation only for nonempty SQL', async ({ page, api }) => {
  await page.goto(`${entityPath}/metrics`)
  await page.getByRole('button', { name: 'SQL', exact: true }).click()
  const editor = page.getByRole('textbox', { name: 'SQL query', exact: true })
  await editor.fill('SELECT 123 AS value')
  await page.getByRole('button', { name: 'Generate from Builder' }).click()
  await expectButtonIcons(page.getByRole('alertdialog'))
  await page.getByRole('alertdialog').getByRole('button', { name: 'Cancel' }).click()
  await expect(editor).toHaveValue('SELECT 123 AS value')
  expect(api.requests.filter((request) => request.path.endsWith('/sql'))).toHaveLength(0)
  await page.getByRole('button', { name: 'Generate from Builder' }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Replace SQL' }).click()
  await expect(editor).toHaveValue('SELECT COUNT(*) FROM [Customer]')
  await editor.fill('')
  await page.getByRole('button', { name: 'Generate from Builder' }).click()
  await expect(editor).toHaveValue('SELECT COUNT(*) FROM [Customer]')
  await expect(page.getByRole('alertdialog')).toHaveCount(0)
})
