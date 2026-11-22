package br.furb.youtubesd.download.storage;

import br.furb.youtubesd.download.TestcontainersConfiguration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.test.web.servlet.MockMvc;
import software.amazon.awssdk.core.sync.RequestBody;
import software.amazon.awssdk.services.s3.S3Client;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;

@SpringBootTest
@AutoConfigureMockMvc
@Import(TestcontainersConfiguration.class)
class S3ConnectionTest {

    @Autowired
    private S3Client s3;

    @Autowired
    private MockMvc mockMvc;

    @Test
    void scriptDeInicializacaoCriaOsBuckets() {
        assertThat(s3.listBuckets().buckets())
                .extracting(bucket -> bucket.name())
                .contains("videos-raw", "videos-hls");
    }

    @Test
    void gravaELeUmObjeto() {
        s3.putObject(r -> r.bucket("videos-hls").key("teste/ola.txt"), RequestBody.fromString("ola"));

        assertThat(s3.getObjectAsBytes(r -> r.bucket("videos-hls").key("teste/ola.txt")).asUtf8String())
                .isEqualTo("ola");
    }

    @Test
    void s3FazParteDoHealth() throws Exception {
        mockMvc.perform(get("/health"))
                .andExpect(jsonPath("$.components.s3.status").value("UP"));
    }
}
