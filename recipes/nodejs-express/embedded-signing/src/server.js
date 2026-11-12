import { config, loadEnv } from './config.js';
import { createApp } from './app.js';
import { createBoldSign } from './boldsign.js';
import { createStore } from './store.js';

loadEnv();
const cfg = config();
if (!cfg.apiKey) console.warn('BOLDSIGN_API_KEY is not set: sending will fail. See .env.example.');
const app = createApp({ cfg, boldsign: createBoldSign(cfg), store: createStore(cfg.storeFile) });
app.listen(cfg.port, () => console.log(`Listening on http://localhost:${cfg.port}/documents (BoldSign region ${cfg.region})`));
