package br.furb.youtubesd.download.progress;

import br.furb.youtubesd.download.MySqlTestConfiguration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.ResultActions;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.put;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@SpringBootTest
@AutoConfigureMockMvc
@Import(MySqlTestConfiguration.class)
class ProgressControllerTest {

    private static final String URL = "/api/download/progress/{videoId}";

    @Autowired
    private MockMvc mockMvc;

    @Test
    void salvaERecuperaProgresso() throws Exception {
        salvar("c1", 120, 600, "2026-11-19T10:00:00Z")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.positionSec").value(120))
                .andExpect(jsonPath("$.status").value("IN_PROGRESS"));

        mockMvc.perform(get(URL, "v1").header(ProgressController.USER_HEADER, "c1"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.videoId").value("v1"))
                .andExpect(jsonPath("$.positionSec").value(120))
                .andExpect(jsonPath("$.lastWatchedAt").value("2026-11-19T10:00:00Z"));
    }

    @Test
    void marcaComoConcluidoAPartirDe95PorCento() throws Exception {
        salvar("c2", 570, 600, "2026-11-19T10:00:00Z")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("COMPLETED"));
    }

    @Test
    void escritaAntigaRetornaEstadoMaisRecente() throws Exception {
        salvar("c3", 300, 600, "2026-11-19T10:00:10Z");

        salvar("c3", 100, 600, "2026-11-19T10:00:00Z")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.positionSec").value(300));
    }

    @Test
    void posicaoMaiorQueDuracaoRetorna400() throws Exception {
        salvar("c4", 700, 600, "2026-11-19T10:00:00Z")
                .andExpect(status().isBadRequest())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_PROBLEM_JSON));
    }

    @Test
    void semUsuarioRetorna400() throws Exception {
        mockMvc.perform(put(URL, "v1")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(body(10, 600, "2026-11-19T10:00:00Z")))
                .andExpect(status().isBadRequest())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_PROBLEM_JSON));
    }

    @Test
    void progressoInexistenteRetorna404() throws Exception {
        mockMvc.perform(get(URL, "v1").header(ProgressController.USER_HEADER, "ninguem"))
                .andExpect(status().isNotFound())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_PROBLEM_JSON));
    }

    private ResultActions salvar(String userId, int positionSec, int durationSec, String clientTs) throws Exception {
        return mockMvc.perform(put(URL, "v1")
                .header(ProgressController.USER_HEADER, userId)
                .contentType(MediaType.APPLICATION_JSON)
                .content(body(positionSec, durationSec, clientTs)));
    }

    private static String body(int positionSec, int durationSec, String clientTs) {
        return """
                {"positionSec": %d, "durationSec": %d, "clientTs": "%s"}
                """.formatted(positionSec, durationSec, clientTs);
    }
}
