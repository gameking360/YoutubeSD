package br.furb.youtubesd.download.playback;

import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.Parameter;
import io.swagger.v3.oas.annotations.tags.Tag;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/download/videos/{videoId}")
@Tag(name = "Reprodução", description = "Sessão de reprodução e entrega HLS")
public class PlaybackController {

    private final PlaybackService service;

    public PlaybackController(PlaybackService service) {
        this.service = service;
    }

    @GetMapping("/playback")
    @Operation(summary = "Abre a sessão de reprodução",
            description = "Retorna o manifesto HLS e a posição de retomada (resumePositionSec). "
                    + "Sem X-User-Id, ou com o vídeo já concluído, a retomada é 0. "
                    + "Use resumePositionSec como startPosition no hls.js. 404 se o vídeo não estiver publicado.")
    public PlaybackSession session(@PathVariable String videoId,
                                   @Parameter(description = "Usuário (temporário até o JWT); opcional")
                                   @RequestHeader(value = "X-User-Id", required = false) String userId) {
        return service.session(videoId, userId);
    }
}
