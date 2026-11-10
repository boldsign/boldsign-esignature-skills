<?php
declare(strict_types=1);

namespace App;

/**
 * A one-page PDF with BoldSign text tags for a signature and a date, drawn in white so they do not show on the signed
 * copy. Send it with setUseTextTags(true): no template and no field coordinates are needed.
 * Tag syntax: {{fieldType|signerIndex|required}}; spaces inside the braces widen the field, a larger font makes it taller.
 * https://developers.boldsign.com/text-tags/introduction/
 */
final class SamplePdf
{
    public const SIGN_TAG = '{{sign|1|*                    }}';
    public const DATE_TAG = '{{date|1|*          }}';

    private static function esc(string $s): string
    {
        return str_replace(['\\', '(', ')'], ['\\\\', '\\(', '\\)'], $s);
    }

    public static function build(string $title = 'Policy acknowledgement', string $signerName = 'the signer'): string
    {
        $content = implode("\n", [
            '0 0 0 rg BT /F1 20 Tf 72 720 Td (' . self::esc($title) . ') Tj ET',
            '0 0 0 rg BT /F1 12 Tf 72 690 Td (I, ' . self::esc($signerName) . ', confirm that I have read and accept this policy.) Tj ET',
            '0 0 0 rg BT /F1 12 Tf 72 600 Td (Signature:) Tj ET',
            '1 1 1 rg BT /F1 20 Tf 160 596 Td (' . self::esc(self::SIGN_TAG) . ') Tj ET',
            '0 0 0 rg BT /F1 12 Tf 72 540 Td (Date:) Tj ET',
            '1 1 1 rg BT /F1 12 Tf 160 540 Td (' . self::esc(self::DATE_TAG) . ') Tj ET',
        ]);
        $content = mb_convert_encoding($content, 'ISO-8859-1', 'UTF-8');
        $objects = [
            '<< /Type /Catalog /Pages 2 0 R >>',
            '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
            '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
            '<< /Length ' . strlen($content) . " >>\nstream\n" . $content . "\nendstream",
            '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
        ];
        $pdf = "%PDF-1.4\n";
        $offsets = [];
        foreach ($objects as $i => $obj) {
            $offsets[] = strlen($pdf);
            $pdf .= ($i + 1) . " 0 obj\n" . $obj . "\nendobj\n";
        }
        $xref = strlen($pdf);
        $pdf .= 'xref' . "\n0 " . (count($objects) + 1) . "\n0000000000 65535 f \n";
        foreach ($offsets as $o) {
            $pdf .= sprintf("%010d 00000 n \n", $o);
        }
        return $pdf . 'trailer' . "\n<< /Size " . (count($objects) + 1) . " /Root 1 0 R >>\nstartxref\n$xref\n%%EOF\n";
    }
}
