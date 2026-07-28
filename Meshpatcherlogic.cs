using System.Drawing;
using System.Linq;
using System.Reflection;
using NiflySharp;
using NiflySharp.Blocks;
using NiflySharp.Enums;
using NifFile = NiflySharp.NifFile;
using BSLightingShaderProperty = NiflySharp.Blocks.BSLightingShaderProperty;
using BSShaderTextureSet = NiflySharp.Blocks.BSShaderTextureSet;
using SkyrimShaderPropertyFlags1 = NiflySharp.Enums.SkyrimShaderPropertyFlags1;
using SkyrimShaderPropertyFlags2 = NiflySharp.Enums.SkyrimShaderPropertyFlags2;


namespace MeshPatcherProject
{
    /// <summary>
    /// Core batch-patching logic, extracted so it can be driven from either a console
    /// entry point or a GUI without duplicating anything. All progress/status goes through
    /// the supplied <paramref name="log"/> callback rather than Console.WriteLine.
    /// </summary>
    internal static class MeshPatcherLogic
    {
        public class RunResult
        {
            public int PatchedFiles;
            public int PatchedShapes;
            public int CopiedFiles;
            public int SkippedFiles;
        }

        public static RunResult Run(string inputFolder, string settingsPath, string outputFolder,
            bool dryRun, bool environmentMapOnly, bool automaticMode, CancellationToken cancellationToken, Action<string> log)
        {
            var result = new RunResult();

            Settings? manualSettings = null;
            Dictionary<string, Settings>? presets = null;
            Settings? defaultPreset = null;

            if (automaticMode)
            {
                var presetsFolder = Path.Combine(AppContext.BaseDirectory, "Presets");
                presets = LoadPresets(presetsFolder, log);

                if (!presets.TryGetValue("default", out defaultPreset))
                    throw new InvalidOperationException(
                        $"Automatic mode needs a fallback preset at '{Path.Combine(presetsFolder, "default.json")}' " +
                        "for files/folders that don't match any other preset keyword, but it wasn't found.");

                log($"Automatic mode: loaded {presets.Count} preset(s) from {presetsFolder}");
            }
            else
            {
                manualSettings = Settings.LoadFromFile(settingsPath);
                log($"Loaded preset '{manualSettings.PresetName}' from {settingsPath}");
            }

            log($"Input:  {inputFolder}");
            log($"Output: {outputFolder}");
            log($"Mode:   {(environmentMapOnly ? "Environment Map shapes only" : "All BSLightingShaderProperty shapes")}");

            var nifFiles = Directory.EnumerateFiles(inputFolder, "*.nif", SearchOption.AllDirectories);

            bool wasStopped = false;

            foreach (var nifPath in nifFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    wasStopped = true;
                    break;
                }

                var relativePath = Path.GetRelativePath(inputFolder, nifPath);
                var outputPath = Path.Combine(outputFolder, relativePath);

                string? matchedKeyword = null;
                var settingsForFile = automaticMode
                    ? ResolvePresetForFile(relativePath, presets!, defaultPreset!, log, out matchedKeyword)
                    : manualSettings!;

                var nif = new NifFile();
                var loadResult = nif.Load(nifPath);
                if (loadResult != 0 || !nif.Valid)
                {
                    log($"  [skip] Failed to load: {relativePath}");
                    result.SkippedFiles++;
                    continue;
                }

                int shapesPatchedInFile = 0;

                foreach (var shape in nif.GetShapes())
                {
                    if (shape.ShaderPropertyRef is null || shape.ShaderPropertyRef.IsEmpty())
                        continue;

                    var shader = nif.GetBlock<INiObject>(shape.ShaderPropertyRef);
                    if (shader is not BSLightingShaderProperty lsp)
                        continue;

                    // Only touch shapes actually set up for environment mapping - everything else
                    // (Default, Glow_Shader, Parallax, Skin_Tint, etc.) is left completely alone,
                    // unless the "Only patch Environment Map shaders" option is turned off.
                    if (environmentMapOnly && !IsEnvironmentMapShader(lsp))
                        continue;

                    ApplySettings(nif, lsp, settingsForFile, log);
                    shapesPatchedInFile++;
                }

                if (!dryRun)
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                if (shapesPatchedInFile > 0)
                {
                    var presetTag = automaticMode ? $" [preset: {matchedKeyword ?? "default"}]" : "";
                    log($"  [patch] {relativePath}{presetTag} ({shapesPatchedInFile} shape(s))");
                    result.PatchedFiles++;
                    result.PatchedShapes += shapesPatchedInFile;

                    if (!dryRun)
                    {
                        try
                        {
                            var saveResult = nif.Save(outputPath);
                            if (saveResult != 0)
                                log($"  [ERROR] Save failed (result={saveResult}) for {outputPath}");
                            else
                                log($"  [saved] {outputPath}");
                        }
                        catch (Exception ex)
                        {
                            log($"  [ERROR] Exception saving {outputPath}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    if (!dryRun)
                        File.Copy(nifPath, outputPath, overwrite: true);
                    result.CopiedFiles++;
                }
            }

            if (wasStopped)
            {
                log($"Stopped by user: patched {result.PatchedShapes} shape(s) across {result.PatchedFiles} file(s), " +
                    $"copied {result.CopiedFiles} unchanged file(s) before stopping.");
            }
            else
            {
                log(dryRun
                    ? $"Dry run complete: would patch {result.PatchedShapes} shape(s) across {result.PatchedFiles} file(s), and copy {result.CopiedFiles} unchanged file(s)."
                    : $"Done: patched {result.PatchedShapes} shape(s) across {result.PatchedFiles} file(s), copied {result.CopiedFiles} unchanged file(s).");
            }

            return result;
        }

        // Automatic mode: one JSON preset per file in Presets/, named after its keyword
        // (e.g. Presets/ironarmor.json -> keyword "ironarmor"). Presets/default.json is
        // mandatory and is used for anything that doesn't match another preset's keyword.
        static Dictionary<string, Settings> LoadPresets(string presetsFolder, Action<string> log)
        {
            if (!Directory.Exists(presetsFolder))
                throw new InvalidOperationException(
                    $"Automatic mode needs a Presets folder at '{presetsFolder}' containing one .json file per " +
                    "preset (plus a mandatory default.json), but that folder doesn't exist.");

            var presets = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.EnumerateFiles(presetsFolder, "*.json"))
            {
                var keyword = Path.GetFileNameWithoutExtension(file);
                try
                {
                    presets[keyword] = Settings.LoadFromFile(file);
                }
                catch (Exception ex)
                {
                    log($"  [warn] Couldn't load preset '{keyword}' from {file}: {ex.Message} - skipping this preset.");
                }
            }

            return presets;
        }

        // Matches a file's relative path (its folder names and its own file name, per the
        // "both folder names and file name" matching rule) against preset keywords as a
        // case-insensitive substring search. If more than one keyword matches, the longest
        // (most specific) keyword wins; a tie between equally-long keywords is logged as a
        // warning and resolved by picking one so the run doesn't stop over it. No match at
        // all falls back to the mandatory "default" preset.
        static Settings ResolvePresetForFile(string relativePath, Dictionary<string, Settings> presets,
            Settings defaultPreset, Action<string> log, out string? matchedKeyword)
        {
            var candidates = GetPathSegments(relativePath).ToList();

            string? bestKeyword = null;
            var tiedKeywords = new List<string>();

            foreach (var keyword in presets.Keys)
            {
                if (keyword.Equals("default", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!candidates.Any(c => c.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (bestKeyword is null || keyword.Length > bestKeyword.Length)
                {
                    bestKeyword = keyword;
                    tiedKeywords.Clear();
                    tiedKeywords.Add(keyword);
                }
                else if (keyword.Length == bestKeyword.Length)
                {
                    tiedKeywords.Add(keyword);
                }
            }

            if (bestKeyword is null)
            {
                matchedKeyword = null;
                return defaultPreset;
            }

            if (tiedKeywords.Count > 1)
                log($"  [warn] {relativePath} matched multiple equally-specific presets " +
                    $"({string.Join(", ", tiedKeywords)}) - using '{bestKeyword}'.");

            matchedKeyword = bestKeyword;
            return presets[bestKeyword];
        }

        static IEnumerable<string> GetPathSegments(string relativePath)
        {
            var dir = Path.GetDirectoryName(relativePath) ?? string.Empty;
            if (!string.IsNullOrEmpty(dir))
            {
                foreach (var part in dir.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                             StringSplitOptions.RemoveEmptyEntries))
                    yield return part;
            }

            yield return Path.GetFileNameWithoutExtension(relativePath);
        }

        // We tried guessing NiflySharp's exact property/enum name for nifxml's "Skyrim Shader Type"
        // field twice and both guesses were wrong for the 2.0.4 package actually in use. Reflection
        // then revealed the real shape: BSLightingShaderProperty exposes several per-game-version
        // backing properties (ShaderType_SK_FO4, ShaderType_FO3_NV, ShaderType_FO76_SF) plus one
        // plain "ShaderType" property (type BSLightingShaderType) that's the version-normalized one
        // meant for direct use - that's the one we want here, not the raw per-version fields.
        static readonly PropertyInfo ShaderTypeProperty = FindShaderTypeProperty();

        static PropertyInfo FindShaderTypeProperty()
        {
            var properties = typeof(BSLightingShaderProperty)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType.IsEnum &&
                            p.Name.Replace("_", "").Contains("ShaderType", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var exact = properties.FirstOrDefault(p => p.Name.Equals("ShaderType", StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact;

            throw new InvalidOperationException(
                $"Couldn't find a plain 'ShaderType' property on BSLightingShaderProperty via reflection " +
                $"(found {properties.Count} enum-typed candidate(s) instead: " +
                $"{string.Join(", ", properties.Select(c => $"{c.PropertyType.Name}.{c.Name}"))}). " +
                "Open BSLightingShaderProperty in your IDE and pick the right one manually - likely whichever " +
                "matches the game version these nif files target.");
        }

        static bool IsEnvironmentMapShader(BSLightingShaderProperty lsp)
        {
            var value = ShaderTypeProperty.GetValue(lsp)?.ToString() ?? string.Empty;
            return string.Equals(value.Replace("_", ""), "EnvironmentMap", StringComparison.OrdinalIgnoreCase);
        }

        // NiflySharp (as of the version in use here) does not generate public setters for
        // Glossiness/SpecularStrength/LightingEffect1/LightingEffect2 on BSLightingShaderProperty -
        // confirmed via reflection: the private backing fields (_glossiness, _specularStrength,
        // _lightingEffect1, _lightingEffect2) exist and are read by INiShader's get-only interface
        // properties, but nothing publicly writes them. Until the library adds real setters (worth
        // filing upstream at github.com/ousnius/NiflySharp/issues), we write those four fields
        // directly via reflection. EnvironmentMapScale IS a normal public settable property, so
        // that one goes through the regular API.
        static readonly Dictionary<string, FieldInfo> ShaderFloatFields = new()
        {
            ["Glossiness"] = GetField("_glossiness"),
            ["SpecularStrength"] = GetField("_specularStrength"),
            ["LightingEffect1"] = GetField("_lightingEffect1"),
            ["LightingEffect2"] = GetField("_lightingEffect2"),
        };

        static FieldInfo GetField(string name) =>
            typeof(BSLightingShaderProperty).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"BSLightingShaderProperty no longer has a private field named '{name}' - " +
                "the NiflySharp version in use may have changed; re-run the reflection dump.");

        static void SetShaderFloat(BSLightingShaderProperty lsp, string name, float value) =>
            ShaderFloatFields[name].SetValue(lsp, value);

        // EmissiveMultiple and EmissiveColor haven't been confirmed against the actual NiflySharp
        // 2.0.4 package in use (no local build/compiler here to check them, and this project's
        // history of guessing NiflySharp member names wrong - twice, for Skyrim Shader Type - makes
        // another blind guess a bad bet). Both are resolved via reflection at runtime instead:
        // prefer a public settable property matching the name, fall back to a private backing
        // field, and for the color specifically try common constructor shapes and common member
        // names (R/G/B, X/Y/Z, Red/Green/Blue). If none of that matches what's actually there,
        // this throws with the real type/member names found, so a fix is a one-line change instead
        // of another round of guessing.
        static readonly (PropertyInfo? Property, FieldInfo? Field) EmissiveMultipleAccessor =
            FindFloatAccessor("EmissiveMultiple", "_emissiveMultiple", "_emissiveMult");

        static (PropertyInfo? Property, FieldInfo? Field) FindFloatAccessor(string propertyName, params string[] backingFieldNames)
        {
            var property = typeof(BSLightingShaderProperty)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.PropertyType == typeof(float) && p.CanWrite &&
                                      p.Name.Replace("_", "").Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            if (property is not null)
                return (property, null);

            foreach (var fieldName in backingFieldNames)
            {
                var field = typeof(BSLightingShaderProperty).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field is not null)
                    return (null, field);
            }

            throw new InvalidOperationException(
                $"Couldn't find a public settable '{propertyName}' float property, or any of its guessed private " +
                $"backing fields ({string.Join(", ", backingFieldNames)}), on BSLightingShaderProperty. Check the " +
                "type in your IDE and update this lookup.");
        }

        static void SetEmissiveMultiple(BSLightingShaderProperty lsp, float value)
        {
            if (EmissiveMultipleAccessor.Property is not null)
                EmissiveMultipleAccessor.Property.SetValue(lsp, value);
            else
                EmissiveMultipleAccessor.Field!.SetValue(lsp, value);
        }

        static readonly PropertyInfo? EmissiveColorProperty = typeof(BSLightingShaderProperty)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.CanWrite && p.Name.Replace("_", "").Equals("EmissiveColor", StringComparison.OrdinalIgnoreCase));

        static readonly FieldInfo? EmissiveColorField = EmissiveColorProperty is null
            ? typeof(BSLightingShaderProperty).GetField("_emissiveColor", BindingFlags.NonPublic | BindingFlags.Instance)
            : null;

        static Type EmissiveColorType =>
            EmissiveColorProperty?.PropertyType ?? EmissiveColorField?.FieldType
            ?? throw new InvalidOperationException(
                "Couldn't find a public settable 'EmissiveColor' property, or a private '_emissiveColor' backing " +
                "field, on BSLightingShaderProperty. Check the type in your IDE and update this lookup.");

        static void SetEmissiveColor(BSLightingShaderProperty lsp, string hexColor)
        {
            Color color;
            try
            {
                color = ColorTranslator.FromHtml(hexColor);
            }
            catch
            {
                color = Color.Black;
            }

            var colorValue = BuildColorValue(EmissiveColorType, color);

            if (EmissiveColorProperty is not null)
                EmissiveColorProperty.SetValue(lsp, colorValue);
            else
                EmissiveColorField!.SetValue(lsp, colorValue);
        }

        static object BuildColorValue(Type colorType, Color color)
        {
            float r = color.R / 255f, g = color.G / 255f, b = color.B / 255f;

            // Most C# NIF color structs (Color3/Color4-style) take (float, float, float[, float]).
            var ctor3 = colorType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float) });
            if (ctor3 is not null)
                return ctor3.Invoke(new object[] { r, g, b });

            var ctor4 = colorType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
            if (ctor4 is not null)
                return ctor4.Invoke(new object[] { r, g, b, 1f });

            // Fall back to a parameterless constructor plus individually-set components.
            var instance = Activator.CreateInstance(colorType)
                ?? throw new InvalidOperationException($"Couldn't construct a default '{colorType.FullName}' for EmissiveColor.");

            SetColorComponent(instance, colorType, r, "R", "X", "Red");
            SetColorComponent(instance, colorType, g, "G", "Y", "Green");
            SetColorComponent(instance, colorType, b, "B", "Z", "Blue");

            return instance;
        }

        static void SetColorComponent(object instance, Type colorType, float value, params string[] candidateNames)
        {
            foreach (var name in candidateNames)
            {
                var property = colorType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property is not null && property.CanWrite)
                {
                    property.SetValue(instance, value);
                    return;
                }

                var field = colorType.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field is not null)
                {
                    field.SetValue(instance, value);
                    return;
                }
            }

            throw new InvalidOperationException(
                $"Couldn't find any of ({string.Join(", ", candidateNames)}) as a public settable member on " +
                $"'{colorType.FullName}' to set an EmissiveColor component. Check the type in your IDE and update this lookup.");
        }

        static void ApplySettings(NifFile nif, BSLightingShaderProperty lsp, Settings settings, Action<string> log)
        {
            // --- Shader floats ---
            SetShaderFloat(lsp, "Glossiness", settings.Shader.Glossiness);
            SetShaderFloat(lsp, "SpecularStrength", settings.Shader.SpecularStrength);
            SetShaderFloat(lsp, "LightingEffect1", settings.Shader.LightingEffect1);
            SetShaderFloat(lsp, "LightingEffect2", settings.Shader.LightingEffect2);
            lsp.EnvironmentMapScale = settings.Shader.EnvironmentScale; // real public property, no workaround needed
            SetEmissiveMultiple(lsp, settings.Shader.EmissiveMultiple);
            SetEmissiveColor(lsp, settings.Shader.EmissiveColor);

            // --- Shader flags (replace wholesale from the preset) ---
            lsp.ShaderFlags_SSPF1 = ParseFlags1(settings.Flags1, log);
            lsp.ShaderFlags_SSPF2 = ParseFlags2(settings.Flags2, log);

            // --- Textures: only overwrite slots that are non-empty in the preset ---
            if (lsp.TextureSetRef is not null && !lsp.TextureSetRef.IsEmpty())
            {
                var textureSet = nif.GetBlock<BSShaderTextureSet>(lsp.TextureSetRef);
                if (textureSet is not null)
                {
                    SetSlotIfNotEmpty(textureSet, 0, settings.Textures.Diffuse);
                    SetSlotIfNotEmpty(textureSet, 1, settings.Textures.Normal);
                    SetSlotIfNotEmpty(textureSet, 2, settings.Textures.Opacity);
                    SetSlotIfNotEmpty(textureSet, 3, settings.Textures.Height);
                    SetSlotIfNotEmpty(textureSet, 4, settings.Textures.Metal); // cubemap slot
                    SetSlotIfNotEmpty(textureSet, 5, settings.Textures.AO);
                    SetSlotIfNotEmpty(textureSet, 6, settings.Textures.Emissive);
                    SetSlotIfNotEmpty(textureSet, 7, settings.Textures.Roughness);
                    // Slot 8 (ID) intentionally left untouched - never used.
                    // Transmissive: only present on newer (FO4+) texture sets - add if/when needed.
                }
            }
        }

        static void SetSlotIfNotEmpty(BSShaderTextureSet textureSet, int slot, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            if (textureSet.Textures is not null && slot < textureSet.Textures.Count)
                textureSet.Textures[slot] = new NiString4(value, false);
        }

        static SkyrimShaderPropertyFlags1 ParseFlags1(List<string> names, Action<string> log)
        {
            SkyrimShaderPropertyFlags1 flags = 0;
            foreach (var name in names)
            {
                if (Enum.TryParse<SkyrimShaderPropertyFlags1>(name, ignoreCase: true, out var parsed))
                    flags |= parsed;
                else
                    log($"  [warn] Unknown Flags1 entry: {name}");
            }
            return flags;
        }

        static SkyrimShaderPropertyFlags2 ParseFlags2(List<string> names, Action<string> log)
        {
            SkyrimShaderPropertyFlags2 flags = 0;
            foreach (var name in names)
            {
                if (Enum.TryParse<SkyrimShaderPropertyFlags2>(name, ignoreCase: true, out var parsed))
                    flags |= parsed;
                else
                    log($"  [warn] Unknown Flags2 entry: {name}");
            }
            return flags;
        }
    }
}