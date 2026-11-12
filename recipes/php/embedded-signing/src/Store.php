<?php
declare(strict_types=1);

namespace App;

/** A tiny JSON file store keyed by BoldSign document ID. Replace it with your database. */
final class Store
{
    public function __construct(private string $path)
    {
    }

    /** @return array<string,array<string,mixed>> */
    private function read(): array
    {
        $raw = @file_get_contents($this->path);
        $data = $raw === false ? null : json_decode($raw, true);
        return is_array($data) ? $data : [];
    }

    /** @return list<array<string,mixed>> */
    public function all(): array
    {
        $all = array_values($this->read());
        usort($all, static fn ($a, $b) => strcmp((string) ($b['createdAt'] ?? ''), (string) ($a['createdAt'] ?? '')));
        return $all;
    }

    /** @return array<string,mixed>|null */
    public function get(string $documentId): ?array
    {
        return $this->read()[$documentId] ?? null;
    }

    /** @param array<string,mixed> $record */
    public function save(array $record): array
    {
        $dir = dirname($this->path);
        if (!is_dir($dir)) {
            mkdir($dir, 0775, true);
        }
        $fh = fopen($this->path, 'c+');
        flock($fh, LOCK_EX);
        $raw = stream_get_contents($fh);
        $data = $raw ? (json_decode($raw, true) ?: []) : [];
        $data[$record['documentId']] = $record;
        ftruncate($fh, 0);
        rewind($fh);
        fwrite($fh, json_encode($data, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES));
        flock($fh, LOCK_UN);
        fclose($fh);
        return $record;
    }
}
