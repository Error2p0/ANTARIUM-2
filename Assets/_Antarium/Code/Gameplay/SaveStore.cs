using System;
using System.IO;
using System.Threading.Tasks;
using Antarium.Content;
using Antarium.Sim;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The save slot on disk. Saves are written to a temporary file and swapped in, keeping the previous save as a
    /// backup, so a crash mid-write never loses the colony. Autosaves compress and write on a worker thread.
    /// </summary>
    public sealed class SaveStore
    {
        readonly string _directory;
        Task _writing;

        public string MainPath { get; }
        public string BackupPath { get; }
        /// <summary>Message of the last failed background write, cleared when read by <see cref="TakeError"/>.</summary>
        string _error;

        public SaveStore(string directory)
        {
            _directory = directory;
            MainPath = Path.Combine(directory, "colony.sav");
            BackupPath = Path.Combine(directory, "colony.bak");
        }

        public bool HasSave => File.Exists(MainPath) || File.Exists(BackupPath);
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
            if (File.Exists(MainPath)) File.Replace(temp, MainPath, BackupPath, ignoreMetadataErrors: true);
            else File.Move(temp, MainPath);
        }

        /// <summary>Deletes the save and its backup (Settings → Erase all progress).</summary>
        public void Delete()
        {
            Wait();
            foreach (var path in new[] { MainPath, BackupPath, MainPath + ".tmp" })
                if (File.Exists(path)) File.Delete(path);
        }

        public SaveHeader ReadHeader()
        {
            string path = File.Exists(MainPath) ? MainPath : BackupPath;
            using var file = File.OpenRead(path);
            return SaveFormat.ReadHeader(file);
        }

        /// <summary>Loads the save, falling back to the backup if the main file is damaged. Throws if neither loads.</summary>
        public Simulation Load(ContentDB content, GameConfig config, out SaveHeader header)
        {
            Wait();
            Exception first = null;
            foreach (var path in new[] { MainPath, BackupPath })
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
    }
}
