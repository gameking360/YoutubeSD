// Com o proxy do `ng serve` (proxy.conf.json), as chamadas vão para o mesmo host (sem CORS)
// e são encaminhadas ao gateway em http://localhost:8080.
// Para falar direto com o gateway, use 'http://localhost:8080' (e libere http://localhost:4200 no CORS dele).
export const CONFIG = {
  gateway: '',
  paths: {
    login: '/api/auth/login',
    refresh: '/api/auth/refresh',
    register: '/api/identity/users',
    me: '/api/identity/users/me',
    quota: '/api/identity/quota/me',
    uploads: '/api/upload/videos',
    catalog: '/api/download/videos',
    progress: '/api/download/progress',
    notifications: '/api/upload/notifications',
  },
  progressIntervalMs: 10_000,
  uploadPollMs: 3_000,
  // partes enviadas em paralelo (o gateway distribui entre as réplicas do Upload)
  uploadConcurrency: 3,
};
