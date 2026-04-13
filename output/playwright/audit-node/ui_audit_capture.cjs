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
  const browser = await chromium.launch({ headless: true });

  async function capture(context, suffix) {
    const page = await context.newPage();
    page.setDefaultTimeout(45000);
    for (const [route, name] of pages) {
      await page.goto(`http://127.0.0.1:5081/${route}`, { waitUntil: 'domcontentloaded', timeout: 45000 });
      await page.waitForTimeout(2500);
      await page.screenshot({ path: path.join(outDir, `${name}-spacing-${suffix}.png`) });
    }
    await page.close();
  }

  await capture(await browser.newContext({ viewport: { width: 1440, height: 1024 } }), 'desktop');
  await capture(await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true }), 'mobile');

  await browser.close();
  console.log('captured');
})();
