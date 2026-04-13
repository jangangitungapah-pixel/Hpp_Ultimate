const { chromium } = require('playwright');
(async() => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1600 } });
  await page.goto('http://127.0.0.1:5081/resep', { waitUntil: 'networkidle' });
  await page.waitForTimeout(1200);
  await page.getByRole('button', { name: 'Edit' }).first().click();
  await page.waitForTimeout(1200);
  const bodyText = await page.locator('body').innerText();
  console.log(bodyText.slice(0, 8000));
  const nodes = await page.locator('div,section,aside,form').evaluateAll(nodes => nodes.map((n,i)=>({i, cls:n.className, text:(n.innerText||'').trim().slice(0,120)})).filter(x => /resep|recipe|editor|modal|overlay|panel/i.test(x.cls || '')));
  console.log('\nNODES\n' + JSON.stringify(nodes.slice(0,120), null, 2));
  await browser.close();
})();
