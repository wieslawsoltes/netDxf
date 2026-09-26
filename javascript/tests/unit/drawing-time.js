import test from 'node:test';
import assert from 'node:assert/strict';
import { DrawingTime, HeaderDateTime, HeaderTimeSpan } from '../../index.js';
import { DrawingTime as Standalone } from '../../netDxf/Units/DrawingTime.js';
import { drawingTimeCorpus } from '../../tools/drawing-time-corpus.mjs';

test('DrawingTime barrel and standalone exports are identical', () => assert.equal(DrawingTime, Standalone));
test('DrawingTime uses the original integer-day epoch rather than astronomical noon', () => {
  assert.equal(DrawingTime.ToJulianCalendar(HeaderDateTime.MinValue), 1721426);
  assert.equal(DrawingTime.ToJulianCalendar(new HeaderDateTime(621355968000000000n)), 2440588);
  assert.equal(DrawingTime.ToJulianCalendar(new HeaderDateTime(630822816000000000n)), 2451545);
});
test('DrawingTime reads wall-clock components without converting DateTimeKind', () => {
  const ticks = 637185312001234567n;
  const values = [0, 1, 2].map(kind => DrawingTime.ToJulianCalendar(new HeaderDateTime(ticks, kind)));
  assert.equal(values[0], values[1]); assert.equal(values[1], values[2]);
});
test('DrawingTime intentionally truncates sub-millisecond DateTime ticks', () => {
  const whole = new HeaderDateTime(637185312001230000n), fractional = new HeaderDateTime(637185312001239999n);
  assert.equal(DrawingTime.ToJulianCalendar(whole), DrawingTime.ToJulianCalendar(fractional));
  assert.equal(fractional.Ticks, 637185312001239999n);
});
test('DrawingTime restores unspecified dates and exact day endpoints', () => {
  const first = DrawingTime.FromJulianCalendar(1721426), last = DrawingTime.FromJulianCalendar(5373484);
  assert.equal(first.Ticks, 0n); assert.equal(first.Kind, 0);
  assert.deepEqual(last.Calendar, [9999,12,31,0,0,0,0]);
  assert.deepEqual(DrawingTime.FromJulianCalendar(2451545.5).Calendar, [2000,1,1,12,0,0,0]);
});
test('DrawingTime preserves Julian range checks including the last-day fraction rejection', () => {
  for (const value of [1721425.999, 5373484.001, Infinity, -Infinity])
    assert.throws(() => DrawingTime.FromJulianCalendar(value), { name: 'ArgumentOutOfRangeException', ParamName: 'date' });
});
test('DrawingTime retains component validation order for NaN Julian inputs', () => {
  assert.throws(() => DrawingTime.FromJulianCalendar(NaN), { name: 'ArgumentOutOfRangeException', ParamName: 'millisecond' });
});
test('DrawingTime durations support negative fractional days without timezone conversion', () => {
  for (const sign of [1, -1]) {
    const span = DrawingTime.EditingTime(sign * 1.25);
    assert.ok(span instanceof HeaderTimeSpan);
    assert.equal(span.Ticks, BigInt(sign) * 1080000000000n);
  }
  assert.equal(DrawingTime.EditingTime(-0).Ticks, 0n);
});
test('DrawingTime rejects duration overflow instead of truncating to an Int64', () => {
  for (const value of [NaN, Infinity, -Infinity, 2147483648, -2147483649])
    assert.throws(() => DrawingTime.EditingTime(value), { name: 'ArgumentOutOfRangeException', ParamName: null });
});
test('DrawingTime corpus is deterministic and independently describes every input', () => {
  const corpus = drawingTimeCorpus();
  assert.deepEqual(corpus, drawingTimeCorpus()); assert.equal(corpus.length, 1497);
  assert.equal(new Set(corpus.map(p => p.name)).size, 1497);
  assert.equal(corpus.reduce((sum, p) => sum + p.request.steps.length, 0), 3879);
  assert.ok(corpus.every(p => !Object.hasOwn(p, 'expected')));
});
