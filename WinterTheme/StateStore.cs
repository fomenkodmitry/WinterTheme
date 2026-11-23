using System.Text.Json;
using WinterTheme.Sim;

namespace WinterTheme;

/// <summary>Keeps snowmen and settled ground snow between launches.</summary>
public static class StateStore
{
    private static readonly string FilePath = Path.Combine(Settings.Dir, "scene.json");

    public sealed class StackDto
    {
        public float X { get; set; }
        public float[] R { get; set; } = Array.Empty<float>();
        public int Hat { get; set; } = -1;
        public uint Scarf { get; set; }
        public bool Nose { get; set; } = true;
    }

    public sealed class ScreenDto
    {
        public int Index { get; set; }
        public float Width { get; set; }
        public float[] Ground { get; set; } = Array.Empty<float>();
        public List<StackDto> Stacks { get; set; } = new();
    }

    public static void Save(IEnumerable<World> worlds)
    {
        try
        {
            var list = new List<ScreenDto>();
            foreach (var w in worlds)
            {
                lock (w)
                {
                    var dto = new ScreenDto
                    {
                        Index = w.Screen.Index,
                        Width = w.W,
                        Ground = w.Ground.H.Select(h => MathF.Round(h, 1)).ToArray(),
                    };
                    foreach (var root in w.Balls.Where(b => b.Below == null))
                    {
                        dto.Stacks.Add(new StackDto
                        {
                            X = root.X,
                            R = root.StackFromRoot().Select(b => b.R).ToArray(),
                            Hat = root.Man?.Hat ?? -1,
                            Scarf = root.Man != null ? (uint)root.Man.Scarf : 0,
                            Nose = root.Man?.HasNose ?? true,
                        });
                    }
                    list.Add(dto);
                }
            }
            Directory.CreateDirectory(Settings.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list));
        }
        catch
        {
            // non-critical
        }
    }

    public static void Load(World w)
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var list = JsonSerializer.Deserialize<List<ScreenDto>>(File.ReadAllText(FilePath));
            var dto = list?.FirstOrDefault(d => d.Index == w.Screen.Index);
            if (dto == null || MathF.Abs(dto.Width - w.W) > 1) return;

            lock (w)
            {
                if (dto.Ground.Length == w.Ground.N)
                    Array.Copy(dto.Ground, w.Ground.H, dto.Ground.Length);

                foreach (var s in dto.Stacks)
                {
                    Ball? below = null;
                    Ball? root = null;
                    foreach (var r in s.R.Take(3))
                    {
                        var b = new Ball { X = Math.Clamp(s.X, r, w.W - r), R = r, Y = w.H - r, Below = below };
                        if (below != null) below.Above = b;
                        root ??= b;
                        below = b;
                        w.Balls.Add(b);
                    }
                    if (root != null && s.R.Length >= 3 && s.Hat >= 0)
                        w.MakeSnowman(root, s.Hat, s.Scarf, s.Nose);
                }
            }
        }
        catch
        {
            // ignore broken state
        }
    }
}
