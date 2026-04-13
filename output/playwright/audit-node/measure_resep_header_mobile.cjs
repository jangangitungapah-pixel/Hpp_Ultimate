const { chromium } = require('playwright');
(async() => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  await page.goto('http://127.0.0.1:5081/resep', { waitUntil: 'networkidle' });
  await page.waitForTimeout(1200);
  await page.getByRole('button', { name: 'Edit' }).first().click();
  await page.waitForTimeout(1200);
  const info = await page.evaluate(() => {
    const sel = s => document.querySelector(s);
    const pick = s => {
      const el = sel(s);
      if (!el) return null;
      const r = el.getBoundingClientRect();
      const cs = getComputedStyle(el);
      return { s, top:r.top, left:r.left, width:r.width, height:r.height, display:cs.display, position:cs.position, justify:cs.justifyContent, align:cs.alignItems, padTop:cs.paddingTop, padBottom:cs.paddingBottom, gap:cs.gap, marginTop:cs.marginTop };
    };
    return [
      pick('.recipe-form-modal'),
      pick('.studio-head'),
      pick('.studio-head-copy'),
      pick('.studio-head-kicker'),
      pick('.studio-head-mainline'),
      pick('.studio-head-glance'),
      pick('.recipe-close-button')
    ];
  });
  console.log(JSON.stringify(info, null, 2));
  await browser.close();
})();
