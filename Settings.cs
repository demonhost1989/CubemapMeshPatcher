using System.Text.Json;
using System.Text.Json.Serialization;


namespace MeshPatcherProject
{
    internal class Settings
    {
        [JsonPropertyName("PresetName")]
        public string PresetName { get; set; } = string.Empty;

        [JsonPropertyName("Textures")]
        public TextureSettings Textures { get; set; } = new();

        [JsonPropertyName("Shader")]
        public ShaderSettings Shader { get; set; } = new();

        [JsonPropertyName("Flags1")]
        public List<string> Flags1 { get; set; } = new();

        [JsonPropertyName("Flags2")]
        public List<string> Flags2 { get; set; } = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
        };

        public static Settings LoadFromFile(string path)
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
            return settings ?? throw new InvalidDataException($"Failed to parse settings file: {path}");
        }
    }

    internal class TextureSettings
    {
        [JsonPropertyName("Diffuse")]
        public string Diffuse { get; set; } = string.Empty;

        [JsonPropertyName("Normal")]
        public string Normal { get; set; } = string.Empty;

        [JsonPropertyName("Opacity")]
        public string Opacity { get; set; } = string.Empty;

        [JsonPropertyName("Roughness")]
        public string Roughness { get; set; } = string.Empty;

        [JsonPropertyName("Metal")]
        public string Metal { get; set; } = string.Empty;

        [JsonPropertyName("AO")]
        public string AO { get; set; } = string.Empty;

        [JsonPropertyName("Height")]
        public string Height { get; set; } = string.Empty;

        [JsonPropertyName("Emissive")]
        public string Emissive { get; set; } = string.Empty;

        [JsonPropertyName("Transmissive")]
        public string Transmissive { get; set; } = string.Empty;

        [JsonPropertyName("ID")]
        public string ID { get; set; } = string.Empty;
    }

    internal class ShaderSettings
    {
        [JsonPropertyName("Glossiness")]
        public float Glossiness { get; set; }

        [JsonPropertyName("SpecularStrength")]
        public float SpecularStrength { get; set; }

        [JsonPropertyName("LightingEffect1")]
        public float LightingEffect1 { get; set; }

        [JsonPropertyName("LightingEffect2")]
        public float LightingEffect2 { get; set; }

        [JsonPropertyName("EnvironmentScale")]
        public float EnvironmentScale { get; set; }
    }
}