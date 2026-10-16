Synthetic two-second silent H.264 baseline / yuv420p MP4 for the real WPF background playback tests.
Generated locally; no third-party footage or production dependency.

Generation command (FFmpeg 7.1 from the imageio-ffmpeg 0.6.0 test tooling wheel):

```text
ffmpeg -f lavfi -i testsrc2=size=160x90:rate=12 -t 2 -c:v libx264 -profile:v baseline -pix_fmt yuv420p -an -movflags +faststart background-loop.mp4
```
