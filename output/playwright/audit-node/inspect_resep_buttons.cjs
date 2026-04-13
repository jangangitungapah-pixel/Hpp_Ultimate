const { chromium } = require('playwright');
(async() => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1400 } });
  await page.goto('http://127.0.0.1:5081/resep', { waitUntil: 'networkidle' });
  await page.waitForTimeout(1500);
  const buttons = await page.locator('button, [role="button"]').evaluateAll(nodes => nodes.map((n, i) => ({i, text:(n.innerText||n.textContent||'').trim(), aria:n.getAttribute('aria-label'), title:n.getAttribute('title'), cls:n.className})).filter(x => x.text || x.aria || x.title));
  console.log(JSON.stringify(buttons.slice(0,120), null, 2));
  await browser.close();
})();
