// Builds a one-page PDF with BoldSign text tags for a signature and a date, drawn in white so they do not
// show on the signed copy. Send it with useTextTags: true: no template and no field coordinates are needed.
// Tag syntax: {{fieldType|signerIndex|required}}; spaces inside the braces make the field wider, a larger font taller.
// https://developers.boldsign.com/text-tags/introduction/
export const SIGN_TAG = '{{sign|1|*                    }}';
export const DATE_TAG = '{{date|1|*          }}';

const esc = (s) => String(s).replace(/[\\()]/g, (c) => `\\${c}`);

export function samplePdf({ title = 'Policy acknowledgement', signerName = 'the signer' } = {}) {
  const lines = [
    ['0 0 0 rg', `BT /F1 20 Tf 72 720 Td (${esc(title)}) Tj ET`],
    ['0 0 0 rg', `BT /F1 12 Tf 72 690 Td (I, ${esc(signerName)}, confirm that I have read and accept this policy.) Tj ET`],
    ['0 0 0 rg', 'BT /F1 12 Tf 72 600 Td (Signature:) Tj ET'],
    ['1 1 1 rg', `BT /F1 20 Tf 160 596 Td (${esc(SIGN_TAG)}) Tj ET`], // larger font = taller field
    ['0 0 0 rg', 'BT /F1 12 Tf 72 540 Td (Date:) Tj ET'],
    ['1 1 1 rg', `BT /F1 12 Tf 160 540 Td (${esc(DATE_TAG)}) Tj ET`],
  ];
  const content = lines.map(([colour, text]) => `${colour} ${text}`).join('\n');
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
    `<< /Length ${Buffer.byteLength(content, 'latin1')} >>\nstream\n${content}\nendstream`,
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
  ];
  let pdf = '%PDF-1.4\n';
  const offsets = [];
  objects.forEach((o, i) => { offsets.push(Buffer.byteLength(pdf, 'latin1')); pdf += `${i + 1} 0 obj\n${o}\nendobj\n`; });
  const xref = Buffer.byteLength(pdf, 'latin1');
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n${offsets.map((n) => `${String(n).padStart(10, '0')} 00000 n \n`).join('')}`;
  pdf += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(pdf, 'latin1');
}
