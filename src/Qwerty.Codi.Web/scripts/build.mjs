import { cpSync, existsSync, lstatSync, mkdirSync, rmSync } from 'node:fs';
import { dirname, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

// Produce un pacchetto statico locale: il comando non pubblica né tocca wwwroot dell'API.
const workspace = resolve(dirname(fileURLToPath(import.meta.url)), '..');
for (const project of ['web.admin', 'web.frontend']) {
  const result = spawnSync(
    process.execPath,
    ['node_modules/@angular/cli/bin/ng.js', 'build', project],
    {
      cwd: workspace,
      stdio: 'inherit',
    },
  );
  if (result.error) throw result.error;
  if (result.status !== 0) process.exit(result.status ?? 1);
}

const dist = resolve(workspace, 'dist');
const site = resolve(dist, 'site');
// La pulizia riguarda esclusivamente l'artefatto generato e rifiuta collegamenti simbolici.
if (!site.startsWith(dist + sep) || (existsSync(site) && lstatSync(site).isSymbolicLink())) {
  throw new Error('Destinazione del bundle non valida.');
}
rmSync(site, { recursive: true, force: true });
mkdirSync(site, { recursive: true });
cpSync(resolve(dist, 'frontend/browser'), site, { recursive: true });
cpSync(resolve(dist, 'admin/browser'), resolve(site, 'admin'), { recursive: true });
console.log('Bundle delle due applicazioni: dist/site (admin sotto /admin/).');
