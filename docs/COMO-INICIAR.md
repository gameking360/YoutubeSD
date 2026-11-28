# Como iniciar o YoutubeSD

Guia passo a passo para subir o ambiente do zero, enviar o primeiro vídeo, assistir e rodar os
cenários de falha. Para entender a arquitetura e as decisões, veja [ARQUITETURA.md](ARQUITETURA.md).

Tempo estimado da primeira subida: 10 a 20 minutos, a maior parte baixando imagens e compilando.

---

## 1. Pré-requisitos

| Ferramenta | Obrigatória? | Para quê |
|---|---|---|
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) aberto | Sim | Todos os serviços e a infraestrutura rodam em containers |
| [.NET SDK 10](https://dotnet.microsoft.com/download) | Para o Aspire | Gerar as chaves (passo 3) e subir pelo Aspire |
| Git Bash (vem com o [Git for Windows](https://git-scm.com/download/win)) | Para os scripts no Windows | Executar os scripts Bash de `infra/` |
| Java 25 | Não | Só para rodar os testes do Download fora do Docker |

FFmpeg, MySQL, Kafka e Redis **não** precisam estar instalados: tudo vem em containers.

**Memória.** Os limites dos containers somam cerca de 7 GB (3 × 768 MB do Download, 2 × 512 MB do
Upload, 2 × 2 GB do media-processor), mais Kafka e MySQL. Reserve pelo menos 8 GB ao Docker
(Docker Desktop → Settings → Resources) ou suba com menos réplicas (passo 4).

Confira a instalação com `docker compose version`. Para usar o Aspire, confira também se
`dotnet --version` retorna `10.x`.

---

## 2. Clonar o repositório

```bash
git clone https://github.com/gameking360/YoutubeSD.git
cd YoutubeSD
```

Todos os comandos deste guia partem da **raiz do repositório**, salvo quando indicado.

---

## 3. Gerar chaves e segredos para o Aspire (uma vez só)

Para usar o Docker Compose, vá direto ao [passo 4](#4-subir-o-ambiente). Para o Aspire, execute o utilitário na raiz.

```bash
dotnet run --project services/security/tools/SecuritySetup
```

O comando cria dois arquivos, ambos fora do Git:

| Arquivo | Usado por |
|---|---|
| `.secrets/security.json` | Aspire |
| `deploy/.env` | Importação opcional na primeira subida do Docker Compose |

Eles guardam a chave RSA que assina os tokens, a chave interna entre Security e Identity e o
token de administrador do reset de cota. Rodar de novo **preserva** os valores existentes.

> **Não apague esses arquivos entre reinícios.** Uma chave nova invalida todos os tokens já
> emitidos, e todas as réplicas precisam da mesma chave.

---

## 4. Subir o ambiente

Escolha **um** dos dois caminhos. Eles usam as mesmas portas, então só um pode estar no ar por vez
(veja [Trocar de ambiente](#trocar-de-ambiente)).

### Opção A — .NET Aspire (recomendado para desenvolver)

```bash
cd Orchestrador/YoutubeOrchestrador/YoutubeOrchestrador.AppHost
dotnet run --launch-profile http
```

O dashboard abre em **http://localhost:15006**. Espere todos os recursos ficarem verdes
(`Running` / `Healthy`); na primeira vez, as imagens Docker do Upload, do media-processor, do
Download e do front são compiladas e isso demora. O front fica em **http://localhost:4200**.

O número de réplicas e os recursos são parâmetros:

```bash
# padrão: Download ×3, Upload ×2, media-processor ×2
dotnet run --launch-profile http -- --DownloadReplicas 1 --UploadReplicas 1 --ProcessorReplicas 1

# processor mais forte (escala vertical)
dotnet run --launch-profile http -- --ProcessorReplicas 3 --ProcessorCpus 2 --ProcessorMemory 4g
```

Para parar: `Ctrl+C`. Redis, Kafka, Kafka UI, MySQL e MiniStack são containers **persistentes**
e continuam rodando; os dados ficam nos volumes para a próxima subida.

### Opção B — Docker Compose (só Docker)

```bash
docker compose --project-directory deploy up --build -d
```

Se o terminal já estiver em `deploy/`, use `docker compose up --build -d`. O Compose carrega
o YAML do AppHost e o `docker-compose.override.yaml`. Na primeira subida, o `security-init`
importa os valores fornecidos em `deploy/.env` e gera as credenciais que faltam. Nas próximas
subidas, reutiliza os valores dos volumes. Security e Identity iniciam depois que o
inicializador termina com sucesso.

A partir da raiz, acompanhe com `docker compose --project-directory deploy ps -a`. Os serviços
devem ficar em execução e os inicializadores `security-init` e `kafka-init` devem terminar com
código zero. O front fica em **http://localhost:4200** e o dashboard do Aspire em **http://localhost:18888**. Para consultar o token de acesso do dashboard, use o comando abaixo.

```bash
docker compose --project-directory deploy logs compose-dashboard
```

Para parar, execute `docker compose --project-directory deploy down`. Os dados e as credenciais ficam nos volumes.

> O `docker-compose.yaml` é gerado a partir do AppHost. Se alguém mudar o `AppHost.cs`, regenere
> na pasta do AppHost:
> `dotnet dnx --yes aspire.cli@13.6.5 -- publish -o ../../../deploy --non-interactive`

---

## 5. Conferir se subiu

| O quê | Como conferir |
|---|---|
| Front | http://localhost:4200 abre a tela de login |
| Gateway | `curl http://localhost:8080/health` responde `Healthy` |
| Kafka e tópicos | http://localhost:8090 lista os 10 tópicos e as `.dlq` |
| Download | Swagger em http://localhost:8401/swagger-ui/index.html |
| Upload | `curl http://localhost:8501/health` |
| media-processor | `curl http://localhost:8601/health` |
| Logs e traces | Dashboard do Aspire (15006 ou 18888) |

---

## 6. Primeiro uso

O jeito mais simples é pela interface: abra **http://localhost:4200**, crie uma conta, envie um
vídeo em "Enviar vídeo" e assista em "Assistir". Os passos abaixo fazem o mesmo pelo terminal.

Rode no **Git Bash**, na raiz do repositório.

### 6.1 Enviar um vídeo de teste (o jeito mais rápido)

```bash
./infra/upload/enviar-video.sh --gerar 20 "Meu primeiro vídeo"
```

O script gera um vídeo de 20 s com FFmpeg num container, cadastra e autentica o usuário
`demo-upload@youtubesd.local`, envia as partes pelo gateway mostrando qual réplica recebeu cada
uma, acompanha a saga e termina com:

```
Publicado. Reprodução: http://localhost:8080/api/download/videos/<videoId>/playback
videoId=<videoId>
```

Para enviar um arquivo seu:

```bash
./infra/upload/enviar-video.sh "C:/Users/voce/Videos/aula.mp4" "Aula 1"          # público
./infra/upload/enviar-video.sh "C:/Users/voce/Videos/aula.mp4" "Aula 1" PRIVATE  # só o dono vê
RETOMAR=<videoId> ./infra/upload/enviar-video.sh "C:/Users/voce/Videos/aula.mp4"  # retoma envio interrompido
```

### 6.2 Assistir

Vídeos públicos tocam sem login. Abra a playlist mestre num player com suporte a HLS, como o VLC
(**Mídia → Abrir fluxo de rede**):

```
http://localhost:8080/api/download/videos/<videoId>/hls/master.m3u8
```

O catálogo público fica em:

```bash
curl http://localhost:8080/api/download/videos
```

### 6.3 Usar a API com seu próprio usuário

```bash
# cadastro (público)
curl -s -X POST http://localhost:8080/api/identity/users \
  -H 'Content-Type: application/json' \
  -d '{"name":"Ana","email":"ana@teste.local","password":"senha123"}'

# login: guarda o access token numa variável
TOKEN=$(curl -s -X POST http://localhost:8080/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"ana@teste.local","password":"senha123"}' \
  | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')

# rotas pessoais
curl -s http://localhost:8080/api/identity/users/me   -H "Authorization: Bearer $TOKEN"
curl -s http://localhost:8080/api/identity/quota/me   -H "Authorization: Bearer $TOKEN"
curl -s http://localhost:8080/api/upload/videos       -H "Authorization: Bearer $TOKEN"
curl -s http://localhost:8080/api/download/progress   -H "Authorization: Bearer $TOKEN"

# enviar vídeo com esse usuário
TOKEN=$TOKEN ./infra/upload/enviar-video.sh --gerar 10 "Vídeo da Ana"
```

O access token vale 15 minutos. Para renovar, use `POST /api/auth/refresh` com o `refreshToken`
devolvido no login (cada refresh só pode ser usado uma vez), ou faça login de novo.

### 6.4 Publicar um vídeo direto no catálogo (sem passar pelo Upload)

Útil para testar só o Download:

```bash
./infra/seed/seed-video.sh demo-1 "Vídeo de demonstração"
./infra/seed/seed-video.sh meu-video "Meu vídeo" "C:/caminho/para/arquivo.mp4"
```

---

## 7. Rodar os cenários de falha

Com o ambiente no ar (de preferência com as réplicas padrão), no Git Bash:

```bash
./infra/falhas/f1-compensacao.sh   # dependência falhando 100% → compensação
./infra/falhas/f2-kill-replica.sh  # réplica do Download morre no meio
./infra/falhas/f2-processor.sh     # réplica do media-processor morre no meio do job
./infra/falhas/f3-duplicada.sh     # mesma mensagem 5 vezes → 1 efeito
./infra/falhas/f4-venenosa.sh      # mensagem inválida → DLQ
./infra/falhas/f6-kafka-fora.sh    # Kafka parado por 30 s
./infra/falhas/f7-prazo.sh         # resultado depois do prazo
./infra/falhas/f8-replicas.sh      # partições divididas entre 3 réplicas
./infra/falhas/limpar.sh           # remove do catálogo os vídeos de teste
```

### Token administrativo

Para o F5 e o reset de cota, carregue o token do ambiente em uso no Git Bash, na raiz do repositório.
Com o Docker Compose, leia o valor do volume montado no Identity.

```bash
export IDENTITY_ADMIN_TOKEN="$(docker compose --project-directory deploy exec -T identityservice sh -c 'cat /run/youtubesd/admin-token/value')"
```

Com o Aspire, use o arquivo criado pelo `SecuritySetup`.

```bash
export IDENTITY_ADMIN_TOKEN=$(grep '^IDENTITY_ADMIN_TOKEN=' deploy/.env | cut -d= -f2-)
```

Com a variável definida, execute o F5 no mesmo terminal.

```bash
./services/identity/scripts/f5-cota.sh
```

Cada script provoca a falha, mostra o estado antes e depois e termina com `RESULTADO: OK` ou
`FALHOU`. F2 precisa de pelo menos 2 réplicas do serviço testado; F8, de 3 réplicas do Download.

---

## 8. Rodar os testes

Os testes de banco sobem um MySQL descartável com Testcontainers, então o Docker precisa estar
aberto. Não precisam do ambiente no ar.

```bash
dotnet test services/security/tests/Security.Tests.csproj
dotnet test services/upload/upload/upload.tests
(cd services/download && ./mvnw verify)
```

Testes que **precisam** do ambiente no ar:

```bash
./services/security/scripts/smoke-auth.sh
# Windows / PowerShell, com o Compose no ar:
powershell -NoProfile -ExecutionPolicy Bypass -File services/security/scripts/validate-compose.ps1
```

---

## 9. Parar, trocar de ambiente e limpar

### Trocar de ambiente

Aspire e Compose usam as mesmas portas.

- **Do Compose para o Aspire:** `docker compose down` em `deploy/`.
- **Do Aspire para o Compose:** pare o AppHost (`Ctrl+C`) e pare os containers persistentes dele
  (`redis`, `kafka`, `kafka-ui`, `mysql`, `ministack`) pelo Docker Desktop ou com `docker stop`
  e os nomes que aparecem em `docker ps`.

### Começar do zero

> Isto **apaga** todos os usuários, vídeos e mensagens.

```bash
# Compose: derruba e remove os volumes
cd deploy && docker compose down -v

# Aspire: pare o AppHost e os containers persistentes, depois remova os volumes
docker volume rm youtubesd-kafka-data youtubesd-mysql-data youtubesd-ministack-data
```

No Compose, `down -v` também remove os volumes de credenciais. Na próxima subida, o
inicializador volta a importar os valores fornecidos em `deploy/.env` e gera os que faltam.
Uma nova chave de assinatura invalida os JWTs anteriores. As chaves do Aspire ficam em `.secrets/security.json`.

---

## 10. Problemas comuns

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| `port is already allocated` (8080, 3306, 9092…) | O outro ambiente ainda está no ar | Veja [Trocar de ambiente](#trocar-de-ambiente) |
| Aspire acusa falta de `SigningKeyBase64` | A chave local ainda precisa ser preparada | Execute o passo 3 |
| Compose avisa `variable is not set` para as credenciais quando elas não foram fornecidas por variável ou arquivo `.env` | O YAML base lê essas variáveis antes de aplicar o override | Confira `docker compose --project-directory deploy ps -a security-init`. O código zero indica que o inicializador preparou as credenciais |
| `security-init` termina com erro | O inicializador encontrou um problema ao gerar ou importar uma credencial | Consulte `docker compose --project-directory deploy logs security-init` |
| `401` em todas as rotas pessoais | Token expirado (15 min) ou chave regenerada | Faça login de novo |
| Upload termina em `FAILED` com cota rejeitada | O usuário já usou as 10 vagas | Zere a cota (veja [Zerar a cota](#zerar-a-cota)), remova vídeos antigos (`DELETE /api/upload/videos/{id}` devolve a vaga) ou use outro usuário |
| Vídeo parado em `PROCESSING` / `QUEUED` | Nenhum media-processor no ar | Confira no dashboard; o vídeo espera na fila até 30 min |
| Script `.sh` falha com `$'\r': command not found` | O arquivo foi salvo por um editor com quebra de linha do Windows (CRLF) | Converta para LF: `sed -i 's/\r$//' <arquivo>.sh` (o `.gitattributes` já baixa os `.sh` com LF) |
| Script não roda no PowerShell ou no CMD | Os scripts são Bash | Use o Git Bash |
| `docker compose up --build` falha baixando pacotes atrás de proxy corporativo | Rede com inspeção TLS | Os Dockerfiles aceitam o segredo de build `build_ca` com a CA da organização (veja `services/security/README.md`) |
| Primeira subida muito lenta | Download de imagens e compilação Maven/.NET | Normal; as próximas usam o cache |

### Zerar a cota

Cada usuário tem 10 vagas. Os vídeos de teste do `enviar-video.sh` usam o usuário
`demo-upload@youtubesd.local`. Carregue o [token administrativo](#token-administrativo) e execute
os comandos no mesmo terminal para devolver as vagas desse usuário.

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"demo-upload@youtubesd.local","password":"SenhaDemo123!"}' \
  | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')
USER_ID=$(curl -s http://localhost:8080/api/identity/users/me -H "Authorization: Bearer $TOKEN" \
  | sed -n 's/.*"id":"\([^"]*\)".*/\1/p')
curl -s -X POST "http://localhost:8080/api/identity/admin/users/$USER_ID/quota/reset" \
  -H "X-Admin-Token: $IDENTITY_ADMIN_TOKEN"
```

Troque o e-mail e a senha para zerar outro usuário. O script do F5 não serve para isso: ele cria
e zera um usuário próprio a cada execução.
