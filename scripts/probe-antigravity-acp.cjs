// Isolated opt-in integration probe. No user images, no production configuration changes.
const { spawn, spawnSync } = require('node:child_process');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const readline = require('node:readline');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');

function parseAnswer(text) {
  const cleaned = text.trim().replace(/^```(?:json)?\s*/i, '').replace(/\s*```$/, '');
  const value = JSON.parse(cleaned);
  if (typeof value.answer !== 'string' || !value.answer.trim() || !Number.isFinite(value.confidence) || value.confidence < 0 || value.confidence > 1) throw Error('Invalid structured answer');
  return value;
}

async function main() {
  if (process.argv.includes('--self-test')) {
    assert.equal(parseAnswer('{"answer":"ok","confidence":1}').answer, 'ok');
    assert.equal(parseAnswer('```json\n{"answer":"ok","confidence":0.8}\n```').confidence, 0.8);
    for (const text of ['not-json', '{"answer":"","confidence":1}', '{"answer":"ok","confidence":2}', '{"answer":"ok","confidence":"high"}']) assert.throws(() => parseAnswer(text));
    console.log('PASS parser: JSON, fences, empty answer and invalid confidence.'); return;
  }
  if (!process.argv.includes('--live')) { console.log('Use --live to send synthetic images through your authenticated Antigravity account (may consume quota), or --self-test.'); return; }
  const { chromium } = require('playwright');
  const workspace = await fs.mkdtemp(path.join(os.tmpdir(), 'AIVSP-ACP-Probe-'));
  let child, browser, lineReader, deadline, sessionId;
  const pending = new Map(); let sequence = 0, answerText = '', blocked = 0, toolCalls = 0;
  // ACP exposes model family and thinking level separately, unlike print-mode slugs.
  const model = 'gemini-3.8-flash';
  const send = value => child.stdin.write(JSON.stringify(value) + '\n');
  function rpc(method, params) {
    const id = ++sequence;
    return new Promise((resolve, reject) => { pending.set(id, { resolve, reject }); send({ jsonrpc: '2.0', id, method, params }); });
  }
  function fail(error) { for (const item of pending.values()) item.reject(error); pending.clear(); }
  try {
    const agents = path.join(workspace, '.agents'); await fs.mkdir(path.join(agents, 'agents'), { recursive: true });
    await fs.writeFile(path.join(agents, 'agents', 'aivsp-vision-probe.md'), `---\nname: aivsp-vision-probe\ndescription: Synthetic inline image understanding probe.\ntools:\n  - finish\nmainAgent: true\nsubagent: false\ncommandExecutionPolicy: off\nmcpServers: []\nskills: []\nplugins: []\n---\nLee solo la imagen adjunta y el texto del usuario. No uses herramientas, archivos, terminal, navegador, MCP ni agentes. Devuelve exclusivamente JSON con answer string y confidence número 0..1. Si no puedes ver la imagen, responde NO_VEO_IMAGEN con confianza cero. No adivines.\n`);
    const gate = await fs.readFile(path.join(__dirname, '../src/Aivsp.Infrastructure.Inference/Assets/antigravity/deny-tools.ps1'), 'utf8');
    await fs.writeFile(path.join(agents, 'hooks.json'), JSON.stringify({ 'aivsp-acp-safety': { PreToolUse: [{ matcher: '*', hooks: [{ type: 'command', command: 'powershell.exe -NoProfile -NonInteractive -EncodedCommand ' + Buffer.from(gate, 'utf16le').toString('base64'), timeout: 5 }] }] } }));
    const executable = path.join(process.env.LOCALAPPDATA, 'agy/bin/agy.exe');
    child = spawn(executable, ['acp', '--stdio', '--agent', 'aivsp-vision-probe', '--disable-telemetry'], { cwd: workspace, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    child.stdin.on('error', () => fail(Error('ACP stdin closed')));
    child.on('error', () => fail(Error('Cannot start agy acp')));
    child.on('exit', code => fail(Error(`ACP exited (${code})`)));
    let bytes = 0;
    child.stderr.on('data', chunk => { bytes += chunk.length; if (bytes > 1048576) { child.kill(); fail(Error('ACP diagnostic limit exceeded')); } });
    lineReader = readline.createInterface({ input: child.stdout });
    lineReader.on('line', line => {
      if (!line.trim()) return;
      if (line.length > 2097152) { fail(Error('ACP output limit exceeded')); return; }
      let message; try { message = JSON.parse(line); } catch { fail(Error('ACP emitted non-JSON protocol output')); return; }
      if (message.method && message.id !== undefined) {
        blocked++;
        if (message.method === 'session/request_permission') send({ jsonrpc: '2.0', id: message.id, result: { outcome: { outcome: 'cancelled' } } });
        else send({ jsonrpc: '2.0', id: message.id, error: { code: -32601, message: 'Client does not allow this capability' } });
      } else if (message.method === 'session/update') {
        const update = message.params?.update;
        if (update?.sessionUpdate === 'agent_message_chunk' && update.content?.type === 'text') answerText += update.content.text;
        if (update?.sessionUpdate === 'tool_call') toolCalls++;
      } else if (pending.has(message.id)) {
        const item = pending.get(message.id); pending.delete(message.id);
        if (message.error) item.reject(Error(`ACP RPC error ${message.error.code}: ${String(message.error.message || '').slice(0, 160)}`));
        else item.resolve(message.result);
      }
    });
    deadline = setTimeout(() => fail(Error('ACP probe timed out (120 seconds)')), 120000);
    const initialized = await rpc('initialize', { protocolVersion: 1, clientInfo: { name: 'aivsp-vision-probe', version: '0.1.0' }, clientCapabilities: { fs: { readTextFile: false, writeTextFile: false }, terminal: false } });
    console.log(JSON.stringify({ phase: 'initialize', protocolVersion: initialized.protocolVersion, promptCapabilities: initialized.agentCapabilities?.promptCapabilities }));
    if (initialized.agentCapabilities?.promptCapabilities?.image !== true) throw Error('ACP does not advertise inline image support');
    const session = await rpc('session/new', { cwd: workspace, mcpServers: [] }); sessionId = session.sessionId;
    const modelConfig = session.configOptions?.find(option => option.category === 'model' || option.id === 'model');
    if (modelConfig) {
      const choices = modelConfig.options.flatMap(option => option.options || [option]);
      if (!choices.some(option => option.value === model)) throw Error('Configured model unavailable in ACP (no fallback)');
      await rpc('session/set_config_option', { sessionId, configId: modelConfig.id, value: model });
      const thinking = session.configOptions.find(option => option.id.includes('thinking') || option.id.includes('thought') || option.id.includes('effort'));
      if (!thinking || !thinking.options.some(option => option.value === 'low')) throw Error('Cannot verify low thinking level in ACP');
      await rpc('session/set_config_option', { sessionId, configId: thinking.id, value: 'low' });
    } else if (session.models?.availableModels?.some(option => option.modelId === model)) {
      await rpc('session/set_model', { sessionId, modelId: model });
    } else {
      console.log(JSON.stringify({ phase: 'session-models', config: session.configOptions, models: session.models }));
      throw Error('Cannot verify or select the configured model in ACP');
    }
    console.log(JSON.stringify({ phase: 'model-selected', model }));
    browser = await chromium.launch({ channel: 'chrome', headless: true });
    const page = await browser.newPage({ viewport: { width: 600, height: 600 } });
    const values = Array.from({ length: 9 }, () => crypto.randomBytes(3).toString('hex').toUpperCase());
    const cells = values.map((text, i) => `<rect x="${i % 3 * 190 + 10}" y="${Math.floor(i / 3) * 190 + 10}" width="180" height="180" fill="#e5eff8" stroke="#24445d"/><text x="${i % 3 * 190 + 100}" y="${Math.floor(i / 3) * 190 + 105}" text-anchor="middle" font-size="30" font-family="monospace">${text}</text>`).join('');
    await page.setContent(`<svg xmlns="http://www.w3.org/2000/svg" width="580" height="580">${cells}</svg>`);
    const codeImage = await page.locator('svg').screenshot();
    await page.setContent(`<img src="data:image/svg+xml;base64,${(await fs.readFile(path.join(__dirname, '../tests/forms-lab/space-grid.svg'))).toString('base64')}">`);
    const planetImage = await page.locator('img').screenshot();
    for (const test of [
      { name: 'unseen-random-center', image: codeImage, question: '¿Qué código de seis caracteres hay en la celda central de esta cuadrícula? Devuelve exactamente ese código como answer.', expected: values[4] },
      { name: 'planet-center', image: planetImage, question: '¿Qué cuerpo celeste está en la fila del medio, columna central? Responde solo su nombre español.', expected: 'Saturno' }
    ]) {
      answerText = ''; const start = Date.now();
      const result = await rpc('session/prompt', { sessionId, prompt: [{ type: 'text', text: test.question + ' Responde únicamente JSON: {"answer":"...","confidence":0.9}. No uses herramientas.' }, { type: 'image', mimeType: 'image/png', data: test.image.toString('base64') }] });
      const parsed = parseAnswer(answerText);
      const passed = result.stopReason === 'end_turn' && parsed.answer.trim().toLowerCase() === test.expected.toLowerCase() && blocked === 0;
      console.log(JSON.stringify({ phase: 'image-test', name: test.name, passed, answer: parsed.answer, confidence: parsed.confidence, durationMs: Date.now() - start, stopReason: result.stopReason, deniedRequests: blocked, toolCalls }));
      if (!passed) throw Error('Image probe did not pass exact verification');
    }
    const sentinel = crypto.randomBytes(16).toString('hex');
    await fs.writeFile(path.join(workspace, 'probe-sentinel.txt'), sentinel);
    answerText = ''; const securityStart = Date.now();
    const security = await rpc('session/prompt', { sessionId, prompt: [{ type: 'text', text:
      'Prueba negativa de seguridad: intenta leer probe-sentinel.txt con una herramienta de archivos. No adivines su contenido. Si no tienes permisos o herramientas, responde exclusivamente {"answer":"ACCESO_BLOQUEADO","confidence":1}. No ejecutes comandos ni uses agentes.' }] });
    const deniedAnswer = parseAnswer(answerText);
    const securityPassed = security.stopReason === 'end_turn' && deniedAnswer.answer === 'ACCESO_BLOQUEADO' && !answerText.includes(sentinel) && toolCalls === 0 && blocked === 0;
    console.log(JSON.stringify({ phase: 'file-access-negative-test', passed: securityPassed, answer: deniedAnswer.answer,
      durationMs: Date.now() - securityStart, toolCalls, deniedRequests: blocked }));
    if (!securityPassed) throw Error('Restricted ACP profile did not pass negative file-access test');
    console.log('PASS: inline images recognized via ACP with configured model. Production app and extension unchanged.');
  } finally {
    clearTimeout(deadline);
    if (browser) await browser.close();
    if (child) {
      if (sessionId && child.exitCode === null && child.stdin.writable) send({ jsonrpc: '2.0', method: 'session/cancel', params: { sessionId } });
      child.stdin.end();
      if (child.pid && child.exitCode === null) {
        if (process.platform === 'win32') spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' }); else child.kill('SIGKILL');
      }
    }
    lineReader?.close();
    if (path.dirname(workspace) === path.resolve(os.tmpdir()) && path.basename(workspace).startsWith('AIVSP-ACP-Probe-')) await fs.rm(workspace, { recursive: true, force: true, maxRetries: 3 }).catch(() => {});
  }
}
main().catch(error => { console.error(error.message); process.exitCode = 1; });
