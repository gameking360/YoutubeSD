package br.furb.youtubesd.download.playback;

import br.furb.youtubesd.download.TestcontainersConfiguration;
import br.furb.youtubesd.download.catalog.CatalogRepository;
import br.furb.youtubesd.download.catalog.Rendition;
import br.furb.youtubesd.download.catalog.VideoPublishedPayload;
import br.furb.youtubesd.download.catalog.VideoRemovedPayload;
import br.furb.youtubesd.download.catalog.Visibility;
import br.furb.youtubesd.download.progress.PlaybackProgress;
import br.furb.youtubesd.download.progress.PlaybackStatus;
import br.furb.youtubesd.download.progress.ProgressRepository;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.test.web.servlet.MockMvc;

import java.time.Instant;
import java.util.List;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@SpringBootTest
@AutoConfigureMockMvc
@Import(TestcontainersConfiguration.class)
class PlaybackSessionTest {

    private static final String URL = "/api/download/videos/{videoId}/playback";
    private static final Instant T = Instant.parse("2026-11-20T10:00:00Z");

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private CatalogRepository catalog;

    @Autowired
    private ProgressRepository progress;

    @Test
    void semUsuarioComecaDoInicio() throws Exception {
        publicar("sess-1");

        mockMvc.perform(get(URL, "sess-1"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.videoId").value("sess-1"))
                .andExpect(jsonPath("$.title").value("Vídeo sess-1"))
                .andExpect(jsonPath("$.durationSec").value(600))
                .andExpect(jsonPath("$.manifestUrl").value("/api/download/videos/sess-1/hls/master.m3u8"))
                .andExpect(jsonPath("$.resumePositionSec").value(0))
                .andExpect(jsonPath("$.renditions[0].name").value("360p"));
    }

    @Test
    void retomaDaPosicaoSalva() throws Exception {
        publicar("sess-2");
        progress.save(new PlaybackProgress("u1", "sess-2", 245, 600, PlaybackStatus.IN_PROGRESS, T));

        mockMvc.perform(get(URL, "sess-2").header("X-User-Id", "u1"))
                .andExpect(jsonPath("$.resumePositionSec").value(245));
    }

    @Test
    void videoConcluidoVoltaAoInicio() throws Exception {
        publicar("sess-3");
        progress.save(new PlaybackProgress("u1", "sess-3", 590, 600, PlaybackStatus.COMPLETED, T));

        mockMvc.perform(get(URL, "sess-3").header("X-User-Id", "u1"))
                .andExpect(jsonPath("$.resumePositionSec").value(0));
    }

    @Test
    void videoRemovidoRetorna404() throws Exception {
        publicar("sess-4");
        catalog.markRemoved(new VideoRemovedPayload("sess-4", null, T.plusSeconds(60)), T.plusSeconds(60));

        mockMvc.perform(get(URL, "sess-4")).andExpect(status().isNotFound());
    }

    @Test
    void videoInexistenteRetorna404() throws Exception {
        mockMvc.perform(get(URL, "nao-existe")).andExpect(status().isNotFound());
    }

    private void publicar(String videoId) {
        catalog.upsertPublished(new VideoPublishedPayload(
                videoId, "dono-1", "Vídeo " + videoId, Visibility.PUBLIC, 600, "videos-hls", videoId + "/master.m3u8",
                List.of(new Rendition("360p", 640, 360, 800000)), null, T), 1, T);
    }
}
