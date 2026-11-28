package br.furb.youtubesd.download.catalog;

import br.furb.youtubesd.download.messaging.InvalidMessageException;
import br.furb.youtubesd.download.messaging.MessageParser;
import br.furb.youtubesd.download.messaging.ParsedMessage;
import jakarta.validation.Validation;
import org.junit.jupiter.api.Test;
import tools.jackson.databind.json.JsonMapper;
import tools.jackson.databind.node.ObjectNode;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.function.Consumer;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class VideoPublishedReaderTest {

    private static final Path EXEMPLO_V1 = Path.of("../../contracts/examples/video-published.v1.json");
    private static final Path EXEMPLO_V2 = Path.of("../../contracts/examples/video-published.v2.json");

    private final JsonMapper mapper = JsonMapper.builder().build();
    private final VideoPublishedReader reader = new VideoPublishedReader(
            new MessageParser(mapper, Validation.buildDefaultValidatorFactory().getValidator()));

    @Test
    void aceitaAV1DoExemploOficial() throws Exception {
        ParsedMessage<VideoPublishedPayload> message = reader.read(Files.readString(EXEMPLO_V1));

        assertThat(message.envelope().version()).isEqualTo(1);
        assertThat(message.payload().videoId()).isEqualTo("456");
        assertThat(message.payload().durationSec()).isEqualTo(1820);
        assertThat(message.payload().masterKey()).isEqualTo("456/master.m3u8");
    }

    @Test
    void aceitaAV2DoExemploOficialEConverteParaOFormatoDoCatalogo() throws Exception {
        ParsedMessage<VideoPublishedPayload> message = reader.read(Files.readString(EXEMPLO_V2));

        assertThat(message.envelope().version()).isEqualTo(2);
        VideoPublishedPayload video = message.payload();
        assertThat(video.videoId()).isEqualTo("789");
        assertThat(video.durationSec()).isEqualTo(1821);
        assertThat(video.hlsBucket()).isEqualTo("videos-hls");
        assertThat(video.masterKey()).isEqualTo("789/master.m3u8");
        assertThat(video.renditions()).hasSize(2);
        assertThat(video.thumbnailKey()).isEqualTo("789/thumb.jpg");
    }

    @Test
    void duracaoMenorQueUmSegundoViraUmSegundo() throws Exception {
        String json = mudar(EXEMPLO_V2, m -> ((ObjectNode) m.get("payload")).put("durationMs", 200));

        assertThat(reader.read(json).payload().durationSec()).isEqualTo(1);
    }

    @Test
    void rejeitaVersaoDesconhecida() throws Exception {
        String json = mudar(EXEMPLO_V2, m -> m.put("version", 3));

        assertThatThrownBy(() -> reader.read(json))
                .isInstanceOf(InvalidMessageException.class)
                .hasMessageContaining("v3 não é suportado");
    }

    @Test
    void rejeitaV2ComOPayloadDaV1() throws Exception {
        assertInvalida(mudar(EXEMPLO_V1, m -> m.put("version", 2)));
    }

    @Test
    void rejeitaV1ComOPayloadDaV2() throws Exception {
        assertInvalida(mudar(EXEMPLO_V2, m -> m.put("version", 1)));
    }

    @Test
    void rejeitaV2SemRenditions() throws Exception {
        assertInvalida(mudar(EXEMPLO_V2, m -> ((ObjectNode) m.get("payload").get("hls")).putArray("renditions")));
    }

    @Test
    void rejeitaOutroTipo() throws Exception {
        assertInvalida(mudar(EXEMPLO_V2, m -> m.put("type", "VideoRemoved")));
    }

    private void assertInvalida(String json) {
        assertThatThrownBy(() -> reader.read(json)).isInstanceOf(InvalidMessageException.class);
    }

    private String mudar(Path exemplo, Consumer<ObjectNode> mudanca) throws Exception {
        ObjectNode message = (ObjectNode) mapper.readTree(Files.readString(exemplo));
        mudanca.accept(message);
        return mapper.writeValueAsString(message);
    }
}
