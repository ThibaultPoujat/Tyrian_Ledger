import { cp, copyFile, mkdir, stat } from 'node:fs/promises';
import { dirname, join } from 'node:path';

async function assertDirectory(path, description) {
  const information = await stat(path);
  if (!information.isDirectory()) throw new Error(`${description} must be a directory.`);
}

async function assertFile(path, description) {
  const information = await stat(path);
  if (!information.isFile()) throw new Error(`${description} must be a regular file.`);
}

export async function assemblePagesArtifact({ distDirectory, outputDirectory, snapshotPath }) {
  await assertDirectory(distDirectory, 'The React distribution directory');
  await assertFile(snapshotPath, 'The generated market snapshot');
  await mkdir(dirname(outputDirectory), { recursive: true });
  await cp(distDirectory, outputDirectory, { errorOnExist: true, force: false, recursive: true });
  await copyFile(snapshotPath, join(outputDirectory, 'market-snapshot.json'));
}

function parseArguments(argumentsList) {
  const values = new Map();
  for (let index = 0; index < argumentsList.length; index += 2) {
    const name = argumentsList[index];
    const value = argumentsList[index + 1];
    if (!['--dist', '--output', '--snapshot'].includes(name) || value === undefined || values.has(name)) {
      throw new Error('Usage: assemble-pages-artifact.mjs --dist <vite-dist-directory> --snapshot <market-snapshot.json> --output <artifact-directory>');
    }
    values.set(name, value);
  }

  if (values.size !== 3) {
    throw new Error('Usage: assemble-pages-artifact.mjs --dist <vite-dist-directory> --snapshot <market-snapshot.json> --output <artifact-directory>');
  }

  return Object.fromEntries(values);
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const argumentsObject = parseArguments(process.argv.slice(2));
  assemblePagesArtifact({
    distDirectory: argumentsObject['--dist'],
    outputDirectory: argumentsObject['--output'],
    snapshotPath: argumentsObject['--snapshot'],
  }).then(
    () => process.stdout.write('Assembled static Pages artifact.\n'),
    (error) => {
      process.stderr.write(`${error instanceof Error ? error.message : 'Could not assemble the Pages artifact.'}\n`);
      process.exitCode = 1;
    },
  );
}
