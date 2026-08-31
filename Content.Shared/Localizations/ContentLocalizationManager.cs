using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;
using static Robust.Shared.CVars;

namespace Content.Shared.Localizations
{
    public sealed partial class ContentLocalizationManager
    {
        [Dependency] private ILocalizationManager _loc = default!;
        [Dependency] private IConfigurationManager _cfg = default!;

        /// <summary>Always-loaded fallback culture (English source strings).</summary>
        public const string FallbackCultureName = "en-US";

        /// <summary>
        /// Cultures offered in Options for the Functional Tutorial (launcher-filter languages).
        /// </summary>
        public static readonly string[] SupportedCultureNames =
        [
            "en-US",
            "de-DE",
            "es-ES",
            "fr-FR",
            "pt-BR",
            "ru-RU",
            "uk-UA",
        ];

        /// <summary>
        /// Custom format strings used for parsing and displaying minutes:seconds timespans.
        /// </summary>
        public static readonly string[] TimeSpanMinutesFormats =
        [
            @"m\:ss",
            @"mm\:ss",
            @"%m",
            @"mm"
        ];

        private bool _clientCultureHooked;

        /// <param name="preferClientCulture">
        /// When true (game client), load <see cref="CVars.LocCultureName"/> as the active culture
        /// with English fallback. Server keeps English only.
        /// </param>
        public void Initialize(bool preferClientCulture = false)
        {
            var en = new CultureInfo(FallbackCultureName);
            _loc.LoadCulture(en);
            RegisterContentFunctions(en);

            // English-only Fluent helpers for pluralization fallbacks.
            _loc.AddFunction(en, "MAKEPLURAL", FormatMakePlural);
            _loc.AddFunction(en, "MANY", FormatMany);

            if (!preferClientCulture)
            {
                _loc.DefaultCulture = en;
                return;
            }

            ApplyClientCulture(_cfg.GetCVar(LocCultureName), force: true);
            if (!_clientCultureHooked)
            {
                _cfg.OnValueChanged(LocCultureName, OnClientCultureCVarChanged);
                _clientCultureHooked = true;
            }
        }

        private void OnClientCultureCVarChanged(string cultureName)
        {
            ApplyClientCulture(cultureName, force: false);
        }

        /// <summary>
        /// Loads and activates a client culture when present under /Locale; falls back to en-US.
        /// </summary>
        public void ApplyClientCulture(string cultureName, bool force)
        {
            if (string.IsNullOrWhiteSpace(cultureName))
                cultureName = FallbackCultureName;

            CultureInfo preferred;
            try
            {
                preferred = CultureInfo.GetCultureInfo(cultureName, predefinedOnly: false);
            }
            catch (CultureNotFoundException)
            {
                preferred = new CultureInfo(FallbackCultureName);
            }

            var en = new CultureInfo(FallbackCultureName);

            if (!string.Equals(preferred.Name, FallbackCultureName, StringComparison.OrdinalIgnoreCase))
            {
                if (!_loc.HasCulture(preferred))
                {
                    try
                    {
                        _loc.LoadCulture(preferred);
                        RegisterContentFunctions(preferred);
                    }
                    catch
                    {
                        // Missing Locale folder — stay on English.
                        preferred = en;
                    }
                }
            }

            if (!force && _loc.DefaultCulture != null &&
                string.Equals(_loc.DefaultCulture.Name, preferred.Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _loc.SetCulture(preferred);
            if (!string.Equals(preferred.Name, FallbackCultureName, StringComparison.OrdinalIgnoreCase))
                _loc.SetFallbackCluture(en);
        }

        private void RegisterContentFunctions(CultureInfo culture)
        {
            _loc.AddFunction(culture, "PRESSURE", FormatPressure);
            _loc.AddFunction(culture, "POWERWATTS", FormatPowerWatts);
            _loc.AddFunction(culture, "POWERJOULES", FormatPowerJoules);
            // NOTE: ENERGYWATTHOURS() still takes a value in joules, but formats as watt-hours.
            _loc.AddFunction(culture, "ENERGYWATTHOURS", FormatEnergyWattHours);
            _loc.AddFunction(culture, "UNITS", FormatUnits);
            _loc.AddFunction(culture, "TOSTRING", args => FormatToString(culture, args));
            _loc.AddFunction(culture, "LOC", FormatLoc);
            _loc.AddFunction(culture, "NATURALFIXED", FormatNaturalFixed);
            _loc.AddFunction(culture, "NATURALPERCENT", FormatNaturalPercent);
            _loc.AddFunction(culture, "PLAYTIME", FormatPlaytime);
        }

        private ILocValue FormatMany(LocArgs args)
        {
            var count = ((LocValueNumber) args.Args[1]).Value;

            if (Math.Abs(count - 1) < 0.0001f)
            {
                return (LocValueString) args.Args[0];
            }

            return (LocValueString) FormatMakePlural(args);
        }

        private ILocValue FormatNaturalPercent(LocArgs args)
        {
            var number = ((LocValueNumber) args.Args[0]).Value * 100;
            var maxDecimals = (int) Math.Floor(((LocValueNumber) args.Args[1]).Value);
            var culture = _loc.DefaultCulture ?? CultureInfo.GetCultureInfo(FallbackCultureName);
            var formatter = (NumberFormatInfo) NumberFormatInfo.GetInstance(culture).Clone();
            formatter.NumberDecimalDigits = maxDecimals;
            return new LocValueString(string.Format(formatter, "{0:N}", number).TrimEnd('0')
                .TrimEnd(char.Parse(formatter.NumberDecimalSeparator)) + "%");
        }

        private ILocValue FormatNaturalFixed(LocArgs args)
        {
            var number = ((LocValueNumber) args.Args[0]).Value;
            var maxDecimals = (int) Math.Floor(((LocValueNumber) args.Args[1]).Value);
            var culture = _loc.DefaultCulture ?? CultureInfo.GetCultureInfo(FallbackCultureName);
            var formatter = (NumberFormatInfo) NumberFormatInfo.GetInstance(culture).Clone();
            formatter.NumberDecimalDigits = maxDecimals;
            return new LocValueString(string.Format(formatter, "{0:N}", number).TrimEnd('0')
                .TrimEnd(char.Parse(formatter.NumberDecimalSeparator)));
        }

        private static readonly Regex PluralEsRule = new("^.*(s|sh|ch|x|z)$");

        private ILocValue FormatMakePlural(LocArgs args)
        {
            var text = ((LocValueString) args.Args[0]).Value;
            var split = text.Split(" ", 1);
            var firstWord = split[0];
            if (PluralEsRule.IsMatch(firstWord))
            {
                if (split.Length == 1)
                    return new LocValueString($"{firstWord}es");
                else
                    return new LocValueString($"{firstWord}es {split[1]}");
            }

            if (split.Length == 1)
                return new LocValueString($"{firstWord}s");
            else
                return new LocValueString($"{firstWord}s {split[1]}");
        }

        // TODO: allow fluent to take in lists of strings so this can be a format function like it should be.
        /// <summary>
        /// Formats a list as per english grammar rules.
        /// </summary>
        public static string FormatList(List<string> list)
        {
            return list.Count switch
            {
                <= 0 => string.Empty,
                1 => list[0],
                2 => $"{list[0]} and {list[1]}",
                _ => $"{string.Join(", ", list.GetRange(0, list.Count - 1))}, and {list[^1]}"
            };
        }

        /// <summary>
        /// Formats a list as per english grammar rules, but uses or instead of and.
        /// </summary>
        public static string FormatListToOr(List<string> list)
        {
            return list.Count switch
            {
                <= 0 => string.Empty,
                1 => list[0],
                2 => $"{list[0]} or {list[1]}",
                _ => $"{string.Join(", ", list.GetRange(0, list.Count - 1))}, or {list[^1]}"
            };
        }

        /// <summary>
        /// Formats a direction struct as a human-readable string.
        /// </summary>
        public static string FormatDirection(Direction dir)
        {
            return Loc.GetString($"zzzz-fmt-direction-{dir.ToString()}");
        }

        /// <summary>
        /// Formats playtime as hours and minutes.
        /// </summary>
        public static string FormatPlaytime(TimeSpan time)
        {
            time = TimeSpan.FromMinutes(Math.Ceiling(time.TotalMinutes));
            var hours = (int) time.TotalHours;
            var minutes = time.Minutes;
            return Loc.GetString($"zzzz-fmt-playtime", ("hours", hours), ("minutes", minutes));
        }

        private static ILocValue FormatLoc(LocArgs args)
        {
            var id = ((LocValueString) args.Args[0]).Value;

            return new LocValueString(Loc.GetString(id, args.Options.Select(x => (x.Key, x.Value.Value!)).ToArray()));
        }

        private static ILocValue FormatToString(CultureInfo culture, LocArgs args)
        {
            var arg = args.Args[0];
            var fmt = ((LocValueString) args.Args[1]).Value;

            var obj = arg.Value;
            if (obj is IFormattable formattable)
                return new LocValueString(formattable.ToString(fmt, culture));

            return new LocValueString(obj?.ToString() ?? "");
        }

        private static ILocValue FormatUnitsGeneric(
            LocArgs args,
            string mode,
            Func<double, double>? transformValue = null)
        {
            const int maxPlaces = 5; // Matches amount in _lib.ftl
            var pressure = ((LocValueNumber) args.Args[0]).Value;

            if (transformValue != null)
                pressure = transformValue(pressure);

            var places = 0;
            while (pressure > 1000 && places < maxPlaces)
            {
                pressure /= 1000;
                places += 1;
            }

            return new LocValueString(Loc.GetString(mode, ("divided", pressure), ("places", places)));
        }

        private static ILocValue FormatPressure(LocArgs args)
        {
            return FormatUnitsGeneric(args, "zzzz-fmt-pressure");
        }

        private static ILocValue FormatPowerWatts(LocArgs args)
        {
            return FormatUnitsGeneric(args, "zzzz-fmt-power-watts");
        }

        private static ILocValue FormatPowerJoules(LocArgs args)
        {
            return FormatUnitsGeneric(args, "zzzz-fmt-power-joules");
        }

        private static ILocValue FormatEnergyWattHours(LocArgs args)
        {
            const double joulesToWattHours = 1.0 / 3600;

            return FormatUnitsGeneric(args, "zzzz-fmt-energy-watt-hours", joules => joules * joulesToWattHours);
        }

        private static ILocValue FormatUnits(LocArgs args)
        {
            if (!Units.Types.TryGetValue(((LocValueString) args.Args[0]).Value, out var ut))
                throw new ArgumentException($"Unknown unit type {((LocValueString) args.Args[0]).Value}");

            var fmtstr = ((LocValueString) args.Args[1]).Value;

            double max = Double.NegativeInfinity;
            var iargs = new double[args.Args.Count - 1];
            for (var i = 2; i < args.Args.Count; i++)
            {
                var n = ((LocValueNumber) args.Args[i]).Value;
                if (n > max)
                    max = n;

                iargs[i - 2] = n;
            }

            if (!ut.TryGetUnit(max, out var mu))
                throw new ArgumentException("Unit out of range for type");

            var fargs = new object[iargs.Length];

            for (var i = 0; i < iargs.Length; i++)
                fargs[i] = iargs[i] * mu.Factor;

            fargs[^1] = Loc.GetString($"units-{mu.Unit.ToLower()}");

            // Before anyone complains about "{"+"${...}", at least it's better than MS's approach...
            // https://docs.microsoft.com/en-us/dotnet/standard/base-types/composite-formatting#escaping-braces
            //
            // Note that the closing brace isn't replaced so that format specifiers can be applied.
            var res = String.Format(
                fmtstr.Replace("{UNIT", "{" + $"{fargs.Length - 1}"),
                fargs
            );

            return new LocValueString(res);
        }

        private static ILocValue FormatPlaytime(LocArgs args)
        {
            var time = TimeSpan.Zero;
            if (args.Args is { Count: > 0 } && args.Args[0].Value is TimeSpan timeArg)
            {
                time = timeArg;
            }

            return new LocValueString(FormatPlaytime(time));
        }
    }
}
