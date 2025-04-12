using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RandomPaintingSwap;

internal static class PluginConfig
{
    internal static ConfigEntry<bool> enableDebugLog;
    internal static ConfigEntry<float> customPaintingChance;

    internal static class Grunge
    {
        internal static ConfigEntry<bool>  enableGrunge;
        internal static ConfigEntry<Color> _BaseColor;
        internal static ConfigEntry<Color> _MainColor;
        internal static ConfigEntry<Color> _CracksColor;
        internal static ConfigEntry<Color> _OutlineColor;
        internal static ConfigEntry<float> _CracksPower;
    }



    internal static void Init(ConfigFile InConfig)
    {
        enableDebugLog = InConfig.Bind
        (
            "General",
            "DebugLog",
            false,
            "Print extra logs for debugging"
        );

        customPaintingChance = InConfig.Bind
        (
            "General",
            "CustomPaintingChance",
            1.0f,
            "The chance of a painting being replaced by a custom painting (1 = 100%, 0.5 = 50%)"
        );

        Grunge.enableGrunge = InConfig.Bind
        (
            "Grunge",
            "EnableGrunge",
            true,
            "Whether the grunge effect is enabled"
        );

        Grunge._BaseColor = InConfig.Bind
        (
            "Grunge",
            "_GrungeBaseColor",
            new Color(0, 0, 0, 1),
            "The base color of the grunge"
        );

        Grunge._MainColor = InConfig.Bind
        (
            "Grunge",
            "_GrungeMainColor",
            new Color(0, 0, 0, 1),
            "The color of the main overlay of grunge"
        );

        Grunge._CracksColor = InConfig.Bind
        (
            "Grunge",
            "_GrungeCracksColor",
            new Color(0x40, 0x40, 0x40, 1),
            "The color of the cracks in the grunge"
        );

        Grunge._OutlineColor = InConfig.Bind
        (
            "Grunge",
            "_GrungeOutlineColor",
            new Color(0, 0, 0, 1),
            "The color of the grunge outlining the painting"
        );

        Grunge._CracksPower = InConfig.Bind
        (
            "Grunge",
            "_GrungeCracksPow",
            1.0f,
            "The inverse of intensity of the cracks. 1.0 will have plenty of cracks, higher numbers will have less cracks (Values below 1.0 will start to look bad)"
        );
    }
}
