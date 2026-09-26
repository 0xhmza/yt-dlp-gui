using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace YtDlpGui
{
    internal partial class MainForm
    {
        // ---- small helpers ---------------------------------------------------
        private static void AddArg(List<string> a, string flag) { a.Add(flag); }

        private static void AddArg(List<string> a, string flag, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            a.Add(flag);
            a.Add(value);
        }

        /// <summary>Trimmed text box content, or null when empty.</summary>
        private static string TextValue(TextBox t)
        {
            if (t == null) return null;
            var s = t.Text.Trim();
            return s.Length == 0 ? null : s;
        }

        /// <summary>Combo value, or null when it is empty or the "(default)" placeholder.</summary>
        private static string ComboValue(ComboBox c)
        {
            if (c == null) return null;
            var s = c.Text.Trim();
            if (s.Length == 0 || s == Default) return null;
            return s;
        }

        /// <summary>First whitespace-delimited token, so "2160 (4K)" becomes "2160".</summary>
        private static string FirstToken(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            int i = s.IndexOf(' ');
            return i < 0 ? s : s.Substring(0, i);
        }

        private static void AddNumberArg(List<string> a, string flag, NumericUpDown n, decimal skipWhen = 0m)
        {
            if (n == null || n.Value == skipWhen) return;
            a.Add(flag);
            a.Add(n.Value.ToString(n.DecimalPlaces > 0 ? "0.##" : "0", CultureInfo.InvariantCulture));
        }

        private static void AddLineArgs(List<string> a, string flag, TextBox t)
        {
            if (t == null) return;
            foreach (var raw in t.Text.Split('\n'))
            {
                var s = raw.Trim().Trim('\r');
                if (s.Length == 0) continue;
                a.Add(flag);
                a.Add(s);
            }
        }

        // =====================================================================
        /// <summary>
        /// Builds the full argument list. <paramref name="urls"/> are appended last.
        /// </summary>
        internal List<string> BuildArgs(List<string> urls)
        {
            var a = new List<string>();

            // Machine-friendly output so the GUI can parse progress reliably. These go first
            // so that anything the user types under "Raw extra arguments" still wins: yt-dlp
            // takes the last occurrence of a repeated option.
            AddArg(a, "--newline");
            AddArg(a, "--progress");

            // One structured progress line per update, instead of scraping a human-readable
            // one. This is what makes a playlist-wide progress bar possible: it carries the
            // playlist position and the exact byte counts, not just a percentage.
            if (App.SupportsProgressTemplate)
                AddArg(a, "--progress-template", Runner.ProgressTemplate);

            // Ten updates a second is plenty for a progress bar and keeps a 500-item playlist
            // from spending its time writing to a pipe.
            if (App.SupportsProgressDelta)
                AddArg(a, "--progress-delta", "0.1");

            if (chkIgnoreConfig.Checked) AddArg(a, "--ignore-config");
            AddArg(a, "--color", ComboValue(cmbColor) ?? "never");

            AddOutputArgs(a);
            AddFormatArgs(a);
            AddSubtitleArgs(a);
            AddPlaylistArgs(a);
            AddPostProcessingArgs(a);
            AddNetworkArgs(a);
            AddAuthArgs(a);
            AddAdvancedArgs(a);

            // Raw escape hatch last so it can override anything above.
            foreach (var raw in txtExtraArgs.Text.Split('\n'))
            {
                var s = raw.Trim().Trim('\r');
                if (s.Length > 0) a.Add(s);
            }

            if (urls != null) a.AddRange(urls);
            return a;
        }

        // ---- Output ----------------------------------------------------------
        private void AddOutputArgs(List<string> a)
        {
            var home = TextValue(txtOutDir);
            if (home != null) AddArg(a, "-P", "home:" + home);

            var temp = TextValue(txtTempDir);
            if (temp != null) AddArg(a, "-P", "temp:" + temp);

            AddArg(a, "-o", ComboValue(cmbTemplate));
            AddArg(a, "-o", TextValue(txtOutTemplateExtra));

            if (chkRestrictFilenames.Checked) AddArg(a, "--restrict-filenames");
            if (chkWindowsFilenames.Checked) AddArg(a, "--windows-filenames");
            if (chkNoMtime.Checked) AddArg(a, "--no-mtime");
            if (chkNoPart.Checked) AddArg(a, "--no-part");
            AddNumberArg(a, "--trim-filenames", numTrimFilenames);

            if (chkNoOverwrites.Checked) AddArg(a, "-w");
            if (chkForceOverwrites.Checked) AddArg(a, "--force-overwrites");
            AddArg(a, chkContinue.Checked ? "-c" : "--no-continue");

            if (chkIgnoreErrors.Checked) AddArg(a, "-i");
            if (chkAbortOnError.Checked) AddArg(a, "--abort-on-error");
            if (chkSkipDownload.Checked) AddArg(a, "--skip-download");
            AddNumberArg(a, "--max-downloads", numMaxDownloads);

            AddArg(a, "--download-archive", TextValue(txtArchive));
            if (chkBreakOnExisting.Checked) AddArg(a, "--break-on-existing");
            if (chkForceWriteArchive.Checked) AddArg(a, "--force-write-archive");
            if (chkNoDownloadArchive.Checked) AddArg(a, "--no-download-archive");

            if (chkWriteUrlLink.Checked) AddArg(a, "--write-url-link");
            if (chkWriteWeblocLink.Checked) AddArg(a, "--write-webloc-link");
            if (chkWriteDesktopLink.Checked) AddArg(a, "--write-desktop-link");
        }

        // ---- Format ----------------------------------------------------------
        private void AddFormatArgs(List<string> a)
        {
            AddArg(a, "-t", ComboValue(cmbPresetAlias));

            var height = FirstToken(cmbMaxHeight.Text);
            if (height == "Best") height = null;                 // "Best available"
            var fps = cmbMaxFps.Text == "Any" ? null : cmbMaxFps.Text;

            if (radFmtAudio.Checked)
            {
                AddArg(a, "-x");
                AddArg(a, "--audio-format", ComboValue(cmbAudioFormat));
                AddArg(a, "--audio-quality", FirstToken(ComboValue(cmbAudioQuality)));

                if (chkKeepVideo.Checked) AddArg(a, "-k");
            }
            else if (radFmtCustom.Checked)
            {
                AddArg(a, "-f", TextValue(txtCustomFormat));
            }
            else
            {
                // Best video+audio, or video only.
                var filter = "";
                if (height != null) filter += "[height<=" + height + "]";
                if (fps != null) filter += "[fps<=" + fps + "]";

                if (radFmtVideoOnly.Checked)
                {
                    AddArg(a, "-f", "bv*" + filter + "/b" + filter);
                }
                else if (filter.Length > 0)
                {
                    AddArg(a, "-f", "bv*" + filter + "+ba/b" + filter + "/bv*+ba/b");
                }
                // No constraints: leave -f alone and let yt-dlp use its default.
            }

            if (!radFmtAudio.Checked)
            {
                AddArg(a, "--merge-output-format", ComboValue(cmbMergeContainer));
            }

            AddArg(a, "--remux-video", ComboValue(cmbRemux));
            AddArg(a, "--recode-video", ComboValue(cmbRecode));

            // Sort order: the user's own fields plus the codec preference.
            var sort = TextValue(txtFormatSort) ?? "";
            string codecPref = null;
            if (cmbVideoCodec.Text.IndexOf("AV1", StringComparison.OrdinalIgnoreCase) >= 0) codecPref = "vcodec:av01";
            else if (cmbVideoCodec.Text.IndexOf("VP9", StringComparison.OrdinalIgnoreCase) >= 0) codecPref = "vcodec:vp9";
            else if (cmbVideoCodec.Text.IndexOf("H.264", StringComparison.OrdinalIgnoreCase) >= 0) codecPref = "vcodec:h264";

            if (codecPref != null && !radFmtAudio.Checked)
                sort = sort.Length > 0 ? codecPref + "," + sort : codecPref;
            if (sort.Length > 0) AddArg(a, "-S", sort);

            if (chkFormatSortForce.Checked) AddArg(a, "--format-sort-force");
            if (chkPreferFreeFormats.Checked) AddArg(a, "--prefer-free-formats");
            if (chkCheckFormats.Checked) AddArg(a, "--check-formats");
            if (chkIgnoreNoFormatsError.Checked) AddArg(a, "--ignore-no-formats-error");
            if (chkVideoMultistreams.Checked) AddArg(a, "--video-multistreams");
            if (chkAudioMultistreams.Checked) AddArg(a, "--audio-multistreams");
            if (chkAllowDynamicMpd.Checked) AddArg(a, "--allow-dynamic-mpd");
            if (chkHlsUseMpegts.Checked) AddArg(a, "--hls-use-mpegts");
            if (chkHlsSplitDiscontinuity.Checked) AddArg(a, "--hls-split-discontinuity");
            if (chkLiveFromStart.Checked) AddArg(a, "--live-from-start");
        }

        // ---- Subtitles, thumbnails, metadata ---------------------------------
        private void AddSubtitleArgs(List<string> a)
        {
            bool anySubs = chkWriteSubs.Checked || chkWriteAutoSubs.Checked || chkEmbedSubs.Checked;

            if (chkWriteSubs.Checked) AddArg(a, "--write-subs");
            if (chkWriteAutoSubs.Checked) AddArg(a, "--write-auto-subs");
            if (chkEmbedSubs.Checked) AddArg(a, "--embed-subs");

            if (anySubs)
            {
                AddArg(a, "--sub-langs", TextValue(txtSubLangs));
                AddArg(a, "--sub-format", ComboValue(cmbSubFormat));
                AddArg(a, "--convert-subs", ComboValue(cmbConvertSubs));
            }

            if (chkWriteThumbnail.Checked) AddArg(a, "--write-thumbnail");
            if (chkWriteAllThumbnails.Checked) AddArg(a, "--write-all-thumbnails");
            if (chkEmbedThumbnail.Checked) AddArg(a, "--embed-thumbnail");
            AddArg(a, "--convert-thumbnails", ComboValue(cmbConvertThumbnails));

            if (chkEmbedMetadata.Checked) AddArg(a, "--embed-metadata");
            if (chkEmbedChapters.Checked) AddArg(a, "--embed-chapters");
            if (chkEmbedInfoJson.Checked) AddArg(a, "--embed-info-json");
            if (chkXattrs.Checked) AddArg(a, "--xattrs");
            if (chkWriteInfoJson.Checked)
            {
                AddArg(a, "--write-info-json");
                AddArg(a, chkCleanInfoJson.Checked ? "--clean-info-json" : "--no-clean-info-json");
            }
            if (chkWriteDescription.Checked) AddArg(a, "--write-description");
            if (chkWriteComments.Checked) AddArg(a, "--write-comments");

            AddArg(a, "--parse-metadata", TextValue(txtParseMetadata));
            AddArg(a, "--replace-in-metadata", TextValue(txtReplaceInMetadata));
        }

        // ---- Playlist and filters --------------------------------------------
        private void AddPlaylistArgs(List<string> a)
        {
            var mode = cmbPlaylistMode.Text;
            if (mode.IndexOf("--yes-playlist", StringComparison.Ordinal) >= 0) AddArg(a, "--yes-playlist");
            else if (mode.IndexOf("--no-playlist", StringComparison.Ordinal) >= 0) AddArg(a, "--no-playlist");

            AddArg(a, "-I", TextValue(txtPlaylistItems));
            if (chkPlaylistRandom.Checked) AddArg(a, "--playlist-random");
            if (chkLazyPlaylist.Checked) AddArg(a, "--lazy-playlist");
            if (chkFlatPlaylist.Checked) AddArg(a, "--flat-playlist");
            if (chkWritePlaylistMetafiles.Checked) AddArg(a, "--write-playlist-metafiles");
            AddArg(a, "--concat-playlist", ComboValue(cmbConcatPlaylist));
            AddNumberArg(a, "--skip-playlist-after-errors", numSkipPlaylistAfterErrors);

            AddArg(a, "--min-filesize", TextValue(txtMinFilesize));
            AddArg(a, "--max-filesize", TextValue(txtMaxFilesize));
            AddArg(a, "--date", TextValue(txtDate));
            AddArg(a, "--dateafter", TextValue(txtDateAfter));
            AddArg(a, "--datebefore", TextValue(txtDateBefore));

            AddArg(a, chkBreakMatchFilters.Checked ? "--break-match-filters" : "--match-filters", TextValue(txtMatchFilters));
            if (chkBreakPerInput.Checked) AddArg(a, "--break-per-input");
            AddNumberArg(a, "--age-limit", numAgeLimit);

            AddArg(a, "--download-sections", TextValue(txtDownloadSections));
            if (chkForceKeyframesAtCuts.Checked) AddArg(a, "--force-keyframes-at-cuts");
            if (chkSplitChapters.Checked) AddArg(a, "--split-chapters");
            AddArg(a, "--remove-chapters", TextValue(txtRemoveChapters));
        }

        // ---- Post-processing ---------------------------------------------------
        private void AddPostProcessingArgs(List<string> a)
        {
            AddArg(a, "--sponsorblock-mark", TextValue(txtSponsorBlockMark));
            AddArg(a, "--sponsorblock-remove", TextValue(txtSponsorBlockRemove));
            AddArg(a, "--sponsorblock-chapter-title", TextValue(txtSponsorBlockTitle));
            AddArg(a, "--sponsorblock-api", TextValue(txtSponsorBlockApi));

            AddArg(a, "--ffmpeg-location", TextValue(txtFfmpegLocation));
            AddArg(a, "--fixup", ComboValue(cmbFixup));
            AddArg(a, "--postprocessor-args", TextValue(txtPostprocessorArgs));
            AddArg(a, "--exec", TextValue(txtExec));
            AddArg(a, "--use-postprocessor", TextValue(txtUsePostprocessor));
        }

        // ---- Network -----------------------------------------------------------
        private void AddConnectionArgs(List<string> a)
        {
            AddArg(a, "--proxy", TextValue(txtProxy));
            AddNumberArg(a, "--socket-timeout", numSocketTimeout);
            AddArg(a, "--source-address", TextValue(txtSourceAddress));
            AddArg(a, "--xff", ComboValue(cmbXff));

            if (chkNoCheckCertificates.Checked) AddArg(a, "--no-check-certificates");
            if (chkPreferInsecure.Checked) AddArg(a, "--prefer-insecure");
            if (chkLegacyServerConnect.Checked) AddArg(a, "--legacy-server-connect");
            if (chkEnableFileUrls.Checked) AddArg(a, "--enable-file-urls");
        }

        private void AddNetworkArgs(List<string> a)
        {
            AddConnectionArgs(a);

            AddArg(a, "-r", TextValue(txtLimitRate));
            AddArg(a, "--throttled-rate", TextValue(txtThrottledRate));
            if (numConcurrentFragments.Value > 1)
                AddArg(a, "-N", ((int)numConcurrentFragments.Value).ToString(CultureInfo.InvariantCulture));
            AddArg(a, "--buffer-size", TextValue(txtBufferSize));
            AddArg(a, "--http-chunk-size", TextValue(txtHttpChunkSize));
            if (chkNoResizeBuffer.Checked) AddArg(a, "--no-resize-buffer");

            AddArg(a, "-R", TextValue(txtRetries));
            AddArg(a, "--fragment-retries", TextValue(txtFragmentRetries));
            AddArg(a, "--file-access-retries", TextValue(txtFileAccessRetries));
            AddArg(a, "--retry-sleep", TextValue(txtRetrySleep));
            if (chkSkipUnavailableFragments.Checked) AddArg(a, "--skip-unavailable-fragments");
            if (chkAbortOnUnavailableFragments.Checked) AddArg(a, "--abort-on-unavailable-fragments");
            if (chkKeepFragments.Checked) AddArg(a, "--keep-fragments");

            AddNumberArg(a, "--sleep-requests", numSleepRequests);
            AddNumberArg(a, "--sleep-interval", numSleepInterval);
            AddNumberArg(a, "--max-sleep-interval", numMaxSleepInterval);
            AddNumberArg(a, "--sleep-subtitles", numSleepSubtitles);

            AddLineArgs(a, "--add-headers", txtHeaders);
            AddArg(a, "--impersonate", ComboValue(cmbImpersonate));
            AddArg(a, "--downloader", TextValue(txtDownloader));
            AddArg(a, "--downloader-args", TextValue(txtDownloaderArgs));
        }

        // ---- Authentication ------------------------------------------------------
        private void AddAuthArgs(List<string> a)
        {
            AddLoginArgs(a);
            AddCookieArgs(a);
            AddCertificateArgs(a);

            AddArg(a, "--ap-mso", TextValue(txtApMso));
            AddArg(a, "--ap-username", TextValue(txtApUsername));
            AddArg(a, "--ap-password", TextValue(txtApPassword));
        }

        private void AddLoginArgs(List<string> a)
        {
            AddArg(a, "-u", TextValue(txtUsername));
            AddArg(a, "-p", TextValue(txtPassword));
            AddArg(a, "--video-password", TextValue(txtVideoPassword));

            if (chkNetrc.Checked) AddArg(a, "-n");
            AddArg(a, "--netrc-location", TextValue(txtNetrcLocation));
            AddArg(a, "--netrc-cmd", TextValue(txtNetrcCmd));
        }

        private void AddCookieArgs(List<string> a)
        {
            AddArg(a, "--cookies", TextValue(txtCookies));
            AddArg(a, "--cookies-from-browser", ComboValue(cmbCookiesFromBrowser));
        }

        private void AddCertificateArgs(List<string> a)
        {
            AddArg(a, "--client-certificate", TextValue(txtClientCert));
            AddArg(a, "--client-certificate-key", TextValue(txtClientCertKey));
            AddArg(a, "--client-certificate-password", TextValue(txtClientCertPassword));
        }

        // ---- Advanced --------------------------------------------------------------
        private void AddAdvancedArgs(List<string> a)
        {
            if (chkVerbose.Checked) AddArg(a, "-v");
            if (chkQuiet.Checked) AddArg(a, "-q");
            if (chkNoWarnings.Checked) AddArg(a, "--no-warnings");
            if (chkPrintTraffic.Checked) AddArg(a, "--print-traffic");
            if (chkWritePages.Checked) AddArg(a, "--write-pages");

            AddArg(a, "--print", TextValue(txtPrint));

            var ptf = TextValue(txtPrintToFile);
            if (ptf != null)
            {
                // --print-to-file takes TEMPLATE FILE as two separate values.
                int sp = ptf.LastIndexOf(' ');
                if (sp > 0)
                {
                    a.Add("--print-to-file");
                    a.Add(ptf.Substring(0, sp).Trim());
                    a.Add(ptf.Substring(sp + 1).Trim());
                }
            }

            if (chkNoCacheDir.Checked) AddArg(a, "--no-cache-dir");
            if (chkMarkWatched.Checked) AddArg(a, "--mark-watched");
            AddArg(a, "--config-locations", TextValue(txtConfigLocations));
            AddArg(a, "--cache-dir", TextValue(txtCacheDir));

            AddExtractorArgs(a);
            AddArg(a, "--js-runtimes", TextValue(txtJsRuntimes));
            AddArg(a, "--wait-for-video", TextValue(txtWaitForVideo));
        }

        private void AddExtractorArgs(List<string> a)
        {
            AddLineArgs(a, "--extractor-args", txtExtractorArgs);
            AddArg(a, "--use-extractors", TextValue(txtUseExtractors));
            AddNumberArg(a, "--extractor-retries", numExtractorRetries);
            AddArg(a, "--plugin-dirs", TextValue(txtPluginDirs));
            AddArg(a, "--compat-options", TextValue(txtCompatOptions));
        }

        /// <summary>
        /// The options that also matter when merely querying a URL - listing formats or
        /// subtitles. Anything that decides *whether the site answers at all* belongs here:
        /// proxies, cookies, headers, credentials, extractor settings, and above all the
        /// JavaScript runtime. Leaving that last one out is why a format list can come back
        /// empty on a URL that downloads perfectly well.
        /// </summary>
        internal List<string> NetworkArgsForQuery()
        {
            var a = new List<string>();
            if (chkIgnoreConfig.Checked) AddArg(a, "--ignore-config");
            AddArg(a, "--color", "never");

            // ---- connection ----
            AddConnectionArgs(a);

            AddLineArgs(a, "--add-headers", txtHeaders);
            AddArg(a, "--impersonate", ComboValue(cmbImpersonate));
            AddArg(a, "-R", TextValue(txtRetries));
            AddArg(a, "--retry-sleep", TextValue(txtRetrySleep));

            // ---- credentials ----
            AddCookieArgs(a);
            AddLoginArgs(a);
            AddCertificateArgs(a);

            // ---- extraction ----
            AddExtractorArgs(a);
            AddArg(a, "--config-locations", TextValue(txtConfigLocations));
            if (chkNoCacheDir.Checked) AddArg(a, "--no-cache-dir");
            AddArg(a, "--cache-dir", TextValue(txtCacheDir));
            AddNumberArg(a, "--age-limit", numAgeLimit);

            // A JavaScript runtime is not optional for YouTube: without it the extractor
            // cannot solve the player challenge, and the format list arrives short or empty.
            AddArg(a, "--js-runtimes", TextValue(txtJsRuntimes));

            // --check-formats and any HLS probing shell out to ffmpeg.
            AddArg(a, "--ffmpeg-location", TextValue(txtFfmpegLocation));

            if (chkVerbose.Checked) AddArg(a, "-v");

            var mode = cmbPlaylistMode.Text;
            if (mode.IndexOf("--no-playlist", StringComparison.Ordinal) >= 0) AddArg(a, "--no-playlist");

            return a;
        }
    }
}
