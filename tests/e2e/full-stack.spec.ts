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
  test(`${name}: signed-in page uses the identity provider and renders JSON-backed workflows & layouts with rich filters`, async ({ page }) => {
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

    // 1. Workflow list: customer-onboarding (text + dropdowns + numeric >=/<= + date range)
    await page.goto(`${base}/workflow/customer-onboarding`);
    await expect(page.locator('.middt-wf-layout')).toBeVisible();
    await expect(page.locator('#wf-filter-fullName')).toBeVisible();
    await expect(page.getByLabel('Segment filtrele')).toBeVisible();
    await expect(page.getByLabel('Risk Seviyesi filtrele')).toBeVisible();
    await expect(page.locator('#wf-filter-monthlyIncome_min')).toBeVisible();
    await expect(page.locator('#wf-filter-monthlyIncome_max')).toBeVisible();
    await expect(page.locator('#wf-filter-createdFrom')).toBeVisible();
    await expect(page.locator('#wf-filter-createdTo')).toBeVisible();

    // 2. Workflow list: credit-allocation (text + numeric >=/<= + date range + start dialog)
    await page.goto(`${base}/workflow/credit-allocation?start=1`);
    await expect(page.getByRole('dialog', { name: /Yeni başlat/ })).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: 'Kapat' }).click();
    await expect(page.locator('#wf-filter-customerKey')).toBeVisible();
    await expect(page.locator('#wf-filter-amount_min')).toBeVisible();
    await expect(page.locator('#wf-filter-amount_max')).toBeVisible();
    await expect(page.locator('#wf-filter-termMonths_min')).toBeVisible();
    await expect(page.locator('#wf-filter-termMonths_max')).toBeVisible();
    await expect(page.locator('#wf-filter-createdFrom')).toBeVisible();
    await expect(page.locator('#wf-filter-createdTo')).toBeVisible();

    // 3. Workflow list: order-payment-orchestration, risk-limit-eval, sla-escalation
    await page.goto(`${base}/workflow/order-payment-orchestration`);
    await expect(page.locator('#wf-filter-customerName')).toBeVisible();
    await expect(page.getByLabel('Ödeme Yöntemi filtrele')).toBeVisible();
    await expect(page.locator('#wf-filter-amount_min')).toBeVisible();
    await expect(page.locator('#wf-filter-createdFrom')).toBeVisible();

    await page.goto(`${base}/workflow/risk-limit-eval`);
    await expect(page.locator('#wf-filter-customerKey')).toBeVisible();
    await expect(page.getByLabel('Kontrol Tipi filtrele')).toBeVisible();
    await expect(page.locator('#wf-filter-amount_min')).toBeVisible();
    await expect(page.locator('#wf-filter-riskScore_min')).toBeVisible();

    await page.goto(`${base}/workflow/sla-escalation`);
    await expect(page.locator('#wf-filter-ticketTitle')).toBeVisible();
    await expect(page.getByLabel('Öncelik Seviyesi filtrele')).toBeVisible();
    await expect(page.getByLabel('Atanan Ekip filtrele')).toBeVisible();
    await expect(page.locator('#wf-filter-amount_min')).toBeVisible();

    // 4. Layout grids: customer-directory, credit-pipeline, order-monitor
    await page.goto(`${base}/ui/customer-directory`);
    await expect(page.locator('.middt-ui-grid')).toBeVisible();
    await expect(page.locator('[data-field="fullName"]')).toBeVisible();
    await expect(page.locator('[data-field="segment"]')).toBeVisible();
    await expect(page.locator('[data-field="riskLevel"]')).toBeVisible();
    await expect(page.locator('[data-field="monthlyIncome_min"]')).toBeVisible();
    await expect(page.locator('[data-field="monthlyIncome_max"]')).toBeVisible();
    await expect(page.locator('[data-field="createdAt_from"]')).toBeVisible();
    await expect(page.locator('[data-field="createdAt_to"]')).toBeVisible();

    await page.goto(`${base}/ui/credit-pipeline`);
    await expect(page.locator('[data-field="customerKey"]')).toBeVisible();
    await expect(page.locator('[data-field="state"]')).toBeVisible();
    await expect(page.locator('[data-field="amount_min"]')).toBeVisible();
    await expect(page.locator('[data-field="termMonths_min"]')).toBeVisible();
    await expect(page.locator('[data-field="createdAt_from"]')).toBeVisible();

    await page.goto(`${base}/ui/order-monitor`);
    await expect(page.locator('[data-field="customerName"]')).toBeVisible();
    await expect(page.locator('[data-field="paymentMethod"]')).toBeVisible();
    await expect(page.locator('[data-field="amount_min"]')).toBeVisible();
    await expect(page.locator('[data-field="createdAt_from"]')).toBeVisible();

    await page.goto(`${base}/ui-admin`);
    await expect(page.locator('#ui-layout-json')).toBeVisible();
  });

  if (name === 'Angular') {
    test(`${name}: anonymous API call does not redirect to login`, async ({ request }) => {
      const response = await request.get(`${base}/api/v1/CustomerOnboarding/getall`, { maxRedirects: 0, headers: { 'X-CSRF': '1' } });
      expect(response.status()).toBe(401);
    });
  }
}

test('PostgreSQL backed identity admin loads and searches users', async ({ page }) => {
  await page.goto(`${identity}/admin/users`);
  await signIn(page);
  await expect(page).toHaveURL(/\/admin\/users/, { timeout: 30_000 });
  await expect(page.getByText('test@test.com').first()).toBeVisible();
  await page.getByRole('textbox', { name: 'Kullanıcı ara' }).fill('user@test.com');
  await page.getByRole('button', { name: 'Ara' }).click();
  await expect(page.getByText('user@test.com').first()).toBeVisible();
});

test('JSON-backed workflows execute end-to-end with ETag/304, task trace, SubFlow forwarding, $self, and FanOut', async ({ page }) => {
  await page.goto(angular);
  await signIn(page);
  const approve = page.getByRole('button', { name: 'İzin ver' });
  if (await approve.isVisible()) await approve.click();
  await expect.poll(() => new URL(page.url()).origin, { timeout: 30_000 }).toBe(angular);

  // 1. Start customer-onboarding (stored in WorkflowInstanceData.Data JSONB)
  const tcNo = String(10000000000 + Math.floor(Math.random() * 89999999999));
  const custStart = await page.request.post(`${angular}/api/workflows/customer-onboarding/instances`, {
    headers: { 'X-CSRF': '1' },
    data: {
      data: {
        fullName: 'E2E JSON Müşteri',
        identityNumber: tcNo,
        email: 'e2e@json.com',
        phone: '05321112233',
        segment: 'Bireysel',
        monthlyIncome: 75000,
      },
    },
  });
  expect(custStart.ok()).toBeTruthy();
  const custInstance = (await custStart.json()).data;
  expect(custInstance.state).toBe('active');
  expect(custInstance.key).toBe(`CUST-${tcNo}`);

  // 2. Query WorkflowDataController (/api/v1/CustomerOnboarding/getall) backed purely by WorkflowDbContext JSON
  const custListRes = await page.request.get(`${angular}/api/v1/CustomerOnboarding/getall`, {
    headers: { 'X-CSRF': '1' },
  });
  expect(custListRes.ok()).toBeTruthy();
  const custList = (await custListRes.json()).data as Array<Record<string, unknown>>;
  expect(custList.some((c) => c.key === `CUST-${tcNo}` && c.fullName === 'E2E JSON Müşteri')).toBe(true);

  // 2b. Verify PostgreSQL JSONB + SQL-pushed filters (text, dropdown, numeric range, date range)
  const wfFilterRes = await page.request.get(
    `${angular}/api/workflows/customer-onboarding/instances?key=CUST-${tcNo}&data.segment=Bireysel&data.riskLevel=D%C3%BC%C5%9F%C3%BCk&data.monthlyIncome_min=50000&data.monthlyIncome_max=100000&data.createdFrom=2025-01-01&data.createdTo=2030-12-31`,
    { headers: { 'X-CSRF': '1' } },
  );
  expect(wfFilterRes.ok()).toBeTruthy();
  const wfFiltered = await wfFilterRes.json();
  expect(wfFiltered.count).toBe(1);
  expect(wfFiltered.data[0].key).toBe(`CUST-${tcNo}`);

  const gridFilterRes = await page.request.post(`${angular}/api/v1/CustomerOnboarding/getitems`, {
    headers: { 'X-CSRF': '1' },
    data: {
      currentPage: 1,
      requestItemSize: 20,
      requestModel: {
        fullName: 'E2E JSON',
        segment: 'Bireysel',
        riskLevel: 'Düşük',
        monthlyIncome_min: 50000,
        monthlyIncome_max: 100000,
        createdAt_from: '2025-01-01',
        createdAt_to: '2030-12-31',
      },
    },
  });
  expect(gridFilterRes.ok()).toBeTruthy();
  const gridFiltered = await gridFilterRes.json();
  expect(gridFiltered.count).toBeGreaterThanOrEqual(1);
  expect((gridFiltered.data as Array<Record<string, unknown>>).some((c) => c.key === `CUST-${tcNo}`)).toBe(true);

  // 3. ETag & 304 Not Modified + Task-level execution trace on customer-onboarding
  const getRes = await page.request.get(`${angular}/api/workflows/customer-onboarding/instances/${custInstance.id}`, {
    headers: { 'X-CSRF': '1' },
  });
  expect(getRes.status()).toBe(200);
  const etag = getRes.headers()['etag'];
  expect(etag).toBeTruthy();

  const notModifiedRes = await page.request.get(`${angular}/api/workflows/customer-onboarding/instances/${custInstance.id}`, {
    headers: { 'X-CSRF': '1', 'If-None-Match': etag },
  });
  expect(notModifiedRes.status()).toBe(304);

  const historyRes = await page.request.get(`${angular}/api/workflows/customer-onboarding/instances/${custInstance.id}/history`, {
    headers: { 'X-CSRF': '1' },
  });
  expect(historyRes.ok()).toBeTruthy();
  const historyBody = await historyRes.json();
  expect(Array.isArray(historyBody.data)).toBe(true);
  expect(historyBody.data[0].tasks.length).toBeGreaterThanOrEqual(4);
  expect(historyBody.data[0].tasks.map((t: { taskType: string }) => t.taskType)).toContain('cache');

  // 4. Start credit-allocation (loadWorkflow + risk-limit-eval SubFlow + $self update-collateral + approve-stage1 + executive-override)
  const creditStart = await page.request.post(`${angular}/api/workflows/credit-allocation/instances`, {
    headers: { 'X-CSRF': '1' },
    data: {
      data: {
        customerKey: `CUST-${tcNo}`,
        amount: 350000,
        termMonths: 24,
        collateralAmount: 500000,
        iban: 'TR330006100519786457841326',
        requireUnderwriting: false,
      },
    },
  });
  expect(creditStart.ok()).toBeTruthy();
  let creditInstance = (await creditStart.json()).data;
  expect(creditInstance.state).toBe('stage1-approval');
  expect(creditInstance.data.customerName).toBe('E2E JSON Müşteri');

  const updateCollateralRes = await page.request.post(
    `${angular}/api/workflows/credit-allocation/instances/${creditInstance.id}/transitions/update-collateral`,
    {
      headers: { 'X-CSRF': '1' },
      data: { collateralAmount: 700000 },
    },
  );
  expect(updateCollateralRes.ok()).toBeTruthy();
  creditInstance = (await updateCollateralRes.json()).data;
  expect(creditInstance.state).toBe('stage1-approval');
  expect(creditInstance.data.ltvRatio).toBe(50);

  const stage1Res = await page.request.post(
    `${angular}/api/workflows/credit-allocation/instances/${creditInstance.id}/transitions/approve-stage1`,
    {
      headers: { 'X-CSRF': '1' },
      data: { stage1Comment: 'E2E 1. aşama onayı' },
    },
  );
  expect(stage1Res.ok()).toBeTruthy();
  creditInstance = (await stage1Res.json()).data;
  expect(creditInstance.state).toBe('stage2-approval');

  const overrideRes = await page.request.post(
    `${angular}/api/workflows/credit-allocation/instances/${creditInstance.id}/transitions/executive-override`,
    {
      headers: { 'X-CSRF': '1' },
      data: { overrideReason: 'E2E üst komite onayı' },
    },
  );
  expect(overrideRes.ok()).toBeTruthy();
  creditInstance = (await overrideRes.json()).data;
  expect(creditInstance.state).toBe('disbursed');

  // Verify graphical workflow flow & traversed path diagram in the UI
  await page.goto(`${angular}/workflow/credit-allocation`);
  await page.locator(`.middt-wf-grid tr:has-text("CUST-${tcNo}")`).first().click();
  await expect(page.locator('.middt-wf-diagram')).toBeVisible();
  await expect(page.locator('.middt-wf-diagram .middt-wf-node-current')).toHaveCount(1);
  await expect(page.locator('.middt-wf-diagram .middt-wf-node-visited').first()).toBeVisible();
  await expect(page.locator('.middt-wf-diagram .middt-wf-edge-traversed').first()).toBeVisible();

  // 5. Start order-payment-orchestration (fanOut parallel checks + HTTP gateway + cache + RabbitMQ publish)
  const orderCode = `E2E-${Date.now()}`;
  const orderStart = await page.request.post(`${angular}/api/workflows/order-payment-orchestration/instances`, {
    headers: { 'X-CSRF': '1' },
    data: {
      data: {
        orderCode,
        customerName: 'E2E JSON Müşteri',
        amount: 18500,
        paymentMethod: 'Kredi Kartı',
      },
    },
  });
  expect(orderStart.ok()).toBeTruthy();
  const orderInstance = (await orderStart.json()).data;
  expect(orderInstance.state).toBe('completed');
  expect(orderInstance.key).toBe(`ORD-${orderCode}`);

  // 6. Start sla-escalation (automatic chaining triage -> priority-routing -> waiting-agent, $self heartbeat-ping, resolve)
  const slaStart = await page.request.post(`${angular}/api/workflows/sla-escalation/instances`, {
    headers: { 'X-CSRF': '1' },
    data: {
      data: {
        ticketTitle: 'E2E SLA Test Çağrısı',
        customerName: 'E2E JSON Müşteri',
        amount: 45000,
        severity: 'Yüksek',
        slaDuration: '00:05:00',
      },
    },
  });
  expect(slaStart.ok()).toBeTruthy();
  let slaInstance = (await slaStart.json()).data;
  expect(slaInstance.state).toBe('waiting-agent');

  const heartbeatRes = await page.request.post(
    `${angular}/api/workflows/sla-escalation/instances/${slaInstance.id}/transitions/heartbeat-ping`,
    {
      headers: { 'X-CSRF': '1' },
      data: { progressNote: 'E2E ilerleme kaydı' },
    },
  );
  expect(heartbeatRes.ok()).toBeTruthy();
  slaInstance = (await heartbeatRes.json()).data;
  expect(slaInstance.data.heartbeatCount).toBe(1);

  const resolveRes = await page.request.post(
    `${angular}/api/workflows/sla-escalation/instances/${slaInstance.id}/transitions/resolve`,
    {
      headers: { 'X-CSRF': '1' },
      data: { resolutionNote: 'E2E çözümlendi' },
    },
  );
  expect(resolveRes.ok()).toBeTruthy();
  slaInstance = (await resolveRes.json()).data;
  expect(slaInstance.state).toBe('resolved');

  // 7. Start risk-limit-eval directly
  const riskStart = await page.request.post(`${angular}/api/workflows/risk-limit-eval/instances`, {
    headers: { 'X-CSRF': '1' },
    data: {
      data: {
        customerKey: `CUST-${tcNo}`,
        amount: 250000,
        checkType: 'E2E Doğrudan Risk Kontrolü',
        requireUnderwriting: false,
      },
    },
  });
  expect(riskStart.ok()).toBeTruthy();
  const riskInstance = (await riskStart.json()).data;
  expect(riskInstance.state).toBe('approved');
});
