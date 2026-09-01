using System.Collections;

namespace MiniOrm.Loading;

public sealed class LazyLoadList<T> : IList<T>
{
    private List<T>? _items;
    private readonly Func<IList<T>> _loader;

    public LazyLoadList(Func<IList<T>> loader)
    {
        _loader = loader;
    }

    public bool IsLoaded => _items is not null;

    private List<T> Items => _items ??= _loader().ToList();

    public IEnumerator<T> GetEnumerator() => Items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Add(T item) => Items.Add(item);
    public void Clear() => Items.Clear();
    public bool Contains(T item) => Items.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => Items.CopyTo(array, arrayIndex);
    public bool Remove(T item) => Items.Remove(item);
    public int Count => Items.Count;
    public bool IsReadOnly => false;
    public int IndexOf(T item) => Items.IndexOf(item);
    public void Insert(int index, T item) => Items.Insert(index, item);
    public void RemoveAt(int index) => Items.RemoveAt(index);
    public T this[int index]
    {
        get => Items[index];
        set => Items[index] = value;
    }
}
