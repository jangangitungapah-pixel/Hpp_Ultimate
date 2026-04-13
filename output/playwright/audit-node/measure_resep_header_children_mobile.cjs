const { chromium } = require('playwright');
(async() => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  await page.goto('http://127.0.0.1:5081/resep', { waitUntil: 'networkidle' });
  await page.waitForTimeout(1200);
  await page.getByRole('button', { name: 'Edit' }).first().click();
  await page.waitForTimeout(1200);
  const info = await page.evaluate(() => {
    const selectors = [
      '.studio-head-kicker .section-tag',
      '.studio-head-badges',
      '.studio-head-badge.subtle',
      '.studio-head-badge.active',
      '.studio-head-mainline > div:first-child',
      '.studio-head-mainline h2',
      '.studio-head-mainline p'
    ];
    return selectors.map(s => {
      const el = document.querySelector(s);
      if (!el) return { s, missing: true };
      const r = el.getBoundingClientRect();
      const cs = getComputedStyle(el);
      return { s, text:(el.textContent||'').trim().slice(0,120), width:r.width, height:r.height, display:cs.display, position:cs.position, flexDirection:cs.flexDirection, justify:cs.justifyContent, align:cs.alignItems, marginTop:cs.marginTop, lineHeight:cs.lineHeight };
    });
  });
  console.log(JSON.stringify(info, null, 2));
  await browser.close();
})();
