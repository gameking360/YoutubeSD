package br.furb.youtubesd.download.observability;

import br.furb.youtubesd.download.config.DownloadProperties;
import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.springframework.stereotype.Component;
import org.springframework.web.filter.OncePerRequestFilter;

import java.io.IOException;

@Component
public class InstanceIdFilter extends OncePerRequestFilter {

    public static final String HEADER = "X-Instance-Id";

    private final DownloadProperties properties;

    public InstanceIdFilter(DownloadProperties properties) {
        this.properties = properties;
    }

    @Override
    protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain chain)
            throws ServletException, IOException {
        response.setHeader(HEADER, properties.instanceId());
        chain.doFilter(request, response);
    }
}
