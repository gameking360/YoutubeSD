package br.furb.youtubesd.download.playback;

import br.furb.youtubesd.download.TestcontainersConfiguration;
import br.furb.youtubesd.download.catalog.CatalogRepository;
import br.furb.youtubesd.download.catalog.Rendition;
import br.furb.youtubesd.download.catalog.VideoPublishedPayload;
import br.furb.youtubesd.download.catalog.Visibility;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.test.web.servlet.MockMvc;
import software.amazon.awssdk.core.sync.RequestBody;
import software.amazon.awssdk.services.s3.S3Client;

import java.time.Instant;
import java.util.List;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@SpringBootTest
@AutoConfigureMockMvc
@Import(TestcontainersConfiguration.class)
class AccessRulesTest {

    private static final String OWNER = "dono-privado";

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private CatalogRepository catalog;

    @Autowired
    private S3Client s3;

    @BeforeEach
    void publicarVideos() {
        publicar("acesso-privado", Visibility.PRIVATE);
        publicar("acesso-nao-listado", Visibility.UNLISTED);
        s3.putObject(r -> r.bucket("videos-hls").key("acesso-privado/master.m3u8"),
                RequestBody.fromString("#EXTM3U\n360p/index.m3u8\n"));
    }

    @Test
    void videoPrivadoTocaParaODono() throws Exception {
        mockMvc.perform(get("/api/download/videos/acesso-privado/playback").header("X-User-Id", OWNER))
                .andExpect(status().isOk());
        mockMvc.perform(get("/api/download/videos/acesso-privado/hls/master.m3u8").header("X-User-Id", OWNER))
                .andExpect(status().isOk());
    }

    @Test
    void videoPrivadoNaoTocaParaOutroUsuarioNemAnonimo() throws Exception {
        mockMvc.perform(get("/api/download/videos/acesso-privado/playback").header("X-User-Id", "intruso"))
                .andExpect(status().isNotFound());
        mockMvc.perform(get("/api/download/videos/acesso-privado/playback"))
                .andExpect(status().isNotFound());
        mockMvc.perform(get("/api/download/videos/acesso-privado/hls/master.m3u8").header("X-User-Id", "intruso"))
                .andExpect(status().isNotFound());
    }

    @Test
    void videoNaoListadoTocaParaQuemTemOLink() throws Exception {
        mockMvc.perform(get("/api/download/videos/acesso-nao-listado/playback"))
                .andExpect(status().isOk());
    }

    private void publicar(String videoId, Visibility visibility) {
        Instant t = Instant.parse("2026-11-21T10:00:00Z");
        catalog.upsertPublished(new VideoPublishedPayload(
                videoId, OWNER, "Vídeo " + videoId, visibility, 60, "videos-hls", videoId + "/master.m3u8",
                List.of(new Rendition("360p", 640, 360, 800000)), null, t), 1, t);
    }
}
