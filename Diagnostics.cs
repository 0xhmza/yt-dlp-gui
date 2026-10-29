using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace YtDlpGui
{
    /// <summary>
    /// What "yt-dlp -v --list-impersonate-targets" reports about the build in use: the
    /// release and variant, the Python it runs on, and whether it can impersonate a browser.
    /// Every field is optional - forks and old builds print less, or nothing at all.
    /// </summary>
    internal sealed class BuildInfo
    {
        public string Version;              // "2026.08.19"
        public string Channel;              // "stable", "nightly", "master"
        public string Variant;              // "win_x86_exe", "win_dir", "pip"...
        public string Python;               // "3.10.11"
        public string JsRuntimes;           // "deno-2.9.5"; null when yt-dlp found none
        public bool JsReported;             // false for builds that predate JavaScript runtimes
        public string RequestHandlers;      // "urllib, requests, websockets"
        public readonly List<string> ImpersonateTargets = new List<string>();
        public int UnavailableTargets;

        public bool CanImpersonate { get { return ImpersonateTargets.Count > 0; } }

        /// <summary>The zip ("_dir") releases, which yt-dlp cannot update in place.</summary>
        public bool IsUnpackaged
        {
            get { return Variant != null && Variant.EndsWith("_dir", StringComparison.Ordinal); }
        }

        private static readonly Regex RxVersion = new Regex(
            @"^\[debug\] yt-dlp version (?:(?<channel>[^@\s]+)@)?(?<version>\S+)", RegexOptions.Compiled);
        private static readonly Regex RxVariant = new Regex(@"\((?<variant>[^()]+)\)\s*$", RegexOptions.Compiled);
        private static readonly Regex RxPython = new Regex(@"^\[debug\] Python (?<v>\S+)", RegexOptions.Compiled);
        private static readonly Regex RxJs = new Regex(@"^\[debug\] JS runtimes: (?<v>.+)$", RegexOptions.Compiled);
        private static readonly Regex RxHandlers = new Regex(@"^\[debug\] Request Handlers: (?<v>.+)$", RegexOptions.Compiled);

        /// <summary>Parses the combined output; returns null when it carries no build details.</summary>
        public static BuildInfo Parse(string output)
        {
            if (string.IsNullOrEmpty(output)) return null;

            var info = new BuildInfo();
            bool any = false, inTable = false;

            foreach (var raw in output.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimEnd();
                Match m;

                if ((m = RxVersion.Match(line)).Success)
                {
                    any = true;
                    info.Version = m.Groups["version"].Value;
                    if (m.Groups["channel"].Success) info.Channel = m.Groups["channel"].Value;
                    var v = RxVariant.Match(line);
                    if (v.Success) info.Variant = v.Groups["variant"].Value.Trim();
                }
                else if ((m = RxPython.Match(line)).Success) { any = true; info.Python = m.Groups["v"].Value; }
                else if ((m = RxJs.Match(line)).Success)
                {
                    any = info.JsReported = true;
                    var js = m.Groups["v"].Value.Trim();
                    info.JsRuntimes = js.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : js;
                }
                else if ((m = RxHandlers.Match(line)).Success) { any = true; info.RequestHandlers = m.Groups["v"].Value.Trim(); }
                else if (line.StartsWith("---", StringComparison.Ordinal)) inTable = true;
                else if (inTable && line.Length > 0 && !line.StartsWith("[", StringComparison.Ordinal))
                {
                    // "Chrome    -    curl_cffi (unavailable)"  or  "Chrome  Windows-10  curl_cffi"
                    any = true;
                    if (line.IndexOf("(unavailable)", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        info.UnavailableTargets++;
                        continue;
                    }
                    var cols = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (cols.Length == 0) continue;
                    var target = cols.Length > 1 && cols[1] != "-" ? cols[0] + ":" + cols[1] : cols[0];
                    if (!info.ImpersonateTargets.Contains(target)) info.ImpersonateTargets.Add(target);
                }
            }
            return any ? info : null;
        }
    }

    /// <summary>
    /// Plain-language advice for the yt-dlp errors people actually run into, pointing at the
    /// control in this window that fixes each one. The patterns follow yt-dlp's own wording,
    /// so a rule that stops matching after an upstream change simply goes quiet.
    /// </summary>
    internal static class Hints
    {
        private sealed class Rule
        {
            public readonly string Key;
            public readonly Regex Pattern;
            public readonly string Text;

            public Rule(string key, string pattern, string text)
            {
                Key = key;
                Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                Text = text;
            }
        }

        // Most specific first: a bot check or a cookie-decryption failure also carries the
        // generic "use --cookies" advice, and the specific hint is the useful one.
        private static readonly Rule[] Rules =
        {
            new Rule("impersonate", @"Impersonate target ""[^""]*"" is not available",
                "This yt-dlp cannot impersonate browsers: it was built without curl_cffi, which the 32-bit " +
                "yt-dlp_x86.exe never includes. Set Network > Impersonate browser back to (default), or use the " +
                "64-bit yt-dlp.exe."),
            new Rule("bot", @"confirm you.{1,2}re not a bot",
                "YouTube wants proof that this is not a bot. Sign in to YouTube in Firefox and choose it under " +
                "Authentication > Or take cookies from (or use a cookies.txt file). A delay under Network > " +
                "Politeness delays also helps."),
            new Rule("dpapi", @"Failed to decrypt with DPAPI",
                "Chrome and Edge encrypt their cookies so that other programs cannot read them. Take cookies from " +
                "Firefox instead, or export a cookies.txt and set Authentication > Cookies file."),
            new Rule("cookiedb", @"Could not copy Chrome cookie database",
                "The browser is holding its cookie database open. Close it completely, background processes " +
                "included, and try again - or take cookies from Firefox or a cookies.txt file."),
            new Rule("login", @"Use --cookies-from-browser or --cookies|Use --cookies, --cookies-from-browser",
                "This needs a signed-in account. Set Authentication > Cookies file, or pick the browser you are " +
                "signed in with under Or take cookies from."),
            new Rule("ffmpeg", @"ffmpeg not found|ffprobe and ffmpeg not found|ffmpeg is not installed",
                "This step needs ffmpeg. Put ffmpeg.exe and ffprobe.exe next to this program, or point " +
                "Post-processing > ffmpeg folder at them, then press F2 to check."),
            new Rule("jsruntime", @"No supported JavaScript runtime could be found",
                "YouTube needs a JavaScript runtime to unlock its formats. Put deno.exe next to this program " +
                "(node.exe, qjs.exe and bun.exe work too) and press F2; Advanced > JavaScript runtimes fills itself in."),
            new Rule("challenge", @"(?:n challenge|Signature) solving failed",
                "yt-dlp could not solve YouTube's player challenge, so some formats are missing. Update yt-dlp " +
                "(Tools > Update yt-dlp) and check that F2 lists a JavaScript runtime. A yt-dlp installed with pip " +
                "may also need Advanced > Remote components set to ejs:github."),
            new Rule("format", @"Requested format is not available",
                "Nothing matches the requested format. Use List formats... to see what this video offers, or relax " +
                "Format > Max quality and the custom selector."),
            new Rule("drm", @"DRM protected",
                "This video is DRM-protected. yt-dlp does not download DRM-protected media."),
            new Rule("429", @"HTTP Error 429|Too Many Requests",
                "The site is rate-limiting this connection. Wait a while, add Network > Politeness delays, or use " +
                "cookies from a signed-in browser."),
            new Rule("403", @"HTTP Error 403",
                "The server refused the download. Sites change constantly and an outdated yt-dlp is the usual " +
                "cause: try Tools > Update yt-dlp first."),
            new Rule("unsupported", @"Unsupported URL:",
                "yt-dlp does not recognise this address. Check that it links to a video or playlist page; " +
                "Tools > List extractors shows the supported sites."),
            new Rule("unpackaged", @"Auto-update is not supported for unpackaged",
                "This yt-dlp is an unpackaged (zip) build, which cannot update itself. Download yt-dlp.exe from " +
                "Tools > Update yt-dlp > Open releases page and put it next to this program."),
            new Rule("pkgmgr", @"installed yt-dlp from a manual build or with a package manager",
                "This yt-dlp was installed by a package manager (pip, winget, Scoop...). Update it with that tool, " +
                "or put the official yt-dlp.exe next to this program instead."),
            new Rule("locked", @"WinError 32|being used by another process",
                "Another program - a media player or an antivirus scan - has the file open. Close it, then " +
                "requeue the item."),
            new Rule("diskfull", @"No space left on device|Errno 28|WinError 112",
                "The download drive is full. Free some space or choose another download folder."),
            new Rule("cert", @"CERTIFICATE_VERIFY_FAILED|certificate verify failed",
                "The secure connection could not be verified. Antivirus web shields, corporate proxies and outdated " +
                "yt-dlp builds cause this. Update yt-dlp first; Network > Skip HTTPS certificate checks is the last resort."),
        };

        /// <summary>
        /// The advice for <paramref name="line"/>, or null. <paramref name="key"/> names the
        /// rule, so the caller can give each piece of advice once per run.
        /// </summary>
        public static string For(string line, out string key)
        {
            key = null;
            if (string.IsNullOrEmpty(line)) return null;
            foreach (var r in Rules)
            {
                if (!r.Pattern.IsMatch(line)) continue;
                key = r.Key;
                return r.Text;
            }
            return null;
        }
    }

    /// <summary>Picks the path of the file being produced out of yt-dlp's status lines.</summary>
    internal static class OutputFiles
    {
        // Later lines supersede earlier ones: a merge replaces the per-format destinations,
        // a remux or audio extraction replaces the merge, and a move out of the temporary
        // folder replaces them all.
        private static readonly Regex[] Patterns =
        {
            new Regex(@"^\[download\] Destination: (?<p>.+)$", RegexOptions.Compiled),
            new Regex(@"^\[download\] (?<p>.+) has already been downloaded$", RegexOptions.Compiled),
            new Regex(@"^\[Merger\] Merging formats into ""(?<p>.+)""$", RegexOptions.Compiled),
            new Regex(@"^\[ExtractAudio\] Destination: (?<p>.+)$", RegexOptions.Compiled),
            new Regex(@"^\[(?:VideoRemuxer|VideoConvertor)\] .*?; Destination: (?<p>.+)$", RegexOptions.Compiled),
            new Regex(@"^\[MoveFiles\] Moving file "".+"" to ""(?<p>.+)""$", RegexOptions.Compiled),
        };

        /// <summary>The path a line announces, or null.</summary>
        public static string FromLine(string line)
        {
            if (string.IsNullOrEmpty(line) || line[0] != '[') return null;
            foreach (var rx in Patterns)
            {
                var m = rx.Match(line);
                if (m.Success) return m.Groups["p"].Value.Trim();
            }
            return null;
        }
    }
}
