// Synthetic browser checks only: no school login, file upload or submission.
// Run with PLAYWRIGHT_MODULE_PATH pointing at Playwright, or install it locally.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const {createHash} = require('node:crypto');
const {chromium} = require(process.env.PLAYWRIGHT_MODULE_PATH || 'playwright');

async function main() {
  const root = path.resolve(__dirname, '..');
  const binary = fs.readFileSync(path.join(root, 'Assets/Plugins/UnityWebView/Android/WebViewPlugin.aar'));
  assert.equal(createHash('sha256').update(binary).digest('hex'), 'ed56d83f103f3700dbbc1f32a426a4fe2da7db658cd737a8f88501a6f68fc42f');
  console.log('PASS: Official file-input Android AAR hash');
  let html;
  // Reuse the collector/template fixtures without starting a local HTTP server.
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, 'check-assignment-document.cjs'), 'utf8'), {
    process: {cwd: () => root},
    require: name => name === 'node:http' ? {
      createServer: handler => {
        handler({}, {writeHead() {}, end(value) { html = value; }});
        return {listen() {}};
      }
    } : require(name)
  });
  const browser = await chromium.launch({headless: true, channel: 'msedge'});
  try {
    const page = await browser.newPage({viewport: {width: 500, height: 900}});
    await page.setContent(html);
    await page.waitForFunction(() => /^(PASS|FAIL)/.test(document.querySelector('#report').textContent));
    const report = await page.locator('#report').textContent();
    assert.ok(report.startsWith('PASS'), report);
    console.log(report);
    const frame = page.frames().find(candidate => candidate.parentFrame());
    await frame.evaluate(() => {
      window.testPackets = [];
      window.Unity = {call: message => window.testPackets.push(JSON.parse(message))};
    });
    await frame.locator('#assignment-submit').click();
    assert.deepEqual(await frame.evaluate(() => window.testPackets), [
      {kind: 'assignment_submission', nonce: 'preview-nonce', url: ''}
    ]);
    console.log('PASS: Upload entry sends the current document nonce without a caller-supplied destination');

    const uploads = [];
    const fixtures = [
      {name: 'project.zip', mimeType: 'application/zip', buffer: Buffer.from([0x50, 0x4b, 3, 4, 0, 1, 2, 255])},
      {name: '과제 설명.txt', mimeType: 'text/plain', buffer: Buffer.from('픽셀 캠퍼스 파일 업로드 확인', 'utf8')}
    ];
    const uploadPage = await browser.newPage();
    await uploadPage.route('https://upload-test.invalid/**', async route => {
      const request = route.request();
      if (request.method() === 'POST') {
        uploads.push({headers: request.headers(), body: request.postDataBuffer()});
        await route.fulfill({contentType: 'text/html; charset=utf-8', body: '<p id="saved">파일 저장 완료 (예시)</p>'});
      } else {
        await route.fulfill({contentType: 'text/html; charset=utf-8', body: '<form method="post" action="/save" enctype="multipart/form-data"><input type="file" name="submission_files[]" multiple><button type="submit">저장</button></form>'});
      }
    });
    await uploadPage.goto('https://upload-test.invalid/assignment');
    await uploadPage.locator('input[type=file]').setInputFiles(fixtures);
    assert.equal(uploads.length, 0, 'Selecting files must not automatically submit');
    await Promise.all([uploadPage.waitForURL('**/save', {timeout: 10000}), uploadPage.getByRole('button', {name: '저장', exact: true}).click()]);
    assert.equal(uploads.length, 1);
    assert.match(uploads[0].headers['content-type'], /^multipart\/form-data; boundary=/);
    for (const fixture of fixtures) {
      assert.ok(uploads[0].body.includes(Buffer.from(fixture.name)), 'Filename preserved');
      assert.ok(uploads[0].body.includes(fixture.buffer), 'File bytes preserved');
    }
    assert.equal(await uploadPage.locator('#saved').textContent(), '파일 저장 완료 (예시)');
    console.log('PASS: Multiple files, Korean filenames and binary bytes reach a synthetic multipart endpoint only after Save');
  } finally {
    await browser.close();
  }
}
main().catch(error => {console.error(error); process.exitCode = 1;});
