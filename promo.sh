#!/usr/bin/env bash
# Renders the promo clip offscreen (real simulation and painter on a mock desktop) and encodes it.
#   ./promo.sh             -> dist/WinterTheme-promo-vertical.mp4   (1080x1920, for Reels / Shorts / TikTok)
#   ./promo.sh horizontal  -> dist/WinterTheme-promo-horizontal.mp4 (1920x1080)
# Needs ffmpeg (set FFMPEG=/path/to/ffmpeg if it is not on PATH).
set -euo pipefail
cd "$(dirname "$0")"

ORIENT="${1:-vertical}"
FFMPEG="${FFMPEG:-ffmpeg}"
frames="$(mktemp -d)"
trap 'rm -rf "$frames"' EXIT

dotnet run --project WinterTheme -c Release -- --render-video "$frames" "$ORIENT"

mkdir -p dist
out="dist/WinterTheme-promo-$ORIENT.mp4"
"$FFMPEG" -hide_banner -loglevel error -y -framerate 30 -i "$frames/f%05d.jpg" \
  -f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000 -shortest \
  -vf "scale=in_range=pc:out_range=tv:out_color_matrix=bt709,format=yuv420p" \
  -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv \
  -c:v libx264 -profile:v high -preset slow -crf 18 -c:a aac -b:a 128k -movflags +faststart \
  "$out"
echo "Built $out"
