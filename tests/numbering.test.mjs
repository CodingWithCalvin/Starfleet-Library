import test from 'node:test';
import assert from 'node:assert/strict';
import {parseBookNumber, numberedReport, suggestedNumberEnd} from '../cloudflare/public/catalog.js';

test('numbered gaps count duplicates once and distinguish formats, wishlist and comics', () => {
  const entry = (number, extra = {}) => ({book: {title: 'Test', series: 1, kind: 0, ownership: 0, format: 0, bookNumber: number, ...extra}});
  const entries = [entry('#01'), entry('1'), entry('2', {format: 2}), entry('3', {ownership: 1}), entry('4', {kind: 1}), entry(''), entry('Book 4'), entry('9'), entry('4', {series: 3})];
  const before = JSON.stringify(entries);
  const report = numberedReport(entries, 1, 0, 1, 4);
  assert.deepEqual(report.slots.map(slot => slot.status), ['Owned', 'Missing', 'On wishlist', 'Missing']);
  assert.equal(report.slots[0].owned.length, 2);
  assert.equal(report.slots[1].otherFormats.length, 1);
  assert.equal(report.slots[2].missing, true);
  assert.equal(report.needsNumber.length, 2);
  assert.equal(report.outsideRange.length, 1);
  assert.equal(numberedReport(entries, 1, null, 1, 4).slots.filter(slot => !slot.missing).length, 2);
  const physical = numberedReport([...entries, entry('4', {format: 1}), entry('', {format: 2})], 1, null, 1, 4, true);
  assert.deepEqual(physical.slots.map(slot => slot.missing), [false, true, true, false]);
  assert.equal(physical.needsNumber.length, 2);
  assert.equal(JSON.stringify(entries), before);
  assert.equal(numberedReport([], 1, null, 1, 4).slots.filter(slot => slot.missing).length, 4);
});
test('numbered ranges and number parsing are bounded and conservative', () => {
  for (const value of ['', '1.5', '-1', '0', '1001', '1-3', '##1', 'Book 1', '２', '999999999999999999']) assert.equal(parseBookNumber(value), null);
  assert.equal(parseBookNumber(' #001 '), 1);
  assert.equal(parseBookNumber('1000'), 1000);
  assert.equal(suggestedNumberEnd([], 1), 63);
  assert.equal(suggestedNumberEnd([], 4), 0);
  for (const [first, last] of [[0, 5], [5, 4], [1, 1001], [1, 1.5], [NaN, 5]]) assert.throws(() => numberedReport([], 1, null, first, last), RangeError);
});
