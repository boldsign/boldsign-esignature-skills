using System.Text;

namespace Recipe;

/// <summary>
/// A one-page PDF with BoldSign text tags for a signature and a date, drawn in white so they do not show on the signed
/// copy. Send it with UseTextTags = true: no template and no field coordinates are needed.
/// Tag syntax: {{fieldType|signerIndex|required}}; spaces inside the braces widen the field, a larger font makes it taller.
/// https://developers.boldsign.com/text-tags/introduction/
/// </summary>
public static class SamplePdf
{
    public const string SignTag = "{{sign|1|*                    }}";
    public const string DateTag = "{{date|1|*          }}";

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    public static byte[] Build(string title = "Policy acknowledgement", string signerName = "the signer")
    {
        var content = string.Join("\n",
            $"0 0 0 rg BT /F1 20 Tf 72 720 Td ({Esc(title)}) Tj ET",
            $"0 0 0 rg BT /F1 12 Tf 72 690 Td (I, {Esc(signerName)}, confirm that I have read and accept this policy.) Tj ET",
            "0 0 0 rg BT /F1 12 Tf 72 600 Td (Signature:) Tj ET",
            $"1 1 1 rg BT /F1 20 Tf 160 596 Td ({Esc(SignTag)}) Tj ET",
            "0 0 0 rg BT /F1 12 Tf 72 540 Td (Date:) Tj ET",
            $"1 1 1 rg BT /F1 12 Tf 160 540 Td ({Esc(DateTag)}) Tj ET");
        var latin1 = Encoding.Latin1;
        var body = latin1.GetBytes(content);
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {body.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(latin1.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = latin1.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) pdf.Append($"{o:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return latin1.GetBytes(pdf.ToString());
    }
}
