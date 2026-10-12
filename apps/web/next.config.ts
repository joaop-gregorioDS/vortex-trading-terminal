import type { NextConfig } from "next";

/**
 * The browser always calls the same origin under /api. In production Nginx proxies that prefix to the
 * API container; in development this rewrite does the same, so the client code needs no base URL and
 * there is no CORS policy to maintain in either environment.
 */
const apiBaseUrl =
  process.env.VORTEX_API_BASE_URL ??
  (process.env.NODE_ENV === "development" ? "http://127.0.0.1:5187" : undefined);

const nextConfig: NextConfig = {
  /* Emits a self-contained server bundle, which is what deploy/Dockerfile.web copies. */
  output: "standalone",
  async redirects() {
    // /swagger without a trailing file makes the UI request its CSS and scripts from the site root.
    if (!apiBaseUrl) return [];
    return [{ source: "/swagger", destination: "/swagger/index.html", permanent: false }];
  },
  async rewrites() {
    // Only an address known at build time can be rewritten. In production Nginx owns the prefix, so
    // a missing value here is expected rather than a misconfiguration.
    if (!apiBaseUrl) return [];
    return [
      { source: "/api/:path*", destination: `${apiBaseUrl}/api/:path*` },
      { source: "/swagger/:path*", destination: `${apiBaseUrl}/swagger/:path*` },
    ];
  },
};

export default nextConfig;
