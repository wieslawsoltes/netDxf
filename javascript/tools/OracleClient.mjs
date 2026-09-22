import { acquireOracleLease } from './oracle-leases.mjs';
import { spawn } from 'node:child_process';
import readline from 'node:readline';
import path from 'node:path';
import { dotnetCommand, oracleRoot } from './dotnet.mjs';

/** Ordered JSONL oracle. Process failure, invalid output and deadlines fail closed. */
export class OracleClient {
  #process;
  #lease;
  #pending = [];
  #failure = null;
  #closed = false;
  #exited = false;
  #stderr = '';
  #exit;
  #timeout;
  #killTimer;

  constructor({ executable = dotnetCommand, args = [path.join(oracleRoot, 'Oracle.dll')], timeout = 60000 } = {}) {
    if (!Number.isFinite(timeout) || timeout <= 0) throw new RangeError('A positive oracle timeout is required.');
    this.#timeout = timeout;
    if (args.some(arg => typeof arg === 'string' && arg.endsWith('.dll') && path.dirname(path.resolve(arg)) === oracleRoot))
      this.#lease = acquireOracleLease(oracleRoot, { childProcess: true });
    try { this.#process = spawn(executable, args, {
      stdio: ['pipe', 'pipe', 'pipe'],
      env: { ...process.env, DOTNET_GCHeapHardLimit: process.env.DOTNET_GCHeapHardLimit || '0x20000000' },
    }); this.#lease?.bind(this.#process.pid); }
    catch (error) { this.#process?.kill(); this.#lease?.release(); throw error; }
    // Register before any request. Waiting for an already emitted exit event used to
    // leave close() unsettled and suppress the original failure and evidence report.
    this.#exit = new Promise(resolve => {
      this.#process.once('close', (code, signal) => {
        this.#exited = true;
        try { this.#lease?.release(); } catch (error) { this.#fail(error); }
        clearTimeout(this.#killTimer);
        if (!this.#closed || code !== 0 || this.#pending.length)
          this.#fail(new Error(`Oracle exited (${code}; signal ${signal ?? 'none'}): ${this.#stderr}`));
        resolve();
      });
    });
    this.#process.stderr.on('data', data => { this.#stderr = (this.#stderr + data).slice(-16384); });
    this.#process.stdin.on('error', error => this.#fail(error));
    this.#process.on('error', error => this.#fail(error));
    readline.createInterface({ input: this.#process.stdout, crlfDelay: Infinity }).on('line', line => {
      const next = this.#pending.shift();
      if (!next) { this.#fail(new Error('Unsolicited oracle response.')); return; }
      clearTimeout(next.timer);
      try { next.resolve(JSON.parse(line)); }
      catch (error) { next.reject(error); this.#fail(error); }
    });
  }

  #fail(error) {
    if (this.#failure) return;
    this.#failure = error;
    for (const item of this.#pending.splice(0)) { clearTimeout(item.timer); item.reject(error); }
    if (!this.#exited) {
      this.#process.kill();
      this.#killTimer = setTimeout(() => this.#process.kill('SIGKILL'), 2000);
    }
  }

  request(input) {
    if (this.#failure || this.#closed) return Promise.reject(this.#failure || new Error('Oracle is closed.'));
    let line;
    try { line = JSON.stringify(input) + '\n'; }
    catch (error) { return Promise.reject(error); }
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => this.#fail(new Error('Oracle request timed out.')), this.#timeout);
      this.#pending.push({ resolve, reject, timer });
      this.#process.stdin.write(line);
    });
  }

  async close() {
    if (!this.#closed) {
      this.#closed = true;
      if (this.#pending.length) this.#fail(new Error('Oracle closed with pending requests.'));
      this.#process.stdin.end();
    }
    await this.#exit;
    if (this.#failure) throw this.#failure;
  }
}
