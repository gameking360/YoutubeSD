package br.furb.youtubesd.download.playback;

import br.furb.youtubesd.download.TestcontainersConfiguration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.test.web.servlet.MockMvc;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.options;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@SpringBootTest
@AutoConfigureMockMvc
@Import(TestcontainersConfiguration.class)
class CorsTest {

    private static final String URL = "/api/download/videos";

    @Autowired
    private MockMvc mockMvc;

    @Test
    void aceitaAOrigemDoPrototipo() throws Exception {
        mockMvc.perform(options(URL)
                        .header("Origin", "http://localhost:5500")
                        .header("Access-Control-Request-Method", "PUT")
                        .header("Access-Control-Request-Headers", "X-User-Id,Content-Type"))
                .andExpect(status().isOk())
                .andExpect(header().string("Access-Control-Allow-Origin", "http://localhost:5500"));
    }

    @Test
    void recusaOrigemDesconhecida() throws Exception {
        mockMvc.perform(options(URL)
                        .header("Origin", "http://site-qualquer.com")
                        .header("Access-Control-Request-Method", "GET"))
                .andExpect(status().isForbidden());
    }

    @Test
    void expoeOHeaderDaInstancia() throws Exception {
        mockMvc.perform(get(URL).header("Origin", "http://localhost:5500"))
                .andExpect(header().string("Access-Control-Expose-Headers", "X-Instance-Id"));
    }
}
