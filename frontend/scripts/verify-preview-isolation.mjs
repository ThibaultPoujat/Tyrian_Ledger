import { readdir, readFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';

const outputDirectory = resolve(new URL('../dist/', import.meta.url).pathname);
const topLevel = await readdir(outputDirectory, { withFileTypes: true });
if (topLevel.some((entry) => entry.isFile() && entry.name === 'preview.html')) {
  throw new Error('The fixture preview entry must not be included in the production build.');
}

async function collectJavaScript(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const paths = [];
  for (const entry of entries) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) paths.push(...await collectJavaScript(path));
    else if (entry.isFile() && entry.name.endsWith('.js')) paths.push(path);
  }
  return paths;
}

const bundles = await collectJavaScript(outputDirectory);
for (const bundlePath of bundles) {
  const content = await readFile(bundlePath, 'utf8');
  if (content.includes('p05a-preview-root') || content.includes('Démonstration · données fictives')) {
    throw new Error('Fixture-only preview code leaked into the production frontend bundle.');
  }
}
console.log('Production frontend excludes the P05A fixture preview.');

