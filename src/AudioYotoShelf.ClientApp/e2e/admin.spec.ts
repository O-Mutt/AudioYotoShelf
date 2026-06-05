import { expect, test } from '@playwright/test'
import { mockBackend } from './helpers'

async function login(page: import('@playwright/test').Page, username: string) {
  await page.goto('/')
  await page.locator('input[type="url"]').fill('http://localhost:13378')
  await page.locator('input[type="text"]').first().fill(username)
  await page.locator('input[type="password"]').fill('password')
  await page.getByRole('button', { name: 'Connect', exact: true }).click()
  await page.getByRole('button', { name: /Continue/ }).click()
  await expect(page).toHaveURL(/\/library/)
}

test('the admin link and dashboard are hidden from non-admins', async ({ page }) => {
  await mockBackend(page, { isAdmin: false })

  await login(page, 'alice')

  await expect(page.getByRole('link', { name: 'Admin' })).toHaveCount(0)
  // The router guard bounces non-admins away from /admin.
  await page.goto('/admin')
  await expect(page).not.toHaveURL(/\/admin/)
})

test('an admin can open the usage dashboard', async ({ page }) => {
  await mockBackend(page, { isAdmin: true })

  await login(page, 'adminuser')

  const adminLink = page.getByRole('link', { name: 'Admin' })
  await expect(adminLink).toBeVisible()
  await adminLink.click()

  await expect(page).toHaveURL(/\/admin/)
  await expect(page.getByText('Total users')).toBeVisible()
  await expect(page.getByText('Transfer success')).toBeVisible()
})
