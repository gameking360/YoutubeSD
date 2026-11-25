// Cliente de upload do YoutubeSD para o navegador (módulo ES, sem dependências).
//
// O arquivo é enviado em partes, pelo gateway, do mesmo jeito para vídeo curto ou longo:
//   1. POST   /api/upload/videos                  → 202 { videoId, chunkSize, totalParts }
//   2. PUT    /api/upload/videos/{id}/parts/{n}   → cada parte (várias em paralelo)
//   3. POST   /api/upload/videos/{id}/complete    → 202; o processamento segue no servidor
//   4. GET    /api/upload/videos/{id}             → status (PENDING, PROCESSING, PUBLISHED, FAILED)
//
// A tela nunca fica travada: o envio roda em segundo plano e o progresso chega por callbacks.
// Se a réplica do Upload cair, a parte é repetida e o gateway manda para outra. Se nenhuma
// estiver disponível (502/503/504 ou rede), o cliente espera, sem limite de tentativas, e continua
// de onde parou. Uma página recarregada retoma com resumeUpload(videoId, file).
//
// Exemplo:
//   const upload = await startUpload({
//     gatewayUrl: 'http://localhost:8080',
//     getToken: () => auth.accessToken,
//     file: input.files[0],
//     title: 'Minha aula',
//     onProgress: ({ sentBytes, totalBytes }) => barra.value = sentBytes / totalBytes,
//     onStatus: (status) => console.log(status.status),
//     onWaiting: ({ retryInMs }) => aviso('Servidor indisponível, tentando de novo...'),
//   });
//   localStorage.setItem('upload-pendente', upload.videoId);
//   await upload.uploaded;        // todas as partes enviadas
//   const final = await upload.finished; // PUBLISHED ou FAILED

const RETRYABLE = new Set([0, 408, 425, 429, 500, 502, 503, 504]);
const MAX_DELAY_MS = 30_000;

export class UploadError extends Error {
  constructor(message, status, body) {
    super(message);
    this.name = 'UploadError';
    this.status = status;
    this.body = body;
  }
}

/** Cria o upload e começa a enviar as partes em segundo plano. */
export async function startUpload(options) {
  const { file, title, visibility = 'PUBLIC' } = options;
  const created = await request(options, 'POST', '/api/upload/videos', JSON.stringify({
    title: title || file.name,
    visibility,
    fileName: file.name,
    sizeBytes: file.size,
    contentType: file.type || null,
  }), 'application/json');

  const status = await request(options, 'GET', created.statusUrl);
  return run(options, status);
}

/** Retoma um envio interrompido (ex.: página recarregada): só manda as partes que faltam. */
export async function resumeUpload(options, videoId) {
  const status = await getStatus(options, videoId);
  if (status.sizeBytes !== options.file.size) {
    throw new UploadError('O arquivo escolhido não é o mesmo deste upload.', 409, status);
  }
  return run(options, status);
}

export function getStatus(options, videoId) {
  return request(options, 'GET', `/api/upload/videos/${encodeURIComponent(videoId)}`);
}

export function cancelUpload(options, videoId) {
  return request(options, 'DELETE', `/api/upload/videos/${encodeURIComponent(videoId)}`);
}

function run(options, status) {
  const uploaded = sendParts(options, status);
  const finished = uploaded.then(() => waitForResult(options, status.videoId));
  // Evita "unhandled rejection" se quem chamou só observar um dos dois.
  uploaded.catch(() => {});
  finished.catch(() => {});
  return { videoId: status.videoId, uploaded, finished };
}

async function sendParts(options, status) {
  const { file, concurrency = 3, onProgress } = options;
  const { videoId, chunkSize, totalParts, sizeBytes } = status;

  if (status.status !== 'PENDING') {
    if (status.status === 'FAILED') {
      throw new UploadError(`Upload encerrado: ${status.failureReason}`, 409, status);
    }
    return; // já foi enviado
  }

  if (status.uploadState === 'RECEIVING') {
    const received = new Set(status.receivedParts);
    const pending = [];
    for (let part = 1; part <= totalParts; part++) {
      if (!received.has(part)) pending.push(part);
    }

    let sentBytes = status.uploadedBytes || 0;
    onProgress?.({ videoId, sentBytes, totalBytes: sizeBytes, part: null, totalParts });

    const worker = async () => {
      while (pending.length > 0) {
        const part = pending.shift();
        const start = (part - 1) * chunkSize;
        const blob = file.slice(start, Math.min(start + chunkSize, sizeBytes));
        await request(options, 'PUT', `/api/upload/videos/${videoId}/parts/${part}`, blob, 'application/octet-stream');
        sentBytes += blob.size;
        onProgress?.({ videoId, sentBytes, totalBytes: sizeBytes, part, totalParts });
      }
    };

    await Promise.all(Array.from({ length: Math.max(1, Math.min(concurrency, pending.length)) }, worker));
  }

  await request(options, 'POST', `/api/upload/videos/${videoId}/complete`);
}

async function waitForResult(options, videoId) {
  const { onStatus, pollIntervalMs = 2000, signal } = options;
  let last = '';
  for (;;) {
    const status = await getStatus(options, videoId);
    const key = `${status.status}|${status.processingState}|${status.quotaState}`;
    if (key !== last) {
      last = key;
      onStatus?.(status);
    }
    if (status.status === 'PUBLISHED' || status.status === 'FAILED' || status.status === 'REMOVED') {
      return status;
    }
    await sleep(pollIntervalMs, signal);
  }
}

/** fetch com espera sem limite enquanto não houver réplica disponível. */
async function request(options, method, path, body, contentType) {
  const { gatewayUrl = '', getToken, signal, onWaiting, requestTimeoutMs = 60_000 } = options;
  let delay = 1000;

  for (;;) {
    signal?.throwIfAborted();

    let response = null;
    try {
      const headers = { Authorization: `Bearer ${await getToken()}` };
      if (contentType) headers['Content-Type'] = contentType;
      // Uma requisição pendurada (réplica morreu sem fechar a conexão) também é repetida.
      const timeout = AbortSignal.timeout(requestTimeoutMs);
      const combined = signal ? AbortSignal.any([signal, timeout]) : timeout;
      response = await fetch(gatewayUrl + path, { method, headers, body, signal: combined });
    } catch (error) {
      if (signal?.aborted) throw error;
      // Erro de rede ou timeout: réplica caiu no meio da parte ou gateway fora. Tenta de novo.
    }

    if (response?.ok) {
      return response.status === 204 ? null : response.json();
    }

    const status = response?.status ?? 0;
    if (!RETRYABLE.has(status)) {
      const problem = await response.json().catch(() => null);
      throw new UploadError(problem?.detail || problem?.title || `HTTP ${status}`, status, problem);
    }

    const retryAfter = Number(response?.headers.get('Retry-After')) * 1000;
    const wait = retryAfter > 0 ? retryAfter : delay;
    onWaiting?.({ method, path, status, retryInMs: wait });
    await sleep(wait, signal);
    delay = Math.min(delay * 2, MAX_DELAY_MS);
  }
}

function sleep(ms, signal) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(resolve, ms);
    signal?.addEventListener('abort', () => {
      clearTimeout(timer);
      reject(signal.reason);
    }, { once: true });
  });
}
