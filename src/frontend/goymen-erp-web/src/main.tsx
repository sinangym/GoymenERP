import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { installAntiforgeryFetch } from './shell/antiforgery.ts'

// KIM-D31: one shared mechanism adds the antiforgery header to every cookie-session write.
installAntiforgeryFetch((import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1')

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
