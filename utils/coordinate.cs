using System;
using System.Text.RegularExpressions;

namespace pewbot.utils;

public static class CoordinateConverter
{
    public static (double? lat, double? lon) ParseDmsToDecimal(string dms)
    {
        // Example: "40°42'46.8\"N 74°00'21.5\"W"
        var pattern = @"(\d+)°\s*(\d+)'?\s*([\d\.]+)""?\s*([NSEW])";
        var matches = Regex.Matches(dms, pattern);
        if (matches.Count != 2) return (null, null);

        double ConvertMatch(Match m)
        {
            double degrees = double.Parse(m.Groups[1].Value);
            double minutes = double.Parse(m.Groups[2].Value);
            double seconds = double.Parse(m.Groups[3].Value);
            double result = degrees + minutes / 60 + seconds / 3600;
            string dir = m.Groups[4].Value;
            if (dir == "S" || dir == "W") result = -result;
            return result;
        }

        double lat = ConvertMatch(matches[0]);
        double lon = ConvertMatch(matches[1]);
        return (lat, lon);
    }

    public static string DecimalToDms(double decimalDegrees, bool isLat)
    {
        var degrees = (int)Math.Abs(decimalDegrees);
        var minutesInt = (int)((Math.Abs(decimalDegrees) - degrees) * 60);
        var seconds = ((Math.Abs(decimalDegrees) - degrees - minutesInt / 60.0) * 3600);
        var direction = isLat ? (decimalDegrees >= 0 ? "N" : "S") : (decimalDegrees >= 0 ? "E" : "W");
        return $"{degrees}°{minutesInt:D2}'{seconds:F1}\"{direction}";
    }
}