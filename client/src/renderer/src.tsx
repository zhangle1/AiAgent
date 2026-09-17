import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './workspace-app';
import '@xterm/xterm/css/xterm.css';
import './style.css';
createRoot(document.getElementById('root')!).render(<StrictMode><App/></StrictMode>);
