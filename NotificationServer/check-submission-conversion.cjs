// Browser fixtures only; no requests or files are sent to the real school server.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {chromium} = require(process.env.PLAYWRIGHT_MODULE_PATH || 'playwright');
const source = fs.readFileSync(path.join(__dirname, '../Assets/Notifications/Resources/SmartleadSubmissionPrepare.txt'), 'utf8');
const fixture = '<!doctype html><html><meta charset="utf-8"><main id="region-main"><form><input type="file" accept=".hwp" multiple><button type="submit">Save</button></form></main><script>window.deliveries=[];window.saves=0;document.querySelector("input").addEventListener("change",async event=>{window.deliveries.push(await Promise.all(Array.from(event.target.files,file=>file.arrayBuffer().then(bytes=>({name:file.name,data:Array.from(new Uint8Array(bytes))})))));});document.querySelector("form").addEventListener("submit",event=>{event.preventDefault();window.saves++;});</script></html>';

async function main() {
  const browser = await chromium.launch({headless:true,channel:'msedge'});
  try {
    const page = await browser.newPage({viewport:{width:500,height:900}});
    // This fulfills every request locally, including the school-looking fixture URL.
    await page.route('**/*', route=>route.fulfill({contentType:'text/html; charset=utf-8',body:fixture}));
    async function setup(available=true) {
      await page.goto('https://smartlead.hallym.ac.kr/mod/assign/view.php?id=91&action=editsubmission');
      await page.evaluate(()=>{window.packets=[];window.Unity={call:packet=>window.packets.push(JSON.parse(packet))};});
      await page.addScriptTag({content:source.replace('__SUBMISSION_CONVERSION_SETTINGS__',JSON.stringify({nonce:'conversion-test-nonce',available}))});
    }
    const input = {name:'학번_이름_보고서.docx',mimeType:'application/vnd.openxmlformats-officedocument.wordprocessingml.document',buffer:Buffer.from([80,75,3,4,0,255,10,32])};
    const converted = Buffer.alloc(512);converted.set([208,207,17,224,161,177,26,225]);converted.write('HWP Document File',64);
    async function reply(error, nonce='conversion-test-nonce') {
      await page.waitForFunction(()=>window.packets.some(packet=>packet.operation==='finish'));
      await page.evaluate(({data,error,nonce})=>{
        const begin=window.packets.find(packet=>packet.operation==='begin');
        window.PixelCampusSubmission.receive(error?{nonce,requestId:begin.requestId,status:'error',error}:{nonce,requestId:begin.requestId,status:'chunk',index:0,total:1,data});
      }, {data:converted.toString('base64'),error,nonce});
    }
    await setup();
    await page.locator('#pixelcampus-target-format').selectOption('hwp');
    const [chooser]=await Promise.all([page.waitForEvent('filechooser'),page.locator('input[type=file]').click()]);
    assert.match(await page.locator('input[type=file]').getAttribute('accept'),/\.docx/);
    await chooser.setFiles(input);
    await page.waitForFunction(()=>window.packets.some(packet=>packet.operation==='finish'));
    assert.equal(await page.locator('input[type=file]').getAttribute('accept'),'.hwp');
    const packets=await page.evaluate(()=>window.packets);
    const original=Buffer.concat(packets.filter(packet=>packet.operation==='chunk').map(packet=>Buffer.from(packet.data,'base64')));
    assert.deepEqual(original,input.buffer);
    assert.ok(packets.every(packet=>packet.nonce==='conversion-test-nonce'&&packet.page.includes('/mod/assign/view.php')));
    assert.deepEqual(await page.evaluate(()=>window.deliveries),[]);
    await page.locator('form').evaluate(form=>form.requestSubmit());
    assert.equal(await page.evaluate(()=>window.saves),0);
    await reply(null,'stale-nonce');
    assert.equal(await page.getByRole('dialog').count(),0);
    await reply();
    await page.getByRole('dialog').waitFor();
    assert.deepEqual(await page.evaluate(()=>window.deliveries),[]);
    await page.getByRole('button',{name:'변환 파일 사용',exact:true}).click();
    await page.waitForFunction(()=>window.deliveries.length===1);
    const delivery=await page.evaluate(()=>window.deliveries[0]);
    assert.equal(delivery[0].name,'학번_이름_보고서.hwp');
    assert.deepEqual(Buffer.from(delivery[0].data),converted);
    console.log('PASS: Source bytes, nonce, accepted input filters, converted HWP bytes and Korean filename survive the handoff');
    console.log('PASS: Conversion, wrong-nonce replies and the confirmation dialog cannot prematurely upload or save');

    await setup();await page.locator('select').selectOption('hwpx');await page.locator('input').setInputFiles(input);
    await reply('Hancom conversion failed');
    await page.getByRole('status').filter({hasText:'Hancom conversion failed'}).waitFor();
    assert.deepEqual(await page.evaluate(()=>window.deliveries),[]);
    assert.equal(await page.locator('input').evaluate(input=>input.files.length),0);
    console.log('PASS: A failed conversion clears the selection instead of uploading the original');

    await setup();await page.locator('select').selectOption('hwp');await page.locator('input').setInputFiles(input);await reply();
    await page.getByRole('button',{name:'취소',exact:true}).click();
    assert.deepEqual(await page.evaluate(()=>window.deliveries),[]);
    assert.equal(await page.locator('input').evaluate(input=>input.files.length),0);
    console.log('PASS: Cancelling confirmation does not attach a file');

    await setup();await page.locator('select').selectOption('hwp');await page.locator('input').setInputFiles(input);
    await page.waitForFunction(()=>window.packets.some(packet=>packet.operation==='finish'));
    await page.getByRole('button',{name:'변환 취소',exact:true}).click();
    await page.getByRole('status').filter({hasText:'문서 변환을 취소했습니다.'}).waitFor();
    assert.deepEqual(await page.evaluate(()=>window.deliveries),[]);
    assert.ok(await page.evaluate(()=>window.packets.some(packet=>packet.operation==='cancel')));
    console.log('PASS: Cancelling a running conversion sends cancellation and never uploads the source');

    await setup();await page.locator('select').selectOption('hwp');
    const dropped=await page.evaluate(()=>{
      const transfer=new DataTransfer();transfer.items.add(new File(['document'],'source.docx'));
      const event=new DragEvent('drop',{bubbles:true,cancelable:true,dataTransfer:transfer});
      document.querySelector('main').dispatchEvent(event);return event.defaultPrevented;
    });
    assert.equal(dropped,true);assert.deepEqual(await page.evaluate(()=>window.deliveries),[]);
    console.log('PASS: Drag-and-drop cannot bypass the selected conversion');

    await setup();await page.locator('select').selectOption('hwp');
    await page.locator('input').setInputFiles({name:'scan.pdf',mimeType:'application/pdf',buffer:Buffer.from('%PDF-1.7')});
    await page.getByRole('status').filter({hasText:'PDF'}).waitFor();
    assert.deepEqual(await page.evaluate(()=>window.deliveries),[]);assert.deepEqual(await page.evaluate(()=>window.packets),[]);
    console.log('PASS: Unsupported PDF input is never relabeled or uploaded');

    await setup(false);
    assert.equal(await page.locator('option[value=hwp]').evaluate(option=>option.disabled),true);
    await page.locator('input').setInputFiles(input);
    await page.waitForFunction(()=>window.deliveries.length===1);
    assert.equal(await page.evaluate(()=>window.deliveries[0][0].name),input.name);
    console.log('PASS: Devices without Hancom retain normal original-file upload and clearly disable conversion');
  } finally {await browser.close();}
}
main().catch(error=>{console.error(error);process.exitCode=1;});
