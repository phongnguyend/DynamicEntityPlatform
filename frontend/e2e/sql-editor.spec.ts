import { test, expect, entityPath } from './fixtures'

for (const feature of ['metrics', 'reports/new']) {
  test(`${feature}: schema search, selection, insertion, undo and redo`, async ({ page }) => {
    await page.goto(`${entityPath}/${feature}`)
    await page.getByRole('button', { name: 'SQL', exact: true }).click()
    const input = page.getByRole('textbox', { name: 'SQL query', exact: true })
    await input.fill('SELECT ')
    await input.press('End')
    await page.locator('.sql-schema-browser > summary').click()
    await page.getByRole('button', { name: 'Company', exact: true }).click()
    await expect(input).toHaveValue('SELECT ')
    await page.getByRole('searchbox', { name: 'Search entities' }).fill('Customer')
    await expect(page.locator('.sql-schema-entities li')).toHaveCount(1)
    await page.getByRole('button', { name: 'Customer', exact: true }).click()
    await page.getByRole('searchbox', { name: 'Search columns' }).fill('FirstName')
    await expect(page.locator('.sql-schema-columns tbody tr')).toHaveCount(1)
    await page.getByRole('button', { name: 'Insert field FirstName', exact: true }).click()
    await expect(input).toHaveValue('SELECT [FirstName]')
    await input.press('ControlOrMeta+z')
    await expect(input).toHaveValue('SELECT ')
    await input.press('ControlOrMeta+Shift+z')
    await expect(input).toHaveValue('SELECT [FirstName]')
    await page.getByRole('button', { name: 'Insert table Customer', exact: true }).click()
    await expect(input).toHaveValue('SELECT [FirstName][Customer]')
    await input.press('ControlOrMeta+z')
    await expect(input).toHaveValue('SELECT [FirstName]')
  })

  test(`${feature}: raw names copy without editing SQL`, async ({ page, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'])
    await page.goto(`${entityPath}/${feature}`)
    await page.getByRole('button', { name: 'SQL', exact: true }).click()
    await page.locator('.sql-schema-browser > summary').click()
    for (const [kind, name] of [
      ['table', 'Customer'],
      ['column', 'FirstName'],
    ]) {
      await page.getByRole('button', { name: `Copy ${kind} name ${name}`, exact: true }).click()
      await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(name)
    }
    await expect(page.getByRole('textbox', { name: 'SQL query', exact: true })).toHaveValue('')
  })

  test(`${feature}: SQL parameters are discovered and validation submits the definition`, async ({ page, api }) => {
    await page.goto(`${entityPath}/${feature}`)
    await page.getByRole('button', { name: 'SQL', exact: true }).click()
    const sql = 'SELECT COUNT(*) FROM [Customer] WHERE [FirstName] = @customerName'
    await page.getByRole('textbox', { name: 'SQL query', exact: true }).fill(sql)
    await expect(page.getByRole('button', { name: '@customerName', exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Validate SQL', exact: true }).click()
    await expect(page.getByText('SQL is valid.', { exact: true })).toBeVisible()
    expect(api.requests.find((request) => request.path.endsWith('/sql'))?.body.sql).toMatchObject({
      enabled: true,
      text: sql,
      parameters: [{ parameter: 'customerName', type: 'Text' }],
    })
  })
}

for (const path of ['metrics', 'reports/new', 'views', 'views/view-customer']) {
  test(`${path}: SQL preview switches virtual and database names`, async ({ page }) => {
    await page.goto(`${entityPath}/${path}`)
    await page.getByRole('button', { name: 'View SQL', exact: true }).click()
    const dialog = page.locator('.modal')
    await expect(dialog.locator('pre').first()).toContainText('[Customer]')
    await dialog.getByRole('button', { name: 'Database names' }).click()
    await expect(dialog.locator('pre').first()).toContainText('[dbo].[Records]')
    await dialog.getByRole('button', { name: 'Virtual names' }).click()
    await expect(dialog.locator('pre').first()).toContainText('[Customer]')
  })
}

test('schema browser stacks in narrow layouts without overflow', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto(`${entityPath}/metrics`)
  await page.getByRole('button', { name: 'SQL', exact: true }).click()
  await page.locator('.sql-schema-browser > summary').click()
  const panes = page.locator('.sql-schema-pane')
  const first = await panes.nth(0).boundingBox()
  const second = await panes.nth(1).boundingBox()
  expect(second!.y).toBeGreaterThan(first!.y + first!.height)
  expect(
    await page.locator('.sql-schema-content').evaluate((element) => element.scrollWidth <= element.clientWidth),
  ).toBe(true)
})
