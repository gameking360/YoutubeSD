package br.furb.youtubesd.download.progress;

import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.Parameter;
import io.swagger.v3.oas.annotations.tags.Tag;
import jakarta.validation.Valid;
import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

import java.util.List;

@RestController
@RequestMapping("/api/download/progress")
@Tag(name = "Progresso de reprodução", description = "Posição em que o usuário parou e lista de continuar assistindo")
public class ProgressController {

    static final String USER_HEADER = "X-User-Id";

    private final ProgressService service;

    public ProgressController(ProgressService service) {
        this.service = service;
    }

    @PutMapping("/{videoId}")
    @Operation(summary = "Salva o progresso",
            description = "Enviar a cada ~10 s durante a reprodução e em pause, seek e fim do vídeo. "
                    + "Escritas com clientTs mais antigo que o salvo são ignoradas. "
                    + "A partir de 95% da duração o status vira COMPLETED. Retorna o estado que ficou salvo.")
    public ProgressResponse save(@Parameter(description = "Usuário (temporário até o JWT)")
                                 @RequestHeader(USER_HEADER) String userId,
                                 @PathVariable String videoId,
                                 @Valid @RequestBody ProgressRequest request) {
        return ProgressResponse.from(service.save(userId, videoId, request));
    }

    @GetMapping("/{videoId}")
    @Operation(summary = "Busca o progresso de um vídeo",
            description = "Retorna 404 se o usuário ainda não assistiu o vídeo.")
    public ProgressResponse get(@Parameter(description = "Usuário (temporário até o JWT)")
                                @RequestHeader(USER_HEADER) String userId,
                                @PathVariable String videoId) {
        return ProgressResponse.from(service.get(userId, videoId));
    }

    @GetMapping
    @Operation(summary = "Lista o progresso por status",
            description = "IN_PROGRESS = continuar assistindo; COMPLETED = vídeos concluídos. "
                    + "Ordenado do mais recente para o mais antigo.")
    public List<ProgressResponse> list(@Parameter(description = "Usuário (temporário até o JWT)")
                                       @RequestHeader(USER_HEADER) String userId,
                                       @RequestParam(defaultValue = "IN_PROGRESS") PlaybackStatus status,
                                       @Parameter(description = "Quantidade máxima de itens (1 a 100)")
                                       @RequestParam(defaultValue = "20") @Min(1) @Max(100) int limit) {
        return service.list(userId, status, limit).stream()
                .map(ProgressResponse::from)
                .toList();
    }
}
