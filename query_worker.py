import urllib.request
import urllib.error

url = "https://aether.aetherservers.workers.dev/api/profiles/minecraft/DeathDragon13"
try:
    with urllib.request.urlopen(url) as response:
        print("Success:", response.read().decode())
except urllib.error.HTTPError as e:
    print("HTTPError Status:", e.code)
    print("HTTPError Body:", e.read().decode())
except Exception as e:
    print("Error:", e)
