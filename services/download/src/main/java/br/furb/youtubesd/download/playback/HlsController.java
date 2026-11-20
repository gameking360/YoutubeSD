package br.furb.youtubesd.download.playback;

import br.furb.youtubesd.download.catalog.CatalogEntry;
import br.furb.youtubesd.download.error.ResourceNotFoundException;
import br.furb.youtubesd.download.storage.HlsStorage;
import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.tags.Tag;
import org.springframework.http.CacheControl;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.servlet.mvc.method.annotation.StreamingResponseBody;
import software.amazon.awssdk.core.ResponseInputStream;
import software.amazon.awssdk.services.s3.model.GetObjectResponse;

import java.time.Duration;
import java.util.regex.Pattern;

@RestController
@RequestMapping("/api/download/videos/{videoId}/hls")
@Tag(name = "Reprodução")
public class HlsController {

    private static final MediaType M3U8 = MediaType.parseMediaType("application/vnd.apple.mpegurl");
    private static final MediaType MPEG_TS = MediaType.parseMediaType("video/mp2t");
    private static final Pattern SEGMENT_NAME = Pattern.compile("[A-Za-z0-9_-]+\\.ts");
    private static final CacheControl SEGMENT_CACHE = CacheControl.maxAge(Duration.ofDays(365)).cachePublic().immutable();

    private final PlaybackService playback;
    private final HlsStorage storage;

    public HlsController(PlaybackService playback, HlsStorage storage) {
        this.playback = playback;
        this.storage = storage;
    }

    @GetMapping("/master.m3u8")
    @Operation(summary = "Playlist mestre HLS", description = "Lista as resoluções disponíveis. Entregue ao player como fonte do vídeo.")
    public ResponseEntity<byte[]> master(@PathVariable String videoId) {
        CatalogEntry video = playback.publishedVideo(videoId);
        return manifest(storage.read(video.hlsBucket(), video.hlsPrefix() + "master.m3u8"));
    }

    @GetMapping("/{rendition}/index.m3u8")
    @Operation(summary = "Playlist de uma resolução", description = "Lista os segmentos da resolução. O player pede sozinho.")
    public ResponseEntity<byte[]> rendition(@PathVariable String videoId, @PathVariable String rendition) {
        CatalogEntry video = playback.publishedVideo(videoId);
        requireRendition(video, rendition);
        return manifest(storage.read(video.hlsBucket(), video.hlsPrefix() + rendition + "/index.m3u8"));
    }

    @GetMapping("/{rendition}/{segment}")
    @Operation(summary = "Segmento de vídeo", description = "Trecho .ts entregue por streaming a partir do S3. O player pede sozinho.")
    public ResponseEntity<StreamingResponseBody> segment(@PathVariable String videoId,
                                                         @PathVariable String rendition,
                                                         @PathVariable String segment) {
        CatalogEntry video = playback.publishedVideo(videoId);
        requireRendition(video, rendition);
        if (!SEGMENT_NAME.matcher(segment).matches()) {
            throw new ResourceNotFoundException("Segmento inválido: " + segment);
        }

        ResponseInputStream<GetObjectResponse> object =
                storage.open(video.hlsBucket(), video.hlsPrefix() + rendition + "/" + segment);
        StreamingResponseBody body = output -> {
            try (object) {
                object.transferTo(output);
            }
        };

        return ResponseEntity.ok()
                .contentType(MPEG_TS)
                .contentLength(object.response().contentLength())
                .cacheControl(SEGMENT_CACHE)
                .body(body);
    }

    private static ResponseEntity<byte[]> manifest(byte[] content) {
        return ResponseEntity.ok()
                .contentType(M3U8)
                .cacheControl(CacheControl.noCache())
                .body(content);
    }

    private static void requireRendition(CatalogEntry video, String rendition) {
        boolean known = video.renditions().stream().anyMatch(r -> r.name().equals(rendition));
        if (!known) {
            throw new ResourceNotFoundException("Resolução não disponível: " + rendition);
        }
    }
}
