import os

replacements = [
    # Full names (exact casing matters)
    ("AETHER LAUNCHER", "FUGO LAUNCHER"),
    ("Aether Launcher", "Fugo Launcher"),
    ("aether-launcher", "fugo-launcher"),
    ("AetherLauncher", "FugoLauncher"),
    ("AETHER CLIENT", "FUGO CLIENT"),
    ("Aether Client", "Fugo Client"),
    ("aether-client", "fugo-client"),
    ("AetherClient", "FugoClient"),

    # Other forms/substrings
    ("optimised for Aether", "optimised for Fugo"),
    ("Optimised for Aether", "Optimised for Fugo"),
    ("Aether-blue", "Fugo-blue"),
    ("Aether sky glow", "Fugo sky glow"),
    ("isAether", "isFugo"),
    ("useAetherOneCape", "useFugoOneCape"),
    ("Aether OptiFine Cape Service", "Fugo OptiFine Cape Service"),
    ("Aether Skin Service", "Fugo Skin Service"),
    ("AetherWorker", "FugoWorker"),
    ("Aether One", "Fugo One"),
    ("aetherOne", "fugoOne"),
    ("aetherCapesDir", "fugoCapesDir"),
    ("aetherCapePath", "fugoCapePath"),
    ("aether-logo.png", "fugo-logo.png"),

    # Let's also do a general case replacement for "Aether" to "Fugo" where appropriate
    # but excluding domain names or worker host.
    # Note: the replacements list is ordered from most specific to least specific
    # to avoid partial replacements.
]

extensions = [
    ".cs", ".axaml", ".sh", ".ps1", ".nsi", ".wxs", ".json", ".md", ".js", ".csx", ".xml", ".csproj", ".sln"
]

exclude_dirs = [
    "bin", "obj", ".git", ".vs", "publish", "dist", "node_modules", ".gemini", ".claude", ".codex"
]

print("Starting rebranding replace...")

for root, dirs, files in os.walk("."):
    # Filter out excluded directories
    dirs[:] = [d for d in dirs if d not in exclude_dirs]
    
    for file in files:
        # Check if file has target extension
        _, ext = os.path.splitext(file)
        if ext.lower() not in extensions:
            continue
            
        path = os.path.join(root, file)
        
        # Read the file
        try:
            with open(path, "r", encoding="utf-8") as f:
                content = f.read()
        except Exception as e:
            # Skip binary or unreadable files
            continue
            
        # Apply replacements
        original = content
        for old, new in replacements:
            content = content.replace(old, new)
            
        if content != original:
            try:
                with open(path, "w", encoding="utf-8") as f:
                    f.write(content)
                print(f"Rebranded strings in: {path}")
            except Exception as e:
                print(f"Error writing {path}: {e}")

print("String replacements complete.")
