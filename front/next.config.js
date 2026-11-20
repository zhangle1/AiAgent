// Frontend runs on :3782 in deployment. This value is the backend API server used only by
// the Next.js server-side rewrite for /api/*.
const backendUrl = process.env.NEXT_PUBLIC_AIAGENT_API_BASE_URL || "http://127.0.0.1:5000";
console.info(`[AiAgent] API proxy target: ${backendUrl}`);

/** @type {import('next').NextConfig} */
const nextConfig = {
  // The deployment script packages this minimal Node.js runtime for a server.
  output: "standalone",
  experimental: {
    // /api rewrites clone the body too; the default 10 MB truncates PDF uploads.
    // Match the backend request ceiling; individual documents remain limited to 25 MB.
    proxyClientMaxBodySize: 200 * 1024 * 1024,
  },
  // Permit development assets and HMR from common local and private-network hosts.
  allowedDevOrigins: ["192.168.1.8","192.168.0.45","192.168.90.162","192.168.1.4","192.168.3.199","192.168.1.6","192.168.3.199", "10.*.*.*", "127.*.*.*", "172.*.*.*", "192.168.*.*", "*.localhost", "**.localhost", "*.local", "**.local"],
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${backendUrl}/api/:path*`,
      },
    ];
  },
};

module.exports = nextConfig;
