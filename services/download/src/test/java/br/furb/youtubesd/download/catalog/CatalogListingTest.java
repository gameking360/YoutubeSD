package br.furb.youtubesd.download.catalog;

import br.furb.youtubesd.download.TestcontainersConfiguration;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.http.MediaType;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.test.web.servlet.MockMvc;

import java.time.Instant;
import java.util.List;

import static org.hamcrest.Matchers.contains;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@SpringBootTest
@AutoConfigureMockMvc
@Import(TestcontainersConfiguration.class)
class CatalogListingTest {

    private static final String URL = "/api/download/videos";

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private CatalogRepository catalog;

    @Autowired
    private JdbcClient jdbc;

    @BeforeEach
    void limparCatalogo() {
        jdbc.sql("DELETE FROM video_catalog").update();
    }

    @Test
    void listaSomenteVideosPublicadosEPublicos() throws Exception {
        publicar("publico", Visibility.PUBLIC, "2026-11-20T10:00:00Z");
        publicar("nao-listado", Visibility.UNLISTED, "2026-11-20T10:01:00Z");
        publicar("privado", Visibility.PRIVATE, "2026-11-20T10:02:00Z");
        publicar("removido", Visibility.PUBLIC, "2026-11-20T10:03:00Z");
        catalog.markRemoved(new VideoRemovedPayload("removido", null, Instant.parse("2026-11-20T10:04:00Z")),
                Instant.parse("2026-11-20T10:04:00Z"));

        mockMvc.perform(get(URL))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.items[*].videoId", contains("publico")))
                .andExpect(jsonPath("$.items[0].title").value("Vídeo publico"))
                .andExpect(jsonPath("$.items[0].durationSec").value(600));
    }

    @Test
    void ordenaDoMaisRecenteParaOMaisAntigo() throws Exception {
        publicar("antigo", Visibility.PUBLIC, "2026-11-20T10:00:00Z");
        publicar("recente", Visibility.PUBLIC, "2026-11-20T12:00:00Z");
        publicar("meio", Visibility.PUBLIC, "2026-11-20T11:00:00Z");

        mockMvc.perform(get(URL))
                .andExpect(jsonPath("$.items[*].videoId", contains("recente", "meio", "antigo")));
    }

    @Test
    void paginaComIndicacaoDeProximaPagina() throws Exception {
        publicar("v1", Visibility.PUBLIC, "2026-11-20T10:00:00Z");
        publicar("v2", Visibility.PUBLIC, "2026-11-20T11:00:00Z");
        publicar("v3", Visibility.PUBLIC, "2026-11-20T12:00:00Z");

        mockMvc.perform(get(URL).param("page", "0").param("size", "2"))
                .andExpect(jsonPath("$.items[*].videoId", contains("v3", "v2")))
                .andExpect(jsonPath("$.hasNext").value(true));

        mockMvc.perform(get(URL).param("page", "1").param("size", "2"))
                .andExpect(jsonPath("$.items[*].videoId", contains("v1")))
                .andExpect(jsonPath("$.hasNext").value(false));
    }

    @Test
    void tamanhoDePaginaInvalidoRetorna400() throws Exception {
        mockMvc.perform(get(URL).param("size", "0"))
                .andExpect(status().isBadRequest())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_PROBLEM_JSON));
    }

    private void publicar(String videoId, Visibility visibility, String publishedAt) {
        Instant instant = Instant.parse(publishedAt);
        catalog.upsertPublished(new VideoPublishedPayload(
                videoId, "dono-1", "Vídeo " + videoId, visibility, 600, "videos-hls", videoId + "/master.m3u8",
                List.of(new Rendition("360p", 640, 360, 800000)), null, instant), 1, instant);
    }
}
