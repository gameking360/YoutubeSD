import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, tap } from 'rxjs';
import { CONFIG } from './config';

export type Visibility = 'PUBLIC' | 'UNLISTED' | 'PRIVATE';
export type VideoStatus = 'PENDING' | 'PROCESSING' | 'PUBLISHED' | 'FAILED' | 'REMOVED';
export type ProgressStatus = 'IN_PROGRESS' | 'COMPLETED';

export interface Rendition { name: string; width: number; height: number; bandwidth: number; }

// ---- Download ----
export interface VideoItem {
  videoId: string; title: string; durationSec: number; thumbnailKey?: string | null; publishedAt?: string;
}
export interface Page<T> { items: T[]; page: number; size: number; hasNext?: boolean; total?: number; }
export interface Playback {
  videoId: string; title: string; durationSec: number; manifestUrl: string;
  resumePositionSec: number; renditions?: Rendition[];
}
export interface Progress {
  videoId: string; positionSec: number; durationSec: number; status: ProgressStatus; lastWatchedAt?: string;
}

// ---- Upload ----
export interface UploadStatus {
  videoId: string; title?: string; visibility?: Visibility; status: VideoStatus;
  failureReason?: string | null; sizeBytes?: number; createdAt?: string; publishedAt?: string | null;
  durationSec?: number | null;
}
export interface UploadDetail extends UploadStatus {
  uploadState: 'RECEIVING' | 'COMPLETING' | 'COMPLETED' | 'ABORTED';
  quotaState: string; processingState?: string | null; processingAttempts: number;
  fileName: string; chunkSize: number; totalParts: number; receivedParts: number[]; uploadedBytes: number;
  queueDeadlineAt?: string | null; deadlineAt?: string | null; updatedAt: string;
  renditions?: Rendition[] | null; playbackUrl?: string | null;
}
export interface UploadInit { videoId: string; chunkSize: number; totalParts: number; }
export interface Notification { id: number; videoId: string; type: string; message: string; createdAt: string; }

// ---- Identity ----
export interface Quota { limit: number; used: number; reserved: number; available: number; }
export interface User { id: string; name: string; email: string; role: string; createdAt: string; quota?: Quota; }

@Injectable({ providedIn: 'root' })
export class ApiService {
  private http = inject(HttpClient);
  private base = CONFIG.gateway;
  private up = `${CONFIG.gateway}${CONFIG.paths.uploads}`;
  private prog = `${CONFIG.gateway}${CONFIG.paths.progress}`;

  /** Usuário logado, compartilhado entre o cabeçalho e a página de perfil. */
  readonly usuario = signal<User | null>(null);

  // ---- Identity ----
  me() { return this.http.get<User>(`${this.base}${CONFIG.paths.me}`).pipe(tap((u) => this.usuario.set(u))); }
  updateMe(name: string) {
    return this.http.put<User>(`${this.base}${CONFIG.paths.me}`, { name }).pipe(tap((u) => this.usuario.set(u)));
  }
  quota() { return this.http.get<Quota>(`${this.base}${CONFIG.paths.quota}`); }

  // ---- Download: catálogo e reprodução ----
  catalog(page = 0, size = 20) {
    return this.http.get<Page<VideoItem>>(`${this.base}${CONFIG.paths.catalog}?page=${page}&size=${size}`);
  }
  playback(id: string) {
    return this.http.get<Playback>(`${this.base}${CONFIG.paths.catalog}/${encodeURIComponent(id)}/playback`);
  }

  // ---- Download: progresso ----
  progressList(status: ProgressStatus = 'IN_PROGRESS', limit = 20) {
    return this.http.get<Progress[]>(`${this.prog}?status=${status}&limit=${limit}`);
  }
  continueWatching() { return this.progressList('IN_PROGRESS'); }
  progress(id: string) { return this.http.get<Progress>(`${this.prog}/${encodeURIComponent(id)}`); }
  progressUrl(id: string) { return `${this.prog}/${encodeURIComponent(id)}`; }
  saveProgress(id: string, positionSec: number, durationSec: number, clientTs = new Date().toISOString()) {
    return this.http.put<Progress>(this.progressUrl(id), { positionSec, durationSec, clientTs });
  }

  // ---- Upload em partes (README do Upload) ----
  // 1) cria o upload (JSON) -> 202 { videoId, chunkSize, totalParts }
  createUpload(title: string, file: File, visibility: Visibility) {
    return this.http.post<UploadInit>(this.up, {
      title,
      visibility,
      fileName: file.name,
      sizeBytes: file.size,
      contentType: file.type || 'video/mp4',
    });
  }
  // 2) envia cada parte (corpo binário; o navegador define o Content-Length)
  uploadPart(videoId: string, part: number, blob: Blob) {
    return this.http.put<{ partNumber: number; sizeBytes: number; eTag: string }>(
      `${this.up}/${encodeURIComponent(videoId)}/parts/${part}`, blob,
      { headers: { 'Content-Type': 'application/octet-stream' }, observe: 'response' });
  }
  // 3) conclui -> 202 (a montagem e o processamento seguem em segundo plano)
  completeUpload(videoId: string) {
    return this.http.post<UploadDetail>(`${this.up}/${encodeURIComponent(videoId)}/complete`, {});
  }
  // status completo, com as partes recebidas (para retomar o envio)
  uploadDetail(videoId: string) { return this.http.get<UploadDetail>(`${this.up}/${encodeURIComponent(videoId)}`); }
  removeUpload(videoId: string) { return this.http.delete<UploadDetail>(`${this.up}/${encodeURIComponent(videoId)}`); }

  myUploads(page = 0, size = 10) {
    return this.http
      .get<Page<UploadStatus> | UploadStatus[]>(`${this.up}?owner=me&page=${page}&size=${size}`)
      .pipe(map((r): Page<UploadStatus> => (Array.isArray(r) ? { items: r, page, size, total: r.length } : r)));
  }
  notifications(limit = 30) { return this.http.get<Notification[]>(`${this.base}${CONFIG.paths.notifications}?limit=${limit}`); }
}
