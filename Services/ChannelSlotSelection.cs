namespace SSTLogAnalyser.Services;

public static class ChannelSlotSelection
{
    public static readonly string[] Rules =
        ["Module default", "128 pins / slot", "64 pins / slot", "32 pins / slot", "8 channels / slot", "Slot x 100", "MIXI physical slots"];

    public static int GetSlot(int channel, string? module, string rule)
    {
        if (channel < 0) return -1;
        var stride = rule switch
        {
            "128 pins / slot" => 128,
            "64 pins / slot" => 64,
            "32 pins / slot" => 32,
            "8 channels / slot" => 8,
            "Slot x 100" => 100,
            "MIXI physical slots" => 16,
            _ => module switch
            {
                "AC" or "TMU" or "PE" or "PPMU" => 128,
                "DPS" or "PMU" => 8,
                "MIXI" or "AWG" or "DTZ" => 16,
                _ => 100
            }
        };
        var slot = channel / stride;
        return rule == "MIXI physical slots" ||
            (rule == "Module default" && module is "MIXI" or "AWG" or "DTZ")
            ? (slot > 7 ? slot + 1 : 8 - slot)
            : slot;
    }
}
