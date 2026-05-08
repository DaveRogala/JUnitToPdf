using PdfSharpCore.Fonts;

namespace JUnitToPdf;

sealed class NickelCityFontResolver : IFontResolver
{
    private readonly Dictionary<string, byte[]> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _family;

    public NickelCityFontResolver(string fontFolder, string family = "Nickel City")
    {
        _family = family;

        if (!Directory.Exists(fontFolder))
            return;

        foreach (var file in Directory.EnumerateFiles(fontFolder, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            var key = Path.GetFileNameWithoutExtension(file);
            try
            {
                _fonts[key] = File.ReadAllBytes(file);
            }
            catch
            {
                // ignore unreadable font file
            }
        }
    }

    public bool HasFonts => _fonts.Count > 0;

    public string DefaultFontName => _family;

    public byte[] GetFont(string faceName)
    {
        if (_fonts.TryGetValue(faceName, out var bytes))
            return bytes;

        // fallback to any loaded font (instead of throwing)
        if (_fonts.Count > 0)
            return _fonts.Values.First();

        // No fonts available; return an empty array so callers can still proceed using platform fonts.
        return Array.Empty<byte>();
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // If no Nickel City fonts were loaded, just defer to platform fonts.
        if (_fonts.Count == 0)
            return PlatformFontResolver.ResolveTypeface(familyName, isBold, isItalic);

        if (!string.Equals(familyName, _family, StringComparison.OrdinalIgnoreCase))
            return PlatformFontResolver.ResolveTypeface(familyName, isBold, isItalic);

        // Prefer common naming patterns.
        string? bold = _fonts.Keys.FirstOrDefault(k => k.Contains("Bold", StringComparison.OrdinalIgnoreCase));
        string? book = _fonts.Keys.FirstOrDefault(k => k.Contains("Book", StringComparison.OrdinalIgnoreCase))
                   ?? _fonts.Keys.FirstOrDefault(k => k.Contains("Regular", StringComparison.OrdinalIgnoreCase))
                   ?? _fonts.Keys.FirstOrDefault(k => k.Contains("Medium", StringComparison.OrdinalIgnoreCase))
                   ?? _fonts.Keys.FirstOrDefault(k => k.Contains("Roman", StringComparison.OrdinalIgnoreCase));

        string? pick = isBold ? (bold ?? book) : (book ?? bold);
        pick ??= _fonts.Keys.FirstOrDefault();

        if (pick is null)
            return PlatformFontResolver.ResolveTypeface(familyName, isBold, isItalic);

        return new FontResolverInfo(pick);
    }
}