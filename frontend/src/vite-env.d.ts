/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_MARKET_SNAPSHOT_PATH?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
