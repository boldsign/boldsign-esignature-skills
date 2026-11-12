import fs from 'node:fs';
import path from 'node:path';

// A tiny JSON file store keyed by BoldSign document ID. Replace with your database.
export function createStore(file) {
  const read = () => { try { return JSON.parse(fs.readFileSync(file, 'utf8')); } catch { return {}; } };
  const write = (all) => { fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, JSON.stringify(all, null, 2)); };
  return {
    all: () => Object.values(read()).sort((a, b) => (a.createdAt < b.createdAt ? 1 : -1)),
    get: (id) => read()[id],
    save(record) { const all = read(); all[record.documentId] = record; write(all); return record; },
  };
}
