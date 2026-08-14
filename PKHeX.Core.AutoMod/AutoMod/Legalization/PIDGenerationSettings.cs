using System;

namespace PKHeX.Core.AutoMod
{
    /// <summary>
    /// Settings for configurable legal PID generation.
    /// Controls how PIDs are generated when creating or editing Pokémon.
    /// </summary>
    public sealed class PIDGenerationSettings
    {
        /// <summary>
        /// If true, PID generation will respect the shiny type setting.
        /// </summary>
        public bool RespectShinyType { get; set; } = true;

        /// <summary>
        /// The shiny type to enforce during PID generation.
        /// </summary>
        public Shiny ShinyType { get; set; } = Core.Shiny.Random;

        /// <summary>
        /// If true, PID generation will respect the requested nature (Gen 3-5 only).
        /// </summary>
        public bool RespectNature { get; set; } = true;

        /// <summary>
        /// The nature to enforce during PID generation. Use <see cref="Nature.Random"/> for any nature.
        /// </summary>
        public Nature Nature { get; set; } = Nature.Random;

        /// <summary>
        /// If true, PID generation will respect the requested gender (Gen 3-5 only).
        /// </summary>
        public bool RespectGender { get; set; } = true;

        /// <summary>
        /// The gender to enforce during PID generation. Use null for any gender.
        /// </summary>
        public byte? Gender { get; set; }

        /// <summary>
        /// If true, PID generation will respect the Hidden Power type (Gen 3-5 only).
        /// </summary>
        public bool RespectHiddenPower { get; set; } = true;

        /// <summary>
        /// The Hidden Power type to enforce during PID generation (-1 for any).
        /// </summary>
        public int HiddenPowerType { get; set; } = -1;

        /// <summary>
        /// If true, the Encryption Constant will be set to match the PID for Gen 3-5.
        /// </summary>
        public bool SyncEncryptionConstant { get; set; } = true;

        /// <summary>
        /// Maximum number of iterations to attempt when searching for a valid PID.
        /// </summary>
        public int MaxIterations { get; set; } = 5_000_000;

        /// <summary>
        /// If true, force the PID to produce a square shiny (XOR = 0) when shiny is requested.
        /// </summary>
        public bool PreferSquareShiny { get; set; } = false;

        /// <summary>
        /// Creates default settings that respect all standard constraints.
        /// </summary>
        public static PIDGenerationSettings Default => new();

        /// <summary>
        /// Creates settings that generate a completely random PID with no constraints.
        /// </summary>
        public static PIDGenerationSettings Random => new()
        {
            RespectShinyType = false,
            RespectNature = false,
            RespectGender = false,
            RespectHiddenPower = false,
            ShinyType = Core.Shiny.Random,
            Nature = Nature.Random,
            Gender = null,
            HiddenPowerType = -1,
        };

        /// <summary>
        /// Creates settings for generating a shiny PID (star or square based on <paramref name="square"/>).
        /// </summary>
        /// <param name="square">If true, forces square shiny; otherwise allows any shiny.</param>
        public static PIDGenerationSettings ForShiny(bool square = false) => new()
        {
            ShinyType = square ? Core.Shiny.AlwaysSquare : Core.Shiny.Always,
            PreferSquareShiny = square,
        };

        /// <summary>
        /// Creates settings for generating a non-shiny PID.
        /// </summary>
        public static PIDGenerationSettings NonShiny => new()
        {
            ShinyType = Core.Shiny.Never,
        };

        /// <summary>
        /// Creates settings for generating a PID with a specific nature.
        /// </summary>
        /// <param name="nature">The nature to enforce.</param>
        public static PIDGenerationSettings WithNature(Nature nature) => new()
        {
            Nature = nature,
            RespectNature = true,
        };

        /// <summary>
        /// Creates settings for generating a PID with a specific gender.
        /// </summary>
        /// <param name="gender">0 = Male, 1 = Female, 2 = Genderless.</param>
        public static PIDGenerationSettings WithGender(byte gender) => new()
        {
            Gender = gender,
            RespectGender = true,
        };

        public override string ToString()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (RespectShinyType)
                parts.Add($"Shiny={ShinyType}");
            if (RespectNature && Nature != Nature.Random)
                parts.Add($"Nature={Nature}");
            if (RespectGender && Gender.HasValue)
                parts.Add($"Gender={Gender}");
            if (RespectHiddenPower && HiddenPowerType >= 0)
                parts.Add($"HPType={HiddenPowerType}");
            return parts.Count > 0 ? string.Join(", ", parts) : "Default";
        }
    }
}
