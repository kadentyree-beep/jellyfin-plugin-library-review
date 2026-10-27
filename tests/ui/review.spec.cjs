const { test, expect } = require('@playwright/test');
const fs = require('node:fs');
const path = require('node:path');
test('review is read-only until separate exact-title confirmation; labels are escaped', async ({ page }) => {
    const root = path.resolve(__dirname, '../../src/LibraryReview/Web');
    await page.setContent(fs.readFileSync(path.join(root, 'review.html'), 'utf8'));
    await page.evaluate(() => {
        window.calls = [];
        window.state = { lastScan: new Date().toISOString(), items: [{ id: '11111111-1111-1111-1111-111111111111', title: '<script>Example</script>', actualMinutes: 190, referenceSource: 'Fixture', file: { path: '/movies/example.mkv' }, status: 'open', findings: [{ severity: 'review', message: 'Runtime mismatch' }] }], actions: [] };
        window.ApiClient = {
            getUrl: p => p, accessToken: () => 'fixture', getPluginConfiguration: async () => ({}),
            ajax: async o => {
                window.calls.push(o);
                if (o.type === 'GET') return window.state;
                if (o.url.endsWith('/Decision')) window.state.items[0].status = JSON.parse(o.data).status;
                if (o.url.endsWith('/Preview')) return { token: 'fixture-token', itemId: window.state.items[0].id, title: window.state.items[0].title, path: '/movies/example.mkv', size: 4, sha256: 'fixture-hash', release: 'fixture-release', expires: new Date(Date.now() + 600000).toISOString() };
                return 'OK';
            }
        };
        window.define = (deps, factory) => { window.controller = factory({}, () => {}); };
    });
    await page.addScriptTag({ content: fs.readFileSync(path.join(root, 'review.js'), 'utf8') });
    await page.evaluate(() => { const view = document.querySelector('#libraryReviewPage'); window.controller(view); view.dispatchEvent(new Event('viewshow')); });
    await expect(page.locator('#lrItems script')).toHaveCount(0);
    expect(await page.evaluate(() => window.calls.filter(c => c.type === 'POST').length)).toBe(0);
    await page.getByRole('button', { name: 'Mark as a problem', exact: true }).click();
    await page.getByRole('button', { name: 'Block release and find replacement…' }).click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await expect(page.locator('#lrConfirmDelete')).toBeDisabled();
    await page.locator('#lrConfirmTitle').fill('wrong');
    await expect(page.locator('#lrConfirmDelete')).toBeDisabled();
    expect(await page.evaluate(() => window.calls.some(c => c.url.endsWith('/Replace')))).toBe(false);
    await page.locator('#lrConfirmTitle').fill('<script>Example</script>');
    await page.locator('#lrConfirmDelete').click();
    expect(await page.evaluate(() => window.calls.filter(c => c.url.endsWith('/Replace')).length)).toBe(1);
});
