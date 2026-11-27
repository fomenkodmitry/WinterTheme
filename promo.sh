#!/usr/bin/env bash
# Renders the horizontal promo clip offscreen (real simulation and painter on a mock desktop)
# and encodes it: dist/WinterTheme-promo-1080p.mp4. Needs ffmpeg (set FFMPEG=/path/to/ffmpeg if not on PATH).
set -euo pipefail
cd "$(dirname "$0")"

FFMPEG="${FFMPEG:-ffmpeg}"
frames="$(mktemp -d)"
trap 'rm -rf "$frames"' EXIT

dotnet run --project WinterTheme -c Release -- --render-video "$frames"

mkdir -p dist
"$FFMPEG" -hide_banner -loglevel error -y -framerate 30 -i "$frames/f%05d.jpg" \
  -f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000 -shortest \
  -vf "scale=in_range=pc:out_range=tv:out_color_matrix=bt709,format=yuv420p" \
  -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv \
  -c:v libx264 -profile:v high -preset slow -crf 18 -c:a aac -b:a 128k -movflags +faststart \
  dist/WinterTheme-promo-1080p.mp4
echo "Built dist/WinterTheme-promo-1080p.mp4"
