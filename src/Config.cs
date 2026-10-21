using System.Text.Json;

namespace CycleSwipes;

/// <summary>Settings from config.json next to the exe. The fields are described in config.json itself.</summary>
internal sealed class Config
{
    public double SwipeThresholdX { get; set; } = 0.12;
    public double FlickMinDistanceX { get; set; } = 0.05;
    public double FlickMinVelocity { get; set; } = 1.0;
    public double HorizontalRatio { get; set; } = 2.0;
    public bool InvertDirection { get; set; }
    public bool LogFrames { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Config Load(string path, Logger log)
    {
        try
        {
            if (!File.Exists(path))
            {
                log.Write($"config.json not found ({path}), using defaults");
                return new Config();
            }
            var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), Options) ?? new Config();
            log.Write($"config.json: swipeThresholdX={config.SwipeThresholdX}, flickMinDistanceX={config.FlickMinDistanceX}, " +
                      $"flickMinVelocity={config.FlickMinVelocity}, horizontalRatio={config.HorizontalRatio}, " +
                      $"invertDirection={config.InvertDirection}, logFrames={config.LogFrames}");
            return config;
        }
        catch (Exception e)
        {
            log.Write($"ERROR parsing config.json: {e.Message}; using defaults");
            return new Config();
        }
    }
}
