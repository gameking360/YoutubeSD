package br.furb.youtubesd.download.catalog;

import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.Parameter;
import io.swagger.v3.oas.annotations.tags.Tag;
import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/download/videos")
@Tag(name = "Catálogo", description = "Vídeos publicados e prontos para reprodução")
public class CatalogController {

    private final CatalogRepository catalog;

    public CatalogController(CatalogRepository catalog) {
        this.catalog = catalog;
    }

    @GetMapping
    @Operation(summary = "Lista os vídeos públicos",
            description = "Somente vídeos publicados com visibilidade PUBLIC, do mais recente para o mais antigo. "
                    + "Use hasNext para saber se existe a próxima página.")
    public PageResponse<VideoSummary> list(@Parameter(description = "Página, começando em 0")
                                           @RequestParam(defaultValue = "0") @Min(0) int page,
                                           @Parameter(description = "Itens por página (1 a 100)")
                                           @RequestParam(defaultValue = "20") @Min(1) @Max(100) int size) {
        return PageResponse.of(catalog.findPublic(page * size, size + 1), page, size);
    }
}
