from PIL import Image
from rembg import remove

input_path = r"Assets/Image/UI/cross.jpg"
output_path = "Assets/Image/UI/cross.png"

# 打开图片
input_image = Image.open(input_path)

# 移除背景
output_image = remove(input_image)

# 保存为 PNG（必须是 PNG 才能保留透明度）
output_image.save(output_path)
print(f"背景已移除，保存为 {output_path}")
