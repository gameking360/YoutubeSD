// Proxy do `ng serve`: /api/** vai para o gateway (http://localhost:8080) sem CORS.
//
// O navegador envia o header Origin (http://localhost:4200) em PUT/POST/DELETE mesmo para o próprio host.
// O proxy repassa esse header, e o Spring do Download o compara com as origens liberadas
// (download.cors.allowed-origins, por padrão só :5500). Resultado: "403 Invalid CORS request" em
// todo PUT /api/download/progress, ou seja, o progresso nunca era salvo (os GETs não levam Origin e
// funcionavam). Como pelo proxy a chamada é de mesma origem, removemos o Origin antes de repassar.
export default {
  '/api': {
    target: 'http://localhost:8080',
    secure: false,
    changeOrigin: true,
    configure: (proxy) => {
      proxy.on('proxyReq', (proxyReq) => proxyReq.removeHeader('origin'));
    },
  },
};
