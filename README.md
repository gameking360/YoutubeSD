# YoutubeSD

Plataforma de vídeos sob demanda com arquitetura de microsserviços.
Trabalho final de Sistemas Distribuídos — FURB.

## Estrutura

```
apphost/      orquestração com .NET Aspire
services/
  identity/   usuários
  security/   autenticação e autorização
  upload/     envio e processamento dos vídeos
  download/   reprodução dos vídeos
frontend/     interface web
contracts/    contratos das mensagens
infra/        scripts de inicialização da infraestrutura
```

## Como executar

Com o Docker aberto:

```
cd apphost
dotnet run --launch-profile http
```
