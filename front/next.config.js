// Frontend runs on :3782. This value is the backend API server used only by
// the Next.js server-side rewrite for /api/*.
const backendUrl = process.env.NEXT_PUBLIC_AIAGENT_API_BASE_URL || "http://localhost:5000";

/** @type {import('next').NextConfig} */
const nextConfig = {
  // The deployment script packages this minimal Node.js runtime for a server.
  output: "standalone",
  // Permit development assets and HMR from common local and private-network hosts.
  allowedDevOrigins: ["192.168.3.199", "10.*.*.*", "127.*.*.*", "172.*.*.*", "192.168.*.*", "*.localhost", "**.localhost", "*.local", "**.local"],
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
