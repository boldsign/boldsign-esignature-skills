import fs from 'node:fs';
import { samplePdf } from '../src/sample-pdf.js';
fs.writeFileSync('sample.pdf', samplePdf());
console.log('Wrote sample.pdf (open it: the text tags are white, so only the labels show).');
