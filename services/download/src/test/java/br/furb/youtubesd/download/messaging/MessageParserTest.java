package br.furb.youtubesd.download.messaging;

import br.furb.youtubesd.download.catalog.VideoPublishedPayload;
import br.furb.youtubesd.download.catalog.Visibility;
import jakarta.validation.Validation;
import org.junit.jupiter.api.Test;
import tools.jackson.databind.json.JsonMapper;
import tools.jackson.databind.node.ObjectNode;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.function.Consumer;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class MessageParserTest {

    private static final Path CONTRACT_EXAMPLE = Path.of("../../contracts/examples/video-published.v1.json");

    private final JsonMapper mapper = JsonMapper.builder().build();
    private final MessageParser parser = new MessageParser(mapper,
            Validation.buildDefaultValidatorFactory().getValidator());

    @Test
    void aceitaOExemploOficialDoContrato() throws Exception {
        ParsedMessage<VideoPublishedPayload> message = parse(Files.readString(CONTRACT_EXAMPLE));

        assertThat(message.envelope().type()).isEqualTo("VideoPublished");
        assertThat(message.payload().videoId()).isEqualTo("456");
        assertThat(message.payload().visibility()).isEqualTo(Visibility.PUBLIC);
        assertThat(message.payload().renditions()).hasSize(2);
    }

    @Test
    void aceitaCamposExtrasParaCompatibilidadeFutura() throws Exception {
        String json = mutate(m -> ((ObjectNode) m.get("payload")).put("campoNovo", "qualquer"));

        assertThat(parse(json).payload().videoId()).isEqualTo("456");
    }

    @Test
    void rejeitaSemMessageId() throws Exception {
        assertInvalid(mutate(m -> m.remove("messageId")));
    }

    @Test
    void rejeitaMessageIdQueNaoEUuid() throws Exception {
        assertInvalid(mutate(m -> m.put("messageId", "abc")));
    }

    @Test
    void rejeitaVisibilidadeInvalida() throws Exception {
        assertInvalid(mutate(m -> ((ObjectNode) m.get("payload")).put("visibility", "SECRETO")));
    }

    @Test
    void rejeitaTipoErrado() throws Exception {
        assertInvalid(mutate(m -> m.put("type", "VideoRemoved")));
    }

    @Test
    void rejeitaVersaoNaoSuportada() throws Exception {
        assertInvalid(mutate(m -> m.put("version", 2)));
    }

    @Test
    void rejeitaSemRenditions() throws Exception {
        assertInvalid(mutate(m -> ((ObjectNode) m.get("payload")).putArray("renditions")));
    }

    @Test
    void rejeitaSemMasterKey() throws Exception {
        assertInvalid(mutate(m -> ((ObjectNode) m.get("payload")).remove("masterKey")));
    }

    @Test
    void rejeitaJsonQuebrado() {
        assertInvalid("{ isso não é json");
    }

    private ParsedMessage<VideoPublishedPayload> parse(String json) {
        return parser.parse(json, "VideoPublished", 1, VideoPublishedPayload.class);
    }

    private void assertInvalid(String json) {
        assertThatThrownBy(() -> parse(json)).isInstanceOf(InvalidMessageException.class);
    }

    private String mutate(Consumer<ObjectNode> change) throws Exception {
        ObjectNode message = (ObjectNode) mapper.readTree(Files.readString(CONTRACT_EXAMPLE));
        change.accept(message);
        return mapper.writeValueAsString(message);
    }
}
