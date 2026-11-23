package br.furb.youtubesd.download.security;

import com.nimbusds.jose.jwk.JWKSet;
import com.nimbusds.jose.jwk.RSAKey;
import com.nimbusds.jose.jwk.gen.RSAKeyGenerator;
import com.nimbusds.jose.jwk.source.ImmutableJWKSet;
import com.sun.net.httpserver.HttpServer;
import org.junit.jupiter.api.Test;
import org.springframework.security.oauth2.jose.jws.SignatureAlgorithm;
import org.springframework.security.oauth2.jwt.*;

import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

class JwksIntegrationTest {
    @Test
    void validatesFromHttpJwksRejectsInvalidClaimsAndKeepsCachedKeyDuringOutage() throws Exception {
        RSAKey key = new RSAKeyGenerator(2048).keyID("shared-key").generate();
        HttpServer server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        byte[] jwks = new JWKSet(key.toPublicJWK()).toString().getBytes(StandardCharsets.UTF_8);
        server.createContext("/api/auth/.well-known/jwks.json", exchange -> {
            exchange.getResponseHeaders().set("Content-Type", "application/json");
            exchange.sendResponseHeaders(200, jwks.length);
            try (var body = exchange.getResponseBody()) { body.write(jwks); }
        });
        server.start();
        try {
            JwtDecoder decoder = new SecurityConfig().securityJwtDecoder(
                    "http://127.0.0.1:" + server.getAddress().getPort() + "/api/auth/.well-known/jwks.json",
                    "youtubesd-security", "youtubesd-api");
            String valid = token(key, "youtubesd-security", "youtubesd-api", Instant.now().plusSeconds(900));
            assertEquals("owner", decoder.decode(valid).getSubject());
            assertThrows(JwtException.class, () -> decoder.decode(token(key, "other", "youtubesd-api", Instant.now().plusSeconds(900))));
            assertThrows(JwtException.class, () -> decoder.decode(token(key, "youtubesd-security", "other", Instant.now().plusSeconds(900))));
            assertThrows(JwtException.class, () -> decoder.decode(token(key, "youtubesd-security", "youtubesd-api", Instant.now().minusSeconds(120))));
            RSAKey other = new RSAKeyGenerator(2048).keyID("shared-key").generate();
            assertThrows(JwtException.class, () -> decoder.decode(token(other, "youtubesd-security", "youtubesd-api", Instant.now().plusSeconds(900))));
            server.stop(0);
            assertEquals("owner", decoder.decode(valid).getSubject());
        } finally { server.stop(0); }
    }

    private static String token(RSAKey key, String issuer, String audience, Instant expires) {
        var encoder = new NimbusJwtEncoder(new ImmutableJWKSet<>(new JWKSet(key)));
        var claims = JwtClaimsSet.builder().subject("owner").issuer(issuer).audience(List.of(audience))
                .issuedAt(expires.minusSeconds(900)).expiresAt(expires).claim("roles", List.of("USER")).build();
        return encoder.encode(JwtEncoderParameters.from(JwsHeader.with(SignatureAlgorithm.RS256).keyId(key.getKeyID()).build(), claims)).getTokenValue();
    }
}
