import { defineConfig } from 'orval';

export default defineConfig({
  ardea: {
    input: { target: '../openapi/ardea.v1.json' },
    output: {
      mode: 'tags-split',
      target: 'projects/ardea/src/app/core/api/generated/endpoints',
      schemas: 'projects/ardea/src/app/core/api/generated/model',
      client: 'angular',
      clean: true,
      prettier: false,
      override: {
        angular: {
          retrievalClient: 'httpResource',
          observeOverloads: false,
        },
      },
    },
  },
});
