// tokens.css per primo (TASKS M0): curve, durate, scala tipografica, keyframes, base kiosk.
import './tokens/tokens.css';
import './fonts.css';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';

const container = document.getElementById('root');
if (!container) throw new Error('Manca #root in index.html');

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
