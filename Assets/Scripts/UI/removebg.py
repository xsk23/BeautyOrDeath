from PIL import Image
from rembg import remove

input_path = (
    r"D:\Program Files\Downloads\grok-image-7d4303b7-815c-48ff-bba3-b4b7bcb55082 (1).png"
)
output_path = "Assets/Image/UI/Lobbyroom/grok-image-a69947ab-961a-4a52-b123c3a-a52d5d9ea77_outp123u123t.png"

# 打开图片
input_image = Image.open(input_path)

# 移除背景
output_image = remove(input_image)

# 保存为 PNG（必须是 PNG 才能保留透明度）
output_image.save(output_path)
print(f"背景已移除，保存为 {output_path}")
