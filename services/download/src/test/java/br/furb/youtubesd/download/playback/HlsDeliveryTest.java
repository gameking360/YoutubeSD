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
import org.springframework.test.web.servlet.MvcResult;
import software.amazon.awssdk.core.sync.RequestBody;
import software.amazon.awssdk.services.s3.S3Client;

import java.time.Instant;
import java.util.List;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.asyncDispatch;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.request;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@SpringBootTest
@AutoConfigureMockMvc
@Import(TestcontainersConfiguration.class)
class HlsDeliveryTest {

    private static final String BASE = "/api/download/videos/hls-1/hls";
    private static final String MASTER = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=800000\n360p/index.m3u8\n";
    private static final String INDEX = "#EXTM3U\n#EXTINF:4.0,\nseg_00000.ts\n#EXT-X-ENDLIST\n";
    private static final byte[] SEGMENT = {0x47, 0x40, 0x11, 0x10, 0x00, 0x42};

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private CatalogRepository catalog;

    @Autowired
    private S3Client s3;

    @BeforeEach
    void publicarVideoComArquivos() {
        Instant t = Instant.parse("2026-11-20T10:00:00Z");
        catalog.upsertPublished(new VideoPublishedPayload(
                "hls-1", "dono-1", "Vídeo HLS", Visibility.PUBLIC, 4, "videos-hls", "hls-1/master.m3u8",
                List.of(new Rendition("360p", 640, 360, 800000)), null, t), 1, t);
        enviar("hls-1/master.m3u8", RequestBody.fromString(MASTER));
        enviar("hls-1/360p/index.m3u8", RequestBody.fromString(INDEX));
        enviar("hls-1/360p/seg_00000.ts", RequestBody.fromBytes(SEGMENT));
    }

    @Test
    void entregaAPlaylistMestre() throws Exception {
        mockMvc.perform(get(BASE + "/master.m3u8"))
                .andExpect(status().isOk())
                .andExpect(header().string("Content-Type", "application/vnd.apple.mpegurl"))
                .andExpect(header().string("Cache-Control", "no-cache"))
                .andExpect(content().string(MASTER));
    }

    @Test
    void entregaAPlaylistDaResolucao() throws Exception {
        mockMvc.perform(get(BASE + "/360p/index.m3u8"))
                .andExpect(status().isOk())
                .andExpect(content().string(INDEX));
    }

    @Test
    void entregaOSegmentoPorStreaming() throws Exception {
        MvcResult started = mockMvc.perform(get(BASE + "/360p/seg_00000.ts"))
                .andExpect(request().asyncStarted())
                .andReturn();

        mockMvc.perform(asyncDispatch(started))
                .andExpect(status().isOk())
                .andExpect(header().string("Content-Type", "video/mp2t"))
                .andExpect(header().string("Cache-Control", "max-age=31536000, public, immutable"))
                .andExpect(content().bytes(SEGMENT));
    }

    @Test
    void resolucaoDesconhecidaRetorna404() throws Exception {
        mockMvc.perform(get(BASE + "/1080p/index.m3u8")).andExpect(status().isNotFound());
    }

    @Test
    void nomeDeSegmentoInvalidoRetorna404() throws Exception {
        mockMvc.perform(get(BASE + "/360p/seg.00000.ts")).andExpect(status().isNotFound());
        mockMvc.perform(get(BASE + "/360p/index.mp4")).andExpect(status().isNotFound());
    }

    @Test
    void segmentoInexistenteRetorna404() throws Exception {
        mockMvc.perform(get(BASE + "/360p/seg_99999.ts")).andExpect(status().isNotFound());
    }

    private void enviar(String key, RequestBody body) {
        s3.putObject(r -> r.bucket("videos-hls").key(key), body);
    }
}
