from moviepy import VideoFileClip


def extract_audio_segment(video_path, output_audio_path, start_time, end_time=None):
    """
    video_path: 视频文件路径
    output_audio_path: 输出音频路径
    start_time: 开始时间，可以是秒数 (例如 10) 或者是 (分, 秒) 或者是 "00:00:10"
    end_time: 结束时间，如果不传则截取到视频结束
    """
    try:
        # 使用 with 自动管理资源
        with VideoFileClip(video_path) as video:
            # 1. 截取指定时段的视频流
            # subclipped 是 MoviePy 2.x 的新用法
            # 如果是旧版本(1.x)，请把 subclipped 改为 subclip
            segment = video.subclipped(start_time, end_time)

            # 2. 提取该段的音频
            audio = segment.audio

            if audio is None:
                print("该视频片段没有音轨！")
                return

            # 3. 写入文件
            audio.write_audiofile(output_audio_path)

            # 注意：segment 是 video 的一个视图，
            # 在 with 语句结束时，video 会被自动关闭

        print(f"提取成功！片段音频已保存至: {output_audio_path}")
    except Exception as e:
        print(f"提取失败: {e}")


# --- 使用示例 ---

# 路径
input_video = r"E:\downloads\b\leisai.mp4"
output_audio = r"E:\downloads\b\leisai_extracted_audio.mp3"

# 示例 1: 从第 5 秒开始，到第 15 秒结束
extract_audio_segment(input_video, output_audio, start_time=0, end_time=16)

# 示例 2: 从第 1 分 20 秒开始，直到视频结束
# extract_audio_segment(input_video, output_audio, start_time="00:01:20")

# 示例 3: 使用元组 (分, 秒)
# extract_audio_segment(input_video, output_audio, start_time=(1, 30), end_time=(2, 0))
