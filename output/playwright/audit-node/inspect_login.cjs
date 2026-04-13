const { chromium } = require('playwright');
(async () => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1024 } });
  await page.goto('http://127.0.0.1:5081/login', { waitUntil: 'networkidle' });
  const inputs = await page.locator('input').evaluateAll(nodes => nodes.map((n, i) => ({ i, type: n.type, id: n.id, cls: n.className, placeholder: n.getAttribute('placeholder'), visible: !!(n.offsetWidth || n.offsetHeight || n.getClientRects().length) })));
  console.log(JSON.stringify(inputs, null, 2));
  console.log((await page.locator('body').innerText()).slice(0, 600));
  await browser.close();
})();
