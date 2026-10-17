using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace YtDlpGui
{
    internal partial class MainForm
    {
        // ---- small helpers ---------------------------------------------------
        private static void A(List<string> a, string flag) { a.Add(flag); }

        private static void A(List<string> a, string flag, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            a.Add(flag);
            a.Add(value);
        }

        /// <summary>Trimmed text box content, or null when empty.</summary>
        private static string T(TextBox t)
        {
            if (t == null) return null;
            var s = t.Text.Trim();
            return s.Length == 0 ? null : s;
        }

        /// <summary>Combo value, or null when it is empty or the "(default)" placeholder.</summary>
        private static string C(ComboBox c)
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

        private static void ANum(List<string> a, string flag, NumericUpDown n, decimal skipWhen = 0m)
        {
            if (n == null || n.Value == skipWhen) return;
            a.Add(flag);
            a.Add(n.Value.ToString(n.DecimalPlaces > 0 ? "0.##" : "0", CultureInfo.InvariantCulture));
        }

        private static void ALines(List<string> a, string flag, TextBox t)
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
        internal List<string> BuildArgs(List<string> urls, bool forQueryOnly)
        {
            var a = new List<string>();

            // Machine-friendly output so the GUI can parse progress reliably. These go first
            // so that anything the user types under "Raw extra arguments" still wins: yt-dlp
            // takes the last occurrence of a repeated option.
            A(a, "--newline");
            A(a, "--progress");

            // One structured progress line per update, instead of scraping a human-readable
            // one. This is what makes a playlist-wide progress bar possible: it carries the
            // playlist position and the exact byte counts, not just a percentage.
            if (App.SupportsProgressTemplate)
                A(a, "--progress-template", Runner.ProgressTemplate);

            // Ten updates a second is plenty for a progress bar and keeps a 500-item playlist
            // from spending its time writing to a pipe.
            if (App.SupportsProgressDelta)
                A(a, "--progress-delta", "0.1");

            if (chkIgnoreConfig.Checked) A(a, "--ignore-config");
            A(a, "--color", C(cmbColor) ?? "never");

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
            var home = T(txtOutDir);
            if (home != null) A(a, "-P", "home:" + home);

            var temp = T(txtTempDir);
            if (temp != null) A(a, "-P", "temp:" + temp);

            var tpl = C(cmbTemplate);
            if (tpl != null) A(a, "-o", tpl);

            var tpl2 = T(txtOutTemplateExtra);
            if (tpl2 != null) A(a, "-o", tpl2);

            if (chkRestrictFilenames.Checked) A(a, "--restrict-filenames");
            if (chkWindowsFilenames.Checked) A(a, "--windows-filenames");
            if (chkNoMtime.Checked) A(a, "--no-mtime");
            if (chkNoPart.Checked) A(a, "--no-part");
            ANum(a, "--trim-filenames", numTrimFilenames);

            if (chkNoOverwrites.Checked) A(a, "-w");
            if (chkForceOverwrites.Checked) A(a, "--force-overwrites");
            A(a, chkContinue.Checked ? "-c" : "--no-continue");

            if (chkIgnoreErrors.Checked) A(a, "-i");
            if (chkAbortOnError.Checked) A(a, "--abort-on-error");
            if (chkSkipDownload.Checked) A(a, "--skip-download");
            ANum(a, "--max-downloads", numMaxDownloads);

            var archive = T(txtArchive);
            if (archive != null) A(a, "--download-archive", archive);
            if (chkBreakOnExisting.Checked) A(a, "--break-on-existing");
            if (chkForceWriteArchive.Checked) A(a, "--force-write-archive");
            if (chkNoDownloadArchive.Checked) A(a, "--no-download-archive");

            if (chkWriteUrlLink.Checked) A(a, "--write-url-link");
            if (chkWriteWeblocLink.Checked) A(a, "--write-webloc-link");
            if (chkWriteDesktopLink.Checked) A(a, "--write-desktop-link");
        }

        // ---- Format ----------------------------------------------------------
        private void AddFormatArgs(List<string> a)
        {
            var preset = C(cmbPresetAlias);
            if (preset != null) A(a, "-t", preset);

            var height = FirstToken(cmbMaxHeight.Text);
            if (height == "Best") height = null;                 // "Best available"
            var fps = cmbMaxFps.Text == "Any" ? null : cmbMaxFps.Text;

            if (radFmtAudio.Checked)
            {
                A(a, "-x");
                var af = C(cmbAudioFormat);
                if (af != null) A(a, "--audio-format", af);

                var aq = C(cmbAudioQuality);
                if (aq != null) A(a, "--audio-quality", FirstToken(aq));

                if (chkKeepVideo.Checked) A(a, "-k");
            }
            else if (radFmtCustom.Checked)
            {
                var f = T(txtCustomFormat);
                if (f != null) A(a, "-f", f);
            }
            else
            {
                // Best video+audio, or video only.
                var filter = "";
                if (height != null) filter += "[height<=" + height + "]";
                if (fps != null) filter += "[fps<=" + fps + "]";

                if (radFmtVideoOnly.Checked)
                {
                    A(a, "-f", "bv*" + filter + "/b" + filter);
                }
                else if (filter.Length > 0)
                {
                    A(a, "-f", "bv*" + filter + "+ba/b" + filter + "/bv*+ba/b");
                }
                // No constraints: leave -f alone and let yt-dlp use its default.
            }

            if (!radFmtAudio.Checked)
            {
                var merge = C(cmbMergeContainer);
                if (merge != null) A(a, "--merge-output-format", merge);
            }

            var remux = C(cmbRemux);
            if (remux != null) A(a, "--remux-video", remux);
            var recode = C(cmbRecode);
            if (recode != null) A(a, "--recode-video", recode);

            // Sort order: the user's own fields plus the codec preference.
            var sort = T(txtFormatSort) ?? "";
            string codecPref = null;
            if (cmbVideoCodec.Text.IndexOf("AV1", StringComparison.OrdinalIgnoreCase) >= 0) codecPref = "vcodec:av01";
            else if (cmbVideoCodec.Text.IndexOf("VP9", StringComparison.OrdinalIgnoreCase) >= 0) codecPref = "vcodec:vp9";
            else if (cmbVideoCodec.Text.IndexOf("H.264", StringComparison.OrdinalIgnoreCase) >= 0) codecPref = "vcodec:h264";

            if (codecPref != null && !radFmtAudio.Checked)
                sort = sort.Length > 0 ? codecPref + "," + sort : codecPref;
            if (sort.Length > 0) A(a, "-S", sort);

            if (chkFormatSortForce.Checked) A(a, "--format-sort-force");
            if (chkPreferFreeFormats.Checked) A(a, "--prefer-free-formats");
            if (chkCheckFormats.Checked) A(a, "--check-formats");
            if (chkIgnoreNoFormatsError.Checked) A(a, "--ignore-no-formats-error");
            if (chkVideoMultistreams.Checked) A(a, "--video-multistreams");
            if (chkAudioMultistreams.Checked) A(a, "--audio-multistreams");
            if (chkAllowDynamicMpd.Checked) A(a, "--allow-dynamic-mpd");
            if (chkHlsUseMpegts.Checked) A(a, "--hls-use-mpegts");
            if (chkHlsSplitDiscontinuity.Checked) A(a, "--hls-split-discontinuity");
            if (chkLiveFromStart.Checked) A(a, "--live-from-start");
        }

        // ---- Subtitles, thumbnails, metadata ---------------------------------
        private void AddSubtitleArgs(List<string> a)
        {
            bool anySubs = chkWriteSubs.Checked || chkWriteAutoSubs.Checked || chkEmbedSubs.Checked;

            if (chkWriteSubs.Checked) A(a, "--write-subs");
            if (chkWriteAutoSubs.Checked) A(a, "--write-auto-subs");
            if (chkEmbedSubs.Checked) A(a, "--embed-subs");

            if (anySubs)
            {
                A(a, "--sub-langs", T(txtSubLangs));
                A(a, "--sub-format", C(cmbSubFormat));
                A(a, "--convert-subs", C(cmbConvertSubs));
            }

            if (chkWriteThumbnail.Checked) A(a, "--write-thumbnail");
            if (chkWriteAllThumbnails.Checked) A(a, "--write-all-thumbnails");
            if (chkEmbedThumbnail.Checked) A(a, "--embed-thumbnail");
            A(a, "--convert-thumbnails", C(cmbConvertThumbnails));

            if (chkEmbedMetadata.Checked) A(a, "--embed-metadata");
            if (chkEmbedChapters.Checked) A(a, "--embed-chapters");
            if (chkEmbedInfoJson.Checked) A(a, "--embed-info-json");
            if (chkXattrs.Checked) A(a, "--xattrs");
            if (chkWriteInfoJson.Checked)
            {
                A(a, "--write-info-json");
                A(a, chkCleanInfoJson.Checked ? "--clean-info-json" : "--no-clean-info-json");
            }
            if (chkWriteDescription.Checked) A(a, "--write-description");
            if (chkWriteComments.Checked) A(a, "--write-comments");

            A(a, "--parse-metadata", T(txtParseMetadata));
            A(a, "--replace-in-metadata", T(txtReplaceInMetadata));
        }

        // ---- Playlist and filters --------------------------------------------
        private void AddPlaylistArgs(List<string> a)
        {
            var mode = cmbPlaylistMode.Text;
            if (mode.IndexOf("--yes-playlist", StringComparison.Ordinal) >= 0) A(a, "--yes-playlist");
            else if (mode.IndexOf("--no-playlist", StringComparison.Ordinal) >= 0) A(a, "--no-playlist");

            A(a, "-I", T(txtPlaylistItems));
            if (chkPlaylistRandom.Checked) A(a, "--playlist-random");
            if (chkLazyPlaylist.Checked) A(a, "--lazy-playlist");
            if (chkFlatPlaylist.Checked) A(a, "--flat-playlist");
            if (chkWritePlaylistMetafiles.Checked) A(a, "--write-playlist-metafiles");
            A(a, "--concat-playlist", C(cmbConcatPlaylist));
            ANum(a, "--skip-playlist-after-errors", numSkipPlaylistAfterErrors);

            A(a, "--min-filesize", T(txtMinFilesize));
            A(a, "--max-filesize", T(txtMaxFilesize));
            A(a, "--date", T(txtDate));
            A(a, "--dateafter", T(txtDateAfter));
            A(a, "--datebefore", T(txtDateBefore));

            var mf = T(txtMatchFilters);
            if (mf != null)
            {
                A(a, chkBreakMatchFilters.Checked ? "--break-match-filters" : "--match-filters", mf);
            }
            if (chkBreakPerInput.Checked) A(a, "--break-per-input");
            ANum(a, "--age-limit", numAgeLimit);

            A(a, "--download-sections", T(txtDownloadSections));
            if (chkForceKeyframesAtCuts.Checked) A(a, "--force-keyframes-at-cuts");
            if (chkSplitChapters.Checked) A(a, "--split-chapters");
            A(a, "--remove-chapters", T(txtRemoveChapters));
        }

        // ---- Post-processing ---------------------------------------------------
        private void AddPostProcessingArgs(List<string> a)
        {
            A(a, "--sponsorblock-mark", T(txtSponsorBlockMark));
            A(a, "--sponsorblock-remove", T(txtSponsorBlockRemove));
            A(a, "--sponsorblock-chapter-title", T(txtSponsorBlockTitle));
            A(a, "--sponsorblock-api", T(txtSponsorBlockApi));

            A(a, "--ffmpeg-location", T(txtFfmpegLocation));
            A(a, "--fixup", C(cmbFixup));
            A(a, "--postprocessor-args", T(txtPostprocessorArgs));
            A(a, "--exec", T(txtExec));
            A(a, "--use-postprocessor", T(txtUsePostprocessor));
        }

        // ---- Network -----------------------------------------------------------
        private void AddNetworkArgs(List<string> a)
        {
            A(a, "--proxy", T(txtProxy));
            ANum(a, "--socket-timeout", numSocketTimeout);
            A(a, "--source-address", T(txtSourceAddress));
            A(a, "--xff", C(cmbXff));

            if (chkNoCheckCertificates.Checked) A(a, "--no-check-certificates");
            if (chkPreferInsecure.Checked) A(a, "--prefer-insecure");
            if (chkLegacyServerConnect.Checked) A(a, "--legacy-server-connect");
            if (chkEnableFileUrls.Checked) A(a, "--enable-file-urls");

            A(a, "-r", T(txtLimitRate));
            A(a, "--throttled-rate", T(txtThrottledRate));
            if (numConcurrentFragments.Value > 1)
                A(a, "-N", ((int)numConcurrentFragments.Value).ToString(CultureInfo.InvariantCulture));
            A(a, "--buffer-size", T(txtBufferSize));
            A(a, "--http-chunk-size", T(txtHttpChunkSize));
            if (chkNoResizeBuffer.Checked) A(a, "--no-resize-buffer");

            A(a, "-R", T(txtRetries));
            A(a, "--fragment-retries", T(txtFragmentRetries));
            A(a, "--file-access-retries", T(txtFileAccessRetries));
            A(a, "--retry-sleep", T(txtRetrySleep));
            if (chkSkipUnavailableFragments.Checked) A(a, "--skip-unavailable-fragments");
            if (chkAbortOnUnavailableFragments.Checked) A(a, "--abort-on-unavailable-fragments");
            if (chkKeepFragments.Checked) A(a, "--keep-fragments");

            ANum(a, "--sleep-requests", numSleepRequests);
            ANum(a, "--sleep-interval", numSleepInterval);
            ANum(a, "--max-sleep-interval", numMaxSleepInterval);
            ANum(a, "--sleep-subtitles", numSleepSubtitles);

            ALines(a, "--add-headers", txtHeaders);
            A(a, "--impersonate", C(cmbImpersonate));
            A(a, "--downloader", T(txtDownloader));
            A(a, "--downloader-args", T(txtDownloaderArgs));
        }

        // ---- Authentication ------------------------------------------------------
        private void AddAuthArgs(List<string> a)
        {
            A(a, "-u", T(txtUsername));
            A(a, "-p", T(txtPassword));
            A(a, "--video-password", T(txtVideoPassword));

            if (chkNetrc.Checked) A(a, "-n");
            A(a, "--netrc-location", T(txtNetrcLocation));
            A(a, "--netrc-cmd", T(txtNetrcCmd));

            A(a, "--cookies", T(txtCookies));
            A(a, "--cookies-from-browser", C(cmbCookiesFromBrowser));

            A(a, "--client-certificate", T(txtClientCert));
            A(a, "--client-certificate-key", T(txtClientCertKey));
            A(a, "--client-certificate-password", T(txtClientCertPassword));

            A(a, "--ap-mso", T(txtApMso));
            A(a, "--ap-username", T(txtApUsername));
            A(a, "--ap-password", T(txtApPassword));
        }

        // ---- Advanced --------------------------------------------------------------
        private void AddAdvancedArgs(List<string> a)
        {
            if (chkVerbose.Checked) A(a, "-v");
            if (chkQuiet.Checked) A(a, "-q");
            if (chkNoWarnings.Checked) A(a, "--no-warnings");
            if (chkPrintTraffic.Checked) A(a, "--print-traffic");
            if (chkWritePages.Checked) A(a, "--write-pages");

            A(a, "--print", T(txtPrint));

            var ptf = T(txtPrintToFile);
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

            if (chkNoCacheDir.Checked) A(a, "--no-cache-dir");
            if (chkMarkWatched.Checked) A(a, "--mark-watched");
            A(a, "--config-locations", T(txtConfigLocations));
            A(a, "--cache-dir", T(txtCacheDir));

            ALines(a, "--extractor-args", txtExtractorArgs);
            A(a, "--use-extractors", T(txtUseExtractors));
            ANum(a, "--extractor-retries", numExtractorRetries);
            A(a, "--plugin-dirs", T(txtPluginDirs));
            A(a, "--compat-options", T(txtCompatOptions));
            A(a, "--js-runtimes", T(txtJsRuntimes));
            A(a, "--wait-for-video", T(txtWaitForVideo));
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
            if (chkIgnoreConfig.Checked) A(a, "--ignore-config");
            A(a, "--color", "never");

            // ---- connection ----
            A(a, "--proxy", T(txtProxy));
            ANum(a, "--socket-timeout", numSocketTimeout);
            A(a, "--source-address", T(txtSourceAddress));
            A(a, "--xff", C(cmbXff));
            if (chkNoCheckCertificates.Checked) A(a, "--no-check-certificates");
            if (chkPreferInsecure.Checked) A(a, "--prefer-insecure");
            if (chkLegacyServerConnect.Checked) A(a, "--legacy-server-connect");
            if (chkEnableFileUrls.Checked) A(a, "--enable-file-urls");

            ALines(a, "--add-headers", txtHeaders);
            A(a, "--impersonate", C(cmbImpersonate));
            A(a, "-R", T(txtRetries));
            A(a, "--retry-sleep", T(txtRetrySleep));

            // ---- credentials ----
            A(a, "--cookies", T(txtCookies));
            A(a, "--cookies-from-browser", C(cmbCookiesFromBrowser));
            A(a, "-u", T(txtUsername));
            A(a, "-p", T(txtPassword));
            A(a, "--video-password", T(txtVideoPassword));
            if (chkNetrc.Checked) A(a, "-n");
            A(a, "--netrc-location", T(txtNetrcLocation));
            A(a, "--netrc-cmd", T(txtNetrcCmd));
            A(a, "--client-certificate", T(txtClientCert));
            A(a, "--client-certificate-key", T(txtClientCertKey));
            A(a, "--client-certificate-password", T(txtClientCertPassword));

            // ---- extraction ----
            ALines(a, "--extractor-args", txtExtractorArgs);
            A(a, "--use-extractors", T(txtUseExtractors));
            ANum(a, "--extractor-retries", numExtractorRetries);
            A(a, "--plugin-dirs", T(txtPluginDirs));
            A(a, "--compat-options", T(txtCompatOptions));
            A(a, "--config-locations", T(txtConfigLocations));
            if (chkNoCacheDir.Checked) A(a, "--no-cache-dir");
            A(a, "--cache-dir", T(txtCacheDir));
            ANum(a, "--age-limit", numAgeLimit);

            // A JavaScript runtime is not optional for YouTube: without it the extractor
            // cannot solve the player challenge, and the format list arrives short or empty.
            A(a, "--js-runtimes", T(txtJsRuntimes));

            // --check-formats and any HLS probing shell out to ffmpeg.
            A(a, "--ffmpeg-location", T(txtFfmpegLocation));

            if (chkVerbose.Checked) A(a, "-v");

            var mode = cmbPlaylistMode.Text;
            if (mode.IndexOf("--no-playlist", StringComparison.Ordinal) >= 0) A(a, "--no-playlist");

            return a;
        }
    }
}
