import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {arcIsbnKey, arcMatches, storyArcReport} from '../cloudflare/public/story-arcs.js';
const arcs = JSON.parse(await readFile(new URL('../cloudflare/public/story-arcs.json', import.meta.url)));
const helix = arcs.find(arc => arc.id === 'double-helix');
const entry = (title, extra = {}) => ({book: {title, isbn:'', series:1, kind:0, format:0, ownership:0, bookNumber:'', ...extra}});
test('story reference has complete, sourced sets with valid ISBNs', () => {
  assert.equal(arcs.length,7);
  assert.equal(arcs.reduce((sum, arc) => sum + arc.parts.length,0),30);
  for (const arc of arcs) {
    assert.deepEqual(arc.parts.map(part => part.number), Array.from({length:arc.parts.length},(_,i)=>i+1));
    assert.ok(arc.sources.length && arc.series.length);
    assert.ok([...arc.parts.flatMap(part=>part.isbns), ...arc.omnibusIsbns].every(isbn=>arcIsbnKey(isbn)));
  }
});
test('Double Helix identifies the missing print part while recognizing ebook omnibus coverage', () => {
  const entries = helix.parts.slice(0,5).map(part=>entry(`Double Helix, Book ${part.number}: ${part.title}`,{isbn:part.isbns[0]}));
  entries.push(structuredClone(entries[0]),entry('Double Helix Omnibus',{format:2}));
  const before = JSON.stringify(entries);
  const print = storyArcReport(entries,arcs,helix,null,true);
  assert.equal(print.ownedCount,5);
  assert.deepEqual(print.slots.filter(slot=>slot.missing).map(slot=>slot.part.title),['The First Virtue']);
  assert.equal(print.slots[5].otherFormats.length,1);
  const all = storyArcReport(entries,arcs,helix,null);
  assert.equal(all.ownedCount,6);
  assert.equal(all.slots[5].status,'Owned via omnibus');
  assert.equal(JSON.stringify(entries),before);
  entries.push(entry('The First Virtue',{ownership:1}));
  assert.equal(storyArcReport(entries,arcs,helix,null,true).slots[5].status,'On wishlist');
  entries.push(entry('The First Virtue',{format:1}));
  assert.equal(storyArcReport(entries,arcs,helix,null,true).ownedCount,6);
  assert.equal(storyArcReport(entries,arcs,helix,0).ownedCount,5);
});
test('arc matching uses exact ISBN/title variants and reports uncertain entries', () => {
  const book = entry('Star Trek: The Next Generation: Double Helix, Book One: Infection').book;
  assert.equal(arcMatches(arcs,helix,helix.parts[0],book),true);
  assert.equal(arcMatches(arcs,helix,helix.parts[1],book),false);
  assert.equal(arcMatches(arcs,helix,helix.parts[0],{...book,kind:1}),false);
  assert.equal(arcMatches(arcs,helix,helix.parts[0],{...book,title:'Double Helix Infection unrelated sequel'}),false);
  const conflicting = {...book,isbn:helix.parts[1].isbns[0],series:3};
  assert.equal(arcMatches(arcs,helix,helix.parts[0],conflicting),false);
  assert.equal(arcMatches(arcs,helix,helix.parts[1],conflicting),true);
  assert.equal(storyArcReport([entry('Double Helix Collection')],arcs,helix,null).unmatched.length,1);
  const destiny = arcs.find(arc=>arc.id==='destiny');
  assert.equal(arcMatches(arcs,destiny,destiny.parts[2],entry('Lost Souls (Destiny, Book III)',{series:7}).book),true);
});
