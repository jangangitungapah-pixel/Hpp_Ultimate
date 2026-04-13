const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
(async() => {
  const outDir = 'C:/Users/hazel/source/repos/Hpp_Ultimate/output/playwright';
  const browser = await chromium.launch({ headless: true });

  async function capture(name, viewport) {
    const page = await browser.newPage({ viewport });
    await page.goto('http://127.0.0.1:5081/resep', { waitUntil: 'networkidle' });
    await page.waitForTimeout(1200);
    await page.getByRole('button', { name: 'Edit' }).first().click();
    await page.waitForTimeout(1200);
    const modal = page.locator('.recipe-form-modal');
    await modal.waitFor();
    const box = await modal.boundingBox();
    await page.screenshot({ path: path.join(outDir, `${name}-top.png`) });
    const scrollInfo = await modal.evaluate(el => ({ scrollHeight: el.scrollHeight, clientHeight: el.clientHeight }));
    const positions = [0, Math.max(0, Math.floor((scrollInfo.scrollHeight - scrollInfo.clientHeight) * 0.45)), Math.max(0, scrollInfo.scrollHeight - scrollInfo.clientHeight)];
    const labels = ['top','mid','bottom'];
    for (let i = 0; i < positions.length; i++) {
      await modal.evaluate((el, pos) => { el.scrollTo({ top: pos, behavior: 'instant' }); }, positions[i]);
      await page.waitForTimeout(400);
      await page.screenshot({ path: path.join(outDir, `${name}-${labels[i]}.png`) });
    }
    const text = await page.locator('body').innerText();
    fs.writeFileSync(path.join(outDir, `${name}.txt`), text);
    await page.close();
  }

  await capture('resep-form-audit-desktop', { width: 1440, height: 1600 });
  await capture('resep-form-audit-mobile', { width: 390, height: 844 });
  await browser.close();
})();
