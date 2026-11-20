package br.furb.youtubesd.download.progress;

import br.furb.youtubesd.download.TestcontainersConfiguration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;

import static org.hamcrest.Matchers.contains;
import static org.hamcrest.Matchers.hasSize;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.put;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@SpringBootTest
@AutoConfigureMockMvc
@Import(TestcontainersConfiguration.class)
class ContinueWatchingTest {

    private static final String LIST_URL = "/api/download/progress";

    @Autowired
    private MockMvc mockMvc;

    @Test
    void listaDoMaisRecenteParaOMaisAntigoSemOsConcluidos() throws Exception {
        salvar("w1", "video-a", 100, "2026-11-20T10:00:00Z");
        salvar("w1", "video-b", 200, "2026-11-20T10:05:00Z");
        salvar("w1", "video-c", 590, "2026-11-20T10:10:00Z");

        mockMvc.perform(get(LIST_URL).header(ProgressController.USER_HEADER, "w1"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$[*].videoId", contains("video-b", "video-a")));
    }

    @Test
    void listaOsConcluidosQuandoPedido() throws Exception {
        salvar("w2", "video-a", 100, "2026-11-20T10:00:00Z");
        salvar("w2", "video-c", 590, "2026-11-20T10:10:00Z");

        mockMvc.perform(get(LIST_URL).param("status", "COMPLETED").header(ProgressController.USER_HEADER, "w2"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$[*].videoId", contains("video-c")));
    }

    @Test
    void respeitaOLimite() throws Exception {
        salvar("w3", "video-a", 100, "2026-11-20T10:00:00Z");
        salvar("w3", "video-b", 200, "2026-11-20T10:05:00Z");

        mockMvc.perform(get(LIST_URL).param("limit", "1").header(ProgressController.USER_HEADER, "w3"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$[*].videoId", contains("video-b")));
    }

    @Test
    void naoMostraVideosDeOutroUsuario() throws Exception {
        salvar("w4", "video-a", 100, "2026-11-20T10:00:00Z");

        mockMvc.perform(get(LIST_URL).header(ProgressController.USER_HEADER, "w5"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$", hasSize(0)));
    }

    @Test
    void limiteForaDoIntervaloRetorna400() throws Exception {
        mockMvc.perform(get(LIST_URL).param("limit", "0").header(ProgressController.USER_HEADER, "w6"))
                .andExpect(status().isBadRequest())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_PROBLEM_JSON));
    }

    @Test
    void statusInvalidoRetorna400() throws Exception {
        mockMvc.perform(get(LIST_URL).param("status", "QUALQUER").header(ProgressController.USER_HEADER, "w6"))
                .andExpect(status().isBadRequest())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_PROBLEM_JSON));
    }

    private void salvar(String userId, String videoId, int positionSec, String clientTs) throws Exception {
        mockMvc.perform(put(LIST_URL + "/{videoId}", videoId)
                        .header(ProgressController.USER_HEADER, userId)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("""
                                {"positionSec": %d, "durationSec": 600, "clientTs": "%s"}
                                """.formatted(positionSec, clientTs)))
                .andExpect(status().isOk());
    }
}
