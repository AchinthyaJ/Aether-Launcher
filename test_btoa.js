const bytes = new Uint8Array(4039).map((_, i) => i % 256);
console.log('Bytes read:', bytes.length);
try {
  const binary = String.fromCharCode(...bytes);
  const base64 = btoa(binary);
  console.log('Base64 length:', base64.length);
} catch (e) {
  console.error('Error:', e);
}
