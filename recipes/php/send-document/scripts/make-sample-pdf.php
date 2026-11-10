<?php
declare(strict_types=1);

require __DIR__ . '/../src/bootstrap.php';

file_put_contents('sample.pdf', App\SamplePdf::build());
echo "Wrote sample.pdf (open it: the text tags are white, so only the labels show).\n";
