import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { viteSingleFile } from 'vite-plugin-singlefile';

// Studio ships as a single double-clickable index.html (no server, no build
// step for the end user). Vite's default build emits <script type="module">
// + a separate stylesheet, which browsers refuse to load cross-origin when
// the page is opened via file:// (opaque "null" origin) — vite-plugin-singlefile
// inlines the JS/CSS/assets straight into the HTML so there's nothing left to fetch.
export default defineConfig({
  plugins: [svelte(), viteSingleFile()],
  base: './',
});
