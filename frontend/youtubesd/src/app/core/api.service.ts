import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CONFIG } from './config';

export interface VideoItem { videoId: string; title: string; durationSec: number; }
export interface Playback {
  videoId: string; title: string; durationSec: number; manifestUrl: string;
  resumePositionSec: number; renditions?: unknown;
}
export interface Progress { videoId: string; positionSec: number; durationSec: number; status: string; }
export interface UploadStatus { videoId: string; title?: string; status: 'PENDING' | 'PROCESSING' | 'PUBLISHED' | 'FAILED'; }
export interface Quota { limit: number; used: number; reserved: number; available: number; }

@Injectable({ providedIn: 'root' })
export class ApiService {
  private http = inject(HttpClient);
  private base = CONFIG.gateway;

  me() { return this.http.get<{ id: string; name: string; email: string }>(`${this.base}${CONFIG.paths.me}`); }
  quota() { return this.http.get<Quota>(`${this.base}${CONFIG.paths.quota}`); }

  catalog() { return this.http.get<{ items: VideoItem[] }>(`${this.base}${CONFIG.paths.catalog}?size=50`); }
  playback(id: string) { return this.http.get<Playback>(`${this.base}${CONFIG.paths.catalog}/${encodeURIComponent(id)}/playback`); }
  continueWatching() { return this.http.get<Progress[]>(`${this.base}${CONFIG.paths.progress}?status=IN_PROGRESS`); }
  saveProgress(id: string, positionSec: number, durationSec: number) {
    return this.http.put<Progress>(`${this.base}${CONFIG.paths.progress}/${encodeURIComponent(id)}`,
      { positionSec, durationSec, clientTs: new Date().toISOString() });
  }

  // O README do Upload ainda não existe: multipart com "file" e "title" é suposição (ver LEIA-ME).
  upload(file: File, title: string) {
    const f = new FormData();
    f.append('title', title);
    f.append('file', file);
    return this.http.post<{ videoId: string }>(`${this.base}${CONFIG.paths.uploads}`, f);
  }
  myUploads() { return this.http.get<UploadStatus[]>(`${this.base}${CONFIG.paths.uploads}?owner=me`); }
}
