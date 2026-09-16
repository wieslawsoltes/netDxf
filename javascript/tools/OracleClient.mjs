import { spawn } from 'node:child_process';
import readline from 'node:readline';
import path from 'node:path';
import { dotnetCommand, oracleRoot } from './dotnet.mjs';
/** One long-lived, ordered JSONL process. Any crash/timeout fails pending work. */
export class OracleClient {
  #process; #pending = []; #failure; #closed = false; #stderr = '';
  constructor() {
    this.#process = spawn(dotnetCommand, [path.join(oracleRoot, 'Oracle.dll')], { stdio: ['pipe', 'pipe', 'pipe'] });
    this.#process.stderr.on('data', data => { this.#stderr = (this.#stderr + data).slice(-16384); });
    this.#process.stdin.on('error', error => this.#fail(error));
    this.#process.on('error', error => this.#fail(error));
    this.#process.on('exit', code => {
      if (!this.#closed || code !== 0) this.#fail(new Error(`Oracle exited (${code}): ${this.#stderr}`));
    });
    readline.createInterface({ input: this.#process.stdout, crlfDelay: Infinity }).on('line', line => {
      const next = this.#pending.shift();
      if (!next) return this.#fail(new Error('Unsolicited oracle response.'));
      clearTimeout(next.timer);
      try { next.resolve(JSON.parse(line)); } catch (error) { next.reject(error); this.#fail(error); }
    });
  }
  #fail(error) {
    if (this.#failure) return;
    this.#failure = error;
    for (const item of this.#pending.splice(0)) { clearTimeout(item.timer); item.reject(error); }
    this.#process.kill();
  }
  request(input) {
    if (this.#failure || this.#closed) return Promise.reject(this.#failure || new Error('Oracle is closed.'));
    return new Promise((resolve, reject) => {
      const item = { resolve, reject, timer: setTimeout(() => this.#fail(new Error('Oracle request timed out.')), 60000) };
      this.#pending.push(item);
      this.#process.stdin.write(JSON.stringify(input) + '\n');
    });
  }
  async close() {
    if (this.#closed) return;
    this.#closed = true;
    if (this.#pending.length) { this.#fail(new Error('Oracle closed with pending requests.')); return; }
    const exited = new Promise(resolve => this.#process.once('exit', resolve));
    this.#process.stdin.end();
    if (this.#process.exitCode === null && !this.#process.killed) await exited;
  }
}
