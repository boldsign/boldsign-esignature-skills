<?php
declare(strict_types=1);

// Shared start-up for the web entry point and the scripts.
require __DIR__ . '/../vendor/autoload.php';

App\Config::loadEnv(__DIR__ . '/../.env');
