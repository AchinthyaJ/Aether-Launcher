import os
import shutil

src1 = "/home/inchara/.gemini/antigravity/brain/f570d8e2-55bd-4344-b0c1-6aedca8f137c/minecraft_icon_1784435421251.png"
src2 = "/home/inchara/.gemini/antigravity/brain/f570d8e2-55bd-4344-b0c1-6aedca8f137c/snapshot_icon_1784435438621.png"
dst1 = "assets/minecraft_icon.png"
dst2 = "assets/snapshot_icon.png"

print("Checking src1:", os.path.exists(src1))
print("Checking src2:", os.path.exists(src2))

try:
    shutil.copy2(src1, dst1)
    print("Copied src1 successfully")
except Exception as e:
    print("Failed copying src1:", e)

try:
    shutil.copy2(src2, dst2)
    print("Copied src2 successfully")
except Exception as e:
    print("Failed copying src2:", e)
