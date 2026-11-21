using System;
using System.IO;
using System.Threading.Tasks;
using Antarium.Content;
using Antarium.Sim;

namespace Antarium.Gameplay
{
    /// <summary>
    /// One save slot on disk (10.6: three slots, each its own bloodline). Saves are written to a temporary file and swapped in,
    /// keeping the previous save as a backup (<c>.bak</c>), so a crash mid-write never loses the colony; an older copy
    /// (<c>.old</c>) is refreshed at most every <see cref="OldBackupMinutes"/> minutes, so even a save that is damaged twice in a
    /// row leaves something to load. Autosaves compress and write on a worker thread. Slot 1 keeps the pre-slot file name
    /// (<c>colony.sav</c>), so older saves simply become slot 1; every file sits in the <c>Saves</c> folder Steam Cloud syncs.
    /// </summary>
    public sealed class SaveStore
    {
        public const int SlotCount = 3;

        readonly string _directory;
        Task _writing;

        /// <summary>0-based slot index.</summary>
        public int Slot { get; }
        public string MainPath { get; }
        public string BackupPath { get; }
        public string OldPath { get; }
        /// <summary>The <c>.old</c> copy is refreshed when it is older than this.</summary>
        public double OldBackupMinutes { get; set; } = 30.0;
        /// <summary>Message of the last failed background write, cleared when read by <see cref="TakeError"/>.</summary>
        string _error;

        public SaveStore(string directory, int slot = 0)
        {
            _directory = directory;
            Slot = slot;
            string name = FileName(slot);
            MainPath = Path.Combine(directory, name + ".sav");
            BackupPath = Path.Combine(directory, name + ".bak");
            OldPath = Path.Combine(directory, name + ".old");
        }

        /// <summary>File name (without extension) of a slot: colony, colony2, colony3.</summary>
        public static string FileName(int slot) => slot <= 0 ? "colony" : $"colony{slot + 1}";

        public bool HasSave => File.Exists(MainPath) || File.Exists(BackupPath) || File.Exists(OldPath);
        public bool IsWriting => _writing != null && !_writing.IsCompleted;

        /// <summary>Writes in the background. Returns false if the previous write is still running (the snapshot is dropped).</summary>
        public bool SaveInBackground(SaveSnapshot snapshot)
        {
            if (IsWriting) return false;
            _writing = Task.Run(() =>
            {
                try
                {
                    Write(snapshot);
                }
                catch (Exception e)
                {
                    _error = e.Message;
                }
            });
            return true;
        }

        /// <summary>Writes now on the calling thread (after any running background write).</summary>
        public void Save(SaveSnapshot snapshot)
        {
            Wait();
            Write(snapshot);
        }

        public void Wait()
        {
            try
            {
                _writing?.Wait();
            }
            catch (AggregateException)
            {
                // reported through TakeError
            }
        }

        public string TakeError()
        {
            var e = _error;
            _error = null;
            return e;
        }

        void Write(SaveSnapshot snapshot)
        {
            Directory.CreateDirectory(_directory);
            string temp = MainPath + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                snapshot.WriteTo(file);
            if (File.Exists(MainPath))
            {
                if (!File.Exists(OldPath) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(OldPath)).TotalMinutes >= OldBackupMinutes)
                {
                    File.Copy(MainPath, OldPath, overwrite: true);
                    File.SetLastWriteTimeUtc(OldPath, DateTime.UtcNow);
                }
                File.Replace(temp, MainPath, BackupPath, ignoreMetadataErrors: true);
            }
            else File.Move(temp, MainPath);
        }

        /// <summary>Deletes the slot's save and its backups (deleting a slot, Settings → Erase all progress).</summary>
        public void Delete()
        {
            Wait();
            foreach (var path in new[] { MainPath, BackupPath, OldPath, MainPath + ".tmp" })
                if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>The header of the newest readable file of the slot, or null (empty or unreadable slot).</summary>
        public SaveHeader TryReadHeader()
        {
            foreach (var path in new[] { MainPath, BackupPath, OldPath })
            {
                if (!File.Exists(path)) continue;
                try
                {
                    using var file = File.OpenRead(path);
                    return SaveFormat.ReadHeader(file);
                }
                catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
                {
                    // try the backups
                }
            }
            return null;
        }

        public SaveHeader ReadHeader() => TryReadHeader() ?? throw new FileNotFoundException("No save found.", MainPath);

        /// <summary>Loads the save, falling back to the backups if the main file is damaged. Throws if none loads.</summary>
        public Simulation Load(ContentDB content, GameConfig config, out SaveHeader header)
        {
            Wait();
            Exception first = null;
            foreach (var path in new[] { MainPath, BackupPath, OldPath })
            {
                if (!File.Exists(path)) continue;
                try
                {
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
                    return SaveGame.Load(file, content, config, out header);
                }
                catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
                {
                    first ??= e;
                }
            }
            throw first ?? new FileNotFoundException("No save found.", MainPath);
        }

        /// <summary>The slot whose save was written last (by the header time), or 0 when no slot has a save.</summary>
        public static int LatestSlot(SaveStore[] stores)
        {
            int best = 0;
            DateTime newest = DateTime.MinValue;
            for (int i = 0; i < stores.Length; i++)
            {
                var h = stores[i].TryReadHeader();
                if (h != null && h.SavedUtc > newest)
                {
                    newest = h.SavedUtc;
                    best = i;
                }
            }
            return best;
        }
    }
}
