using System.Diagnostics.CodeAnalysis;

// LaunchPad [StationeersMod] uses the same reverse-DNS style GUID strings as other Stationeers mods
// the analyzer expects a System.Guid parseable format.
[assembly: SuppressMessage("Usage", "CA2243:Attribute string literals should parse correctly")]
