// Tipos do youtubesd-upload.js (para usar no Angular/TypeScript).

export type VideoStatus = 'PENDING' | 'PROCESSING' | 'PUBLISHED' | 'FAILED' | 'REMOVED';
export type Visibility = 'PUBLIC' | 'UNLISTED' | 'PRIVATE';

export interface Rendition {
  name: string;
  width: number;
  height: number;
  bandwidth: number;
}

export interface UploadStatus {
  videoId: string;
  title: string;
  visibility: Visibility;
  status: VideoStatus;
  failureReason: string | null;
  /** COMPLETING: todas as partes chegaram e o original está sendo montado no servidor. */
  uploadState: 'RECEIVING' | 'COMPLETING' | 'COMPLETED' | 'ABORTED';
  quotaState: 'REQUESTED' | 'RESERVED' | 'REJECTED' | 'RELEASE_REQUESTED' | 'CONFIRMED';
  processingState: 'QUEUED' | 'RUNNING' | null;
  processingAttempts: number;
  fileName: string;
  sizeBytes: number;
  chunkSize: number;
  totalParts: number;
  receivedParts: number[];
  uploadedBytes: number;
  queueDeadlineAt: string | null;
  deadlineAt: string | null;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  durationSec: number | null;
  renditions: Rendition[] | null;
  /** Quando PUBLISHED: rota do Download para o player. */
  playbackUrl: string | null;
}

export interface UploadOptions {
  /** Ex.: http://localhost:8080 (o frontend fala só com o gateway). */
  gatewayUrl?: string;
  /** Chamado a cada requisição: devolva um access token válido (renove antes de expirar). */
  getToken: () => string | Promise<string>;
  file: File;
  title?: string;
  visibility?: Visibility;
  /** Partes enviadas em paralelo. Padrão: 3. */
  concurrency?: number;
  pollIntervalMs?: number;
  /** Tempo máximo de cada requisição antes de repetir. Padrão: 60000. */
  requestTimeoutMs?: number;
  signal?: AbortSignal;
  onProgress?: (progress: { videoId: string; sentBytes: number; totalBytes: number; part: number | null; totalParts: number }) => void;
  onStatus?: (status: UploadStatus) => void;
  /** Nenhuma réplica disponível: o cliente vai esperar e tentar de novo sozinho. */
  onWaiting?: (info: { method: string; path: string; status: number; retryInMs: number }) => void;
}

export interface UploadHandle {
  videoId: string;
  /** Resolve quando todas as partes foram enviadas e o envio foi concluído. */
  uploaded: Promise<void>;
  /** Resolve com o status final (PUBLISHED ou FAILED). */
  finished: Promise<UploadStatus>;
}

export declare class UploadError extends Error {
  status: number;
  body: unknown;
}

export declare function startUpload(options: UploadOptions): Promise<UploadHandle>;
export declare function resumeUpload(options: UploadOptions, videoId: string): Promise<UploadHandle>;
export declare function getStatus(options: Omit<UploadOptions, 'file'>, videoId: string): Promise<UploadStatus>;
export declare function cancelUpload(options: Omit<UploadOptions, 'file'>, videoId: string): Promise<UploadStatus>;
