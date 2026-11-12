"""A one-page PDF with BoldSign text tags for a signature and a date, drawn in white so they do not show on the signed
copy. Send it with use_text_tags=True: no template and no field coordinates are needed.
Tag syntax: {{fieldType|signerIndex|required}}; spaces inside the braces widen the field, a larger font makes it taller.
https://developers.boldsign.com/text-tags/introduction/
"""
SIGN_TAG = "{{sign|1|*                    }}"
DATE_TAG = "{{date|1|*          }}"


def _esc(s: str) -> str:
    return s.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")


def sample_pdf(title: str = "Policy acknowledgement", signer_name: str = "the signer") -> bytes:
    lines = [
        f"0 0 0 rg BT /F1 20 Tf 72 720 Td ({_esc(title)}) Tj ET",
        f"0 0 0 rg BT /F1 12 Tf 72 690 Td (I, {_esc(signer_name)}, confirm that I have read and accept this policy.) Tj ET",
        "0 0 0 rg BT /F1 12 Tf 72 600 Td (Signature:) Tj ET",
        f"1 1 1 rg BT /F1 20 Tf 160 596 Td ({_esc(SIGN_TAG)}) Tj ET",
        "0 0 0 rg BT /F1 12 Tf 72 540 Td (Date:) Tj ET",
        f"1 1 1 rg BT /F1 12 Tf 160 540 Td ({_esc(DATE_TAG)}) Tj ET",
    ]
    content = "\n".join(lines).encode("latin-1")
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
        b"<< /Length " + str(len(content)).encode() + b" >>\nstream\n" + content + b"\nendstream",
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    ]
    pdf, offsets = b"%PDF-1.4\n", []
    for i, obj in enumerate(objects, start=1):
        offsets.append(len(pdf))
        pdf += f"{i} 0 obj\n".encode() + obj + b"\nendobj\n"
    xref = len(pdf)
    pdf += f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n".encode()
    pdf += b"".join(f"{n:010d} 00000 n \n".encode() for n in offsets)
    pdf += f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode()
    return pdf
