import { RouterProvider } from 'react-router'
import { ShellProvider } from './shell/ShellProvider'
import { shellRouter } from './shell/router'
import './App.css'

export default function App() {
  return (
    <ShellProvider>
      <RouterProvider router={shellRouter} />
    </ShellProvider>
  )
}
