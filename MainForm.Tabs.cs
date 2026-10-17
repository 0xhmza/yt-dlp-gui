using System;
using System.Windows.Forms;

namespace YtDlpGui
{
    internal partial class MainForm
    {
        // ---- Output ---------------------------------------------------------
        internal TextBox txtOutDir, txtTempDir, txtArchive, txtOutTemplateExtra;
        internal ComboBox cmbTemplate;
        internal NumericUpDown numTrimFilenames, numMaxDownloads;
        internal CheckBox chkRestrictFilenames, chkWindowsFilenames, chkNoOverwrites, chkForceOverwrites,
                          chkContinue, chkNoPart, chkNoMtime, chkSkipDownload, chkIgnoreErrors, chkAbortOnError,
                          chkBreakOnExisting, chkForceWriteArchive, chkNoDownloadArchive, chkWriteDesktopLink,
                          chkWriteUrlLink, chkWriteWeblocLink;

        // ---- Format ---------------------------------------------------------
        internal RadioButton radFmtBest, radFmtAudio, radFmtVideoOnly, radFmtCustom;
        internal ComboBox cmbMaxHeight, cmbMaxFps, cmbVideoCodec, cmbMergeContainer, cmbAudioFormat,
                          cmbAudioQuality, cmbRemux, cmbRecode, cmbPresetAlias, cmbFormatSortPreset;
        internal TextBox txtCustomFormat, txtFormatSort;
        internal CheckBox chkKeepVideo, chkFormatSortForce, chkPreferFreeFormats, chkCheckFormats,
                          chkVideoMultistreams, chkAudioMultistreams, chkAllowDynamicMpd, chkHlsUseMpegts,
                          chkHlsSplitDiscontinuity, chkLiveFromStart, chkIgnoreNoFormatsError;


        // ---- Subtitles & metadata -------------------------------------------
        internal CheckBox chkWriteSubs, chkWriteAutoSubs, chkEmbedSubs, chkWriteThumbnail, chkWriteAllThumbnails,
                          chkEmbedThumbnail, chkEmbedMetadata, chkEmbedChapters, chkEmbedInfoJson, chkWriteInfoJson,
                          chkWriteDescription, chkWriteComments, chkCleanInfoJson, chkXattrs;
        internal TextBox txtSubLangs, txtParseMetadata, txtReplaceInMetadata;
        internal ComboBox cmbSubFormat, cmbConvertSubs, cmbConvertThumbnails;

        // ---- Playlist & filters ---------------------------------------------
        internal ComboBox cmbPlaylistMode, cmbConcatPlaylist;
        internal TextBox txtPlaylistItems, txtMinFilesize, txtMaxFilesize, txtDate, txtDateBefore, txtDateAfter,
                         txtMatchFilters, txtDownloadSections, txtRemoveChapters;
        internal CheckBox chkPlaylistRandom, chkLazyPlaylist, chkFlatPlaylist, chkWritePlaylistMetafiles,
                          chkBreakMatchFilters, chkBreakPerInput, chkForceKeyframesAtCuts, chkSplitChapters;
        internal NumericUpDown numAgeLimit, numSkipPlaylistAfterErrors;

        // ---- Post-processing -------------------------------------------------
        internal TextBox txtSponsorBlockMark, txtSponsorBlockRemove, txtSponsorBlockTitle, txtSponsorBlockApi,
                         txtPostprocessorArgs, txtExec, txtFfmpegLocation, txtUsePostprocessor;
        internal ComboBox cmbFixup;

        // ---- Network ---------------------------------------------------------
        internal TextBox txtProxy, txtSourceAddress, txtRetries, txtFileAccessRetries, txtFragmentRetries,
                         txtRetrySleep, txtBufferSize, txtHttpChunkSize, txtLimitRate, txtThrottledRate,
                         txtHeaders, txtDownloaderArgs, txtDownloader;
        internal ComboBox cmbXff, cmbImpersonate;
        internal NumericUpDown numSocketTimeout, numConcurrentFragments, numSleepRequests, numSleepInterval,
                               numMaxSleepInterval, numSleepSubtitles;
        internal CheckBox chkSkipUnavailableFragments, chkAbortOnUnavailableFragments, chkKeepFragments,
                          chkNoResizeBuffer, chkNoCheckCertificates, chkPreferInsecure, chkLegacyServerConnect,
                          chkEnableFileUrls;

        // ---- Authentication ---------------------------------------------------
        internal TextBox txtUsername, txtPassword, txtVideoPassword, txtNetrcLocation, txtNetrcCmd, txtCookies,
                         txtClientCert, txtClientCertKey, txtClientCertPassword, txtApMso, txtApUsername, txtApPassword;
        internal ComboBox cmbCookiesFromBrowser;
        internal CheckBox chkNetrc;

        // ---- Advanced ----------------------------------------------------------
        internal CheckBox chkVerbose, chkQuiet, chkNoWarnings, chkPrintTraffic, chkWritePages, chkIgnoreConfig,
                          chkNoCacheDir, chkMarkWatched;
        internal TextBox txtConfigLocations, txtCacheDir, txtExtractorArgs, txtUseExtractors,
                         txtPluginDirs, txtCompatOptions, txtExtraArgs, txtPrint, txtPrintToFile, txtWaitForVideo,
                         txtJsRuntimes;
        internal ComboBox cmbColor;
        internal NumericUpDown numExtractorRetries;

        private const string Default = "(default)";

        private void BuildTabs()
        {
            // One layout pass for the whole tree instead of one per control added.
            _tabs.SuspendLayout();
            try
            {
                BuildOutputTab();
                BuildFormatTab();
                BuildSubtitlesTab();
                BuildPlaylistTab();
                BuildPostProcessingTab();
                BuildNetworkTab();
                BuildAuthTab();
                BuildAdvancedTab();
            }
            finally
            {
                _tabs.ResumeLayout(true);
            }
        }

        // =====================================================================
        private void BuildOutputTab()
        {
            var host = Ui.Tab(_tabs, "Output");

            var g = Ui.Group(host, "Destination");
            txtOutDir = Ui.PathRow(g, "Download folder", "outDir", true,
                "Where finished files are written (-P home:FOLDER).");
            txtTempDir = Ui.PathRow(g, "Temporary folder", "tempDir", true,
                "Optional scratch folder for partial files (-P temp:FOLDER). Handy when downloading to a slow or network drive.");

            cmbTemplate = Ui.Cmb("outTemplate", new[]
            {
                "%(title)s [%(id)s].%(ext)s",
                "%(title)s.%(ext)s",
                "%(uploader)s - %(title)s.%(ext)s",
                "%(upload_date>%Y-%m-%d)s - %(title)s.%(ext)s",
                "%(playlist_title|)s/%(playlist_index|)s - %(title)s.%(ext)s",
                "%(extractor)s/%(uploader)s/%(title)s [%(id)s].%(ext)s"
            }, true);
            Ui.Row(g, "File name template", cmbTemplate,
                "Output template (-o). Right-click the Help menu for the full field reference.");

            txtOutTemplateExtra = Ui.Txt("outTemplateExtra");
            Ui.Row(g, "Extra -o rules", txtOutTemplateExtra,
                "Optional additional output templates, one per line is not supported here - use TYPE:TEMPLATE, e.g. thumbnail:thumbs/%(id)s.%(ext)s");

            var gn = Ui.Group(host, "File names");
            Ui.Flow(gn,
                chkRestrictFilenames = Ui.Chk("restrictFilenames", "ASCII-only names",
                    "--restrict-filenames: strip spaces and non-ASCII characters."),
                chkWindowsFilenames = Ui.Chk("windowsFilenames", "Strict Windows-safe names",
                    "--windows-filenames: remove every character Windows dislikes."),
                chkNoMtime = Ui.Chk("noMtime", "Do not set file time from upload date",
                    "--no-mtime"),
                chkNoPart = Ui.Chk("noPart", "Write directly to the output file",
                    "--no-part: skip .part files."));
            numTrimFilenames = Ui.Num("trimFilenames", 0, 500, 0);
            Ui.RowFlow(gn, "Trim names to", numTrimFilenames, Ui.Note("characters (0 = no limit, --trim-filenames)"));

            var go = Ui.Group(host, "Existing files");
            Ui.Flow(go,
                chkNoOverwrites = Ui.Chk("noOverwrites", "Never overwrite (-w)"),
                chkForceOverwrites = Ui.Chk("forceOverwrites", "Always overwrite (--force-overwrites)"),
                chkContinue = Ui.Chk("continueDl", "Resume partial downloads (-c)"));
            chkContinue.Checked = true;

            var gb = Ui.Group(host, "Behaviour");
            Ui.Flow(gb,
                chkIgnoreErrors = Ui.Chk("ignoreErrors", "Continue past errors (-i)"),
                chkAbortOnError = Ui.Chk("abortOnError", "Abort on first error (--abort-on-error)"),
                chkSkipDownload = Ui.Chk("skipDownload", "Skip the download itself (--skip-download)",
                    "Useful together with subtitle, thumbnail or metadata writing."));
            chkIgnoreErrors.Checked = true;
            numMaxDownloads = Ui.Num("maxDownloads", 0, 100000, 0);
            Ui.RowFlow(gb, "Stop after", numMaxDownloads, Ui.Note("downloads (0 = unlimited, --max-downloads)"));

            var ga = Ui.Group(host, "Download archive");
            txtArchive = Ui.PathRow(ga, "Archive file", "archiveFile", false,
                "--download-archive: records every completed video so it is never fetched twice.",
                "Archive file (*.txt)|*.txt|All files (*.*)|*.*");
            Ui.Flow(ga,
                chkBreakOnExisting = Ui.Chk("breakOnExisting", "Stop when an archived item is hit (--break-on-existing)"),
                chkForceWriteArchive = Ui.Chk("forceWriteArchive", "Record even on failure (--force-write-archive)"),
                chkNoDownloadArchive = Ui.Chk("noDownloadArchive", "Ignore the archive this run (--no-download-archive)"));

            var gl = Ui.Group(host, "Shortcut files");
            Ui.Flow(gl,
                chkWriteUrlLink = Ui.Chk("writeUrlLink", "Windows .url (--write-url-link)"),
                chkWriteWeblocLink = Ui.Chk("writeWeblocLink", "macOS .webloc (--write-webloc-link)"),
                chkWriteDesktopLink = Ui.Chk("writeDesktopLink", "Linux .desktop (--write-desktop-link)"));
        }

        // =====================================================================
        private void BuildFormatTab()
        {
            var host = Ui.Tab(_tabs, "Format");

            var g = Ui.Group(host, "What to download");
            Ui.Flow(g,
                radFmtBest = Ui.Rad("fmtBest", "Best video + audio",
                    "Picks the best video and best audio and merges them with ffmpeg."),
                radFmtAudio = Ui.Rad("fmtAudio", "Audio only",
                    "-x: download and extract the audio track."),
                radFmtVideoOnly = Ui.Rad("fmtVideoOnly", "Video only (no audio)"),
                radFmtCustom = Ui.Rad("fmtCustom", "Custom format selector"));
            radFmtBest.Checked = true;

            cmbMaxHeight = Ui.Cmb("maxHeight", new[] { "Best available", "4320 (8K)", "2160 (4K)", "1440", "1080", "720", "480", "360", "240", "144" });
            cmbMaxFps = Ui.Cmb("maxFps", new[] { "Any", "60", "30" });
            cmbVideoCodec = Ui.Cmb("videoCodec", new[] { "Any", "Prefer AV1", "Prefer VP9", "Prefer H.264 (most compatible)" });
            Ui.RowFlow(g, "Max quality", cmbMaxHeight, cmbMaxFps, cmbVideoCodec);

            cmbMergeContainer = Ui.Cmb("mergeContainer", new[] { Default, "mp4", "mkv", "webm", "ogg", "flv", "avi", "mov" });
            Ui.Row(g, "Merge into", cmbMergeContainer,
                "--merge-output-format: container used when video and audio are combined.");

            txtCustomFormat = Ui.Txt("customFormat");
            Ui.Row(g, "Custom selector", txtCustomFormat,
                "-f, e.g.  bestvideo[height<=1080]+bestaudio/best  or a bare format ID from 'List formats...'.");
            Ui.Full(g, Ui.Note("The custom selector is only used when 'Custom format selector' is chosen above."));

            var ga = Ui.Group(host, "Audio extraction");
            cmbAudioFormat = Ui.Cmb("audioFormat", new[] { "best", "mp3", "m4a", "aac", "opus", "vorbis", "flac", "wav", "alac" });
            cmbAudioQuality = Ui.Cmb("audioQuality", new[] { Default, "0 (best VBR)", "5 (medium VBR)", "9 (worst VBR)", "320K", "256K", "192K", "128K", "96K", "64K" });
            Ui.RowFlow(ga, "Audio format", cmbAudioFormat, cmbAudioQuality);
            Ui.Flow(ga,
                chkKeepVideo = Ui.Chk("keepVideo", "Keep the original video file (-k)"));

            var gr = Ui.Group(host, "Convert after download");
            cmbRemux = Ui.Cmb("remux", new[] { Default, "mp4", "mkv", "webm", "mov", "avi", "flv", "mka", "m4a", "ogg", "aac", "flac", "mp3", "opus" });
            cmbRecode = Ui.Cmb("recode", new[] { Default, "mp4", "mkv", "webm", "mov", "avi", "flv", "gif", "mp3", "m4a", "ogg", "aac", "flac", "opus" });
            Ui.Row(gr, "Remux (fast, no re-encode)", cmbRemux, "--remux-video: only changes the container when needed.");
            Ui.Row(gr, "Re-encode (slow)", cmbRecode, "--recode-video: always re-encodes with ffmpeg.");

            var gs = Ui.Group(host, "Format sorting and fallbacks");
            cmbFormatSortPreset = Ui.Cmb("formatSortPreset", new[]
            {
                Default,
                "Best quality (quality,res,fps,hdr:12,vcodec)",
                "Smallest file (+size,+br)",
                "Best compatibility (vcodec:h264,acodec:aac,ext:mp4)",
                "Highest bitrate (br)"
            });
            Ui.Row(gs, "Sort preset", cmbFormatSortPreset, "Fills the -S field below with a common sort order.");
            cmbFormatSortPreset.SelectedIndexChanged += delegate
            {
                if (_loading) return;
                var t = cmbFormatSortPreset.Text;
                int open = t.IndexOf('(');
                if (open > 0) txtFormatSort.Text = t.Substring(open + 1).TrimEnd(')');
                else if (t == Default) txtFormatSort.Text = "";
            };

            txtFormatSort = Ui.Txt("formatSort");
            Ui.Row(gs, "Sort fields (-S)", txtFormatSort, "--format-sort, e.g.  res:1080,fps,vcodec:av01");

            cmbPresetAlias = Ui.Cmb("presetAlias", new[] { Default, "mp3", "aac", "mp4", "mkv", "sleep" });
            Ui.Row(gs, "Preset alias (-t)", cmbPresetAlias,
                "yt-dlp's built-in shortcut presets. These override some of the settings above.");

            Ui.Flow(gs,
                chkFormatSortForce = Ui.Chk("formatSortForce", "Force sort order (--format-sort-force)"),
                chkPreferFreeFormats = Ui.Chk("preferFreeFormats", "Prefer free formats (--prefer-free-formats)"),
                chkCheckFormats = Ui.Chk("checkFormats", "Verify formats are downloadable (--check-formats)"),
                chkIgnoreNoFormatsError = Ui.Chk("ignoreNoFormatsError", "Ignore 'no formats' errors"));
            Ui.Flow(gs,
                chkVideoMultistreams = Ui.Chk("videoMultistreams", "Allow multiple video streams"),
                chkAudioMultistreams = Ui.Chk("audioMultistreams", "Allow multiple audio streams"),
                chkAllowDynamicMpd = Ui.Chk("allowDynamicMpd", "Allow dynamic DASH (--allow-dynamic-mpd)"));

            var gh = Ui.Group(host, "Streams and live");
            Ui.Flow(gh,
                chkHlsUseMpegts = Ui.Chk("hlsUseMpegts", "HLS as MPEG-TS (--hls-use-mpegts)",
                    "Makes a live stream playable while it is still downloading."),
                chkHlsSplitDiscontinuity = Ui.Chk("hlsSplitDiscontinuity", "Split on HLS discontinuities"),
                chkLiveFromStart = Ui.Chk("liveFromStart", "Download live streams from the start (--live-from-start)"));
        }

        // =====================================================================
        private void BuildSubtitlesTab()
        {
            var host = Ui.Tab(_tabs, "Subtitles & Metadata");

            var g = Ui.Group(host, "Subtitles");
            Ui.Flow(g,
                chkWriteSubs = Ui.Chk("writeSubs", "Download subtitles (--write-subs)"),
                chkWriteAutoSubs = Ui.Chk("writeAutoSubs", "Include auto-generated (--write-auto-subs)"),
                chkEmbedSubs = Ui.Chk("embedSubs", "Embed into the video (--embed-subs)"));

            txtSubLangs = Ui.Txt("subLangs", "en.*");
            Ui.Row(g, "Languages", txtSubLangs,
                "--sub-langs, e.g.  en.*,ar  or  all,-live_chat . Use Tools > List subtitles to see what exists.");

            cmbSubFormat = Ui.Cmb("subFormat", new[] { Default, "best", "srt", "vtt", "ass", "srt/best", "vtt/best" });
            cmbConvertSubs = Ui.Cmb("convertSubs", new[] { Default, "srt", "vtt", "ass", "lrc" });
            Ui.RowFlow(g, "Preferred / convert to", cmbSubFormat, cmbConvertSubs);

            var gt = Ui.Group(host, "Thumbnails");
            Ui.Flow(gt,
                chkWriteThumbnail = Ui.Chk("writeThumbnail", "Save thumbnail (--write-thumbnail)"),
                chkWriteAllThumbnails = Ui.Chk("writeAllThumbnails", "Save all thumbnails"),
                chkEmbedThumbnail = Ui.Chk("embedThumbnail", "Embed as cover art (--embed-thumbnail)"));
            cmbConvertThumbnails = Ui.Cmb("convertThumbnails", new[] { Default, "jpg", "png", "webp" });
            Ui.Row(gt, "Convert thumbnails to", cmbConvertThumbnails, "--convert-thumbnails");

            var gm = Ui.Group(host, "Metadata");
            Ui.Flow(gm,
                chkEmbedMetadata = Ui.Chk("embedMetadata", "Embed title/artist/etc (--embed-metadata)"),
                chkEmbedChapters = Ui.Chk("embedChapters", "Embed chapters (--embed-chapters)"),
                chkEmbedInfoJson = Ui.Chk("embedInfoJson", "Embed the info JSON (mkv only)"),
                chkXattrs = Ui.Chk("xattrs", "Write extended attributes (--xattrs)"));
            Ui.Flow(gm,
                chkWriteInfoJson = Ui.Chk("writeInfoJson", "Save .info.json"),
                chkCleanInfoJson = Ui.Chk("cleanInfoJson", "Strip private fields from it"),
                chkWriteDescription = Ui.Chk("writeDescription", "Save description"),
                chkWriteComments = Ui.Chk("writeComments", "Save comments (slow)"));
            chkCleanInfoJson.Checked = true;

            txtParseMetadata = Ui.Txt("parseMetadata");
            Ui.Row(gm, "Parse metadata", txtParseMetadata,
                "--parse-metadata, e.g.  title:%(artist)s - %(title)s");
            txtReplaceInMetadata = Ui.Txt("replaceInMetadata");
            Ui.Row(gm, "Replace in metadata", txtReplaceInMetadata,
                "--replace-in-metadata, e.g.  title \"  \" \" \"");
        }

        // =====================================================================
        private void BuildPlaylistTab()
        {
            var host = Ui.Tab(_tabs, "Playlist & Filters");

            var g = Ui.Group(host, "Playlists");
            cmbPlaylistMode = Ui.Cmb("playlistMode", new[]
            {
                Default,
                "Download the whole playlist (--yes-playlist)",
                "Single video only (--no-playlist)"
            });
            Ui.Row(g, "When a URL is part of a playlist", cmbPlaylistMode);

            txtPlaylistItems = Ui.Txt("playlistItems");
            Ui.Row(g, "Items to download", txtPlaylistItems,
                "-I, e.g.  1:10  or  1,3,7-9  or  ::-1  to reverse.");

            Ui.Flow(g,
                chkPlaylistRandom = Ui.Chk("playlistRandom", "Random order (--playlist-random)"),
                chkLazyPlaylist = Ui.Chk("lazyPlaylist", "Process entries as they arrive (--lazy-playlist)"),
                chkFlatPlaylist = Ui.Chk("flatPlaylist", "Do not expand entries (--flat-playlist)"),
                chkWritePlaylistMetafiles = Ui.Chk("writePlaylistMetafiles", "Write playlist metadata files"));

            cmbConcatPlaylist = Ui.Cmb("concatPlaylist", new[] { Default, "never", "always", "multi_video" });
            Ui.Row(g, "Concatenate playlist into one file", cmbConcatPlaylist, "--concat-playlist");

            numSkipPlaylistAfterErrors = Ui.Num("skipPlaylistAfterErrors", 0, 10000, 0);
            Ui.RowFlow(g, "Abort playlist after", numSkipPlaylistAfterErrors,
                Ui.Note("failures (0 = never, --skip-playlist-after-errors)"));

            var gf = Ui.Group(host, "Filters");
            txtMinFilesize = Ui.Txt("minFilesize");
            txtMaxFilesize = Ui.Txt("maxFilesize");
            Ui.RowFlow(gf, "File size at least / at most", txtMinFilesize, txtMaxFilesize);
            Ui.Full(gf, Ui.Note("Use sizes like 50k, 10M, 2G  (--min-filesize / --max-filesize)."));

            txtDate = Ui.Txt("date");
            txtDateAfter = Ui.Txt("dateAfter");
            txtDateBefore = Ui.Txt("dateBefore");
            Ui.RowFlow(gf, "Exact date / after / before", txtDate, txtDateAfter, txtDateBefore);
            Ui.Full(gf, Ui.Note("YYYYMMDD, or relative like  today-2weeks  (--date, --dateafter, --datebefore)."));

            txtMatchFilters = Ui.Txt("matchFilters");
            Ui.Row(gf, "Match filter", txtMatchFilters,
                "--match-filters, e.g.  duration < 600 & view_count > 1000");
            Ui.Flow(gf,
                chkBreakMatchFilters = Ui.Chk("breakMatchFilters", "Stop when the filter fails (--break-match-filters)"),
                chkBreakPerInput = Ui.Chk("breakPerInput", "Apply 'stop' rules per URL (--break-per-input)"));

            numAgeLimit = Ui.Num("ageLimit", 0, 99, 0);
            Ui.RowFlow(gf, "Age limit", numAgeLimit, Ui.Note("years (0 = off, --age-limit)"));

            var gc = Ui.Group(host, "Chapters and sections");
            txtDownloadSections = Ui.Txt("downloadSections");
            Ui.Row(gc, "Download only sections", txtDownloadSections,
                "--download-sections, e.g.  *10:15-15:00  or  intro  (a chapter regex).");
            Ui.Flow(gc,
                chkForceKeyframesAtCuts = Ui.Chk("forceKeyframesAtCuts", "Re-encode at cut points for accuracy"),
                chkSplitChapters = Ui.Chk("splitChapters", "Split into one file per chapter (--split-chapters)"));
            txtRemoveChapters = Ui.Txt("removeChapters");
            Ui.Row(gc, "Remove chapters matching", txtRemoveChapters, "--remove-chapters, a regex or  *start-end  range.");
        }

        // =====================================================================
        private void BuildPostProcessingTab()
        {
            var host = Ui.Tab(_tabs, "Post-processing");

            var g = Ui.Group(host, "SponsorBlock");
            txtSponsorBlockMark = Ui.Txt("sponsorBlockMark");
            txtSponsorBlockRemove = Ui.Txt("sponsorBlockRemove");
            Ui.Row(g, "Mark as chapters", txtSponsorBlockMark,
                "--sponsorblock-mark, e.g.  all  or  sponsor,selfpromo,intro");
            Ui.Row(g, "Cut out entirely", txtSponsorBlockRemove,
                "--sponsorblock-remove, e.g.  sponsor,selfpromo  (removed segments cannot be marked).");
            Ui.Full(g, Ui.Note("Categories: sponsor, intro, outro, selfpromo, preview, filler, interaction, music_offtopic, poi_highlight, chapter, all"));
            txtSponsorBlockTitle = Ui.Txt("sponsorBlockTitle");
            Ui.Row(g, "Chapter title template", txtSponsorBlockTitle, "--sponsorblock-chapter-title");
            txtSponsorBlockApi = Ui.Txt("sponsorBlockApi");
            Ui.Row(g, "API URL", txtSponsorBlockApi, "--sponsorblock-api");

            var gf = Ui.Group(host, "ffmpeg");
            txtFfmpegLocation = Ui.PathRow(gf, "ffmpeg folder", "ffmpegLocation", true,
                "--ffmpeg-location. Filled in automatically when ffmpeg.exe sits next to this program.");
            cmbFixup = Ui.Cmb("fixup", new[] { Default, "never", "warn", "detect_or_warn", "force" });
            Ui.Row(gf, "Fix broken files", cmbFixup, "--fixup");
            txtPostprocessorArgs = Ui.Txt("postprocessorArgs");
            Ui.Row(gf, "Post-processor arguments", txtPostprocessorArgs,
                "--postprocessor-args, e.g.  ffmpeg:-vcodec libx265 -crf 28");

            var ge = Ui.Group(host, "Run a command afterwards");
            txtExec = Ui.Txt("exec");
            Ui.Row(ge, "Command", txtExec,
                "--exec, e.g.  echo {}   ({} is replaced with the final file path).");
            txtUsePostprocessor = Ui.Txt("usePostprocessor");
            Ui.Row(ge, "Extra post-processor", txtUsePostprocessor, "--use-postprocessor NAME[:ARGS]");
        }

        // =====================================================================
        private void BuildNetworkTab()
        {
            var host = Ui.Tab(_tabs, "Network");

            var g = Ui.Group(host, "Connection");
            txtProxy = Ui.Txt("proxy");
            Ui.Row(g, "Proxy", txtProxy, "--proxy, e.g.  socks5://127.0.0.1:1080  (empty string disables an inherited proxy).");
            numSocketTimeout = Ui.Num("socketTimeout", 0, 3600, 0);
            txtSourceAddress = Ui.Txt("sourceAddress");
            Ui.RowFlow(g, "Timeout (s) / bind address", numSocketTimeout, txtSourceAddress);
            cmbXff = Ui.Cmb("xff", new[] { Default, "default", "never", "US", "GB", "DE" }, true);
            Ui.Row(g, "Geo bypass (--xff)", cmbXff, "default, never, an ISO 3166-2 country code, or an IP block.");
            Ui.Flow(g,
                chkNoCheckCertificates = Ui.Chk("noCheckCertificates", "Skip HTTPS certificate checks"),
                chkPreferInsecure = Ui.Chk("preferInsecure", "Prefer unencrypted connection"),
                chkLegacyServerConnect = Ui.Chk("legacyServerConnect", "Allow legacy TLS renegotiation"),
                chkEnableFileUrls = Ui.Chk("enableFileUrls", "Allow file:// URLs"));

            var gs = Ui.Group(host, "Speed and throttling");
            txtLimitRate = Ui.Txt("limitRate");
            txtThrottledRate = Ui.Txt("throttledRate");
            Ui.RowFlow(gs, "Rate limit / re-extract below", txtLimitRate, txtThrottledRate);
            Ui.Full(gs, Ui.Note("e.g.  4.2M  or  500K  (-r / --throttled-rate)."));
            numConcurrentFragments = Ui.Num("concurrentFragments", 1, 128, 1);
            Ui.RowFlow(gs, "Concurrent fragments (-N)", numConcurrentFragments,
                Ui.Note("Speeds up DASH/HLS downloads considerably."));
            txtBufferSize = Ui.Txt("bufferSize");
            txtHttpChunkSize = Ui.Txt("httpChunkSize");
            Ui.RowFlow(gs, "Buffer size / chunk size", txtBufferSize, txtHttpChunkSize);
            Ui.Flow(gs, chkNoResizeBuffer = Ui.Chk("noResizeBuffer", "Do not auto-resize the buffer"));

            var gr = Ui.Group(host, "Retries");
            txtRetries = Ui.Txt("retries");
            txtFragmentRetries = Ui.Txt("fragmentRetries");
            txtFileAccessRetries = Ui.Txt("fileAccessRetries");
            Ui.RowFlow(gr, "Downloads / fragments / file access", txtRetries, txtFragmentRetries, txtFileAccessRetries);
            Ui.Full(gr, Ui.Note("A number or the word  infinite  (-R, --fragment-retries, --file-access-retries)."));
            txtRetrySleep = Ui.Txt("retrySleep");
            Ui.Row(gr, "Retry delay", txtRetrySleep, "--retry-sleep, e.g.  linear=1::2  or  exp=1:20");
            Ui.Flow(gr,
                chkSkipUnavailableFragments = Ui.Chk("skipUnavailableFragments", "Skip unavailable fragments"),
                chkAbortOnUnavailableFragments = Ui.Chk("abortOnUnavailableFragments", "Abort on a missing fragment"),
                chkKeepFragments = Ui.Chk("keepFragments", "Keep fragments after merging"));

            var gp = Ui.Group(host, "Politeness delays");
            numSleepRequests = Ui.Num("sleepRequests", 0, 3600, 0, 2);
            numSleepInterval = Ui.Num("sleepInterval", 0, 86400, 0, 2);
            numMaxSleepInterval = Ui.Num("maxSleepInterval", 0, 86400, 0, 2);
            numSleepSubtitles = Ui.Num("sleepSubtitles", 0, 3600, 0);
            Ui.RowFlow(gp, "Between requests / before each download", numSleepRequests, numSleepInterval);
            Ui.RowFlow(gp, "Max random delay / before subtitles", numMaxSleepInterval, numSleepSubtitles);
            Ui.Full(gp, Ui.Note("Seconds. Useful when a site rate-limits you (0 = off)."));

            var gd = Ui.Group(host, "Headers and downloader");
            txtHeaders = Ui.Multi("headers", 3);
            Ui.Row(gd, "Extra HTTP headers", txtHeaders,
                "One per line, e.g.  Referer: https://example.com   (--add-headers).");
            cmbImpersonate = Ui.Cmb("impersonate", new[] { Default, "chrome", "chrome-110", "edge", "safari", "firefox" }, true);
            Ui.Row(gd, "Impersonate browser", cmbImpersonate,
                "--impersonate: mimics a browser TLS fingerprint. Requires curl-cffi support in yt-dlp.");
            txtDownloader = Ui.Txt("downloader");
            txtDownloaderArgs = Ui.Txt("downloaderArgs");
            Ui.Row(gd, "External downloader", txtDownloader, "--downloader, e.g.  aria2c  or  dash,m3u8:native");
            Ui.Row(gd, "Downloader arguments", txtDownloaderArgs, "--downloader-args, e.g.  aria2c:-x8 -s8");
        }

        // =====================================================================
        private void BuildAuthTab()
        {
            var host = Ui.Tab(_tabs, "Authentication");

            var g = Ui.Group(host, "Account");
            txtUsername = Ui.Txt("username");
            Ui.Row(g, "Username", txtUsername, "-u");
            txtPassword = Ui.Txt("password");
            txtPassword.UseSystemPasswordChar = true;
            Ui.Row(g, "Password", txtPassword, "-p");
            txtVideoPassword = Ui.Txt("videoPassword");
            txtVideoPassword.UseSystemPasswordChar = true;
            Ui.Row(g, "Video password", txtVideoPassword, "--video-password, for individual protected videos.");
            Ui.Full(g, Ui.Note("Credentials typed here are stored in plain text in the settings file. Prefer cookies or .netrc."));

            var gn = Ui.Group(host, ".netrc");
            Ui.Flow(gn, chkNetrc = Ui.Chk("netrc", "Use a .netrc file (-n)"));
            txtNetrcLocation = Ui.PathRow(gn, ".netrc file", "netrcLocation", false, "--netrc-location");
            txtNetrcCmd = Ui.Txt("netrcCmd");
            Ui.Row(gn, "Command that prints netrc", txtNetrcCmd, "--netrc-cmd");

            var gc = Ui.Group(host, "Cookies");
            txtCookies = Ui.PathRow(gc, "Cookies file", "cookies", false,
                "--cookies: a Netscape-format cookies.txt.",
                "Cookie file (*.txt)|*.txt|All files (*.*)|*.*");
            cmbCookiesFromBrowser = Ui.Cmb("cookiesFromBrowser", new[]
            {
                Default, "brave", "chrome", "chromium", "edge", "firefox", "opera", "safari", "vivaldi", "whale"
            }, true);
            Ui.Row(gc, "Or take cookies from", cmbCookiesFromBrowser,
                "--cookies-from-browser. You can append a profile, e.g.  firefox:default-release");
            Ui.Full(gc, Ui.Note("Close the browser first - a locked cookie database cannot be read."));

            var gt = Ui.Group(host, "Client certificate");
            txtClientCert = Ui.PathRow(gt, "Certificate file", "clientCertificate", false, "--client-certificate");
            txtClientCertKey = Ui.PathRow(gt, "Private key file", "clientCertificateKey", false, "--client-certificate-key");
            txtClientCertPassword = Ui.Txt("clientCertificatePassword");
            txtClientCertPassword.UseSystemPasswordChar = true;
            Ui.Row(gt, "Key password", txtClientCertPassword, "--client-certificate-password");

            var ga = Ui.Group(host, "Adobe Pass (cable TV provider)");
            txtApMso = Ui.Txt("apMso");
            Ui.Row(ga, "Provider (MSO) ID", txtApMso, "--ap-mso. Tools > List extractors does not list these; see --ap-list-mso.");
            txtApUsername = Ui.Txt("apUsername");
            Ui.Row(ga, "Provider username", txtApUsername, "--ap-username");
            txtApPassword = Ui.Txt("apPassword");
            txtApPassword.UseSystemPasswordChar = true;
            Ui.Row(ga, "Provider password", txtApPassword, "--ap-password");
        }

        // =====================================================================
        private void BuildAdvancedTab()
        {
            var host = Ui.Tab(_tabs, "Advanced");

            var g = Ui.Group(host, "Logging");
            Ui.Flow(g,
                chkVerbose = Ui.Chk("verbose", "Verbose output (-v)"),
                chkQuiet = Ui.Chk("quiet", "Quiet (-q)"),
                chkNoWarnings = Ui.Chk("noWarnings", "Hide warnings"),
                chkPrintTraffic = Ui.Chk("printTraffic", "Dump HTTP traffic"),
                chkWritePages = Ui.Chk("writePages", "Save downloaded pages"));
            cmbColor = Ui.Cmb("color", new[] { "never", "auto", "always", "no_color" });
            Ui.Row(g, "Colour codes", cmbColor,
                "--color. 'never' keeps the log readable; the other values embed ANSI escapes.");

            txtPrint = Ui.Txt("print");
            Ui.Row(g, "Print fields", txtPrint, "--print, e.g.  %(title)s %(duration)s");
            txtPrintToFile = Ui.Txt("printToFile");
            Ui.Row(g, "Print to file", txtPrintToFile, "--print-to-file TEMPLATE FILE (give both, space separated).");

            var gc = Ui.Group(host, "Configuration and cache");
            Ui.Flow(gc,
                chkIgnoreConfig = Ui.Chk("ignoreConfig", "Ignore yt-dlp config files (--ignore-config)",
                    "Recommended: guarantees this window is the only thing deciding the options."),
                chkNoCacheDir = Ui.Chk("noCacheDir", "Disable the cache (--no-cache-dir)"),
                chkMarkWatched = Ui.Chk("markWatched", "Mark videos watched (--mark-watched)"));
            chkIgnoreConfig.Checked = true;
            txtConfigLocations = Ui.Txt("configLocations");
            Ui.Row(gc, "Extra config file", txtConfigLocations, "--config-locations");
            txtCacheDir = Ui.PathRow(gc, "Cache folder", "cacheDir", true, "--cache-dir");

            var ge = Ui.Group(host, "Extractors");
            txtExtractorArgs = Ui.Multi("extractorArgs", 3);
            Ui.Row(ge, "Extractor arguments", txtExtractorArgs,
                "One per line, e.g.  youtube:player_client=android,web   (--extractor-args).");
            txtUseExtractors = Ui.Txt("useExtractors");
            Ui.Row(ge, "Only these extractors", txtUseExtractors, "--use-extractors, e.g.  youtube,twitter");
            numExtractorRetries = Ui.Num("extractorRetries", 0, 100, 0);
            Ui.RowFlow(ge, "Extractor retries", numExtractorRetries, Ui.Note("(0 = leave at the yt-dlp default)"));
            txtPluginDirs = Ui.Txt("pluginDirs");
            Ui.Row(ge, "Plugin folders", txtPluginDirs, "--plugin-dirs");
            txtCompatOptions = Ui.Txt("compatOptions");
            Ui.Row(ge, "Compatibility options", txtCompatOptions, "--compat-options, e.g.  youtube-dl  or  no-live-chat");
            txtJsRuntimes = Ui.Txt("jsRuntimes");
            Ui.Row(ge, "JavaScript runtimes", txtJsRuntimes, "--js-runtimes, e.g.  deno  or  node");

            var gw = Ui.Group(host, "Waiting");
            txtWaitForVideo = Ui.Txt("waitForVideo");
            Ui.Row(gw, "Wait for a scheduled stream", txtWaitForVideo,
                "--wait-for-video, e.g.  30  or  30-300  (seconds between checks).");

            var gx = Ui.Group(host, "Raw extra arguments");
            txtExtraArgs = Ui.Multi("extraArgs", 4);
            Ui.Row(gx, "Passed verbatim", txtExtraArgs,
                "Anything not covered by the tabs. One argument per line, e.g.\r\n--extractor-args\r\nyoutube:formats=dashy");
            Ui.Full(gx, Ui.Note("One argument per line - do not add your own quotes, they are handled for you."));
        }

        /// <summary>Greys out the parts of the Format tab that do not apply to the chosen mode.</summary>
        private void UpdateFormatEnablement()
        {
            if (radFmtAudio == null) return;

            bool audio = radFmtAudio.Checked;
            bool custom = radFmtCustom.Checked;
            bool quality = !custom;

            cmbAudioFormat.Enabled = audio;
            cmbAudioQuality.Enabled = audio;
            chkKeepVideo.Enabled = audio;
            txtCustomFormat.Enabled = custom;
            cmbMaxHeight.Enabled = quality && !audio;
            cmbMaxFps.Enabled = quality && !audio;
            cmbVideoCodec.Enabled = quality && !audio;
            cmbMergeContainer.Enabled = !audio;
        }
    }
}
