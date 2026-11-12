using System.Numerics;

namespace GroundWar.Sim.Map;

/// <summary>The battlefield split into squares, each scored for cover, visibility, height and who controls it.
/// Every AI layer can read it. Control is refreshed a few times a second.</summary>
public sealed class BattleGrid
{
    public float CellSize { get; }
    public int Width { get; }
    public int Height { get; }
    /// <summary>Best cover in the cell, 0..1.</summary>
    public float[] Cover { get; }
    /// <summary>How open the view from this cell is, 0 (boxed in) .. 1 (open field).</summary>
    public float[] Visibility { get; }
    /// <summary>Ground height in metres at the middle of the cell.</summary>
    public float[] GroundHeight { get; }
    /// <summary>-1 = Blue controls, +1 = Red controls, 0 = contested or empty.</summary>
    public float[] Control { get; }

    public BattleGrid(BattleMap map, float cellSize = 5f)
    {
        CellSize = cellSize;
        Width = (int)MathF.Ceiling(map.Width / cellSize);
        Height = (int)MathF.Ceiling(map.Height / cellSize);
        int n = Width * Height;
        Cover = new float[n];
        Visibility = new float[n];
        GroundHeight = new float[n];
        Control = new float[n];

        foreach (var cp in map.CoverPoints)
        {
            int idx = IndexOf(cp.Position);
            if (idx >= 0) Cover[idx] = MathF.Max(Cover[idx], cp.Strength);
        }

        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            var c = CellCenter(x, y);
            int i = y * Width + x;
            GroundHeight[i] = map.Terrain.HeightAt(c);
            var kind = map.Terrain.KindAt(c);
            // Woods are cover everywhere in them; crops only hide a man who keeps low.
            if (kind == GroundKind.Woods) Cover[i] = MathF.Max(Cover[i], 0.45f);
            else if (kind == GroundKind.Crops) Cover[i] = MathF.Max(Cover[i], 0.15f);
        }

        const int rays = 16;
        const float rayLength = 150f;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            var c = CellCenter(x, y);
            int open = 0;
            for (int r = 0; r < rays; r++)
            {
                float a = MathF.Tau * r / rays;
                var end = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * rayLength;
                if (!map.SightBlocked(c, end)) open++;
            }
            Visibility[y * Width + x] = open / (float)rays;
        }
    }

    public Vector2 CellCenter(int x, int y) => new((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);

    public int IndexOf(Vector2 p)
    {
        int x = (int)(p.X / CellSize), y = (int)(p.Y / CellSize);
        if (x < 0 || y < 0 || x >= Width || y >= Height) return -1;
        return y * Width + x;
    }

    /// <summary>Recompute control from where the fighting soldiers stand.</summary>
    public void UpdateControl(IEnumerable<(Vector2 pos, int sign)> soldiers, float radius = 30f)
    {
        Array.Clear(Control);
        int reach = (int)MathF.Ceiling(radius / CellSize);
        foreach (var (pos, sign) in soldiers)
        {
            int cx = (int)(pos.X / CellSize), cy = (int)(pos.Y / CellSize);
            for (int y = Math.Max(0, cy - reach); y <= Math.Min(Height - 1, cy + reach); y++)
            for (int x = Math.Max(0, cx - reach); x <= Math.Min(Width - 1, cx + reach); x++)
            {
                float d = Vector2.Distance(CellCenter(x, y), pos);
                if (d > radius) continue;
                Control[y * Width + x] += sign * (1f - d / radius) * 0.35f;
            }
        }
        for (int i = 0; i < Control.Length; i++) Control[i] = Math.Clamp(Control[i], -1f, 1f);
    }
}
