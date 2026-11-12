<?php
declare(strict_types=1);

namespace App;

final class Response
{
    /** @param array<string,string> $headers */
    public function __construct(public int $status = 200, public string $body = '', public array $headers = [])
    {
    }

    public static function json(mixed $data, int $status = 200): self
    {
        return new self($status, json_encode($data, JSON_UNESCAPED_SLASHES), ['Content-Type' => 'application/json']);
    }

    public static function html(string $html, int $status = 200, array $headers = []): self
    {
        return new self($status, $html, ['Content-Type' => 'text/html; charset=utf-8'] + $headers);
    }

    public static function redirect(string $to, int $status = 302): self
    {
        return new self($status, '', ['Location' => $to]);
    }

    public function send(): void
    {
        http_response_code($this->status);
        foreach ($this->headers as $k => $v) {
            header("$k: $v");
        }
        echo $this->body;
    }
}
