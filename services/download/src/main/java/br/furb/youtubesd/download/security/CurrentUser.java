package br.furb.youtubesd.download.security;

import jakarta.servlet.http.HttpServletRequest;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.security.core.Authentication;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationToken;
import org.springframework.stereotype.Component;

import java.util.Optional;

@Component
public class CurrentUser {

    public static final String HEADER = "X-User-Id";

    private final HttpServletRequest request;
    private final boolean jwtMode;

    public CurrentUser(HttpServletRequest request, @Value("${download.auth.mode}") String mode) {
        this.request = request;
        this.jwtMode = "jwt".equalsIgnoreCase(mode);
    }

    public Optional<String> id() {
        return jwtMode ? fromToken() : fromHeader();
    }

    public String require() {
        return id().orElseThrow(UnauthenticatedException::new);
    }

    private Optional<String> fromToken() {
        Authentication authentication = SecurityContextHolder.getContext().getAuthentication();
        if (authentication instanceof JwtAuthenticationToken token) {
            return Optional.ofNullable(token.getToken().getSubject()).filter(sub -> !sub.isBlank());
        }
        return Optional.empty();
    }

    private Optional<String> fromHeader() {
        return Optional.ofNullable(request.getHeader(HEADER)).map(String::trim).filter(id -> !id.isEmpty());
    }
}
