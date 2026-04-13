const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

(async () => {
  const outDir = 'C:/Users/hazel/source/repos/Hpp_Ultimate/output/playwright';
  fs.mkdirSync(outDir, { recursive: true });
  const pages = [
    ['produk','produk'],
    ['gudang','gudang'],
    ['resep','resep'],
    ['produksi','produksi'],
    ['belanja','belanja'],
    ['pos','pos'],
    ['pembukuan','pembukuan'],
    ['pengaturan','pengaturan']
  ];
  async function login(page) {
    await page.goto('http://127.0.0.1:5081/login', { waitUntil: 'networkidle' });
    const inputs = page.locator('input');
    await inputs.nth(0).fill('admin');
    await inputs.nth(1).fill('admin');
    await page.getByRole('button', { name: /masuk/i }).click();
    await page.waitForLoadState('networkidle');
  }
  const browser = await chromium.launch({ headless: true });
  const desktop = await browser.newContext({ viewport: { width: 1440, height: 1024 } });
  const desktopPage = await desktop.newPage();
  await login(desktopPage);
  for (const [route, name] of pages) {
    await desktopPage.goto(`http://127.0.0.1:5081/${route}`, { waitUntil: 'networkidle' });
    await desktopPage.screenshot({ path: path.join(outDir, `${name}-spacing-desktop.png`), fullPage: true });
  }
  const mobile = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  const mobilePage = await mobile.newPage();
  await login(mobilePage);
  for (const [route, name] of pages) {
    await mobilePage.goto(`http://127.0.0.1:5081/${route}`, { waitUntil: 'networkidle' });
    await mobilePage.screenshot({ path: path.join(outDir, `${name}-spacing-mobile.png`), fullPage: true });
  }
  await browser.close();
  console.log('captured');
})();
