namespace Lander.Core.Vision;

/// <summary>
/// Borde seguido de un escaneo a otro, en coordenadas locales de su ventana (origen en la esquina superior
/// izquierda). Guarda una franja de píxeles alrededor del borde para comprobar que no ha cambiado y para
/// medir cuánto se desplaza con el scroll.
/// </summary>
public sealed class TrackedEdge
{
    internal TrackedEdge(int id, int y, int left, int right, bool rising, int contrast)
    {
        Id = id;
        Y = y;
        Left = left;
        Right = right;
        Rising = rising;
        Contrast = contrast;
    }

    /// <summary>Identificador estable mientras el borde siga ahí (distinto para cada borde de la ventana).</summary>
    public int Id { get; }

    /// <summary>Superficie del borde (fila del elemento) relativa a la ventana.</summary>
    public int Y { get; internal set; }

    public int Left { get; internal set; }

    public int Right { get; internal set; }

    public bool Rising { get; }

    public int Contrast { get; internal set; }

    /// <summary>Escaneos seguidos en los que se ha visto sin cambios.</summary>
    public int Hits { get; internal set; }

    public int Length => Right - Left;

    /// <summary>Muestras de la franja, por filas.</summary>
    internal byte[] Band { get; set; } = [];

    /// <summary>Fila local de la primera fila de la franja.</summary>
    internal int BandTop { get; set; }

    internal int BandRows { get; set; }

    /// <summary>Columna local de la primera muestra y distancia entre muestras.</summary>
    internal int BandLeft { get; set; }

    internal int BandStep { get; set; } = 1;

    internal int BandColumns { get; set; }
}

/// <summary>
/// Sigue los bordes de contenido de una ventana entre escaneos: conserva su identidad, los confirma cuando
/// aparecen igual en escaneos seguidos (descarta así vídeos y animaciones) y aplica los desplazamientos del
/// scroll.
/// </summary>
public sealed class ContentEdgeTracker
{
    /// <summary>Filas de la franja por encima de la superficie (fondo liso, sirve para alinear).</summary>
    public const int BandAbove = 6;

    /// <summary>Filas de la franja desde la superficie hacia abajo (lo que distingue un borde de otro).</summary>
    public const int BandBelow = 18;

    /// <summary>Muestras por fila de la franja como mucho.</summary>
    private const int MaxBandColumns = 160;

    /// <summary>Diferencia media (0-255) a partir de la cual la franja ha cambiado.</summary>
    public const double ChangedThreshold = 6;

    private List<TrackedEdge> _edges = [];
    private int _nextId = 1;

    /// <summary>Escaneos seguidos sin cambios necesarios para confirmar un borde.</summary>
    public int ConfirmScans { get; init; } = 2;

    public IReadOnlyList<TrackedEdge> Edges => _edges;

    public IEnumerable<TrackedEdge> Confirmed => _edges.Where(e => e.Hits >= ConfirmScans);

    public TrackedEdge? Find(int id) => _edges.Find(e => e.Id == id);

    /// <summary>
    /// Incorpora un escaneo nuevo. <paramref name="offsetX"/> y <paramref name="offsetY"/> sitúan el píxel
    /// (0, 0) de la imagen en coordenadas locales de la ventana. Los bordes que ya no aparecen se olvidan.
    /// </summary>
    public void Update(GrayImage image, int offsetX, int offsetY, IReadOnlyList<ContentEdge> detected)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(detected);

        var next = new List<TrackedEdge>(detected.Count);
        var used = new bool[_edges.Count];
        foreach (var found in detected)
        {
            var y = found.Y + offsetY;
            var left = found.Left + offsetX;
            var right = found.Right + offsetX;

            var match = -1;
            var bestOverlap = 0;
            for (var i = 0; i < _edges.Count; i++)
            {
                var old = _edges[i];
                if (used[i] || old.Rising != found.Rising || Math.Abs(old.Y - y) > 1)
                {
                    continue;
                }

                var overlap = Math.Min(old.Right, right) - Math.Max(old.Left, left);
                if (overlap > bestOverlap && overlap >= 0.6 * Math.Min(old.Length, right - left))
                {
                    bestOverlap = overlap;
                    match = i;
                }
            }

            TrackedEdge edge;
            if (match >= 0 && BandDifference(_edges[match], image, offsetX, offsetY, y - _edges[match].Y) <= ChangedThreshold)
            {
                used[match] = true;
                edge = _edges[match];
                edge.Y = y;
                edge.Left = left;
                edge.Right = right;
                edge.Contrast = found.Contrast;
                edge.Hits++;
            }
            else
            {
                if (match >= 0)
                {
                    used[match] = true;
                }

                edge = new TrackedEdge(_nextId++, y, left, right, found.Rising, found.Contrast) { Hits = 1 };
            }

            CaptureBand(edge, image, offsetX, offsetY);
            next.Add(edge);
        }

        _edges = next;
    }

    /// <summary>Desplaza todos los bordes <paramref name="dy"/> píxeles (scroll medido).</summary>
    public void Shift(int dy)
    {
        foreach (var edge in _edges)
        {
            edge.Y += dy;
            edge.BandTop += dy;
        }
    }

    public void Remove(int id) => _edges.RemoveAll(e => e.Id == id);

    public void Clear() => _edges.Clear();

    /// <summary>
    /// Diferencia media entre la franja guardada de <paramref name="edge"/>, desplazada <paramref name="dy"/>
    /// filas, y la misma zona de <paramref name="image"/>. Infinito si no se solapan lo suficiente.
    /// </summary>
    public static double BandDifference(TrackedEdge edge, GrayImage image, int offsetX, int offsetY, int dy)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(image);

        long sum = 0;
        var samples = 0;
        var pixels = image.Pixels;
        for (var row = 0; row < edge.BandRows; row++)
        {
            var imageY = edge.BandTop + row + dy - offsetY;
            if (imageY < 0 || imageY >= image.Height)
            {
                continue;
            }

            var line = pixels.Slice(imageY * image.Width, image.Width);
            var band = edge.Band.AsSpan(row * edge.BandColumns, edge.BandColumns);
            for (var col = 0; col < edge.BandColumns; col++)
            {
                var imageX = edge.BandLeft + col * edge.BandStep - offsetX;
                if ((uint)imageX < (uint)image.Width)
                {
                    sum += Math.Abs(line[imageX] - band[col]);
                    samples++;
                }
            }
        }

        return samples * 2 < edge.BandRows * edge.BandColumns || samples == 0 ? double.PositiveInfinity : (double)sum / samples;
    }

    private static void CaptureBand(TrackedEdge edge, GrayImage image, int offsetX, int offsetY)
    {
        var top = Math.Max(edge.Y - BandAbove, offsetY);
        var bottom = Math.Min(edge.Y + BandBelow, offsetY + image.Height);
        var left = Math.Max(edge.Left, offsetX);
        var right = Math.Min(edge.Right, offsetX + image.Width);
        var rows = Math.Max(0, bottom - top);
        var step = Math.Max(1, (right - left + MaxBandColumns - 1) / MaxBandColumns);
        var columns = right > left ? (right - left + step - 1) / step : 0;

        if (edge.Band.Length != rows * columns)
        {
            edge.Band = new byte[rows * columns];
        }

        var pixels = image.Pixels;
        for (var row = 0; row < rows; row++)
        {
            var line = pixels.Slice((top + row - offsetY) * image.Width, image.Width);
            for (var col = 0; col < columns; col++)
            {
                edge.Band[row * columns + col] = line[left + col * step - offsetX];
            }
        }

        edge.BandTop = top;
        edge.BandRows = rows;
        edge.BandLeft = left;
        edge.BandStep = step;
        edge.BandColumns = columns;
    }
}
