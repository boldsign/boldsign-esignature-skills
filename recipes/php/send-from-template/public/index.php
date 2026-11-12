<?php
declare(strict_types=1);

// Front controller. Local: composer start (php -S 127.0.0.1:3000 public/index.php). Production: point your web server here.
require __DIR__ . '/../src/bootstrap.php';

use App\{App, BoldSignClient, Config, Request, Store};

$cfg = Config::fromEnv();
$store = new Store(__DIR__ . '/../' . $cfg->storeFile);
(new App($cfg, new BoldSignClient($cfg), $store))->handle(Request::fromGlobals())->send();
