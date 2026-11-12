<?php
declare(strict_types=1);

namespace App;

/**
 * Maps each template role to a signer from your application. Edit signerForRole for your data model: here every role
 * is filled by the demo user (SIGNER_NAME, SIGNER_EMAIL), so a one-role template works as is.
 */
final class Roles
{
    /** @return array{name:string,email:string} */
    public static function signerForRole(array $role, array $user): array
    {
        return ['name' => (string) ($user['name'] ?? ''), 'email' => (string) ($user['email'] ?? '')];
    }

    /** Builds the role list for /v1/template/send. Fails loudly if a role has no signer. */
    public static function map(array $templateRoles, array $user): array
    {
        return array_map(function (array $role) use ($user) {
            $signer = self::signerForRole($role, $user);
            if ($signer['email'] === '') {
                throw new \InvalidArgumentException('No signer for template role "' . $role['name'] . '"');
            }
            return ['role_index' => $role['index'], 'signer_role' => $role['name'], 'signer_name' => $signer['name'], 'signer_email' => $signer['email']];
        }, $templateRoles);
    }

    /** Compares the configured roles with the template's own roles (from GET /v1/template/properties). */
    public static function compare(array $configured, array $fromTemplate): array
    {
        $key = fn (array $r) => $r['index'] . ':' . strtolower((string) $r['name']);
        $want = array_map($key, $configured);
        $have = array_map($key, $fromTemplate);
        return [
            'missing_in_template' => array_values(array_filter($configured, fn ($r) => !in_array($key($r), $have, true))),
            'not_configured' => array_values(array_filter($fromTemplate, fn ($r) => !in_array($key($r), $want, true))),
        ];
    }
}
