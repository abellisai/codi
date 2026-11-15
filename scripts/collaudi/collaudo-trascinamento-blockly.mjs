#!/usr/bin/env node
/**
 * Collaudo QA/B9: il gesto reale di trascinamento Blockly dal cassetto al workspace.
 *
 * Guida un browser reale con Playwright. Il gesto è solo puntatore: pointerdown,
 * pointermove multipli, pointerup. Il copione non costruisce mai blocchi con newBlock()
 * né con window.ng.getComponent.
 *
 * Controlli eseguiti:
 * 1. L'accesso admin funziona.
 * 2. Il workspace dell'editor ValueProgram si monta.
 * 3. Il cassetto offre il vocabolario ValueProgram.
 * 4. Trascinare val_start dal cassetto al canvas muove davvero il blocco.
 * 5. Il salvataggio persiste il blocco trascinato nel workspace_json della bozza.
 * 6. Trascinare val_show sotto val_start crea la connessione next.
 * 7. Trascinare val_number dentro l'ingresso di val_show crea la catena annidata.
 * 8. Ogni salvataggio incrementa row_version nella tabella exercise_draft.
 *
 * Il conteggio dei blocchi sul canvas esclude sempre la subtree .blocklyFlyout:
 * .blocklyBlockCanvas esiste anche dentro il cassetto. Contare senza escludere la
 * subtree fa sembrare composto un progetto vuoto (lezione del copione vecchio).
 *
 * Richiesta: Playwright 1.65 o superiore, node 20 o superiore, API in ascolto su 5200,
 * admin in ascolto su 4200. Playwright è risolto dal node_modules del repo o
 * dall'installazione mise di playwright.
 *
 * Uso:
 *   node scripts/collaudi/collaudo-trascinamento-blockly.mjs
 *   node scripts/collaudi/collaudo-trascinamento-blockly.mjs --headed --out tmp/collaudo-b9
 *
 * Uscita: exit 0 con tutti i controlli verdi, exit 1 con il primo controllo fallito.
 * Evidenze: schermate ed evidenze.json nella directory di output.
 */
import { createRequire } from 'node:module';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const argv = process.argv.slice(2);
const FLAG_HEADED = argv.includes('--headed');
const OUT = argv.includes('--out') ? path.resolve(argv[argv.indexOf('--out') + 1]) : path.join(ROOT, 'tmp', 'collaudi-b9');
const BASE = 'https://localhost:4200';
const API = 'https://localhost:5200';
const EXERCISE_ID = '8c8782fe-54ce-4a46-b9d2-b14f673a2161'; // VP01 - Somma di blocchi (QA/demo)
const DB = path.join(ROOT, 'src/Qwerty.Codi.Api/data/codi.db');

process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'; // solo localhost in QA

// ---------------------------------------------------------------- helper

function findPlaywright() {
  const req = createRequire(import.meta.url);
  const candidates = ['playwright'];
  const mise = path.join(os.homedir(), '.local/share/mise/installs/playwright');
  if (fs.existsSync(mise)) {
    for (const version of fs.readdirSync(mise).sort().reverse()) {
      candidates.push(path.join(mise, version, 'node_modules/playwright'));
    }
  }
  for (const candidate of candidates) {
    try {
      return req(candidate);
    } catch {
      // altro candidato
    }
  }
  throw new Error('Playwright non trovato: npm install -D playwright nel workspace web o mise install playwright');
}

function readSeedAdmin() {
  const settings = JSON.parse(fs.readFileSync(path.join(ROOT, 'src/Qwerty.Codi.Api/appsettings.Development.json'), 'utf8'));
  const seed = settings.Codi?.SeedAdmin;
  if (!seed?.Username || !seed?.Password) throw new Error('Codi:SeedAdmin mancante in appsettings.Development.json');
  return seed; // mai stampare la password
}

function sqliteQuery(sql) {
  return execFileSync('sqlite3', [DB, sql], { encoding: 'utf8' }).trim();
}

function draftWorkspaceJson() {
  return sqliteQuery(`SELECT workspace_json FROM exercise_draft WHERE exercise_id='${EXERCISE_ID}';`);
}

function draftRowVersion() {
  return Number(sqliteQuery(`SELECT row_version FROM exercise_draft WHERE exercise_id='${EXERCISE_ID}';`));
}

const log = (label, value) => console.log(`[${label}] ${typeof value === 'string' ? value : JSON.stringify(value)}`);

// ---------------------------------------------------------------- playwright

const { chromium } = findPlaywright();
fs.mkdirSync(OUT, { recursive: true });
const evidence = { startedAt: new Date().toISOString(), exerciseId: EXERCISE_ID, checks: [], saves: [], drags: [] };

async function shot(page, name) {
  const file = path.join(OUT, `${name}.png`);
  await page.screenshot({ path: file });
  log('schermata', file);
}

/** Porta il workspace dentro il viewport: il gesto lavora in coordinate viewport. */
async function ensureWorkspaceVisible(page) {
  await page.evaluate(`(() => { const el = document.querySelector('.blocklySvg'); if (el) el.scrollIntoView({ block: 'center', inline: 'center' }); })()`);
  await page.waitForTimeout(400);
  const r = await page.evaluate(`(() => { const r = document.querySelector('.blocklySvg').getBoundingClientRect(); return { x: r.x, y: r.y, w: r.width, h: r.height }; })()`);
  log('workspace visibile', JSON.stringify(r));
  return r;
}

async function check(name, ok, detail) {
  evidence.checks.push({ name, ok, detail });
  log(ok ? 'OK' : 'FALLITO', `${name}: ${detail ?? ''}`);
  if (!ok) {
    fs.writeFileSync(path.join(OUT, 'evidenze.json'), JSON.stringify(evidence, null, 2));
    console.error('collaudo interrotto al controllo fallito');
    process.exitCode = 1;
    throw new Error(name);
  }
}

// Letture DOM del workspace. I gruppi del cassetto esclusi perché stanno dentro .blocklyFlyout.
const SCRIPT_CANVAS_BLOCKS = `(() => {
  const groups = [...document.querySelectorAll('.blocklyBlockCanvas g.blocklyDraggable')]
    .filter((g) => !g.closest('.blocklyFlyout'));
  return groups.map((g) => { const r = g.getBoundingClientRect(); return { text: (g.textContent || '').replace(/\\s+/g, ''), x: r.x, y: r.y, w: r.width, h: r.height }; });
})()`;
const SCRIPT_FLYOUT_BLOCKS = `(() => {
  const groups = [...document.querySelectorAll('.blocklyFlyout g.blocklyDraggable')];
  return groups.map((g) => { const r = g.getBoundingClientRect(); return { text: (g.textContent || '').replace(/\\s+/g, ''), x: r.x, y: r.y, w: r.width, h: r.height }; });
})()`;
// Filtra i blocchi del cassetto: Blockly v13 li dichiara nel workspace, quindi
// getAllBlocks li include. Le copie del cassetto restano sotto la subtree .blocklyFlyout.
const SCRIPT_BLOCK_FILTER = `const outsideFlyout = (b) => {
  let node = b.getSvgRoot() && b.getSvgRoot().parentElement;
  while (node) { if (node.classList && node.classList.contains('blocklyFlyout')) return false; node = node.parentElement; }
  return true;
};`;

// Node 26 serializza il sorgente pigro delle funzioni: Playwright vedrebbe il
// placeholder non interpolato. Le espressioni valutate vanno costruite per
// concatenazione e i parametri inseriti come JSON.
function blockFilterExpr(bodyExpr) {
  return `(() => { ${SCRIPT_BLOCK_FILTER} return ${bodyExpr}; })()`;
}

// Lettura dello stato del workspace per la sola verifica delle connessioni.
const SCRIPT_CONNECTIONS = `(() => {
  const el = document.querySelector('app-value-program-program-editor');
  if (!el || !window.ng || !window.ng.getComponent) return null;
  const component = window.ng.getComponent(el);
  const workspace = component && component.workspace;
  if (!workspace || !workspace.getTopBlocks) return null;
  const target = (conn) => { const c = conn && conn.targetConnection; if (!c) return null; const b = c.getBlock ? c.getBlock() : (c.getSourceBlock ? c.getSourceBlock() : null); return b ? { type: b.type, id: b.id } : null; };
  return {
    mode: 'workspace-api-lettura',
    top: workspace.getTopBlocks(true).map((b) => ({ type: b.type, id: b.id, next: target(b.nextConnection), previous: target(b.previousConnection), inputs: Object.fromEntries((b.inputList || []).filter((i) => i.connection).map((i) => [i.name, target(i.connection)])) })),
  };
})()`;

function findGroup(groups, normalizedText) {
  return groups.find((g) => g.text === normalizedText || g.text.includes(normalizedText));
}

/** Piano di aggancio: calcola il gesto correttivo dalla geometria reale dei blocchi. */
function planNudge(page, dragType, anchorType, anchorConnectionName) {
  const expr = `(() => {
    ${SCRIPT_BLOCK_FILTER}
    const args = ${JSON.stringify({ dragType, anchorType, anchorConnectionName })};
    const host = document.querySelector('app-value-program-program-editor');
    const workspace = window.ng.getComponent(host).workspace;
    const blocks = workspace.getAllBlocks(true).filter(outsideFlyout);
    const dragBlock = blocks.find((b) => b.type === args.dragType);
    const anchor = blocks.find((b) => b.type === args.anchorType);
    if (!dragBlock || !anchor) return null;
    const dragConnection = dragBlock.previousConnection || dragBlock.outputConnection;
    const input = (anchor.inputList || []).find((i) => i.name === args.anchorConnectionName && i.connection) || (anchor.inputMap ? anchor.inputMap[args.anchorConnectionName] : null);
    const anchorConnection = args.anchorConnectionName === 'next' ? anchor.nextConnection : (input ? input.connection : null);
    if (!dragConnection || !anchorConnection) return null;
    const toScreen = (ctm, offset) => {
      const point = new DOMPoint(offset.x, offset.y).matrixTransform(ctm);
      return { x: point.x, y: point.y };
    };
    const dragConnectionScreen = toScreen(dragBlock.getSvgRoot().getScreenCTM(), dragConnection.getOffsetInBlock());
    const anchorConnectionScreen = toScreen(anchor.getSvgRoot().getScreenCTM(), anchorConnection.getOffsetInBlock());
    // Presa sulla fascia SVG interna al blocco, vicino alla tacca: fuori dal rettangolo il puntatore non afferra nulla.
    const rect = dragBlock.getSvgRoot().getBoundingClientRect();
    const grabX = Math.min(Math.max(dragConnectionScreen.x, rect.x + 3), rect.x + rect.width - 3);
    const grab = { x: grabX, y: Math.min(Math.max(dragConnectionScreen.y - 2, rect.y + 3), rect.y + rect.height - 3) };
    const target = { x: grab.x + (anchorConnectionScreen.x - dragConnectionScreen.x), y: grab.y + (anchorConnectionScreen.y - dragConnectionScreen.y) };
    return { grab, target, dragConnectionScreen, anchorConnectionScreen };
  })()`;
  return page.evaluate(expr);
}

/** Gesto reale sul canvas: afferra il blocco appena rilasciato e lo allaccia al punto esatto. */
async function nudgeConnect(page, dragType, anchorType, anchorConnectionName, label) {
  const plan = await planNudge(page, dragType, anchorType, anchorConnectionName);
  if (!plan) {
    log('aggancio', `${label}: piano non disponibile`);
    return false;
  }
  log('aggancio', `${label}: da ${JSON.stringify(plan.grab)} a ${JSON.stringify(plan.target)}`);
  await page.mouse.move(plan.grab.x, plan.grab.y);
  await page.waitForTimeout(120);
  await page.mouse.down();
  const steps = 18;
  for (let step = 1; step <= steps; step += 1) {
    const t = step / steps;
    await page.mouse.move(plan.grab.x + (plan.target.x - plan.grab.x) * t, plan.grab.y + (plan.target.y - plan.grab.y) * t);
    await page.waitForTimeout(35);
  }
  await page.mouse.move(plan.target.x + 6, plan.target.y + 4);
  await page.waitForTimeout(60);
  await page.mouse.move(plan.target.x - 4, plan.target.y - 3);
  await page.waitForTimeout(60);
  await page.mouse.move(plan.target.x, plan.target.y);
  await page.waitForTimeout(80);
  await page.mouse.up();
  await page.waitForTimeout(400);
  evidence.drags.push({ label, plan });
  return true;
}

/** Lettura live di una connessione di valore: delta, posizione e stato di connessione. */
function valueGeometry(page, dragType, anchorType, inputName, excludeIds = []) {
  const expr = `(() => {
    ${SCRIPT_BLOCK_FILTER}
    const args = ${JSON.stringify({ dragType, anchorType, inputName, excludeIds })};
    const host = document.querySelector('app-value-program-program-editor');
    const workspace = window.ng.getComponent(host).workspace;
    const blocks = workspace.getAllBlocks(true).filter(outsideFlyout);
    const candidates = blocks.filter(
      (b) => b.type === args.dragType && b.outputConnection && !args.excludeIds.includes(b.id),
    );
    const dragged = candidates[candidates.length - 1];
    const anchor = blocks.find((b) => b.type === args.anchorType);
    if (!dragged || !anchor) return null;
    const input = (anchor.inputList || []).find((i) => i.name === args.inputName && i.connection);
    if (!input) return null;
    const draggedOffset = dragged.outputConnection.getOffsetInBlock();
    const draggedScreen = new DOMPoint(draggedOffset.x, draggedOffset.y).matrixTransform(dragged.getSvgRoot().getScreenCTM());
    const anchorOffset = input.connection.getOffsetInBlock();
    const anchorScreen = new DOMPoint(anchorOffset.x, anchorOffset.y).matrixTransform(anchor.getSvgRoot().getScreenCTM());
    return {
      dx: anchorScreen.x - draggedScreen.x,
      dy: anchorScreen.y - draggedScreen.y,
      dragScreen: { x: draggedScreen.x, y: draggedScreen.y },
      connected: Boolean(dragged.outputConnection.targetConnection),
    };
  })()`;
  return page.evaluate(expr);
}

/** Dump diagnostico dei blocchi con posizione della tacca di uscita. */
const SCRIPT_DUMP_BLOCKS = `(() => {
  ${SCRIPT_BLOCK_FILTER}
  const ws = window.ng.getComponent(document.querySelector('app-value-program-program-editor')).workspace;
  const dump = (b) => {
    const svg = b.getSvgRoot();
    const ct = svg.getScreenCTM();
    const o = b.outputConnection ? b.outputConnection.getOffsetInBlock() : null;
    const p = o && ct ? new DOMPoint(o.x, o.y).matrixTransform(ct) : null;
    const r = svg.getBoundingClientRect();
    return {
      type: b.type,
      id: b.id,
      rect: { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width) },
      out: p ? { x: Math.round(p.x), y: Math.round(p.y) } : null,
      target: b.outputConnection && b.outputConnection.targetConnection ? 'CONNESSO' : null,
      parent: svg.parentElement ? (svg.parentElement.getAttribute('class') || svg.parentElement.tagName) : null,
    };
  };
  return ws.getAllBlocks(true).filter(outsideFlyout).map(dump);
})()`;

/** Id dei blocchi realmente sul canvas (cassetto escluso) prima di un gesto. */
function canvasBlockIds(page) {
  const expr = `(() => {
    ${SCRIPT_BLOCK_FILTER}
    const ws = window.ng.getComponent(document.querySelector('app-value-program-program-editor')).workspace;
    return ws.getAllBlocks(true).filter(outsideFlyout).map((b) => b.id);
  })()`;
  return page.evaluate(expr);
}

/** Gesto identico alla sonda che aggancia: 8 round di allineamento, jiggle finale, guardia di nascita. */
async function dragIntoValueInput(page, flyoutGroup, dragType, anchorType, inputName, label) {
  // Id prima del gesto: la geometria lavora solo sul blocco nato dal gesto, non sui residui.
  const beforeIds = await canvasBlockIds(page);
  const grabX = flyoutGroup.x + Math.max(6, flyoutGroup.w / 2);
  const grabY = flyoutGroup.y + 4; // fascia SVG sopra il campo numero
  await page.mouse.move(grabX - 25, grabY);
  await page.waitForTimeout(120);
  await page.waitForTimeout(250);
  let pointer = { x: grabX, y: grabY };
  let geo = null;
  for (let bornGuard = 0; bornGuard < 3; bornGuard++) {
    await page.mouse.move(grabX, grabY);
    await page.waitForTimeout(250);
    await page.mouse.down();
    await page.waitForTimeout(200);
    // primo movimento breve: Blockly crea la copia del blocco sul workspace
    await page.mouse.move(pointer.x + 10, pointer.y + 6);
    await page.waitForTimeout(300);
    pointer = { x: pointer.x + 10, y: pointer.y + 6 };
    geo = await valueGeometry(page, dragType, anchorType, inputName, beforeIds);
    if (geo) break;
    log(label, `nascita mancata (guardia ${bornGuard}): riprovo il gesto dal cassetto`);
    await page.mouse.up();
    await page.waitForTimeout(300);
    flyoutGroup = (await openFlyout(page)).find((g) => g.text === flyoutGroup.text) ?? flyoutGroup;
  }
  log(label, `partenza puntatore ${JSON.stringify(pointer)}`);
  log(label, `stato blocchi: ${JSON.stringify(await page.evaluate(SCRIPT_DUMP_BLOCKS))}`);
  let connected = false;
  for (let round = 0; round < 8 && geo; round++) {
    connected = geo.connected;
    if (connected) { log(label, `connessione agganciata a mezza corsa (round ${round})`); break; }
    const nx = pointer.x + geo.dx;
    const ny = pointer.y + geo.dy;
    for (let step = 1; step <= 8; step++) {
      await page.mouse.move(pointer.x + (nx - pointer.x) * (step / 8), pointer.y + (ny - pointer.y) * (step / 8));
      await page.waitForTimeout(30);
    }
    pointer = { x: nx, y: ny };
    await page.waitForTimeout(40);
    geo = await valueGeometry(page, dragType, anchorType, inputName, beforeIds);
    log(label, `round ${round} ${JSON.stringify(geo)}`);
  }
  // Blockly ricontrolla le connessioni sull'ultimo movimento prima del rilascio
  await page.mouse.move(pointer.x + 3, pointer.y + 2);
  await page.waitForTimeout(60);
  await page.mouse.move(pointer.x, pointer.y);
  await page.waitForTimeout(80);
  await page.mouse.up();
  await page.waitForTimeout(500);
  connected = (await valueGeometry(page, dragType, anchorType, inputName, beforeIds))?.connected ?? false;
  return connected;
}

/** Il cassetto può essere chiuso dopo un gesto: riapre con un click sull'area vuota. */
async function openFlyout(page) {
  let blocks = await page.evaluate(SCRIPT_FLYOUT_BLOCKS);
  const visible = blocks.length > 0 && blocks.some((b) => b.x >= 0 && b.y >= 0);
  if (!visible) {
    const svg = await ensureWorkspaceVisible(page);
    await page.mouse.click(svg.x + svg.w - 140, svg.y + svg.h - 30);
    await page.waitForTimeout(700);
    blocks = await page.evaluate(SCRIPT_FLYOUT_BLOCKS);
  }
  return blocks;
}

/** Pulizia con gesti reali: Ctrl+A e Delete su Blockly; fallback menu "Delete Block". */
async function cleanWorkspace(page, count) {
  log('pulizia', `workspace iniziale non vuoto (${count} blocchi residui): pulizia con gesti reali`);
  const svg = await ensureWorkspaceVisible(page);
  await page.mouse.click(svg.x + svg.w - 140, svg.y + svg.h - 30);
  await page.waitForTimeout(400);
  await page.keyboard.press('Control+a');
  await page.waitForTimeout(400);
  await page.keyboard.press('Delete');
  await page.waitForTimeout(700);
  let after = await page.evaluate(SCRIPT_CANVAS_BLOCKS);
  if (after.length > 0) {
    log('pulizia', 'scorciatoia non sufficiente: fallback menu Elimina blocco su ogni blocco residuo');
    for (let round = 1; after.length > 0 && round <= 20; round += 1) {
      const group = after[0];
      await page.mouse.click(group.x + Math.min(group.w / 2, 60), group.y + Math.min(group.h / 2, 18), { button: 'right' });
      await page.waitForTimeout(500);
      const menuTexts = await page.$$eval('.blocklyMenuItem', (els) => els.map((e) => (e.textContent || '').trim()));
      log('menu contesto', JSON.stringify(menuTexts));
      const item = page.locator('.blocklyMenuItem:not(.blocklyMenuItemDisabled)').filter({ hasText: /delete( \d+)? block|elimina( \d+)? blocco/i }).first();
      await item.waitFor({ state: 'visible', timeout: 5000 });
      await item.click();
      await page.waitForTimeout(700);
      after = await page.evaluate(SCRIPT_CANVAS_BLOCKS);
    }
  }
  const dumpAfter = await page.evaluate(SCRIPT_DUMP_BLOCKS);
  log('pulizia', `dump finale: ${JSON.stringify(dumpAfter)}`);
  const nestedParent = dumpAfter.filter((b) => b.parent === 'g');
  await check('pulizia workspace', after.length === 0 && nestedParent.length === 0, `residui rimossi: ${count} -> ${after.length}, genitori annidati 'g': ${nestedParent.length}`);
}

/**
 * Gesto reale: pointerdown sul blocco del cassetto, pointermove a piccoli passi
 * (prima dentro il cassetto, poi fuori), pointerup sul punto di destinazione con
 * oscillazione breve perché Blockly mostri lasanction prima del rilascio.
 */
async function dragFlyoutBlockTo(page, flyoutGroup, target, label, grabOffset = null) {
  const dx = grabOffset ? grabOffset.x : Math.min(flyoutGroup.w / 2, 60);
  const dy = grabOffset ? grabOffset.y : Math.min(flyoutGroup.h / 2, 18);
  const startX = flyoutGroup.x + dx;
  const startY = flyoutGroup.y + dy;
  await page.mouse.move(startX, startY);
  await page.waitForTimeout(120);
  await page.mouse.down();
  const steps = 26;
  for (let step = 1; step <= steps; step += 1) {
    const t = step / steps;
    const x = startX + (target.x - startX) * t;
    const y = startY + (target.y - startY) * t;
    await page.mouse.move(x, y);
    await page.waitForTimeout(35);
  }
  // oscillazione breve sul punto di arrivo: Blockly calcola l'aggancio a ogni movimento
  await page.mouse.move(target.x + 8, target.y + 6);
  await page.waitForTimeout(60);
  await page.mouse.move(target.x - 6, target.y - 4);
  await page.waitForTimeout(60);
  await page.mouse.move(target.x, target.y);
  await page.waitForTimeout(60);
  await page.mouse.up();
  await page.waitForTimeout(400);
  const canvas = await page.evaluate(SCRIPT_CANVAS_BLOCKS);
  const moved = canvas.length;
  const connectionState = await page.evaluate(SCRIPT_CONNECTIONS);
  evidence.drags.push({ label, from: { x: startX, y: startY }, to: target, canvasBlocks: moved, connections: connectionState });
  log('trascinamento', `${label}: canvas blocchi ${moved}`);
  return { canvas, connectionState };
}

async function saveDraft(page, tag) {
  const before = draftRowVersion();
  const responses = [];
  const onResponse = async (res) => {
    if (res.url().includes('/execute/command/SaveExerciseDraft')) {
      let body = '';
      try {
        body = await res.text();
      } catch {
        body = 'corpo non leggibile';
      }
      responses.push({ status: res.status(), body });
    }
  };
  page.on('response', onResponse);
  const button = page.getByRole('button', { name: 'Salva bozza' });
  await button.waitFor({ state: 'visible', timeout: 10000 });
  await button.click();
  const deadline = Date.now() + 12000;
  while (responses.length === 0 && Date.now() < deadline) await page.waitForTimeout(250);
  page.off('response', onResponse);
  const workspace = draftWorkspaceJson();
  const after = draftRowVersion();
  evidence.saves.push({ tag, httpStatus: responses[0]?.status ?? null, errorBody: responses[0]?.status >= 400 ? responses[0]?.body : undefined, rowVersionBefore: before, rowVersionAfter: after, workspaceJson: workspace });
  log('salvataggio', `${tag}: HTTP ${responses[0]?.status ?? 'nessuna risposta'} row_version ${before} -> ${after}`);
  if (responses[0] && responses[0].status >= 400) log('corpo errore', responses[0].body.slice(0, 400));
  return { workspace, responses, before, after };
}

// ---------------------------------------------------------------- fase 1: probe servizi

async function main() {
  const apiProbe = await fetch(`${API}/api/v1/admin/bootstrap`, { method: 'GET' });
  const adminProbe = await fetch(`${BASE}/admin/`, { method: 'GET', redirect: 'manual' });
  log('probe', `API bootstrap ${apiProbe.status}, admin ${adminProbe.status}`);
  if (apiProbe.status >= 400 && apiProbe.status !== 401) throw new Error('API non in ascolto su 5200');
  if (adminProbe.status >= 500) throw new Error('admin non in ascolto su 4200');

  const seed = readSeedAdmin();
  const browser = await chromium.launch({ headless: !FLAG_HEADED, args: ['--ignore-certificate-errors', '--disable-dev-shm-usage'] });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, ignoreHTTPSErrors: true });
  const page = await context.newPage();
  page.on('pageerror', (error) => log('pageerror', error.message));

  try {
    // fase 2: login admin
    await page.goto(`${BASE}/admin/`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('input[name="ng.form0.username"]', { timeout: 15000 });
    await page.fill('input[name="ng.form0.username"]', seed.Username);
    await page.fill('input[name="ng.form0.password"]', seed.Password);
    await page.press('input[name="ng.form0.password"]', 'Enter');
    await page.waitForURL(/\/admin\/(dashboard|exercises|$|home)/, { timeout: 15000 }).catch(() => {});
    const loginOk = !page.url().includes('login') && (await page.content()).includes('Esercizi');
    await check('accesso admin', loginOk, page.url());

    // fase 3: editor VP01, workspace montato, cassetto con il vocabolario
    await page.goto(`${BASE}/admin/exercises/${EXERCISE_ID}/editor`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('.blocklySvg', { timeout: 20000 });
    await page.waitForSelector('.blocklyFlyout', { timeout: 20000 });
    await page.waitForTimeout(1200);
    await ensureWorkspaceVisible(page);
    const initial = await page.evaluate(SCRIPT_CANVAS_BLOCKS);
    if (initial.length > 0) {
      await cleanWorkspace(page, initial.length);
    }
    // il gesto di pulizia può chiudere il cassetto: openFlyout lo riapre se serve
    const allowedBlocks = JSON.parse(sqliteQuery(`SELECT json_extract(definition_json,'$.toolbox.allowedBlocks') FROM exercise_draft WHERE exercise_id='${EXERCISE_ID}';`));
    const flyoutBlocks = await openFlyout(page);
    log('cassetto', `blocchi presenti ${flyoutBlocks.length}, permessi ${allowedBlocks.length}`);
    await check('workspace montato', (await page.$$('.blocklySvg')).length === 1, 'svg Blockly presente');
    await check('cassetto con il vocabolario', flyoutBlocks.length >= allowedBlocks.length, `cassetto ${flyoutBlocks.length} blocchi, vocabolario ${allowedBlocks.join(',')}`);
    const startGroup = findGroup(flyoutBlocks, 'quandosiclicca');
    await check('blocco di avvio nel cassetto', Boolean(startGroup), startGroup ? `testo ${startGroup.text}` : 'etichetta avvio non trovata');

    const baseline = await page.evaluate(SCRIPT_CANVAS_BLOCKS);
    log('baseline', `blocchi sul canvas prima del gesto: ${baseline.length}`);
    await shot(page, '01-editor-aperto');

    // fase 4: primo trascinamento val_start -> canvas
    const svg = await ensureWorkspaceVisible(page);
    const targetStart = { x: svg.x + 320, y: svg.y + 150 };
    const first = await dragFlyoutBlockTo(page, startGroup, targetStart, 'val_start');
    await check('trascinamento val_start', first.canvas.length > baseline.length, `canvas ${baseline.length} -> ${first.canvas.length}`);
    await shot(page, '02-val-start-trascinato');

    // fase 5: salvataggio e prova sqlite del primo blocco
    const save1 = await saveDraft(page, 'dopo val_start');
    await check('salvataggio HTTP', (save1.responses[0]?.status ?? 0) >= 200 && (save1.responses[0]?.status ?? 0) < 300, `stato ${save1.responses[0]?.status}`);
    await check('bozza contiene val_start', save1.workspace.includes('val_start'), save1.workspace.slice(0, 240));
    await check('row_version incrementato', save1.after > save1.before, `${save1.before} -> ${save1.after}`);

    // fase 6: secondo trascinamento val_show sotto val_start (catena next)
    const flyoutAfter = await openFlyout(page);
    const showGroup = findGroup(flyoutAfter, 'dire');
    await check('blocco dire nel cassetto', Boolean(showGroup), showGroup ? `testo ${showGroup.text}` : 'etichetta dire non trovata');
    const canvasAfterSave1 = await page.evaluate(SCRIPT_CANVAS_BLOCKS);
    const startOnCanvas = findGroup(canvasAfterSave1, 'quandosiclicca');
    const targetShow = { x: startOnCanvas.x + startOnCanvas.w / 2, y: startOnCanvas.y + startOnCanvas.h + 6 };
    const second = await dragFlyoutBlockTo(page, showGroup, targetShow, 'val_show');
    let connectionState = second.connectionState;
    let nextConnected = connectionState?.top?.some((b) => b.type === 'val_start' && b.next?.type === 'val_show');
    if (!nextConnected) {
      await nudgeConnect(page, 'val_show', 'val_start', 'next', 'aggancio val_show sotto val_start');
      connectionState = await page.evaluate(SCRIPT_CONNECTIONS);
      nextConnected = connectionState?.top?.some((b) => b.type === 'val_start' && b.next?.type === 'val_show');
    }
    await check('connessione next val_start -> val_show', Boolean(nextConnected), JSON.stringify(connectionState ?? 'lettura connessioni non disponibile'));
    await shot(page, '03-val-show-connesso');

    // fase 7: terzo trascinamento val_number dentro l'ingresso di val_show
    let flyoutNow = await openFlyout(page);
    const canvasNow = await page.evaluate(SCRIPT_CANVAS_BLOCKS);
    const showOnCanvas = findGroup(canvasNow, 'dire');
    let numberGroup = flyoutNow.find((g) => /^\d+$/.test(g.text));
    // il cassetto scorre: porta il blocco numero nella parte visibile con la rotellina
    const svgForScroll = await ensureWorkspaceVisible(page);
    for (let guard = 0; numberGroup && numberGroup.y + numberGroup.h > svgForScroll.y + svgForScroll.h - 12 && guard < 12; guard += 1) {
      await page.mouse.move(svgForScroll.x + 24, svgForScroll.y + 120);
      await page.mouse.wheel(0, 110);
      await page.waitForTimeout(400);
      flyoutNow = await page.evaluate(SCRIPT_FLYOUT_BLOCKS);
      numberGroup = flyoutNow.find((g) => /^\d+$/.test(g.text));
    }
    await check('blocco numero nel cassetto', Boolean(numberGroup), numberGroup ? `testo "${numberGroup.text}"` : 'etichetta numero non trovata');
    // L'aggancio dell'ingresso di valore: gesto con correzione live sulla connessione reale.
    const inputOccupied = async () =>
      page.evaluate(blockFilterExpr(`(() => {
        const ws = window.ng.getComponent(document.querySelector('app-value-program-program-editor')).workspace;
        const show = ws.getAllBlocks(true).filter(outsideFlyout).find((b) => b.type === 'val_show');
        const input = show ? (show.inputList || []).find((i) => i.name === 'VALUE' && i.connection) : null;
        const target = input && input.connection && input.connection.targetConnection
          ? input.connection.targetConnection.getSourceBlock()
          : null;
        return { occupied: Boolean(target), targetType: target ? target.type : null };
      })()`));
    let nestedNumber = false;
    let nested = null;
    for (let attempt = 1; attempt <= 3 && !nestedNumber; attempt += 1) {
      await dragIntoValueInput(page, numberGroup, 'val_number', 'val_show', 'VALUE', `val_number prova ${attempt}`);
      nested = await inputOccupied();
      nestedNumber = nested.occupied && nested.targetType === 'val_number';
      if (!nestedNumber) {
        // il blocco può essere stato smaltito: il cassetto riaperto per la prova successiva
        flyoutNow = await openFlyout(page);
        const visibleNumber = flyoutNow.find((g) => /^\d+$/.test(g.text));
        if (visibleNumber) numberGroup = visibleNumber;
      }
    }
    await check('val_number annidato in val_show', Boolean(nestedNumber), JSON.stringify(nested ?? 'lettura non disponibile'));
    await shot(page, '04-val-number-annidato');

    // fase 8: salvataggio finale e catena completa nella bozza
    const save2 = await saveDraft(page, 'dopo la catena');
    await check('salvataggio finale HTTP', (save2.responses[0]?.status ?? 0) >= 200 && (save2.responses[0]?.status ?? 0) < 300, `stato ${save2.responses[0]?.status}`);
    const doc = JSON.parse(save2.workspace);
    const blocks = doc?.blocks?.blocks ?? [];
    // Il seriale salva la catena come next: { block: ... } sul blocco padre.
    const chain = blocks.some((b) => {
      const nx = b.next?.block ?? b.next;
      return b.type === 'val_start' && nx?.type === 'val_show' && nx.inputs?.VALUE?.block?.type === 'val_number';
    });
    await check(
      'catena completa nel workspace_json',
      chain,
      JSON.stringify({ blocchi: blocks.length, tipi: blocks.map((b) => b.type) }),
    );
    await check('row_version cresciuto due volte', save2.after > save1.after, `${save1.after} -> ${save2.after}`);
    await shot(page, '05-fine-collaudo');

    fs.writeFileSync(path.join(OUT, 'evidenze.json'), JSON.stringify(evidence, null, 2));
    log('esito', `tutti i controlli verdi (${evidence.checks.length} controlli). Evidenze in ${OUT}`);
  } finally {
    await context.close();
    await browser.close();
  }
}

main().catch((error) => {
  console.error(`ERRORE: ${error.message}`);
  process.exitCode = 1;
});
