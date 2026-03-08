import os

import chardet  # pip install chardet
import docx  # pip install python-docx


def get_file_content(file_path):
    """
    尝试读取文件内容，自动处理编码问题
    """
    try:
        # 1. 尝试直接读取为 UTF-8 (最快)
        with open(file_path, "r", encoding="utf-8") as f:
            return f.read()
    except UnicodeDecodeError:
        try:
            # 2. 如果失败，使用二进制模式读取并检测编码
            with open(file_path, "rb") as f:
                raw_data = f.read()
                result = chardet.detect(raw_data)
                encoding = result["encoding"]
                if encoding:
                    return raw_data.decode(encoding)
                else:
                    # 3. 如果检测不到，尝试 latin-1 (可以读取任意字节流，不会报错但可能有乱码)
                    return raw_data.decode("latin-1")
        except Exception as e:
            return f"Error decoding file: {e}"
    except Exception as e:
        return f"Error reading file: {e}"


def resolve_output_path(input_dir, output_filename="unity_code.md", place="inside"):
    """
    根据输入目录计算输出 markdown 路径
    place: "inside" -> 输入目录下
           "parent" -> 输入目录上一级
    """
    input_dir = os.path.abspath(input_dir)

    if place == "inside":
        return os.path.join(input_dir, output_filename)
    if place == "parent":
        return os.path.join(os.path.dirname(input_dir), output_filename)

    raise ValueError("place 只能是 'inside' 或 'parent'")


def generate_wp_code_markdown(
    root_dir,
    output_file,
    include_dirs=None,
    include_files=None,
    exclude_dirs=None,
    exclude_files=None,
):
    # WordPress 及 Web 开发常见后缀
    code_extensions = (
        # 核心逻辑
        ".php",
        ".inc",
        # 前端
        ".js",
        ".jsx",
        ".ts",
        ".tsx",
        ".vue",
        ".css",
        ".scss",
        ".sass",
        ".less",
        ".html",
        ".htm",
        # 配置与数据
        ".json",
        ".xml",
        ".yaml",
        ".yml",
        ".sql",  # 数据库导出
        ".ini",
        ".conf",
        ".htaccess",
        ".config",
        ".txt",
        ".md",
        ".svg",  # SVG本质是XML代码
        # 其他代码
        ".py",
        ".sh",
        ".bat",
        # 文档
        ".docx",
        ".cs",
    )

    # 默认初始化
    if include_dirs is None:
        include_dirs = []
    if include_files is None:
        include_files = []

    # 默认排除 WordPress 中不需要分析的目录
    if exclude_dirs is None:
        exclude_dirs = [
            ".git",
            ".vs",
            ".idea",
            ".vscode",  # IDE和版本控制
            "bin",
            "obj",
            "node_modules",
            "vendor",  # 依赖包
            "uploads",
            "cache",
            "upgrade",  # WP 动态资源
            "wp-content/uploads",
            "wp-content/cache",  # 具体路径匹配
        ]

    if exclude_files is None:
        exclude_files = [".DS_Store", "Thumbs.db", "wp-config-sample.php"]

    print(f"开始扫描目录: {root_dir}")
    print(f"结果将保存至: {output_file}")

    file_count = 0

    with open(output_file, "w", encoding="utf-8") as md_file:
        md_file.write(f"# WordPress Code Repository: {os.path.basename(root_dir)}\n\n")
        md_file.write("> Auto generated code dump.\n\n")

        for root, dirs, files in os.walk(root_dir):
            # 1. 过滤排除的目录 (修改 dirs 列表以阻止 os.walk 进入)
            dirs[:] = [
                d
                for d in dirs
                if d not in exclude_dirs
                and not any(ex in os.path.join(root, d) for ex in exclude_dirs)
            ]

            # 2. 检查包含目录逻辑 (如果指定了 include_dirs)
            # 这里的逻辑是：如果当前路径不是 include_dirs 的子路径，也不是 include_dirs 的父路径，则跳过
            if include_dirs:
                # 简单判断：当前 root 是否包含在任何 include_dirs 中，或者 include_dirs 是否包含在当前 root 中
                # 这里为了简化，假设 include_dirs 是相对于 root_dir 的名字
                # 如果当前 root 路径中不包含任何指定的 include 文件夹名，且我们已经深入到子目录，则可能需要跳过
                # 但为了保险起见，建议让 os.walk 遍历，在文件层级过滤
                pass

            for file in files:
                # 过滤文件名
                if file in exclude_files:
                    continue
                if include_files and file not in include_files:
                    continue

                # 过滤后缀
                if not file.lower().endswith(code_extensions):
                    continue

                file_path = os.path.join(root, file)

                # 再次确认目录包含逻辑 (更精准)
                if include_dirs:
                    rel_dir = os.path.relpath(root, root_dir)
                    # 如果当前文件的相对目录 不在 包含列表中，且不是根目录
                    is_included = False
                    for inc_dir in include_dirs:
                        if inc_dir in rel_dir.split(os.sep):
                            is_included = True
                            break
                    if not is_included and rel_dir != ".":
                        continue

                relative_path = os.path.relpath(file_path, root_dir)
                print(f"[{file_count + 1}] Processing: {relative_path}")

                md_file.write(f"## {relative_path}\n\n")

                # 处理 DOCX
                if file.lower().endswith(".docx"):
                    try:
                        doc = docx.Document(file_path)
                        md_file.write("```text\n")
                        for para in doc.paragraphs:
                            if para.text.strip():
                                md_file.write(para.text + "\n")
                        md_file.write("\n```\n\n")
                    except Exception as e:
                        md_file.write(f"> Error reading DOCX: {e}\n\n")

                # 处理普通代码文件
                else:
                    # 获取扩展名用于 markdown 高亮 (去掉点)
                    ext = file.split(".")[-1].lower()
                    # 映射一些扩展名到 markdown 支持的标准语言名
                    lang_map = {
                        "cs": "csharp",
                        "py": "python",
                        "js": "javascript",
                        "ts": "typescript",
                        "vue": "html",
                        "htm": "html",
                        "htaccess": "apache",
                        "conf": "nginx",
                    }
                    lang = lang_map.get(ext, ext)

                    md_file.write(f"```{lang}\n")
                    content = get_file_content(file_path)
                    md_file.write(content)
                    md_file.write("\n```\n\n")

                file_count += 1

    print(f"\n完成! 共处理 {file_count} 个文件。")


# ================= 配置区域 =================

# 输入目录路径
root_directory = r"E:\UnityProjects\Mirror_Lobby\Assets\Scripts"

# 输出文件名与位置: inside(输入目录下) / parent(输入目录上一级)
output_filename = "unity_code.md"
output_place = "inside"
output_md = resolve_output_path(root_directory, output_filename, output_place)

# 如果只想导出特定目录 (例如只看主题或插件)
# include_dirs = ['wp-content', 'themes', 'plugins']
include_dirs = []

# 如果只想导出特定文件
include_files = []

# 额外的排除目录 (在默认排除基础上增加)
exclude_dirs = [
    "wp-admin",
    "wp-includes",
    "easyshop",
    "shopire",
    "twentytwentyfive",
    "twentytwentythree",
    "twentytwentytwo",
    "uploads",
    "plugins",
    "languages",
    "fonts",
]  # 如果你只想看用户代码，建议排除这两个核心目录
exclude_files = []  # 额外排除特定文件

# 执行生成
generate_wp_code_markdown(
    root_directory, output_md, include_dirs, include_files, exclude_dirs, exclude_files
)
