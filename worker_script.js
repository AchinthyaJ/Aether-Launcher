/**
 * Aether Launcher — Server Discovery + Skin Service Worker
 * Combines: server discovery/invite, Yggdrasil-compatible skin API,
 *           texture serving, and session relay for multiplayer skin visibility.
 *
 * KV Bindings required (Cloudflare Dashboard → Settings → Bindings):
 *   SERVERS  — KV Namespace for discovery + skins + sessions
 *
 * Paste this ENTIRE file into the Cloudflare Workers Quick Edit editor
 * and click "Save and Deploy"
 */

const WORKER_HOST = "aether.aetherservers.workers.dev";
const API_ROOT    = `https://${WORKER_HOST}`;

const CORS = {
  "Access-Control-Allow-Origin":  "*",
  "Access-Control-Allow-Methods": "GET, POST, PUT, DELETE, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, Authorization",
  "Content-Type": "application/json",
};

const JSON_HEADERS = { "Content-Type": "application/json", ...CORS };
const PNG_HEADERS  = { "Content-Type": "image/png", "Cache-Control": "public, max-age=86400, immutable" };

// ─── Key Management (Web Crypto RSA for signature) ───────────────────────────

let cachedPrivateKey = null;
let cachedPublicPem = null;
function uint8ArrayToBase64(arr) {
  const CHUNK_SIZE = 0x8000;
  let index = 0;
  const length = arr.length;
  let result = '';
  while (index < length) {
    const slice = arr.subarray(index, Math.min(index + CHUNK_SIZE, length));
    result += String.fromCharCode.apply(null, slice);
    index += CHUNK_SIZE;
  }
  return btoa(result);
}

function arrayBufferToBase64(buffer) {
  return uint8ArrayToBase64(new Uint8Array(buffer));
}

function derToPem(derBuffer, label = "PUBLIC KEY") {
  const b64 = arrayBufferToBase64(derBuffer);
  const lines = [];
  for (let i = 0; i < b64.length; i += 64) {
    lines.push(b64.slice(i, i + 64));
  }
  return `-----BEGIN ${label}-----\n${lines.join("\n")}\n-----END ${label}-----\n`;
}

async function getSigningKeys(env) {
  if (cachedPrivateKey && cachedPublicPem) {
    return { privateKey: cachedPrivateKey, publicPem: cachedPublicPem };
  }

  try {
    const privateJwkStr = await env.SERVERS.get("signature_key:private_jwk");
    const publicPemStr = await env.SERVERS.get("signature_key:public_pem");

    if (privateJwkStr && publicPemStr) {
      const jwk = JSON.parse(privateJwkStr);
      const privateKey = await crypto.subtle.importKey(
        "jwk",
        jwk,
        {
          name: "RSASSA-PKCS1-v1_5",
          hash: { name: "SHA-1" }
        },
        false,
        ["sign"]
      );
      cachedPrivateKey = privateKey;
      cachedPublicPem = publicPemStr;
      return { privateKey, publicPem: publicPemStr };
    }
  } catch (err) {
    console.error("Failed to load signing keys from KV:", err);
  }

  // Generate new 2048-bit RSA key pair
  const keyPair = await crypto.subtle.generateKey(
    {
      name: "RSASSA-PKCS1-v1_5",
      modulusLength: 2048,
      publicExponent: new Uint8Array([1, 0, 1]),
      hash: { name: "SHA-1" },
    },
    true,
    ["sign", "verify"]
  );

  const privateJwk = await crypto.subtle.exportKey("jwk", keyPair.privateKey);
  const publicSpki = await crypto.subtle.exportKey("spki", keyPair.publicKey);
  const publicPem = derToPem(publicSpki, "PUBLIC KEY");

  // Persist to KV — throw on failure to avoid silent in-memory-only keys
  // that differ across Worker isolates (Bug 3)
  try {
    await env.SERVERS.put("signature_key:private_jwk", JSON.stringify(privateJwk));
    await env.SERVERS.put("signature_key:public_pem", publicPem);
  } catch (err) {
    throw new Error("FATAL: Failed to persist signing keys to KV. " + ((err && err.message) || err));
  }

  cachedPrivateKey = keyPair.privateKey;
  cachedPublicPem = publicPem;

  return { privateKey: keyPair.privateKey, publicPem };
}

async function signString(str, privateKey) {
  const encoder = new TextEncoder();
  const data = encoder.encode(str);
  const signatureBuffer = await crypto.subtle.sign(
    { name: "RSASSA-PKCS1-v1_5" },
    privateKey,
    data
  );
  return arrayBufferToBase64(signatureBuffer);
}

// ─── Router ────────────────────────────────────────────────────────────────────

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    const path = url.pathname;
    const method = request.method;

    if (method === "OPTIONS") return new Response(null, { headers: CORS });

    try {
      // ── ALI Metadata (required by authlib-injector) ──
      if (path === "/" && method === "GET") return aliMetadata(env);

      // ── Discovery endpoints ──
      if (path === "/announce"  && method === "POST") return handleAnnounce(request, env);
      if (path === "/heartbeat" && method === "POST") return handleHeartbeat(request, env);
      if (path === "/shutdown"  && method === "POST") return handleShutdown(request, env);
      if (path === "/servers"   && method === "GET")  return handleFetchServers(url, env);
      if (path.startsWith("/resolve/") && method === "GET")
        return handleResolveInvite(path.slice(9), env);

      // ── Skin upload  PUT /api/skins/:username ──
      if (path.startsWith("/api/skins/") && method === "PUT")
        return handleSkinUpload(path.slice(11), request, env);

      // ── Cape upload  PUT /api/capes/:username ──
      if (path.startsWith("/api/capes/") && method === "PUT")
        return handleCapeUpload(path.slice(11), request, env);

      // ── Texture serving  GET /textures/:hash ──
      if (path.startsWith("/textures/") && method === "GET")
        return handleServeTexture(path.slice(10), env);

      // ── Skin by UUID  GET /skin/:uuid ──
      if (path.startsWith("/skin/") && method === "GET")
        return handleServeSkinByUUID(path.slice(6), env);

      // ── Cape by UUID  GET /cape/:uuid ──
      if (path.startsWith("/cape/") && method === "GET")
        return handleServeCapeByUUID(path.slice(6), env);

      // ── Profile lookup  GET /api/profiles/minecraft/:username ──
      if (path.startsWith("/api/profiles/minecraft/") && method === "GET")
        return handleProfileByUsername(path.slice(24), env);

      // ── GameProfile with textures  GET /sessionserver/session/minecraft/profile/:uuid ──
      if (path.startsWith("/sessionserver/session/minecraft/profile/") && method === "GET")
        return handleProfileByUUID(path.slice(41), url, env);

      // ── Session join relay (client → POST) ──
      if (path === "/sessionserver/session/minecraft/join" && method === "POST")
        return handleJoin(request, env);

      // ── Session verify relay (server → GET) ──
      if (path === "/sessionserver/session/minecraft/hasJoined" && method === "GET")
        return handleHasJoined(url, env);

      // ── CustomSkinLoader API  GET /csl/:username.json ──
      if (path.startsWith("/csl/") && path.endsWith(".json") && method === "GET")
        return handleCustomSkinAPI(path.slice(5, -5), env);

      return json404("Not Found");
    } catch (err) {
      return jsonError(err.message || "Internal Server Error", 500);
    }
  },
};

// ─── ALI Metadata ─────────────────────────────────────────────────────────────

async function aliMetadata(env) {
  let signaturePublickey = "";
  try {
    const keys = await getSigningKeys(env);
    signaturePublickey = keys.publicPem;
  } catch (err) {
    console.error("Error fetching signature public key:", err);
  }

  return jsonOk({
    meta: { serverName: "Aether Skin Service", implementationName: "AetherWorker", implementationVersion: "1.0.0" },
    skinDomains: [WORKER_HOST],
    signaturePublickey,
    authserver: `${API_ROOT}/authserver`,
    sessionserver: `${API_ROOT}/sessionserver`,
    api: `${API_ROOT}/api`,
    namespace: "urn:uuid:aether-skin-ns",
  });
}

// ─── Skin Upload ──────────────────────────────────────────────────────────────

async function handleSkinUpload(username, request, env) {
  try {
    if (!username) return jsonError("Missing username", 400);

    // Bug 1: Normalize username to lowercase for consistent UUID + KV keys
    const lowerUsername = username.toLowerCase();

    const contentType = request.headers.get("Content-Type") || "";
    let pngBytes, model = "classic";

    if (contentType.includes("multipart/form-data")) {
      const form = await request.formData();
      const skinFile = form.get("skin");
      model = form.get("model") || "classic";
      if (!skinFile || typeof skinFile === "string") return jsonError("Missing skin file", 400);
      pngBytes = new Uint8Array(await skinFile.arrayBuffer());
    } else if (contentType === "image/png") {
      pngBytes = new Uint8Array(await request.arrayBuffer());
      model = new URL(request.url).searchParams.get("model") || "classic";
    } else {
      return jsonError("Expected multipart/form-data with 'skin' field or image/png body", 400);
    }

    // Basic PNG header validation (magic bytes)
    if (pngBytes.length < 8 ||
        pngBytes[0] !== 0x89 || pngBytes[1] !== 0x50 || pngBytes[2] !== 0x4E || pngBytes[3] !== 0x47)
      return jsonError("File is not a valid PNG", 400);

    if (pngBytes.length > 256 * 1024)
      return jsonError("Skin file too large (max 256KB)", 400);

    // Compute SHA-256 hash for content-addressed storage
    const hashBuffer = await crypto.subtle.digest("SHA-256", pngBytes);
    const hash = Array.from(new Uint8Array(hashBuffer)).map(b => b.toString(16).padStart(2, "0")).join("");

    // Store texture in KV
    await env.SERVERS.put(`texture:${hash}`, uint8ArrayToBase64(pngBytes), {
      expirationTtl: 60 * 60 * 24 * 365, // 1 year
    });

    // Generate stable offline UUID using original casing
    const uuid = offlineUUID(username);

    // Store skin metadata (KV keys use lowercased username)
    const skinMeta = { username, uuid, model, textureHash: hash, uploadedAt: Date.now() };
    await env.SERVERS.put(`skin:${lowerUsername}`, JSON.stringify(skinMeta), {
      expirationTtl: 60 * 60 * 24 * 365,
    });
    // Also index by UUID for profile lookups
    await env.SERVERS.put(`skinuuid:${uuid}`, JSON.stringify(skinMeta), {
      expirationTtl: 60 * 60 * 24 * 365,
    });

    return jsonOk({
      hash,
      url: `${API_ROOT}/textures/${hash}`,
      uuid,
      model,
    });
  } catch (err) {
    return jsonError("Upload handler crash: " + err.message + "\nStack: " + err.stack, 500);
  }
}

// ─── Texture Serving ──────────────────────────────────────────────────────────

async function handleServeTexture(hash, env) {
  const isMcmeta = hash.endsWith(".mcmeta");
  const realHash = isMcmeta ? hash.slice(0, -7) : hash;

  if (!realHash || !/^[0-9a-f]{64}$/.test(realHash)) return json404("Invalid texture hash");

  const b64 = await env.SERVERS.get(`texture:${realHash}`);
  if (!b64) return json404("Texture not found");

  const binary = atob(b64);
  const bytes = Uint8Array.from(binary, c => c.charCodeAt(0));
  const isGif = bytes[0] === 0x47 && bytes[1] === 0x49 && bytes[2] === 0x46;

  if (isMcmeta) {
    try {
      if (isGif) {
        const mcmeta = {
          animation: {
            interpolate: true,
            frametime: 2
          }
        };
        return new Response(JSON.stringify(mcmeta), {
          status: 200,
          headers: {
            "Content-Type": "application/json",
            "Access-Control-Allow-Origin": "*",
          }
        });
      }

      if (bytes.length >= 24 &&
          bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4E && bytes[3] === 0x47) {
        const pngWidth  = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        const pngHeight = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];

        const frameHeight = (pngWidth >= 128) ? 64 : 32;
        if (pngHeight > frameHeight && pngHeight % frameHeight === 0) {
          const mcmeta = {
            animation: {
              interpolate: true,
              frametime: 2
            }
          };
          return new Response(JSON.stringify(mcmeta), {
            status: 200,
            headers: {
              "Content-Type": "application/json",
              "Access-Control-Allow-Origin": "*",
            }
          });
        }
      }
    } catch (e) { /* ignore and 404 */ }
    return json404("No animation metadata for this texture");
  }

  const contentType = isGif ? "image/gif" : "image/png";
  return new Response(bytes, {
    status: 200,
    headers: {
      "Content-Type": contentType,
      "Cache-Control": "public, max-age=604800",
      "Access-Control-Allow-Origin": "*",
    }
  });
}

// ─── Skin by UUID ─────────────────────────────────────────────────────────────

async function handleServeSkinByUUID(rawUuid, env) {
  const uuid = rawUuid.replace(/-/g, "").toLowerCase();
  const metaStr = await env.SERVERS.get(`skinuuid:${uuid}`);
  if (!metaStr) {
    const mojangSkin = await serveMojangSkin(uuid);
    if (mojangSkin) return mojangSkin;
    return json404("No skin found for this UUID");
  }

  const meta = JSON.parse(metaStr);
  if (!meta.textureHash) return json404("Skin metadata has no texture hash");

  return handleServeTexture(meta.textureHash, env);
}

async function serveMojangSkin(uuid) {
  try {
    const res = await fetch(`https://sessionserver.mojang.com/session/minecraft/profile/${uuid}`);
    if (res.status === 200) {
      const profile = await res.json();
      if (profile && profile.properties) {
        for (const prop of profile.properties) {
          if (prop.name === "textures") {
            const textures = JSON.parse(atob(prop.value));
            if (textures && textures.textures && textures.textures.SKIN && textures.textures.SKIN.url) {
              const skinRes = await fetch(textures.textures.SKIN.url);
              if (skinRes.status === 200) {
                const headers = new Headers(skinRes.headers);
                headers.set("Cache-Control", "public, max-age=3600");
                return new Response(skinRes.body, { status: 200, headers });
              }
            }
          }
        }
      }
    }
  } catch (err) {
    console.error(`[Worker] serveMojangSkin failed for ${uuid}:`, err);
  }
  return null;
}

// ─── Cape Upload ──────────────────────────────────────────────────────────────

async function handleCapeUpload(username, request, env) {
  try {
    if (!username) return jsonError("Missing username", 400);

    const lowerUsername = username.toLowerCase();

    const contentTypeHeader = request.headers.get("Content-Type") || "";
    let fileBytes;

    if (contentTypeHeader.includes("multipart/form-data")) {
      const form = await request.formData();
      const capeFile = form.get("cape");
      if (!capeFile || typeof capeFile === "string") return jsonError("Missing cape file", 400);
      fileBytes = new Uint8Array(await capeFile.arrayBuffer());
    } else {
      fileBytes = new Uint8Array(await request.arrayBuffer());
    }

    if (fileBytes.length < 24)
      return jsonError("File is too short", 400);

    const isPng = fileBytes[0] === 0x89 && fileBytes[1] === 0x50 && fileBytes[2] === 0x4E && fileBytes[3] === 0x47;
    const isGif = fileBytes[0] === 0x47 && fileBytes[1] === 0x49 && fileBytes[2] === 0x46;

    if (!isPng && !isGif)
      return jsonError("File is not a valid PNG or GIF", 400);

    if (fileBytes.length > 256 * 1024)
      return jsonError("Cape file too large (max 256KB)", 400);

    let isAnimated = false;
    let width = 0;
    let height = 0;

    if (isPng) {
      width  = (fileBytes[16] << 24) | (fileBytes[17] << 16) | (fileBytes[18] << 8) | fileBytes[19];
      height = (fileBytes[20] << 24) | (fileBytes[21] << 16) | (fileBytes[22] << 8) | fileBytes[23];
      const frameHeight = (width >= 128) ? 64 : 32;
      isAnimated = height > frameHeight && height % frameHeight === 0;
    } else if (isGif) {
      width = fileBytes[6] | (fileBytes[7] << 8);
      height = fileBytes[8] | (fileBytes[9] << 8);
      isAnimated = true; // GIFs are always animated capes
    }

    // Animated cape limit: 1 per IP address
    if (isAnimated) {
      const clientIp = request.headers.get("CF-Connecting-IP") || request.headers.get("X-Forwarded-For") || "unknown";
      const claimKey = `animated_cape_ip:${clientIp}`;
      const existingClaim = await env.SERVERS.get(claimKey);

      if (existingClaim && existingClaim !== lowerUsername) {
        return jsonError(
          `Animated capes are limited to 1 per IP. This IP already has an animated cape registered to "${existingClaim}".`,
          429
        );
      }

      // Register this IP → username claim (30 day TTL)
      await env.SERVERS.put(claimKey, lowerUsername, { expirationTtl: 60 * 60 * 24 * 30 });
    }

    // Compute SHA-256 hash for content-addressed storage
    const hashBuffer = await crypto.subtle.digest("SHA-256", fileBytes);
    const hash = Array.from(new Uint8Array(hashBuffer)).map(b => b.toString(16).padStart(2, "0")).join("");

    // Store texture in KV (reuses same texture namespace as skins)
    await env.SERVERS.put(`texture:${hash}`, uint8ArrayToBase64(fileBytes), {
      expirationTtl: 60 * 60 * 24 * 365,
    });

    const uuid = offlineUUID(username);

    // Store cape metadata (include animated flag and isGif flag)
    const capeMeta = { username, uuid, textureHash: hash, animated: isAnimated, isGif, uploadedAt: Date.now() };
    await env.SERVERS.put(`cape:${lowerUsername}`, JSON.stringify(capeMeta), {
      expirationTtl: 60 * 60 * 24 * 365,
    });
    await env.SERVERS.put(`capeuuid:${uuid}`, JSON.stringify(capeMeta), {
      expirationTtl: 60 * 60 * 24 * 365,
    });

    const frameCount = isGif ? countGifFrames(fileBytes) : (isAnimated ? height / ((width >= 128) ? 64 : 32) : 1);

    return jsonOk({
      hash,
      url: `${API_ROOT}/textures/${hash}`,
      uuid,
      animated: isAnimated,
      frames: frameCount,
    });
  } catch (err) {
    return jsonError("Cape upload crash: " + err.message + "\nStack: " + err.stack, 500);
  }
}

// ─── Cape by UUID ─────────────────────────────────────────────────────────────

async function handleServeCapeByUUID(rawUuid, env) {
  const uuid = rawUuid.replace(/-/g, "").toLowerCase();
  const metaStr = await env.SERVERS.get(`capeuuid:${uuid}`);
  if (!metaStr) {
    const mojangCape = await serveMojangCape(uuid);
    if (mojangCape) return mojangCape;
    return json404("No cape found for this UUID");
  }

  const meta = JSON.parse(metaStr);
  if (!meta.textureHash) return json404("Cape metadata has no texture hash");

  return handleServeTexture(meta.textureHash, env);
}

async function serveMojangCape(uuid) {
  try {
    const res = await fetch(`https://sessionserver.mojang.com/session/minecraft/profile/${uuid}`);
    if (res.status === 200) {
      const profile = await res.json();
      if (profile && profile.properties) {
        for (const prop of profile.properties) {
          if (prop.name === "textures") {
            const textures = JSON.parse(atob(prop.value));
            if (textures && textures.textures && textures.textures.CAPE && textures.textures.CAPE.url) {
              const capeRes = await fetch(textures.textures.CAPE.url);
              if (capeRes.status === 200) {
                const headers = new Headers(capeRes.headers);
                headers.set("Cache-Control", "public, max-age=3600");
                return new Response(capeRes.body, { status: 200, headers });
              }
            }
          }
        }
      }
    }
  } catch (err) {
    console.error(`[Worker] serveMojangCape failed for ${uuid}:`, err);
  }
  return null;
}

// ─── Shared GameProfile Builder ───────────────────────────────────────────────

/**
 * Builds a full GameProfile object with base64-encoded textures property and RSA signature.
 * Shared by handleProfileByUsername, handleProfileByUUID, and handleHasJoined.
 */
async function buildGameProfile(uuid, username, meta, env, unsigned, capeMeta) {
  const textureUrl = meta ? `${API_ROOT}/textures/${meta.textureHash}` : null;
  const model = (meta && meta.model) || "classic";

  // Always include SKIN.url — use the texture hash URL if available,
  // otherwise fall back to the /skin/:uuid endpoint
  const skinUrl = textureUrl || `${API_ROOT}/skin/${uuid}`;

  // Look up cape metadata if not passed in
  if (!capeMeta) {
    try {
      const capeStr = await env.SERVERS.get(`capeuuid:${uuid}`);
      if (capeStr) capeMeta = JSON.parse(capeStr);
    } catch (e) { /* ignore */ }
  }
  const capeUrl = capeMeta ? `${API_ROOT}/textures/${capeMeta.textureHash}` : null;
  const isAnimated = capeMeta && (capeMeta.animated || capeMeta.isGif);

  const textures = {
    SKIN: {
      url: skinUrl,
      ...(model === "slim" ? { metadata: { model: "slim" } } : {}),
    }
  };
  if (capeUrl) {
    textures.CAPE = { url: capeUrl };
  }

  const texturesPayload = {
    timestamp: Date.now(),
    profileId: uuid,
    profileName: username,
    textures,
  };

  const texturesBase64 = btoa(JSON.stringify(texturesPayload));

  let signature = "";
  if (!unsigned) {
    try {
      const keys = await getSigningKeys(env);
      signature = await signString(texturesBase64, keys.privateKey);
    } catch (err) {
      console.error("Signing failed in buildGameProfile:", err);
    }
  }

  return {
    id: uuid,
    name: username,
    properties: [
      {
        name: "textures",
        value: texturesBase64,
        ...(unsigned ? {} : { signature }),
      }
    ],
  };
}

// ─── Profile by Username ──────────────────────────────────────────────────────

async function handleProfileByUsername(username, env) {
  if (!username) return json404("Missing username");

  const lowerUsername = username.toLowerCase();
  const metaStr = await env.SERVERS.get(`skin:${lowerUsername}`);
  const meta = metaStr ? JSON.parse(metaStr) : null;

  const actualUsername = meta ? meta.username : username;
  const uuid = meta ? meta.uuid : offlineUUID(username);

  const profile = await buildGameProfile(uuid, actualUsername, meta, env, false);
  return jsonOk(profile);
}

// ─── GameProfile by UUID (with textures) ──────────────────────────────────────

async function handleProfileByUUID(rawUuid, url, env) {
  const uuid = rawUuid.replace(/-/g, "").toLowerCase();
  if (!uuid || uuid.length !== 32) return json404("Invalid UUID");

  const unsigned = url.searchParams.get("unsigned") === "true";

  // 1. Look up skin metadata by UUID
  let metaStr = await env.SERVERS.get(`skinuuid:${uuid}`);
  let meta = metaStr ? JSON.parse(metaStr) : null;
  let capeMeta = null;
  let username = (meta && meta.username) || uuidToUsername(uuid);

  // 2. If not found in KV, check if it's a Mojang UUID
  if (!meta) {
    try {
      const mojangRes = await fetch(`https://sessionserver.mojang.com/session/minecraft/profile/${uuid}`);
      if (mojangRes.status === 200) {
        const mojangProfile = await mojangRes.json();
        if (mojangProfile && mojangProfile.name) {
          username = mojangProfile.name;
          const lowerUsername = username.toLowerCase();

          // Check KV for custom skin
          const customMetaStr = await env.SERVERS.get(`skin:${lowerUsername}`);
          if (customMetaStr) {
            meta = JSON.parse(customMetaStr);
          }

          // Check KV for custom cape
          const customCapeStr = await env.SERVERS.get(`cape:${lowerUsername}`);
          if (customCapeStr) {
            capeMeta = JSON.parse(customCapeStr);
          }

          // If they have no custom skin in our KV, proxy and re-sign Mojang's official profile
          if (!meta) {
            if (mojangProfile.properties) {
              for (const prop of mojangProfile.properties) {
                if (prop.name === "textures") {
                  const texturesObj = JSON.parse(atob(prop.value));
                  const newBase64 = btoa(JSON.stringify(texturesObj));
                  let signature = "";
                  if (!unsigned) {
                    const keys = await getSigningKeys(env);
                    signature = await signString(newBase64, keys.privateKey);
                  }
                  prop.value = newBase64;
                  if (!unsigned) {
                    prop.signature = signature;
                  } else {
                    delete prop.signature;
                  }
                }
              }
            }
            return jsonOk(mojangProfile);
          }
        }
      }
    } catch (err) {
      console.error(`[Worker] Failed to resolve Mojang UUID ${uuid}:`, err);
    }
  }

  // 3. Fallback username if still not resolved
  if (!username) username = "Player";

  // 4. Build and return the profile (passing capeMeta if resolved)
  const profile = await buildGameProfile(uuid, username, meta, env, unsigned, capeMeta);
  return jsonOk(profile);
}

// ─── Session Join (client calls this when connecting to a server) ──────────────

async function handleJoin(request, env) {
  const body = await request.json();
  const { accessToken, selectedProfile, serverId, ip } = body;

  if (!selectedProfile || !serverId)
    return jsonError("Missing selectedProfile or serverId", 400);

  const uuid = (selectedProfile.id || selectedProfile).replace(/-/g, "").toLowerCase();

  // Store session with short TTL — server will call hasJoined within seconds
  const session = {
    uuid,
    username: selectedProfile.name || uuid,
    serverId,
    ip: ip || "unknown",
    expiry: Date.now() + 60_000, // 60 second window
  };
  console.log(`[handleJoin] serverId=${serverId} username=${session.username} uuid=${uuid} ip=${session.ip}`);
  await env.SERVERS.put(`session:${serverId}`, JSON.stringify(session), { expirationTtl: 60 });

  return new Response(null, { status: 204, headers: CORS });
}

// ─── HasJoined (server calls this to verify the client joined) ────────────────

async function handleHasJoined(url, env) {
  const username = url.searchParams.get("username");
  const serverId = url.searchParams.get("serverId");

  if (!username || !serverId) return json404("Missing username or serverId");

  // Bug 1: Lowercase session username for consistent KV lookups
  const lowerUsername = username.toLowerCase();

  console.log(`[handleHasJoined] serverId=${serverId} username=${username}`);

  const sessionStr = await env.SERVERS.get(`session:${serverId}`);
  if (!sessionStr) {
    console.log(`[handleHasJoined] NO session found for serverId=${serverId}`);
    return json404("Session not found");
  }

  const session = JSON.parse(sessionStr);
  console.log(`[handleHasJoined] Found session: username=${session.username} uuid=${session.uuid}`);

  // Verify session matches (case-insensitive)
  if (session.username.toLowerCase() !== lowerUsername) {
    console.log(`[handleHasJoined] Username mismatch: session=${session.username} request=${username}`);
    return json404("Username mismatch");
  }

  // Return full GameProfile using shared helper
  const uuid = session.uuid;
  const metaStr = await env.SERVERS.get(`skinuuid:${uuid}`);
  const meta = metaStr ? JSON.parse(metaStr) : null;
  console.log(`[handleHasJoined] Skin meta for uuid=${uuid}: ${meta ? 'found (hash=' + (meta.textureHash ? meta.textureHash.slice(0,8) : 'none') + ')' : 'none'}`);

  const profile = await buildGameProfile(uuid, username, meta, env, false);
  return jsonOk(profile);
}

// ─── CustomSkinLoader API (CustomSkinAPI format) ──────────────────────────────

async function handleCustomSkinAPI(username, env) {
  if (!username) return json404("Missing username");

  const lowerUsername = username.toLowerCase();
  const metaStr = await env.SERVERS.get(`skin:${lowerUsername}`);
  const meta = metaStr ? JSON.parse(metaStr) : null;

  // Look up cape metadata
  const capeStr = await env.SERVERS.get(`cape:${lowerUsername}`);
  const capeMeta = capeStr ? JSON.parse(capeStr) : null;

  if (!meta && !capeMeta) {
    // Return valid but empty response — CSL will fall through to next source
    return jsonOk({
      username,
      skins: {},
      cape: null,
    });
  }

  const skins = {};
  if (meta) {
    const textureUrl = `${API_ROOT}/textures/${meta.textureHash}`;
    const model = meta.model || "classic";
    if (model === "slim") {
      skins.slim = textureUrl;
    } else {
      skins["default"] = textureUrl;
    }
  }

  const cape = capeMeta ? `${API_ROOT}/textures/${capeMeta.textureHash}` : null;

  return jsonOk({
    username: (meta && meta.username) || username,
    skins,
    cape,
  });
}

// ─── Discovery: Announce ──────────────────────────────────────────────────────

async function handleAnnounce(request, env) {
  const body = await request.json();
  const { inviteCode, hostUserId, serverName, endpoint, players, autoInvite } = body;

  if (!inviteCode || !hostUserId || !serverName || !endpoint)
    return jsonError("Missing required fields", 400);

  const code = inviteCode.toLowerCase().replace(/[^a-z0-9-_]/g, "");
  if (!code) return jsonError("Invalid inviteCode format", 400);

  const data = {
    inviteCode: code, hostUserId, serverName, endpoint,
    online: true,
    players: Array.isArray(players) ? players : [],
    autoInvite: autoInvite ?? false,
    lastHeartbeat: Math.floor(Date.now() / 1000),
  };

  await env.SERVERS.put(`server:${code}`, JSON.stringify(data), { expirationTtl: 7200 });
  return jsonOk({ success: true, message: `Server '${serverName}' announced.` });
}

// ─── Discovery: Heartbeat ─────────────────────────────────────────────────────

async function handleHeartbeat(request, env) {
  const { inviteCode } = await request.json();
  if (!inviteCode) return jsonError("Missing inviteCode", 400);

  const key = `server:${inviteCode.toLowerCase()}`;
  const str = await env.SERVERS.get(key);
  if (!str) return jsonError("Server not registered", 404);

  const data = JSON.parse(str);
  data.lastHeartbeat = Math.floor(Date.now() / 1000);
  data.online = true;
  await env.SERVERS.put(key, JSON.stringify(data), { expirationTtl: 7200 });
  return jsonOk({ success: true });
}

// ─── Discovery: Shutdown ──────────────────────────────────────────────────────

async function handleShutdown(request, env) {
  const { inviteCode } = await request.json();
  if (!inviteCode) return jsonError("Missing inviteCode", 400);
  await env.SERVERS.delete(`server:${inviteCode.toLowerCase()}`);
  return jsonOk({ success: true });
}

// ─── Discovery: Fetch active servers ─────────────────────────────────────────

async function handleFetchServers(url, env) {
  const userId = url.searchParams.get("userId");
  if (!userId) return jsonError("Missing userId", 400);

  const list = await env.SERVERS.list({ prefix: "server:" });
  const results = [];

  await Promise.all(list.keys.map(async k => {
    const str = await env.SERVERS.get(k.name);
    if (!str) return;
    const srv = JSON.parse(str);
    const allowed = srv.autoInvite || srv.players.includes(userId) || srv.hostUserId === userId;
    if (srv.online && allowed)
      results.push({ inviteCode: srv.inviteCode, serverName: srv.serverName, endpoint: srv.endpoint, online: true });
  }));

  return jsonOk(results);
}

// ─── Discovery: Resolve invite ────────────────────────────────────────────────

async function handleResolveInvite(inviteCode, env) {
  if (!inviteCode) return jsonError("Missing inviteCode", 400);
  const str = await env.SERVERS.get(`server:${inviteCode.toLowerCase()}`);
  if (!str) return new Response(JSON.stringify({ online: false }), { status: 404, headers: JSON_HEADERS });
  const srv = JSON.parse(str);
  return jsonOk({ inviteCode: srv.inviteCode, serverName: srv.serverName, endpoint: srv.endpoint, online: srv.online });
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function jsonOk(body)         { return new Response(JSON.stringify(body), { status: 200, headers: JSON_HEADERS }); }
function jsonError(msg, code) { return new Response(JSON.stringify({ error: msg }), { status: code, headers: JSON_HEADERS }); }
function json404(msg)         { return new Response(JSON.stringify({ error: msg }), { status: 404, headers: JSON_HEADERS }); }

/**
 * Replicates Minecraft's offline UUID algorithm:
 * UUID.nameUUIDFromBytes(("OfflinePlayer:" + name).getBytes(UTF_8))
 * Returns UUID formatted as no-dashes hex string.
 */
function offlineUUID(username) {
  const input = "OfflinePlayer:" + username;
  const bytes = new TextEncoder().encode(input);
  const hash = md5Bytes(bytes);

  // Set version 3 and variant 2 bits
  hash[6] = (hash[6] & 0x0f) | 0x30;
  hash[8] = (hash[8] & 0x3f) | 0x80;

  return Array.from(hash).map(b => b.toString(16).padStart(2, "0")).join("");
}

function uuidToUsername(uuid) {
  return null; // Can't reverse UUID → username without KV lookup
}

// Minimal MD5 implementation for UUID generation (RFC 1321)
function md5Bytes(input) {
  function safeAdd(x, y) { const lx=(x&0xFFFF)+(y&0xFFFF); return (((x>>16)+(y>>16)+(lx>>16))<<16)|(lx&0xFFFF); }
  function bitRotateLeft(num, cnt) { return (num<<cnt)|(num>>>(32-cnt)); }
  function md5cmn(q,a,b,x,s,t) { return safeAdd(bitRotateLeft(safeAdd(safeAdd(a,q),safeAdd(x,t)),s),b); }
  function md5ff(a,b,c,d,x,s,t) { return md5cmn((b&c)|((~b)&d),a,b,x,s,t); }
  function md5gg(a,b,c,d,x,s,t) { return md5cmn((b&d)|(c&(~d)),a,b,x,s,t); }
  function md5hh(a,b,c,d,x,s,t) { return md5cmn(b^c^d,a,b,x,s,t); }
  function md5ii(a,b,c,d,x,s,t) { return md5cmn(c^(b|(~d)),a,b,x,s,t); }

  const inputLen = input.length;
  const inputPadded = new Uint8Array(((inputLen+8)>>6<<6)+64);
  inputPadded.set(input);
  inputPadded[inputLen] = 0x80;
  new DataView(inputPadded.buffer).setUint32((inputPadded.length-8),inputLen<<3,true);

  const M = new Int32Array(inputPadded.buffer);
  let a=1732584193,b=-271733879,c=-1732584194,d=271733878;

  for(let i=0;i<M.length;i+=16){
    const [aa,bb,cc,dd]=[a,b,c,d];
    a=md5ff(a,b,c,d,M[i+0],7,-680876936);d=md5ff(d,a,b,c,M[i+1],12,-389564586);c=md5ff(c,d,a,b,M[i+2],17,606105819);b=md5ff(b,c,d,a,M[i+3],22,-1044525330);
    a=md5ff(a,b,c,d,M[i+4],7,-176418897);d=md5ff(d,a,b,c,M[i+5],12,1200080426);c=md5ff(c,d,a,b,M[i+6],17,-1473231341);b=md5ff(b,c,d,a,M[i+7],22,-45705983);
    a=md5ff(a,b,c,d,M[i+8],7,1770035416);d=md5ff(d,a,b,c,M[i+9],12,-1958414417);c=md5ff(c,d,a,b,M[i+10],17,-42063);b=md5ff(b,c,d,a,M[i+11],22,-1990404162);
    a=md5ff(a,b,c,d,M[i+12],7,1804603682);d=md5ff(d,a,b,c,M[i+13],12,-40341101);c=md5ff(c,d,a,b,M[i+14],17,-1502002290);b=md5ff(b,c,d,a,M[i+15],22,1236535329);
    a=md5gg(a,b,c,d,M[i+1],5,-165796510);d=md5gg(d,a,b,c,M[i+6],9,-1069501632);c=md5gg(c,d,a,b,M[i+11],14,643717713);b=md5gg(b,c,d,a,M[i+0],20,-373897302);
    a=md5gg(a,b,c,d,M[i+5],5,-701558691);d=md5gg(d,a,b,c,M[i+10],9,38016083);c=md5gg(c,d,a,b,M[i+15],14,-660478335);b=md5gg(b,c,d,a,M[i+4],20,-405537848);
    a=md5gg(a,b,c,d,M[i+9],5,568446438);d=md5gg(d,a,b,c,M[i+14],9,-1019803690);c=md5gg(c,d,a,b,M[i+3],14,-187363961);b=md5gg(b,c,d,a,M[i+8],20,1163531501);
    a=md5gg(a,b,c,d,M[i+13],5,-1444681467);d=md5gg(d,a,b,c,M[i+2],9,-51403784);c=md5gg(c,d,a,b,M[i+7],14,1735328473);b=md5gg(b,c,d,a,M[i+12],20,-1926607734);
    a=md5hh(a,b,c,d,M[i+5],4,-378558);d=md5hh(d,a,b,c,M[i+8],11,-2022574463);c=md5hh(c,d,a,b,M[i+11],16,1839030562);b=md5hh(b,c,d,a,M[i+14],23,-35309556);
    a=md5hh(a,b,c,d,M[i+1],4,-1530992060);d=md5hh(d,a,b,c,M[i+4],11,1272893353);c=md5hh(c,d,a,b,M[i+7],16,-155497632);b=md5hh(b,c,d,a,M[i+10],23,-1094730640);
    a=md5hh(a,b,c,d,M[i+13],4,681279174);d=md5hh(d,a,b,c,M[i+0],11,-358537222);c=md5hh(c,d,a,b,M[i+3],16,-722521979);b=md5hh(b,c,d,a,M[i+6],23,76029189);
    a=md5hh(a,b,c,d,M[i+9],4,-640364487);d=md5hh(d,a,b,c,M[i+12],11,-421815835);c=md5hh(c,d,a,b,M[i+15],16,530742520);b=md5hh(b,c,d,a,M[i+2],23,-995338651);
    a=md5ii(a,b,c,d,M[i+0],6,-198630844);d=md5ii(d,a,b,c,M[i+7],10,1126891415);c=md5ii(c,d,a,b,M[i+14],15,-1416354905);b=md5ii(b,c,d,a,M[i+5],21,-57434055);
    a=md5ii(a,b,c,d,M[i+12],6,1700485571);d=md5ii(d,a,b,c,M[i+3],10,-1894986606);c=md5ii(c,d,a,b,M[i+10],15,-1051523);b=md5ii(b,c,d,a,M[i+1],21,-2054922799);
    a=md5ii(a,b,c,d,M[i+8],6,1873313359);d=md5ii(d,a,b,c,M[i+15],10,-30611744);c=md5ii(c,d,a,b,M[i+6],15,-1560198380);b=md5ii(b,c,d,a,M[i+13],21,1309151649);
    a=md5ii(a,b,c,d,M[i+4],6,-145523070);d=md5ii(d,a,b,c,M[i+11],10,-1120210379);c=md5ii(c,d,a,b,M[i+2],15,718787259);b=md5ii(b,c,d,a,M[i+9],21,-343485551);
    a=safeAdd(a,aa);b=safeAdd(b,bb);c=safeAdd(c,cc);d=safeAdd(d,dd);
  }

  const result = new Uint8Array(16);
  const dv = new DataView(result.buffer);
  dv.setInt32(0,a,true);dv.setInt32(4,b,true);dv.setInt32(8,c,true);dv.setInt32(12,d,true);
  return result;
}

function countGifFrames(bytes) {
  let count = 0;
  for (let i = 0; i < bytes.length - 2; i++) {
    if (bytes[i] === 0x21 && bytes[i+1] === 0xF9 && bytes[i+2] === 0x04) {
      count++;
    }
  }
  return count > 0 ? count : 1;
}
