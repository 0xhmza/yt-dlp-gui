using System;

namespace YtDlpGui
{
    /// <summary>An immutable read of the tracker, safe to use from the UI thread.</summary>
    internal class ProgressSnapshot
    {
        public double Overall = -1;      // 0..1 across the whole run, -1 unknown
        public double Job = -1;          // 0..1 across the current queue entry
        public double File = -1;         // 0..1 across the file being transferred

        public int JobIndex, JobCount;   // 1-based position in the queue
        public int ItemIndex, ItemCount; // 1-based position in the playlist

        public long Downloaded = -1, Total = -1;
        public double Speed = -1, Eta = -1;
        public int FragIndex = -1, FragCount = -1;

        public string FileName;
        public string PostProcessor;
        public double RunElapsed;
        public long RunBytes;

        /// <summary>Seconds left for the whole run, extrapolated from what is done so far.</summary>
        public double OverallEta
        {
            get
            {
                if (Overall <= 0.001 || Overall >= 1 || RunElapsed < 3) return -1;
                return RunElapsed * (1 - Overall) / Overall;
            }
        }
    }

    /// <summary>
    /// Turns the stream of per-file progress reports into one figure for the whole run.
    ///
    /// yt-dlp reports progress per *file*, and one playlist entry is often two files (a video
    /// stream and an audio stream) that are merged afterwards. So a file percentage is fed
    /// into a per-entry figure weighted by bytes, that into a per-queue-entry figure weighted
    /// by playlist position, and that into the overall figure. The result only ever moves
    /// forwards, which is the one thing a progress bar must promise.
    ///
    /// Everything is fed from yt-dlp's reader thread and read from the UI thread, so every
    /// member is touched under the lock.
    /// </summary>
    internal class ProgressTracker
    {
        private readonly object _gate = new object();

        // run
        private int _jobCount, _jobIndex;
        private DateTime _runStart;
        private long _runBytes;                 // bytes finished in earlier files of this run

        // current queue entry
        private int _itemIndex = -1, _itemCount = -1;

        // current playlist entry
        private long _itemDone;                 // bytes of finished streams inside this entry
        private double _itemFractionMax;

        // current file
        private string _file;
        private long _fileDownloaded = -1, _fileTotal = -1;
        private double _speed = -1, _eta = -1;
        private int _fragIndex = -1, _fragCount = -1;
        private string _post;

        /// <summary>
        /// While an entry is still being worked on its figure is held just short of full, so
        /// that a video stream completing does not show a finished bar while the audio stream
        /// it will be merged with has not started.
        /// </summary>
        private const double InProgressCap = 0.995;

        public void BeginRun(int jobCount)
        {
            lock (_gate)
            {
                _jobCount = Math.Max(1, jobCount);
                _jobIndex = 0;
                _runStart = DateTime.UtcNow;
                _runBytes = 0;
                ResetJob();
            }
        }

        public void BeginJob(int zeroBasedIndex)
        {
            lock (_gate)
            {
                _jobIndex = Math.Max(0, zeroBasedIndex);
                ResetJob();
            }
        }

        /// <summary>Called when a queue entry ends, so its share counts as complete.</summary>
        public void EndJob()
        {
            lock (_gate)
            {
                RollUpFile();
                _runBytes += _itemDone;
                ResetJob();
                _jobIndex++;
            }
        }

        private void ResetJob()
        {
            _itemIndex = -1;
            _itemCount = -1;
            ResetItem();
        }

        private void ResetItem()
        {
            _itemDone = 0;
            _itemFractionMax = 0;
            _file = null;
            _fileDownloaded = -1;
            _fileTotal = -1;
            _speed = -1;
            _eta = -1;
            _fragIndex = -1;
            _fragCount = -1;
            _post = null;
        }

        /// <summary>Folds the file in progress into the entry total and clears the file slot.</summary>
        private void RollUpFile()
        {
            long done = _fileTotal > 0 ? _fileTotal : (_fileDownloaded > 0 ? _fileDownloaded : 0);
            _itemDone += done;
            _fileDownloaded = -1;
            _fileTotal = -1;
            _fragIndex = -1;
            _fragCount = -1;
        }

        public void Feed(ProgressInfo p)
        {
            if (p == null) return;
            lock (_gate)
            {
                if (p.Kind == ProgressKind.Post)
                {
                    _post = p.PostProcessor;
                    return;
                }

                if (p.Kind == ProgressKind.Item)
                {
                    SetItem(p.PlaylistIndex, p.PlaylistCount);
                    return;
                }

                if (p.PlaylistIndex > 0) SetItem(p.PlaylistIndex, p.PlaylistCount);

                // A new file inside the same entry: bank the one that just finished.
                var name = p.Filename;
                if (name != null && !string.Equals(name, _file, StringComparison.Ordinal))
                {
                    if (_file != null) RollUpFile();
                    _file = name;
                    _fileDownloaded = -1;
                    _fileTotal = -1;
                }

                if (p.Downloaded >= 0) _fileDownloaded = p.Downloaded;
                if (p.Total > 0) _fileTotal = p.Total;
                if (p.Speed >= 0) _speed = p.Speed;
                _eta = p.Eta;
                if (p.FragIndex >= 0) _fragIndex = p.FragIndex;
                if (p.FragCount > 0) _fragCount = p.FragCount;
                _post = null;                     // bytes are moving again
            }
        }

        private void SetItem(int index, int count)
        {
            if (count > 0) _itemCount = count;
            if (index <= 0 || index == _itemIndex) return;

            _itemIndex = index;
            ResetItem();                          // a fresh entry starts its own accounting
        }

        // =====================================================================
        public ProgressSnapshot Read()
        {
            var s = new ProgressSnapshot();
            lock (_gate)
            {
                s.JobCount = _jobCount;
                s.JobIndex = _jobIndex + 1;
                s.ItemIndex = _itemIndex;
                s.ItemCount = _itemCount;
                s.Downloaded = _fileDownloaded;
                s.Total = _fileTotal;
                s.Speed = _speed;
                s.Eta = _eta;
                s.FragIndex = _fragIndex;
                s.FragCount = _fragCount;
                s.FileName = _file;
                s.PostProcessor = _post;
                s.RunElapsed = _runStart == default(DateTime) ? 0 : (DateTime.UtcNow - _runStart).TotalSeconds;
                s.RunBytes = _runBytes + _itemDone + Math.Max(0, _fileDownloaded);

                s.File = FileFraction();
                double item = ItemFraction(s.File);

                if (item >= 0)
                {
                    if (item > InProgressCap) item = InProgressCap;
                    if (item > _itemFractionMax) _itemFractionMax = item;
                    item = _itemFractionMax;
                }

                s.Job = JobFraction(item);

                // Entries already finished count in full even while the next one is still
                // being resolved and has nothing to report yet. Only before the very first
                // byte of the run is the overall figure genuinely unknown - and showing a
                // marquee there is exactly right, because yt-dlp is still talking to the site.
                s.Overall = _jobCount <= 0 || (_jobIndex == 0 && s.Job < 0)
                    ? -1
                    : Math.Min(1.0, (_jobIndex + Math.Max(0, s.Job)) / _jobCount);
            }
            return s;
        }

        private double FileFraction()
        {
            if (_fileTotal > 0 && _fileDownloaded >= 0)
                return Math.Min(1.0, (double)_fileDownloaded / _fileTotal);
            if (_fragCount > 0 && _fragIndex >= 0)
                return Math.Min(1.0, (double)_fragIndex / _fragCount);
            return -1;
        }

        /// <summary>
        /// Byte-weighted across every stream of the entry, so a 100 MiB video followed by a
        /// 5 MiB audio track fills the bar in proportion instead of twice from zero.
        /// </summary>
        private double ItemFraction(double fileFraction)
        {
            long total = _itemDone + Math.Max(_fileTotal, _fileDownloaded);
            if (total > 0)
            {
                long done = _itemDone + Math.Max(0, _fileDownloaded);
                return Math.Min(1.0, (double)done / total);
            }
            return fileFraction;
        }

        private double JobFraction(double itemFraction)
        {
            if (_itemCount > 0 && _itemIndex > 0)
            {
                double f = itemFraction < 0 ? 0 : itemFraction;
                return Math.Min(1.0, ((_itemIndex - 1) + f) / _itemCount);
            }
            return itemFraction;
        }
    }
}
