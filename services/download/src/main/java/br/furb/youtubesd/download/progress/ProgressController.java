package br.furb.youtubesd.download.progress;

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
public class ProgressController {

    static final String USER_HEADER = "X-User-Id";

    private final ProgressService service;

    public ProgressController(ProgressService service) {
        this.service = service;
    }

    @PutMapping("/{videoId}")
    public ProgressResponse save(@RequestHeader(USER_HEADER) String userId,
                                 @PathVariable String videoId,
                                 @Valid @RequestBody ProgressRequest request) {
        return ProgressResponse.from(service.save(userId, videoId, request));
    }

    @GetMapping("/{videoId}")
    public ProgressResponse get(@RequestHeader(USER_HEADER) String userId,
                                @PathVariable String videoId) {
        return ProgressResponse.from(service.get(userId, videoId));
    }

    @GetMapping
    public List<ProgressResponse> list(@RequestHeader(USER_HEADER) String userId,
                                       @RequestParam(defaultValue = "IN_PROGRESS") PlaybackStatus status,
                                       @RequestParam(defaultValue = "20") @Min(1) @Max(100) int limit) {
        return service.list(userId, status, limit).stream()
                .map(ProgressResponse::from)
                .toList();
    }
}
