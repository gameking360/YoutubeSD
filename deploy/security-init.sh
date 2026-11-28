#!/bin/bash
set -euo pipefail
umask 077

store=/run/youtubesd
# Serialize initializers sharing these volumes, including a retry after interruption.
exec 9<"$store/internal-key"
flock -x 9

generate_signing_key() {
    openssl genpkey -quiet -algorithm RSA -pkeyopt rsa_keygen_bits:3072 |
        openssl pkcs8 -topk8 -nocrypt -outform DER |
        openssl base64 -A
}

generate_token() {
    openssl rand -hex 32
}

valid_signing_key() {
    local bits
    bits=$(printf '%s' "$1" | tr -d ' \t\r\n' | base64 --decode |
        openssl pkcs8 -inform DER -nocrypt |
        openssl rsa -pubout 2>/dev/null |
        openssl pkey -pubin -noout -text |
        sed -nE 's/^Public-Key: \(([0-9]+) bit\).*/\1/p') || return 1
    [[ $bits -ge 2048 ]]
}

ensure_credential() {
    local directory=$1 supplied=$2 generator=$3
    local file="$store/$directory/value"
    if [[ -e $file ]]; then
        if [[ ! -s $file ]]; then
            echo "A credencial persistida em $file está vazia." >&2
            return 1
        fi
        return 0
    fi

    if [[ -z $supplied ]]; then
        supplied=$("$generator")
    elif [[ $directory == signing-key ]] && ! valid_signing_key "$supplied"; then
        echo 'SECURITY_SIGNING_KEY deve ser uma chave privada RSA PKCS#8 em Base64, com pelo menos 2048 bits.' >&2
        return 1
    fi
    printf '%s' "$supplied" > "$file.new"
    mv "$file.new" "$file"
}

ensure_credential signing-key "${SECURITY_SIGNING_KEY:-}" generate_signing_key
ensure_credential internal-key "${IDENTITY_INTERNAL_KEY:-}" generate_token
ensure_credential admin-token "${IDENTITY_ADMIN_TOKEN:-}" generate_token
