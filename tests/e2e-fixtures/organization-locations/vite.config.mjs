import sharedConfig from '../../../src/frontend/goymen-erp-web/vite.config.ts'

// Fixture transport only; the shared frontend remains the application owner.
export default {
  ...sharedConfig,
  server: {
    ...sharedConfig.server,
    host: '127.0.0.1',
    port: 27173,
    strictPort: true,
    proxy: {
      ...sharedConfig.server?.proxy,
      '/api': {
        ...sharedConfig.server?.proxy?.['/api'],
        target: 'http://127.0.0.1:27080',
        changeOrigin: true,
      },
    },
  },
}
