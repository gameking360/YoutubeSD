package br.furb.youtubesd.download.security;

import br.furb.youtubesd.download.TestcontainersConfiguration;
import br.furb.youtubesd.download.catalog.CatalogRepository;
import br.furb.youtubesd.download.catalog.Rendition;
import br.furb.youtubesd.download.catalog.VideoPublishedPayload;
import br.furb.youtubesd.download.catalog.Visibility;
import com.nimbusds.jose.JOSEException;
import com.nimbusds.jose.jwk.JWKSet;
import com.nimbusds.jose.jwk.RSAKey;
import com.nimbusds.jose.jwk.gen.RSAKeyGenerator;
import com.nimbusds.jose.jwk.source.ImmutableJWKSet;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.context.annotation.Primary;
import org.springframework.http.MediaType;
import org.springframework.security.oauth2.jose.jws.SignatureAlgorithm;
import org.springframework.security.oauth2.jwt.JwsHeader;
import org.springframework.security.oauth2.jwt.JwtClaimsSet;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.security.oauth2.jwt.JwtEncoderParameters;
import org.springframework.security.oauth2.jwt.NimbusJwtDecoder;
import org.springframework.security.oauth2.jwt.NimbusJwtEncoder;
import org.springframework.security.oauth2.jwt.JwtValidators;
import org.springframework.security.oauth2.jwt.JwtClaimValidator;
import org.springframework.security.oauth2.core.DelegatingOAuth2TokenValidator;
import org.springframework.test.web.servlet.MockMvc;

import java.time.Instant;
import java.util.List;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.put;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@SpringBootTest(properties = "download.auth.mode=jwt")
@AutoConfigureMockMvc
@Import({TestcontainersConfiguration.class, JwtModeTest.Keys.class})
class JwtModeTest {

    private static final RSAKey SECURITY_KEY = newKey();
    private static final RSAKey OTHER_KEY = newKey();
    private static final String PROGRESS = "/api/download/progress/{videoId}";

    @TestConfiguration(proxyBeanMethods = false)
    static class Keys {
        @Bean
        @Primary
        JwtDecoder jwtDecoder() throws JOSEException {
            var decoder = NimbusJwtDecoder.withPublicKey(SECURITY_KEY.toRSAPublicKey()).build();
            decoder.setJwtValidator(new DelegatingOAuth2TokenValidator<>(
                    JwtValidators.createDefaultWithIssuer("youtubesd-security"),
                    new JwtClaimValidator<List<String>>("aud", aud -> aud != null && aud.contains("youtubesd-api"))));
            return decoder;
        }
    }

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private CatalogRepository catalog;

    @Test
    void progressoSemTokenRetorna401() throws Exception {
        mockMvc.perform(get(PROGRESS, "v1"))
                .andExpect(status().isUnauthorized())
                .andExpect(header().string("WWW-Authenticate", "Bearer"))
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_PROBLEM_JSON))
                .andExpect(jsonPath("$.status").value(401));
    }

    @Test
    void usuarioVemDoSubDoTokenEOHeaderEIgnorado() throws Exception {
        String token = token("jwt-user", SECURITY_KEY, Instant.now().plusSeconds(300));

        mockMvc.perform(put(PROGRESS, "jwt-video")
                        .header("Authorization", "Bearer " + token)
                        .header("X-User-Id", "outra-pessoa")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"positionSec\": 42, \"durationSec\": 600, \"clientTs\": \"2026-11-21T10:00:00Z\"}"))
                .andExpect(status().isOk());

        mockMvc.perform(get(PROGRESS, "jwt-video").header("Authorization", "Bearer " + token))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.positionSec").value(42));

        String outro = token("outra-pessoa", SECURITY_KEY, Instant.now().plusSeconds(300));
        mockMvc.perform(get(PROGRESS, "jwt-video").header("Authorization", "Bearer " + outro))
                .andExpect(status().isNotFound());
    }

    @Test
    void tokenAssinadoPorOutraChaveRetorna401() throws Exception {
        String falso = token("jwt-user", OTHER_KEY, Instant.now().plusSeconds(300));

        mockMvc.perform(get(PROGRESS, "v1").header("Authorization", "Bearer " + falso))
                .andExpect(status().isUnauthorized());
    }

    @Test
    void tokenExpiradoRetorna401() throws Exception {
        String expirado = token("jwt-user", SECURITY_KEY, Instant.now().minusSeconds(120));

        mockMvc.perform(get(PROGRESS, "v1").header("Authorization", "Bearer " + expirado))
                .andExpect(status().isUnauthorized());
    }

    @Test
    void healthSwaggerECatalogoContinuamPublicos() throws Exception {
        mockMvc.perform(get("/health")).andExpect(status().isOk());
        mockMvc.perform(get("/v3/api-docs")).andExpect(status().isOk());
        mockMvc.perform(get("/api/download/videos")).andExpect(status().isOk());
    }

    @Test
    void videoPrivadoUsaODonoDoToken() throws Exception {
        Instant t = Instant.parse("2026-11-21T10:00:00Z");
        catalog.upsertPublished(new VideoPublishedPayload(
                "jwt-privado", "dono-jwt", "Privado", Visibility.PRIVATE, 60, "videos-hls", "jwt-privado/master.m3u8",
                List.of(new Rendition("360p", 640, 360, 800000)), null, t), 1, t);
        String dono = token("dono-jwt", SECURITY_KEY, Instant.now().plusSeconds(300));
        String outro = token("intruso", SECURITY_KEY, Instant.now().plusSeconds(300));

        mockMvc.perform(get("/api/download/videos/jwt-privado/playback").header("Authorization", "Bearer " + dono))
                .andExpect(status().isOk());
        mockMvc.perform(get("/api/download/videos/jwt-privado/playback").header("Authorization", "Bearer " + outro))
                .andExpect(status().isNotFound());
        mockMvc.perform(get("/api/download/videos/jwt-privado/playback").header("X-User-Id", "dono-jwt"))
                .andExpect(status().isNotFound());
    }

    private static String token(String subject, RSAKey key, Instant expiresAt) {
        JwtClaimsSet claims = JwtClaimsSet.builder()
                .subject(subject)
                .issuer("youtubesd-security")
                .audience(List.of("youtubesd-api"))
                .issuedAt(expiresAt.minusSeconds(900))
                .expiresAt(expiresAt)
                .build();
        NimbusJwtEncoder encoder = new NimbusJwtEncoder(new ImmutableJWKSet<>(new JWKSet(key)));
        return encoder.encode(JwtEncoderParameters.from(JwsHeader.with(SignatureAlgorithm.RS256).build(), claims))
                .getTokenValue();
    }

    private static RSAKey newKey() {
        try {
            return new RSAKeyGenerator(2048).keyID(java.util.UUID.randomUUID().toString()).generate();
        } catch (JOSEException ex) {
            throw new IllegalStateException(ex);
        }
    }
}
