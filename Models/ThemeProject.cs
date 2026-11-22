using System.Collections.Generic;

namespace ThemeStudio3DS.Models;

public sealed record ThemeProject(string Name, string Author, string Accent, IReadOnlyList<ThemeResource> Resources);
