import test from 'node:test';
import assert from 'node:assert/strict';
import {readdir, readFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

test('Unity asset metadata contains valid 32-character GUIDs, including the assignment HTML template', async () => {
  const root = fileURLToPath(new URL('../Assets/', import.meta.url));
  let checked = 0;
  async function scan(directory) {
    for (const entry of await readdir(directory, {withFileTypes: true})) {
      const filename = path.join(directory, entry.name);
      if (entry.isDirectory()) await scan(filename);
      else if (entry.name.endsWith('.meta')) {
        const contents = await readFile(filename, 'utf8');
        const guid = contents.match(/^guid: (\S+)/m)?.[1];
        assert.match(guid || '', /^[a-f0-9]{32}$/, `Unity ignores assets with malformed GUIDs: ${filename}`);
        checked++;
      }
    }
  }
  await scan(root);
  assert.ok(checked > 0);
  const metadata = await readFile(path.join(root, 'Notifications/Resources/AssignmentDetail.html.meta'), 'utf8');
  assert.match(metadata, /^TextScriptImporter:/m);
});
