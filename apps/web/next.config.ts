import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  /* Emits a self-contained server bundle, which is what deploy/Dockerfile.web copies. */
  output: "standalone",
};

export default nextConfig;
