import { defineConfig } from 'drizzle-kit';
export default defineConfig({schema:'./host/schema.ts',out:'./drizzle',dialect:'sqlite'});
