import { defineConfig } from 'vitest/config'

export default defineConfig({
    // esbuild handles TSX in tests (we deliberately don't load @vitejs/plugin-react
    // here because Vite 8 / Vitest 3 have a nested-vite Plugin shape mismatch).
    // The automatic JSX runtime lets us skip the manual `import React` in test files.
    esbuild: {
        jsx: 'automatic',
        jsxImportSource: 'react',
    },
    test: {
        environment: 'jsdom',
        globals: true,
        setupFiles: './src/test/setup.ts',
        include: ['src/**/*.{test,spec}.{ts,tsx}'],
        css: false,
    },
})
