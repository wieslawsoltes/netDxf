// Input-only calendar/duration scenarios; no native results are embedded.
import { doubleBits } from './wire.mjs';
export function drawingTimeCorpus() {
  const cases = [];
  const call = (member, value, signature = 'Double', id) => ({ kind: 'call', type: 'Units.DrawingTime', member,
    args: [value], signature: [signature], ...(id ? { id } : {}) });
  const add = (name, category, steps) => cases.push({ name: 'drawing-time/' + name, category, request: { steps } });
  const number = n => ({ double: doubleBits(n) });
  const from = value => call('FromJulianCalendar', value);
  const elapsed = value => call('EditingTime', value);
  const date = (ticks, kind) => ({ datetime: { ticks: String(ticks), kind } });
  const timestamp = (name, ticks) => {
    for (const kind of [0, 1, 2]) add(`timestamp/${name}/${kind}`, 'timestamps', [
      call('ToJulianCalendar', date(ticks, kind), 'DateTime', 'julian'),
      call('FromJulianCalendar', { ref: 'julian' }, 'Double', 'restored'),
      call('ToJulianCalendar', { ref: 'restored' }, 'DateTime')
    ]);
  };
  // Include sub-millisecond ticks, century leap rules, Gregorian transition dates,
  // the DateTime endpoints and a modern wall-clock value in all three kinds.
  const namedTicks = [0n, 1n, 9999n, 10000n, 863999999999n, 864000000000n,
    31241376000000000n, 499163040000000000n, 599266080000000000n,
    621355968000000000n, 630822816000000000n, 637185312001234567n,
    639256032001234567n, 3155378112000000000n, 3155378975999999999n];
  namedTicks.forEach((ticks, i) => timestamp('edge-' + i, ticks));
  const edges = [NaN, Infinity, -Infinity, 0, -0, Number.MIN_VALUE, -Number.MIN_VALUE,
    Number.MAX_VALUE, -Number.MAX_VALUE, 1 / 86400000, -1 / 86400000, 0.125, -0.125,
    0.5, -0.5, 1, -1, 1.23456789, -1.23456789, 1721425, 1721426, 2299160, 2299161,
    2440588, 2451545, 5373483, 5373484, 5373485, 10675199.116730064, -10675199.116730064,
    10675199.116730066, -10675199.116730066, 2147483647, -2147483648, 2147483648, -2147483649];
  for (const n of edges) {
    const bits = BigInt('0x' + doubleBits(n));
    for (const offset of [-1n, 0n, 1n]) {
      const value = { double: BigInt.asUintN(64, bits + offset).toString(16).padStart(16, '0').toUpperCase() };
      add(`boundary/${value.double}`, 'boundaries', [from(value), elapsed(value)]);
    }
  }
  // Distinct quiet/signalling NaNs with payloads, not a canonical NaN sentinel.
  for (const double of ['7FF0000000000001','FFF0000000000001','7FF8000000000042','FFF8000000000042'])
    add('payload/' + double, 'nonfinite', [from({ double }), elapsed({ double })]);
  for (let hour = 0; hour < 24; hour++) for (const ms of [0, 1, 999]) {
    const n = 2451545 + (hour * 3600000 + 59 * 60000 + 59 * 1000 + ms) / 86400000;
    add(`clock/${hour}/${ms}`, 'clock-fractions', [from(number(n)), elapsed(number(n - 2451545)), elapsed(number(2451545 - n))]);
  }
  let state = 0x1da7e001;
  const random = () => (state = (Math.imul(state, 1664525) + 1013904223) >>> 0);
  for (let i = 0; i < 256; i++) {
    const day = random() % 3652059, tick = (BigInt(random()) << 32n | BigInt(random())) % 864000000000n;
    timestamp('random-' + i, BigInt(day) * 864000000000n + tick);
    const julian = 1721426 + (random() / 4294967296) * (5373484 - 1721426);
    const duration = (random() / 4294967296 - 0.5) * 22000000;
    add('random-values/' + i, 'randomized', [from(number(julian)), elapsed(number(duration))]);
    const bits = (BigInt(random()) << 32n | BigInt(random())).toString(16).padStart(16, '0').toUpperCase();
    add('random-bits/' + i, 'binary64', [from({ double: bits }), elapsed({ double: bits })]);
  }
  // Boundary neighborhoods overlap intentionally above; keep one scenario per bit input.
  return [...new Map(cases.map(item => [item.name, item])).values()];
}
