using System.Drawing.Drawing2D;

namespace MeshPatcherProject
{
    /// <summary>
    /// Loads a PNG from Resources/ (shipped alongside the exe - see the csproj's
    /// CopyToOutputDirectory rule) and scales it down for use as a button icon. Shared between
    /// MainForm and PresetEditorForm so there's one place that knows where icons live and how
    /// they're resized.
    /// </summary>
    internal static class IconLoader
    {
        /// <summary>
        /// Returns null (and appends a diagnostic to <paramref name="errors"/> if given) instead
        /// of throwing when a file is missing/unreadable, so callers can fall back to a text label
        /// rather than the app failing to start or open a dialog.
        /// </summary>
        public static Image? LoadIcon(string fileName, int size, List<string>? errors = null)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", fileName);
            try
            {
                using var original = Image.FromFile(path);

                var resized = new Bitmap(size, size);
                using var g = Graphics.FromImage(resized);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawImage(original, 0, 0, size, size);
                return resized;
            }
            catch (Exception ex)
            {
                errors?.Add($"{fileName} (looked for it at: {path})\n{ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }
}