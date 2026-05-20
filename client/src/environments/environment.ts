export const environment = {
  production: false,
  apiBase: '/api',
  /** Same origin → dev proxy (`ws: true`). Docker nginx forwards `/api` to the API container. */
  jobsHubUrl: '/api/hubs/jobs',
};
