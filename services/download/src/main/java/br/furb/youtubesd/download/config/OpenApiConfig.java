package br.furb.youtubesd.download.config;

import io.swagger.v3.oas.models.OpenAPI;
import io.swagger.v3.oas.models.info.Info;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

@Configuration
public class OpenApiConfig {

    @Bean
    public OpenAPI downloadOpenApi() {
        return new OpenAPI().info(new Info()
                .title("YoutubeSD — Download")
                .version("v1")
                .description("""
                        Reprodução de vídeos, progresso e retomada.

                        **Autenticação (temporária):** enquanto o JWT do Security não estiver integrado, \
                        o usuário é identificado pelo header `X-User-Id`.

                        **Erros:** todas as respostas de erro seguem o formato ProblemDetail \
                        (`application/problem+json`), com `status`, `title` e `detail`.
                        """));
    }
}
