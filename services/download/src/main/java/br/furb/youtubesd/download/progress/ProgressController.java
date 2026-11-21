package br.furb.youtubesd.download.progress;

import br.furb.youtubesd.download.security.CurrentUser;
import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.Parameter;
import io.swagger.v3.oas.annotations.enums.ParameterIn;
import io.swagger.v3.oas.annotations.tags.Tag;
import jakarta.validation.Valid;
import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

import java.util.List;

@RestController
@RequestMapping("/api/download/progress")
@Tag(name = "Progresso de reprodução", description = "Posição em que o usuário parou e lista de continuar assistindo")
public class ProgressController {

    static final String USER_HEADER = CurrentUser.HEADER;

    private final ProgressService service;
    private final CurrentUser currentUser;

    public ProgressController(ProgressService service, CurrentUser currentUser) {
        this.service = service;
        this.currentUser = currentUser;
    }

    @PutMapping("/{videoId}")
    @Operation(summary = "Salva o progresso",
            description = "Enviar a cada ~10 s durante a reprodução e em pause, seek e fim do vídeo. "
                    + "Escritas com clientTs mais antigo que o salvo são ignoradas. "
                    + "A partir de 95% da duração o status vira COMPLETED. Retorna o estado que ficou salvo. "
                    + "401 sem usuário.")
    @Parameter(name = CurrentUser.HEADER, in = ParameterIn.HEADER, description = "Usuário (modo header)")
    public ProgressResponse save(@PathVariable String videoId, @Valid @RequestBody ProgressRequest request) {
        return ProgressResponse.from(service.save(currentUser.require(), videoId, request));
    }

    @GetMapping("/{videoId}")
    @Operation(summary = "Busca o progresso de um vídeo",
            description = "Retorna 404 se o usuário ainda não assistiu o vídeo e 401 sem usuário.")
    @Parameter(name = CurrentUser.HEADER, in = ParameterIn.HEADER, description = "Usuário (modo header)")
    public ProgressResponse get(@PathVariable String videoId) {
        return ProgressResponse.from(service.get(currentUser.require(), videoId));
    }

    @GetMapping
    @Operation(summary = "Lista o progresso por status",
            description = "IN_PROGRESS = continuar assistindo; COMPLETED = vídeos concluídos. "
                    + "Ordenado do mais recente para o mais antigo. 401 sem usuário.")
    @Parameter(name = CurrentUser.HEADER, in = ParameterIn.HEADER, description = "Usuário (modo header)")
    public List<ProgressResponse> list(@RequestParam(defaultValue = "IN_PROGRESS") PlaybackStatus status,
                                       @Parameter(description = "Quantidade máxima de itens (1 a 100)")
                                       @RequestParam(defaultValue = "20") @Min(1) @Max(100) int limit) {
        return service.list(currentUser.require(), status, limit).stream()
                .map(ProgressResponse::from)
                .toList();
    }
}
