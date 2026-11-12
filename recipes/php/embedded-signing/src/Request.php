<?php
declare(strict_types=1);

namespace App;

/** The few request details the app needs, so routes can be tested without a web server. */
final class Request
{
    /** @param array<string,string> $headers lower-case names */
    public function __construct(
        public string $method,
        public string $path,
        public array $headers = [],
        public string $body = '',
        public array $query = [],
        public array $form = [],
    ) {
    }

    public static function fromGlobals(): self
    {
        $headers = [];
        foreach ($_SERVER as $k => $v) {
            if (str_starts_with($k, 'HTTP_')) {
                $headers[strtolower(str_replace('_', '-', substr($k, 5)))] = (string) $v;
            }
        }
        if (isset($_SERVER['CONTENT_TYPE'])) {
            $headers['content-type'] = (string) $_SERVER['CONTENT_TYPE'];
        }
        return new self(
            $_SERVER['REQUEST_METHOD'] ?? 'GET',
            parse_url($_SERVER['REQUEST_URI'] ?? '/', PHP_URL_PATH) ?: '/',
            $headers,
            (string) file_get_contents('php://input'), // raw bytes, exactly as sent (the webhook signature covers them)
            $_GET,
            $_POST,
        );
    }

    public function header(string $name): string
    {
        return $this->headers[strtolower($name)] ?? '';
    }

    /** JSON body or form fields. */
    public function input(): array
    {
        $json = str_contains($this->header('content-type'), 'json') ? json_decode($this->body, true) : null;
        return is_array($json) ? $json : $this->form;
    }

    public function wantsHtml(): bool
    {
        $accept = $this->header('accept');
        return $accept !== '' && str_contains($accept, 'text/html') && !str_starts_with($accept, 'application/json');
    }
}
