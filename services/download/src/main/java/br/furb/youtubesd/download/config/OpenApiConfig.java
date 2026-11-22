package br.furb.youtubesd.download.config;

import io.swagger.v3.oas.models.Components;
import io.swagger.v3.oas.models.OpenAPI;
import io.swagger.v3.oas.models.info.Info;
import io.swagger.v3.oas.models.security.SecurityRequirement;
import io.swagger.v3.oas.models.security.SecurityScheme;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

@Configuration
public class OpenApiConfig {

    private static final String BEARER = "bearer";

    @Bean
    public OpenAPI downloadOpenApi() {
        return new OpenAPI()
                .info(new Info()
                        .title("YoutubeSD — Download")
                        .version("v1")
                        .description("""
                                Reprodução de vídeos, progresso e retomada.

                                **Autenticação** (variável `AUTH_MODE` do serviço):
                                - `header` (padrão enquanto o Security não existe): usuário no header `X-User-Id`.
                                - `jwt`: `Authorization: Bearer <token>` emitido pelo Security; o usuário é o claim `sub`. \
                                Use o botão **Authorize** para colar o token.

                                Endpoints de progresso exigem usuário (401 sem ele). Catálogo, sessão e HLS são abertos, \
                                mas vídeos `PRIVATE` só respondem para o dono (404 para os demais).

                                **Erros:** sempre ProblemDetail (`application/problem+json`), com `status`, `title` e `detail`.
                                """))
                .components(new Components().addSecuritySchemes(BEARER, new SecurityScheme()
                        .type(SecurityScheme.Type.HTTP)
                        .scheme("bearer")
                        .bearerFormat("JWT")))
                .addSecurityItem(new SecurityRequirement().addList(BEARER));
    }
}
