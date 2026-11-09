import { test, expect, entityPath } from './fixtures'
import { expectButtonIcons } from './button-icon-assertions'

const pages = [
  ['/tenants', 'New tenant'],
  ['/entities', 'New entity'],
  ['/dashboards', 'New dashboard'],
  ['/dashboards/overview', 'Add cards'],
  ['/alerts', 'Save alert'],
  ['/views/new', 'Save view'],
  [`${entityPath}/records`, 'New record'],
  [`${entityPath}/records/new`, 'Save'],
  [`${entityPath}/fields`, 'Create index'],
  [`${entityPath}/views`, 'View JSON'],
  [`${entityPath}/views/view-customer`, 'View SQL'],
  [`${entityPath}/metrics`, 'Save metric'],
  [`${entityPath}/reports`, 'CSV'],
  [`${entityPath}/reports/new`, 'Save report'],
  [`${entityPath}/alerts`, 'Save alert'],
  [`${entityPath}/webhooks`, 'Save webhook'],
]

for (const [path, readyButton] of pages) {
  test(`button icons: ${path}`, async ({ page }) => {
    await page.goto(path)
    await expect(page.getByRole('button', { name: readyButton, exact: true })).toBeVisible()
    await expectButtonIcons(page)
  })
}

for (const [path, trigger] of [
  ['/tenants', 'New tenant'],
  ['/entities', 'New entity'],
  ['/dashboards', 'New dashboard'],
  [`${entityPath}/metrics`, 'Edit JSON'],
  [`${entityPath}/records`, 'Import'],
  [`${entityPath}/fields`, 'Create index'],
]) {
  test(`dialog button icons: ${trigger}`, async ({ page }) => {
    await page.goto(path)
    await page.getByRole('button', { name: trigger, exact: true }).click()
    await expect(page.locator('.modal')).toBeVisible()
    await expectButtonIcons(page.locator('.modal'))
  })
}

for (const feature of ['metrics', 'reports/new']) {
  test(`SQL icons: ${feature} parameter uses Plus instead of a duplicated @`, async ({ page }) => {
    await page.goto(`${entityPath}/${feature}`)
    await page.getByRole('button', { name: 'SQL', exact: true }).click()
    await page.getByRole('textbox', { name: 'SQL query', exact: true }).fill('SELECT @take')
    const parameter = page.getByRole('button', { name: '@take', exact: true })
    await expect(parameter).toHaveText('@take')
    await expect(parameter.locator('svg')).toHaveCount(1)
    await expect(parameter.locator('svg.lucide-plus')).toBeVisible()
    await expect(parameter.locator('svg.lucide-at-sign')).toHaveCount(0)
    await page.locator('.sql-schema-browser > summary').click()
    await expect(page.getByRole('button', { name: 'Insert field FirstName' })).toBeVisible()
    await expectButtonIcons(page)
    await page.getByRole('button', { name: 'Company', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Company', exact: true })).toHaveAttribute('aria-pressed', 'true')
    await expectButtonIcons(page.locator('.sql-schema-browser'))
  })
}

test('SQL preview icons stay visible when switching name modes', async ({ page }) => {
  await page.goto(`${entityPath}/metrics`)
  await page.getByRole('button', { name: 'View SQL', exact: true }).click()
  const modal = page.locator('.modal')
  await expect(modal.getByRole('button', { name: 'Virtual names' })).toBeVisible()
  await expectButtonIcons(modal)
  await modal.getByRole('button', { name: 'Database names' }).click()
  await expect(modal.getByRole('button', { name: 'Database names' })).toHaveAttribute('aria-pressed', 'true')
  await expectButtonIcons(modal)
})

test('color swatch icons remain visible when selected and unselected', async ({ page }) => {
  await page.goto(`${entityPath}/views`)
  await page.getByRole('button', { name: 'Edit', exact: true }).click()
  const modal = page.locator('.modal')
  const green = modal.getByRole('button', { name: 'Green', exact: true })
  await expect(green).toHaveAttribute('aria-pressed', 'false')
  await expectButtonIcons(modal)
  await green.click()
  await expect(green).toHaveAttribute('aria-pressed', 'true')
  await expectButtonIcons(modal)
  await modal.getByRole('button', { name: 'No color', exact: true }).click()
  await expect(green).toHaveAttribute('aria-pressed', 'false')
  await expectButtonIcons(modal)
})

test('dashboard parameter dialogs include icons on their actions', async ({ page }) => {
  await page.goto('/dashboards/overview')
  await page.getByRole('button', { name: 'Parameters (0)', exact: true }).click()
  const modal = page.locator('.modal')
  await expect(modal.getByRole('button', { name: 'Add parameter' })).toBeVisible()
  await expectButtonIcons(modal)
  await modal.getByRole('button', { name: 'Add parameter' }).click()
  await expect(modal.getByRole('button', { name: 'Apply parameter' })).toBeDisabled()
  await expectButtonIcons(modal)
  await modal.getByLabel('Name', { exact: true }).fill('customer')
  await modal.getByRole('button', { name: 'Apply parameter' }).click()
  await expect(modal.getByRole('button', { name: 'Delete customer', exact: true })).toBeVisible()
  await expectButtonIcons(modal)
  await modal.getByRole('button', { name: 'Delete customer', exact: true }).click()
  await expect(modal.getByRole('button', { name: 'Delete parameter', exact: true })).toBeVisible()
  await expectButtonIcons(modal)
})

test('save icons remain visible while disabled and saving', async ({ page }) => {
  let release!: () => void
  const pending = new Promise<void>((resolve) => {
    release = resolve
  })
  await page.route('**/api/dashboards', async (route) => {
    if (route.request().method() === 'POST') {
      await pending
    }
    await route.fallback()
  })
  try {
    await page.goto('/dashboards')
    await page.getByRole('button', { name: 'New dashboard', exact: true }).click()
    const modal = page.locator('.modal')
    await expect(modal.getByRole('button', { name: 'Create dashboard', exact: true })).toBeDisabled()
    await expectButtonIcons(modal)
    await modal.getByLabel('Name', { exact: true }).fill('Icon test')
    await modal.getByRole('button', { name: 'Create dashboard', exact: true }).click()
    const saving = modal.getByRole('button', { name: 'Saving…', exact: true })
    await expect(saving).toBeDisabled()
    await expect(saving.locator('svg.lucide-save')).toBeVisible()
    await expectButtonIcons(modal)
  } finally {
    release()
  }
  await expect(page.getByRole('heading', { name: 'Icon test', exact: true })).toBeVisible()
})
