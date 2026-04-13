const { chromium } = require('playwright');
(async() => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1400 } });
  await page.goto('http://127.0.0.1:5081/resep', { waitUntil: 'networkidle' });
  await page.waitForTimeout(1500);
  const text = await page.locator('body').innerText();
  console.log(text.slice(0, 6000));
  await browser.close();
})();
