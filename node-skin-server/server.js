const http = require("http");
const https = require("https");
const fs = require("fs");
const path = require("path");
const crypto = require("crypto");

function readArg(flag, fallback) {
  const index = process.argv.indexOf(flag);
  if (index === -1 || index + 1 >= process.argv.length) {
    return fallback;
  }
  return process.argv[index + 1];
}

const port = Number.parseInt(readArg("--port", "47135"), 10);
const storageDir = readArg(
  "--storage",
  path.join(process.env.HOME || process.cwd(), ".death-client", "skin-server"),
);

fs.mkdirSync(storageDir, { recursive: true });

const skinPath = path.join(storageDir, "current-skin.png");
const capePath = path.join(storageDir, "current-cape.png");
const statePath = path.join(storageDir, "state.json");

// Generate local RSA key pair for authlib-injector signing
const { publicKey, privateKey } = crypto.generateKeyPairSync("rsa", {
  modulusLength: 2048,
});
const publicPem = publicKey.export({ type: "spki", format: "pem" });

function signString(str) {
  const sign = crypto.createSign("SHA1");
  sign.update(str);
  return sign.sign(privateKey, "base64");
}

function getFileHash(filePath) {
  try {
    if (fs.existsSync(filePath)) {
      const data = fs.readFileSync(filePath);
      return crypto.createHash("md5").update(data).digest("hex");
    }
  } catch (err) {
    console.error("Error hashing file:", err);
  }
  return "default";
}

function getLocalState() {
  try {
    if (fs.existsSync(statePath)) {
      return JSON.parse(fs.readFileSync(statePath, "utf8"));
    }
  } catch (err) {
    console.error("Error reading state.json:", err);
  }
  return null;
}

function getPreferredSkinUrl(state) {
  if (state && typeof state.skinUrl === "string" && state.skinUrl.length > 0) {
    return state.skinUrl;
  }
  const hash = getFileHash(skinPath);
  return `http://127.0.0.1:${port}/v1/skins/current-${hash}.png`;
}

function getPreferredCapeUrl(state) {
  if (state && typeof state.capeUrl === "string" && state.capeUrl.length > 0) {
    return state.capeUrl;
  }
  const hash = getFileHash(capePath);
  return `http://127.0.0.1:${port}/v1/capes/current-${hash}.png`;
}

function getStaticCapeUrl(state) {
  const staticPath = path.join(storageDir, "current-cape-static.png");
  const hash = getFileHash(fs.existsSync(staticPath) ? staticPath : capePath);
  return `http://127.0.0.1:${port}/v1/capes/static-${hash}.png`;
}

function sendJson(res, statusCode, payload) {
  const body = Buffer.from(JSON.stringify(payload, null, 2));
  res.writeHead(statusCode, {
    "Content-Type": "application/json; charset=utf-8",
    "Cache-Control": "no-store",
    "Content-Length": body.length,
    "Access-Control-Allow-Origin": "*",
  });
  res.end(body);
}

function sendFile(res, filePath) {
  if (!fs.existsSync(filePath)) {
    sendJson(res, 404, { error: "Not found" });
    return;
  }

  const stat = fs.statSync(filePath);
  res.writeHead(200, {
    "Content-Type": "image/png",
    "Cache-Control": "no-store",
    "Content-Length": stat.size,
    "Access-Control-Allow-Origin": "*",
  });

  fs.createReadStream(filePath).pipe(res);
}

// Proxy helper with robust error handling and fallbacks
function proxyRequest(req, res, targetPath, responseModifier = null, errorHandler = null) {
  const options = {
    hostname: "aether.aetherservers.workers.dev",
    port: 443,
    path: targetPath,
    method: req.method,
    headers: {
      ...req.headers,
      host: "aether.aetherservers.workers.dev",
    },
  };

  // Skip headers that might cause decompression/encoding issues
  delete options.headers["accept-encoding"];

  const proxyReq = https.request(options, (proxyRes) => {
    if (responseModifier) {
      const chunks = [];
      proxyRes.on("data", (chunk) => chunks.push(chunk));
      proxyRes.on("end", () => {
        const data = Buffer.concat(chunks);
        const modified = responseModifier(proxyRes.statusCode, proxyRes.headers, data);
        res.writeHead(modified.statusCode || proxyRes.statusCode, modified.headers || proxyRes.headers);
        res.end(modified.data);
      });
    } else {
      res.writeHead(proxyRes.statusCode, proxyRes.headers);
      proxyRes.pipe(res);
    }
  });

  proxyReq.on("error", (err) => {
    console.error("Proxy error:", err);
    if (errorHandler) {
      errorHandler(err);
    } else {
      // Default fallback based on path to prevent Mojang / authlib crashes
      if (targetPath.startsWith("/sessionserver/session/minecraft/profile/")) {
        console.log(`[Proxy] Connection failed, serving 204 fallback for profile request`);
        res.writeHead(204, {
          "Cache-Control": "no-store",
          "Access-Control-Allow-Origin": "*",
        });
        res.end();
      } else {
        sendJson(res, 500, { error: "Proxy connection failed: " + err.message });
      }
    }
  });

  req.pipe(proxyReq);
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url, `http://${req.headers.host || "127.0.0.1"}`);
  const pathname = url.pathname;

  // CORS preflight
  if (req.method === "OPTIONS") {
    res.writeHead(200, {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST, PUT, DELETE, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, Authorization",
    });
    res.end();
    return;
  }

  // Local health check
  if (pathname === "/health") {
    sendJson(res, 200, {
      ok: true,
      storageDir,
      skinPresent: fs.existsSync(skinPath),
      capePresent: fs.existsSync(capePath),
    });
    return;
  }

  // Local shutdown (used to clean up orphaned processes on restart)
  if (pathname === "/shutdown" && (req.method === "POST" || req.method === "GET")) {
    sendJson(res, 200, { ok: true, message: "Shutting down" });
    console.log("Shutdown requested, exiting...");
    setTimeout(() => process.exit(0), 100);
    return;
  }

  // Local skin/cape endpoints
  if (pathname.startsWith("/v1/skins/current")) {
    sendFile(res, skinPath);
    return;
  }

  if (pathname.startsWith("/v1/capes/current")) {
    sendFile(res, capePath);
    return;
  }

  if (pathname.startsWith("/v1/capes/static")) {
    const staticPath = path.join(storageDir, "current-cape-static.png");
    if (fs.existsSync(staticPath)) {
      sendFile(res, staticPath);
    } else {
      sendFile(res, capePath);
    }
    return;
  }

  // OptiFine API cape requests (used by CustomSkinLoader for native animated capes)
  if (pathname.startsWith("/capes/") && (pathname.endsWith(".png") || pathname.endsWith(".png.mcmeta")) && req.method === "GET") {
    const match = pathname.match(/^\/capes\/(.+?)(?:\.png)?(\.png\.mcmeta)?$/);
    if (match) {
      const requestedUsername = match[1].toLowerCase();
      const isMcmeta = !!match[2];
      const state = getLocalState();
      
      if (state && state.username && requestedUsername === state.username.toLowerCase()) {
        const targetFile = isMcmeta ? (capePath + ".mcmeta") : capePath;
        if (fs.existsSync(targetFile)) {
          console.log(`[Proxy] Serving local OptiFine cape ${isMcmeta ? "metadata" : "texture"} for ${state.username}`);
          sendFile(res, targetFile);
          return;
        }
      }
      res.writeHead(404);
      res.end();
      return;
    }
  }

  // 1.5 CustomSkinLoader texture endpoints
  if (pathname.startsWith("/csl/textures/current-skin")) {
    sendFile(res, skinPath);
    return;
  }
  if (pathname.startsWith("/csl/textures/current-cape")) {
    sendFile(res, capePath);
    return;
  }
  if (pathname.startsWith("/csl/textures/http:/") || pathname.startsWith("/csl/textures/https:/") || pathname.startsWith("/csl/textures/http://") || pathname.startsWith("/csl/textures/https://")) {
    const match = req.url.match(/\/csl\/textures\/(https?:\/\/?.+)/);
    if (match) {
      let targetUrl = match[1];
      if (targetUrl.startsWith("https:/") && !targetUrl.startsWith("https://")) {
        targetUrl = "https://" + targetUrl.substring(7);
      } else if (targetUrl.startsWith("http:/") && !targetUrl.startsWith("http://")) {
        targetUrl = "http://" + targetUrl.substring(6);
      }
      res.writeHead(302, { Location: targetUrl });
      res.end();
      return;
    }
  }

  // 1. Intercept / (ALI metadata)
  if (pathname === "/" && req.method === "GET") {
    const handleLocalMetadata = () => {
      const localMetadata = {
        meta: {
          serverName: "Aether Skin Service (Local Fallback)",
          implementationName: "aether-skin-service",
          implementationVersion: "1.0.0"
        },
        skinDomains: [
          "aether.aetherservers.workers.dev",
          "127.0.0.1",
          "localhost"
        ],
        signaturePublickey: publicPem
      };
      sendJson(res, 200, localMetadata);
    };

    proxyRequest(
      req,
      res,
      pathname,
      (status, headers, data) => {
        try {
          if (status !== 200) {
            throw new Error(`Worker returned status ${status}`);
          }
          const metadata = JSON.parse(data.toString("utf8"));
          // Inject our local public key and allowed domains
          metadata.signaturePublickey = publicPem;
          if (!metadata.skinDomains) metadata.skinDomains = [];
          if (!metadata.skinDomains.includes("127.0.0.1")) metadata.skinDomains.push("127.0.0.1");
          if (!metadata.skinDomains.includes("localhost")) metadata.skinDomains.push("localhost");
          
          const body = Buffer.from(JSON.stringify(metadata, null, 2));
          const newHeaders = { ...headers };
          newHeaders["content-length"] = body.length;
          return { statusCode: status, headers: newHeaders, data: body };
        } catch (err) {
          console.warn("[Proxy] Failed to parse/modify ALI metadata, using local fallback:", err.message);
          const localMetadata = {
            meta: {
              serverName: "Aether Skin Service (Local Fallback)",
              implementationName: "aether-skin-service",
              implementationVersion: "1.0.0"
            },
            skinDomains: [
              "aether.aetherservers.workers.dev",
              "127.0.0.1",
              "localhost"
            ],
            signaturePublickey: publicPem
          };
          const body = Buffer.from(JSON.stringify(localMetadata, null, 2));
          return {
            statusCode: 200,
            headers: {
              "Content-Type": "application/json; charset=utf-8",
              "Cache-Control": "no-store",
              "Content-Length": body.length,
              "Access-Control-Allow-Origin": "*",
            },
            data: body
          };
        }
      },
      (err) => {
        console.warn("[Proxy] Metadata request failed, using local metadata fallback:", err.message);
        handleLocalMetadata();
      }
    );
    return;
  }

  // 2. Intercept profile requests
  if (pathname.startsWith("/sessionserver/session/minecraft/profile/") && req.method === "GET") {
    const match = pathname.match(/^\/sessionserver\/session\/minecraft\/profile\/([a-fA-F0-9]{32})/);
    if (match) {
      const requestedUuid = match[1].toLowerCase();
      const state = getLocalState();
      const localUuid = state && state.uuid ? state.uuid.replace(/-/g, "").toLowerCase() : null;

      // If requested UUID is the local player, resolve entirely locally offline
      if (localUuid && requestedUuid === localUuid) {
        console.log(`[Proxy] Serving profile response locally for local player ${state.username} (${requestedUuid})`);
        
        const profile = {
          id: localUuid,
          name: state.username,
          properties: []
        };

        const textures = {
          timestamp: Date.now(),
          profileId: localUuid,
          profileName: state.username,
          signatureRequired: true,
          textures: {}
        };

        if (fs.existsSync(skinPath)) {
          textures.textures.SKIN = {
            url: getPreferredSkinUrl(state),
            metadata: {
              model: state.model || "classic"
            }
          };
        }

        if (state && state.isCapeAnimated) {
          textures.textures.CAPE = {
            url: getStaticCapeUrl(state)
          };
        } else if (fs.existsSync(capePath)) {
          textures.textures.CAPE = {
            url: getPreferredCapeUrl(state)
          };
        }

        const texturesJson = JSON.stringify(textures);
        const texturesBase64 = Buffer.from(texturesJson).toString("base64");
        const signature = signString(texturesBase64);

        profile.properties.push({
          name: "textures",
          value: texturesBase64,
          signature: signature
        });

        sendJson(res, 200, profile);
        return;
      }
    }

    // Otherwise proxy to Cloudflare Worker
    proxyRequest(req, res, pathname + url.search, (status, headers, data) => {
      try {
        if (status !== 200) return { statusCode: status, headers, data };

        const profile = JSON.parse(data.toString("utf8"));
        const state = getLocalState();

        if (profile.properties) {
          for (const prop of profile.properties) {
            if (prop.name === "textures") {
              const texturesJson = Buffer.from(prop.value, "base64").toString("utf8");
              const textures = JSON.parse(texturesJson);

              if (
                state &&
                state.username &&
                profile.name &&
                profile.name.toLowerCase() === state.username.toLowerCase()
              ) {
                if (!textures.textures) textures.textures = {};

                // 1. Force SKIN texture
                if (fs.existsSync(skinPath)) {
                  textures.textures.SKIN = {
                    url: getPreferredSkinUrl(state),
                    metadata: {
                      model: state.model || "classic"
                    }
                  };
                  console.log(`[Proxy] Forced local SKIN texture URL and model (${state.model || "classic"}) for ${profile.name}`);
                }

                // 2. Force CAPE texture
                if (state.isCapeAnimated) {
                  textures.textures.CAPE = {
                    url: getStaticCapeUrl(state)
                  };
                  console.log(`[Proxy] Forced static fallback CAPE texture URL for animated cape of ${profile.name}`);
                } else if (fs.existsSync(capePath)) {
                  textures.textures.CAPE = {
                    url: getPreferredCapeUrl(state)
                  };
                  console.log(`[Proxy] Forced static CAPE texture URL for ${profile.name}`);
                } else {
                  delete textures.textures.CAPE;
                  console.log(`[Proxy] Removed CAPE texture for ${profile.name} (no cape configured)`);
                }
              }

              // Re-encode and re-sign
              const newTexturesJson = JSON.stringify(textures);
              const newTexturesBase64 = Buffer.from(newTexturesJson).toString("base64");
              const newSignature = signString(newTexturesBase64);

              prop.value = newTexturesBase64;
              prop.signature = newSignature;
            }
          }
        }

        const body = Buffer.from(JSON.stringify(profile, null, 2));
        const newHeaders = { ...headers };
        newHeaders["content-length"] = body.length;
        return { statusCode: status, headers: newHeaders, data: body };
      } catch (err) {
        console.error("Failed to parse/modify profile response:", err);
        return { statusCode: status, headers, data };
      }
    });
    return;
  }

  // 3. Intercept CSL requests
  if (pathname.startsWith("/csl/") && pathname.endsWith(".json") && req.method === "GET") {
    const match = pathname.match(/^\/csl\/(.+)\.json$/);
    if (match) {
      const requestedUsername = match[1].toLowerCase();
      const state = getLocalState();
      if (state && state.username && requestedUsername === state.username.toLowerCase()) {
        console.log(`[Proxy] Serving local CSL profile for player ${state.username}`);
        const csl = {
          username: state.username,
          skins: {}
        };
        if (fs.existsSync(skinPath)) {
          const model = state.model || "classic";
          const hash = getFileHash(skinPath);
          if (model === "slim") {
            csl.skins.slim = `current-skin-${hash}.png`;
          } else {
            csl.skins.default = `current-skin-${hash}.png`;
          }
        }
        if (fs.existsSync(capePath) && !state.isCapeAnimated) {
          const hash = getFileHash(capePath);
          csl.cape = `current-cape-${hash}.png`;
        }
        sendJson(res, 200, csl);
        return;
      }
    }

    proxyRequest(req, res, pathname, (status, headers, data) => {
      try {
        if (status !== 200) return { statusCode: status, headers, data };

        const csl = JSON.parse(data.toString("utf8"));
        const state = getLocalState();

        if (
          state &&
          state.username &&
          csl.username &&
          csl.username.toLowerCase() === state.username.toLowerCase()
        ) {
          // 1. Override skins if skinPath exists
          if (fs.existsSync(skinPath)) {
            if (!csl.skins) csl.skins = {};
            const skinUrl = getPreferredSkinUrl(state);
            const model = state.model || "classic";
            if (model === "slim") {
              csl.skins.slim = skinUrl;
              delete csl.skins.default;
            } else {
              csl.skins.default = skinUrl;
              delete csl.skins.slim;
            }
            csl.skin = skinUrl;
            console.log(`[Proxy] Set CSL local skin for ${csl.username} (${model})`);
          }

          // 2. Override cape
          if (state.isCapeAnimated) {
            delete csl.cape;
            console.log(`[Proxy] Removed CSL cape URL for animated cape on ${csl.username}`);
          } else if (fs.existsSync(capePath)) {
            csl.cape = getPreferredCapeUrl(state);
            console.log(`[Proxy] Set CSL local cape for ${csl.username}`);
          } else {
            delete csl.cape;
            console.log(`[Proxy] Removed CSL cape for ${csl.username} (no cape configured)`);
          }
        }

        const body = Buffer.from(JSON.stringify(csl, null, 2));
        const newHeaders = { ...headers };
        newHeaders["content-length"] = body.length;
        return { statusCode: status, headers: newHeaders, data: body };
      } catch (err) {
        console.error("Failed to parse/modify CSL response:", err);
        return { statusCode: status, headers, data };
      }
    });
    return;
  }

  // Catch-all proxy for other endpoints (announcements, texture serving, skin/cape lookup, session relay)
  proxyRequest(req, res, pathname + url.search);
});

server.listen(port, "127.0.0.1", () => {
  console.log(`listening on http://127.0.0.1:${port}`);
});
