
// <auto-generated>
// @formatter:off

namespace WixToolset.Harvesters
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.RegularExpressions;
  
    public static class HarvesterFilterExtensions
    {
        public static void Do<T>(this IEnumerable<T> source, Action<T> action)
        {   // note: omitted arg/null checks
            foreach (var item in source) { action(item); }
        }
    }

    public class HarvesterFilter
    {
        private static int count;
        private readonly int nr;
        private enum Mode { Unknow, Incl, Excl }

        private readonly object _lock = new object();
        private string _fileName;
        private string _startPath;
        private string _fragment;

        private Mode _mode = Mode.Unknow;

        private bool _read;
        private bool _isLoaded;

        private List<(Regex regex, int prio, bool isNegation)> _regList;

        private HarvesterFilter()
        {
            this.nr=++count;
            this.deb($"#### HarvesterFilter {this.nr}");
        }

        public void Load(string fileNameArgs, string startPath)
        {
            var args = fileNameArgs.Split(';');
            var filterFile = Path.GetFullPath(args[0]);
            var fragment = args.ElementAtOrDefault(1);
            this.Load(filterFile, startPath, fragment);
        }

        public void Load(string fileName, string startPath, string fragment)
        {
            this.deb($"HarvesterFilter load: {fileName},  {startPath}:{fragment}");
            this._fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            this._startPath = startPath;
            this._fragment = fragment;
            if(!String.IsNullOrWhiteSpace(fragment))
            {
                this.ReloadKeyList();
            }

            this._isLoaded = true;
        }

        private static readonly Regex RegComment = new Regex(@"^#", RegexOptions.Compiled);
        private static readonly Regex RegExit = new Regex(@"^<", RegexOptions.Compiled);
        private static readonly Regex RegIgnore = new Regex(@"^[+-]", RegexOptions.Compiled);
        private static readonly Regex RegKey = new Regex(@"^[.\w\\:*!/]", RegexOptions.Compiled);
        private static readonly Regex RegBlockMarker = new Regex(@"^###", RegexOptions.Compiled); // Block-Marker (###)

        private HashSet<string> LoadKeyList()
        {
            this._mode = Mode.Unknow;
            var list = new HashSet<string>();

            var fileName = this._fileName;
            if (!File.Exists(fileName))
            {
                //deb($"File not found: {fileName}");
                return list;
            }

            var regInc = new Regex($@"^\+{this._fragment}", RegexOptions.Compiled);
            var regEx = new Regex($@"^-{this._fragment}", RegexOptions.Compiled);
            var regCommentInline = new Regex(@"\s+#.*$", RegexOptions.Compiled); // Entfernt alles nach # mit vorangestelltem Leerraum

            bool ignoreBlock = false; // Steuert, ob der aktuelle Block ignoriert werden soll

            foreach (var line in File.ReadLines(fileName))
            {
                // Block-Ignorierung: Start oder Ende
                if (RegBlockMarker.IsMatch(line))
                {
                    ignoreBlock = !ignoreBlock; // Toggle den Block-Ignorierungszustand
                    continue;
                }

                // Wenn wir in einem zu ignorierenden Block sind, überspringe die Zeile
                if (ignoreBlock)
                {
                    continue; // Alles innerhalb des Blocks wird ignoriert, einschließlich RegExit.
                }

                // Exit-Tag außerhalb ignorierten Blocks
                if (RegExit.IsMatch(line))
                {
                    break; // Beende die Schleife, wenn ein Exit-Tag gefunden wird.
                }

                // Einzelne Zeilen ignorieren, die mit # beginnen
                if (RegComment.IsMatch(line))
                {
                    continue; // Überspringe Kommentarzeilen.
                }

                if (!this._read)
                {
                    if (regInc.IsMatch(line))
                    {
                        this._read = true;
                        if (this._mode == Mode.Unknow)
                        {
                            this._mode = Mode.Incl;
                        }
                    }
                    else if (regEx.IsMatch(line))
                    {
                        this._read = true;
                        if (this._mode == Mode.Unknow)
                        {
                            this._mode = Mode.Excl;
                        }
                    }
                }
                else // Implizit: this._read ist true
                {
                    if (RegIgnore.IsMatch(line))
                    {
                        continue; // Überspringe Zeilen, die mit "+" oder "-" beginnen.
                    }

                    if (RegKey.IsMatch(line))
                    {
                        list.Add(regCommentInline.Replace(line, "").Trim()); // Füge gültige Schlüssel zur Liste hinzu.
                    }
                    else
                    {
                        this._read = false; // Deaktiviere den Lese-Modus, wenn die Zeile ungültig ist.
                    }
                }
            }

            return list;
        }

        private bool IsFiltered(string file)
        {
            lock (this._lock)
            {
                return !this.IsIncl(file);
            }
        }

        public bool IsIncl(string file)
        {
            if(this._regList==null) { return true; }

            if(!this._isLoaded)
            {
                this.deb(" ++++ WARNING HarvesterFilter not loaded ++++ ");
                return true;
            }

            //deb($"_startPath: {_startPath}: {this._fragment} # {file}");
            var input = GetRelativePath(this._startPath, file);

            //if (input.Contains("tx4ole")) System.Diagnostics.Debugger.Break();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                input = input.Replace('\\', '/');
            }

            if (!input.StartsWith("/"))
            {
                input = "/" + input;
            }

            var accept = this._mode switch
            {
                Mode.Incl => false,
                Mode.Excl or _ => true
            };
            var prio = 0;

            this._regList.Do(re =>
            {
                if (re.regex.IsMatch(input) && re.prio > prio)
                {
                    prio = re.prio;
                    accept = this._mode switch
                    {
                        Mode.Incl => !re.isNegation,
                        Mode.Excl or _ => re.isNegation
                    };
                }
            });

            return accept;

        }

        private void ReloadKeyList()
        {
            lock (this._lock)
            {
                this._regList = new List<(Regex regex, int prio, bool isNegation)>();
                var regexOptions = RegexOptions.Compiled | RegexOptions.IgnoreCase;
                this.LoadKeyList().Do(pattern =>
                {
                    var isNegation = pattern.StartsWith("!");
                    if (isNegation)
                    {
                        pattern = pattern.Substring(1);
                    }

                    var rxpattern = CreateGitignoreRegex(pattern);
                    this._regList.Add((new Regex(rxpattern, regexOptions), pattern.Length, isNegation));

                });
#if true
                this.deb($"-- filter:{this._fragment}:{this.nr} ({this._mode})");
                this._regList.GroupBy(r => r.isNegation).Do(g =>
                {
                    this.deb($"  {(g.Key ? "negative" : "positive")}");
                    g.OrderBy(r => r.prio).Do(r =>
                    {
                        this.deb($"    {r.regex} : {r.prio}");
                    });
                });
#endif
            }
        }

        private static readonly Regex RangePattern = new Regex(@"^((?:[^\[\\]|(?:\\.))*)\[((?:[^\]\\]|(?:\\.))*)\]", RegexOptions.Compiled);
        private static readonly Regex BackslashPattern = new Regex(@"\\(.)", RegexOptions.Compiled);
        private static readonly Regex SpecialCharsPattern = new Regex(@"[\-\[\]\{\}\(\)\+\.\\\^\$\|]", RegexOptions.Compiled);
        private static readonly Regex QuestionMarkPattern = new Regex(@"\?", RegexOptions.Compiled);
        private static readonly Regex DoubleAsteriskSlashPattern = new Regex(@"\/\*\*\/", RegexOptions.Compiled);
        private static readonly Regex LeadingDoubleAsteriskPattern = new Regex(@"^\*\*\/", RegexOptions.Compiled);
        private static readonly Regex TrailingDoubleAsteriskPattern = new Regex(@"\/\*\*$", RegexOptions.Compiled);
        private static readonly Regex DoubleAsteriskPattern = new Regex(@"\*\*", RegexOptions.Compiled);
        private static readonly Regex AsteriskPattern = new Regex(@"\/\*(\/|$)", RegexOptions.Compiled);
        private static readonly Regex WildcardPattern = new Regex(@"\*", RegexOptions.Compiled);
        private static readonly Regex SlashPattern = new Regex(@"\/", RegexOptions.Compiled);

        private static string CreateGitignoreRegex(string gitignorePattern)
        {
            // https://git-scm.com/docs/gitignore#_pattern_format
            var regexBuilder = new StringBuilder();
            bool isRooted = false, isDirectory = false;

            // Remove leading '/' if present
            if (gitignorePattern.StartsWith("/"))
            {
                isRooted = true;
                gitignorePattern = gitignorePattern.Substring(1);
            }

            // Remove trailing '/' if present
            if (gitignorePattern.EndsWith("/"))
            {
                isDirectory = true;
                gitignorePattern = gitignorePattern.Substring(0, gitignorePattern.Length - 1);
            }

            // Convert gitignore pattern to regex
            string ConvertPatternToRegex(string patternPart)
            {
                if (String.IsNullOrEmpty(patternPart))
                {
                    return patternPart;
                }

                // Unescape backslashes
                patternPart = BackslashPattern.Replace(patternPart, "$1");

                // Escape special regex characters
                patternPart = SpecialCharsPattern.Replace(patternPart, @"\$&");

                // Replace '?' with '[^/]'
                patternPart = QuestionMarkPattern.Replace(patternPart, "[^/]");

                // Handle '/**/'
                patternPart = DoubleAsteriskSlashPattern.Replace(patternPart, "(?:/|(?:/.+/))");

                // Handle '**/'
                patternPart = LeadingDoubleAsteriskPattern.Replace(patternPart, "(?:|(?:.+/))");

                // Handle '/**'
                patternPart = TrailingDoubleAsteriskPattern.Replace(patternPart, _ =>
                {
                    isDirectory = true;
                    return "(?:|(?:/.+))";
                });

                // Handle '**'
                patternPart = DoubleAsteriskPattern.Replace(patternPart, ".*");

                // Handle '/*' or '/*/'
                patternPart = AsteriskPattern.Replace(patternPart, "/[^/]+$1");

                // Handle '*' (wildcard)
                patternPart = WildcardPattern.Replace(patternPart, "[^/]*");

                // Escape '/'
                patternPart = SlashPattern.Replace(patternPart, @"\/");

                return patternPart;
            }

            // Process character ranges
            while (RangePattern.IsMatch(gitignorePattern))
            {
                var match = RangePattern.Match(gitignorePattern);
                if (match.Groups[1].Value.Contains('/'))
                {
                    isRooted = true;
                }

                regexBuilder.Append(ConvertPatternToRegex(match.Groups[1].Value));
                regexBuilder.Append('[').Append(match.Groups[2].Value).Append(']');
                gitignorePattern = gitignorePattern.Substring(match.Length);
            }

            // Process remaining pattern
            if (!String.IsNullOrWhiteSpace(gitignorePattern))
            {
                if (gitignorePattern.Contains('/'))
                {
                    isRooted = true;
                }

                regexBuilder.Append(ConvertPatternToRegex(gitignorePattern));
            }

            // Add prefix and suffix based on pattern type
            regexBuilder.Insert(0, isRooted ? @"^\/" : @"\/");
            regexBuilder.Append(isDirectory ? @"\/" : @"(?:$|\/)");

            return regexBuilder.ToString();
        }

        private static string GetRelativePath(string basePath, string targetPath)
        {
            if (String.IsNullOrEmpty(basePath))
            {
                throw new ArgumentNullException(nameof(basePath));
            }

            if (String.IsNullOrEmpty(targetPath))
            {
                throw new ArgumentNullException(nameof(targetPath));
            }

            var baseUri = new Uri(AppendDirectorySeparatorChar(basePath));
            var targetUri = new Uri(targetPath);

            var relativeUri = baseUri.MakeRelativeUri(targetUri);
            var relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            // Konvertiere URI-Pfade zu Windows-Pfaden
            return relativePath.Replace('/', Path.DirectorySeparatorChar);

            bool EndsInDirectorySeparator(string path)
            {
                if (String.IsNullOrEmpty(path))
                {
                    return false;
                }

                var lastChar = path[path.Length - 1];
                return lastChar == Path.DirectorySeparatorChar || lastChar == Path.AltDirectorySeparatorChar;
            }

            string AppendDirectorySeparatorChar(string path)
            {
                if (!EndsInDirectorySeparator(path))
                {
                    return path + Path.DirectorySeparatorChar;
                }
                return path;
            }
        }

        private void deb(string msg) { Console.WriteLine(msg); }

        private static readonly ConcurrentDictionary<Type, HarvesterFilter> _instances = new();
        
        public static HarvesterFilter Instance<T>()
        {
            return _instances.GetOrAdd(typeof(T), _ => new HarvesterFilter() );
        }
    }

}
