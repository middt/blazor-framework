import { expect, test, type Page } from '@playwright/test';

const blazor = process.env.BLAZOR_BASE ?? 'http://localhost:8080';
const angular = process.env.ANGULAR_BASE ?? 'http://localhost:8081';
const identity = process.env.IDENTITY_BASE ?? 'http://localhost:8083';

async function signIn(page: Page): Promise<void> {
  await expect(page.locator('#Input_Username')).toBeVisible();
  await page.locator('#Input_Username').fill('test@test.com');
  await page.locator('#Input_Password').fill('test');
  await page.locator('button[type="submit"]').first().click();
}

for (const [name, base] of [['Blazor', blazor], ['Angular', angular]] as const) {
  test(`${name}: signed-in page uses the identity provider`, async ({ page }) => {
    await page.goto(base);
    await signIn(page);
    const approve = page.getByRole('button', { name: 'İzin ver' });
    if (await approve.isVisible()) await approve.click();
    await expect.poll(() => new URL(page.url()).origin, { timeout: 30_000 }).toBe(base);
    if (name === 'Angular') {
      const user = await page.request.get(`${base}/bff/user`, { headers: { 'X-CSRF': '1' } });
      expect(user.ok()).toBeTruthy();
      expect((await user.json()).isAuthenticated).toBe(true);
    }
    await page.goto(`${base}/workflow/order-approval`);
    await expect(page.locator('.middt-wf-layout')).toBeVisible();
    await page.goto(`${base}/workflow/order-approval?start=1`);
    await expect(page.getByRole('dialog', { name: /Yeni başlat/ })).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: 'Kapat' }).click();
    await page.goto(`${base}/ui-admin`);
    await expect(page.locator('#ui-layout-json')).toBeVisible();
  });

  if (name === 'Angular') {
    test(`${name}: anonymous API call does not redirect to login`, async ({ request }) => {
      const response = await request.get(`${base}/api/v1/Product/getall`, { maxRedirects: 0, headers: { 'X-CSRF': '1' } });
      expect(response.status()).toBe(401);
    });
  }
}

test('SQL Server backed identity admin loads and searches users', async ({ page }) => {
  await page.goto(`${identity}/admin/users`);
  await signIn(page);
  await expect(page).toHaveURL(/\/admin\/users/, { timeout: 30_000 });
  await expect(page.getByText('test@test.com').first()).toBeVisible();
  await page.getByRole('textbox', { name: 'Kullanıcı ara' }).fill('user@test.com');
  await page.getByRole('button', { name: 'Ara' }).click();
  await expect(page.getByText('user@test.com').first()).toBeVisible();
});
