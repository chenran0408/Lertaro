using System.Collections;

namespace Lertaro.Core;

public sealed partial class FileRecordCollection
{
    public IEnumerator<FileRecord> GetEnumerator() => new Enumerator(this);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Enumerator : IEnumerator<FileRecord>
    {
        private readonly FileRecordCollection _owner;
        private readonly int _version;
        private int _index;
        private bool _hasCurrent;
        private FileRecord _current;

        public Enumerator(FileRecordCollection owner)
        {
            _owner = owner;
            _version = Volatile.Read(ref owner._version);
        }

        public FileRecord Current
        {
            get
            {
                CheckVersion();
                if (!_hasCurrent)
                    throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                return _current;
            }
        }

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            CheckVersion();
            if (_index >= _owner.Count)
            {
                _hasCurrent = false;
                _current = default;
                return false;
            }

            _current = _owner[_index++];
            _hasCurrent = true;
            return true;
        }

        public void Reset()
        {
            CheckVersion();
            _index = 0;
            _hasCurrent = false;
            _current = default;
        }

        public void Dispose() { }

        private void CheckVersion()
        {
            if (_version != Volatile.Read(ref _owner._version))
                throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
        }
    }
}
