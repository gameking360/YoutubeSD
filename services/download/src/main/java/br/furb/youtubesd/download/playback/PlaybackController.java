package br.furb.youtubesd.download.playback;

import br.furb.youtubesd.download.security.CurrentUser;
import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.Parameter;
import io.swagger.v3.oas.annotations.enums.ParameterIn;
import io.swagger.v3.oas.annotations.tags.Tag;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/download/videos/{videoId}")
@Tag(name = "Reprodução", description = "Sessão de reprodução e entrega HLS")
public class PlaybackController {

    private final PlaybackService service;
    private final CurrentUser currentUser;

    public PlaybackController(PlaybackService service, CurrentUser currentUser) {
        this.service = service;
        this.currentUser = currentUser;
    }

    @GetMapping("/playback")
    @Operation(summary = "Abre a sessão de reprodução",
            description = "Retorna o manifesto HLS e a posição de retomada (resumePositionSec). "
                    + "Sem usuário, ou com o vídeo já concluído, a retomada é 0. "
                    + "Use resumePositionSec como startPosition no hls.js. "
                    + "404 se o vídeo não estiver publicado ou for PRIVATE de outro usuário.")
    @Parameter(name = CurrentUser.HEADER, in = ParameterIn.HEADER, description = "Usuário (modo header); opcional")
    public PlaybackSession session(@PathVariable String videoId) {
        return service.session(videoId, currentUser.id());
    }
}
