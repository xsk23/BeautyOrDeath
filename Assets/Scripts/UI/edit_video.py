from moviepy import VideoFileClip


def trim_video(input_path, output_path, start_time, end_time=None):
    """
    裁剪视频并保存
    input_path: 输入视频路径
    output_path: 输出视频路径
    start_time: 开始时间 (秒，或字符串 "00:00:13")
    end_time: 结束时间 (秒，或字符串 "00:00:30")，若为 None 则到结尾
    """
    try:
        # 使用 with 自动管理资源
        with VideoFileClip(input_path) as video:
            # 1. 截取指定时段
            # MoviePy 2.x 使用 subclipped
            # MoviePy 1.x 使用 subclip
            trimmed_clip = video.subclipped(start_time, end_time)

            # 2. 导出视频
            # codec="libx264" 是最通用的 MP4 编码
            # audio_codec="aac" 确保音频也正确编码
            trimmed_clip.write_videofile(
                output_path, codec="libx264", audio_codec="aac"
            )

        print(f"裁剪成功！视频已保存至: {output_path}")
    except Exception as e:
        print(f"裁剪失败: {e}")


# --- 使用示例 ---

# 你的原始视频路径
input_video = r"E:\downloads\b\wanezhiyuan.mp4"
# 裁剪后的保存路径
output_video = r"E:\downloads\b\wanezhiyuan_trimmed.mp4"

# 裁剪从第 13 秒到第 30 秒
trim_video(input_video, output_video, start_time=85, end_time=90)
