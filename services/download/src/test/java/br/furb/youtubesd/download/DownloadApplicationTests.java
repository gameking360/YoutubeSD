package br.furb.youtubesd.download;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@SpringBootTest(properties = {
		"download.instance-id=teste-1",
		"management.health.db.enabled=false",
		"spring.flyway.enabled=false",
		"spring.kafka.listener.auto-startup=false"
})
@AutoConfigureMockMvc
class DownloadApplicationTests {

	@Autowired
	private MockMvc mockMvc;

	@Test
	void healthRespondeComIdentificacaoDaInstancia() throws Exception {
		mockMvc.perform(get("/health"))
				.andExpect(status().isOk())
				.andExpect(header().string("X-Instance-Id", "teste-1"));
	}

	@Test
	void rotaInexistenteRetornaProblemDetail() throws Exception {
		mockMvc.perform(get("/rota-inexistente"))
				.andExpect(status().isNotFound())
				.andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_PROBLEM_JSON))
				.andExpect(jsonPath("$.status").value(404));
	}

	@Test
	void documentacaoOpenApiListaOsEndpoints() throws Exception {
		mockMvc.perform(get("/v3/api-docs"))
				.andExpect(status().isOk())
				.andExpect(jsonPath("$.info.title").value("YoutubeSD — Download"))
				.andExpect(jsonPath("$.paths['/api/download/progress/{videoId}'].put").exists())
				.andExpect(jsonPath("$.paths['/api/download/progress'].get").exists());
	}

	@Test
	void swaggerUiEstaDisponivel() throws Exception {
		mockMvc.perform(get("/swagger-ui/index.html"))
				.andExpect(status().isOk());
	}

}
