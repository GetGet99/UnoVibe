using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace QuickMarkup.Infra.Collections;

/// <summary>
/// A keyed set that allows lookup of values by key.
/// </summary>
/// <remarks>
/// The key state of a value must remain unchanged while the item is in the collection.
/// Otherwise, behavior is undefined.
/// </remarks>
public class ReactiveKeyedSet<TKey, TValue>(Func<TValue, TKey> keyFn) : ICollection<TValue>, IReference where TKey : notnull
{
    /// <summary>
    /// Notifies on read <c>this[key]</c> or <see cref="TryGetValue"/>. Default is on
    /// </summary>
    /// <remarks>
    /// Turn this off to optimize the code to not notify from pure key read.
    /// AddAndReplace function will throw when this is false.<br />
    /// 
    /// Operation is undefined when item with same key is readded after removed with different values.
    /// </remarks>
    public bool RerunReadFromKey { get; set; } = true;

    readonly Dictionary<TKey, TValue> backingDict = [];

    public TValue this[TKey key]
    {
        get
        {
            if (RerunReadFromKey)
                ReferenceTracker.NotifyRefernceRead(this);
            return backingDict[key];
        }
    }

    public int Count
    {
        get
        {
            ReferenceTracker.NotifyRefernceRead(this);
            return backingDict.Count;
        }
    }

    public bool IsReadOnly => false;

    public event Action? ValueChanged;

    public bool Add(TValue item)
    {
        var key = keyFn(item);

        if (backingDict.TryGetValue(key, out var existing))
        {
            if (EqualityComparer<TValue>.Default.Equals(existing, item))
                return false;

            throw new InvalidOperationException("An item with the same key already exists.");
        }
        backingDict.Add(key, item);
        ValueChanged?.Invoke();
        return true;
    }

    public bool AddOrReplace(TValue item)
    {
        var key = keyFn(item);

        if (backingDict.TryGetValue(key, out var existing) &&
            EqualityComparer<TValue>.Default.Equals(existing, item))
            return false;

        backingDict[key] = item;
        ValueChanged?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (Count is 0) return;
        backingDict.Clear();
        ValueChanged?.Invoke();
    }

    public bool ContainsKey(TKey item)
    {
        ReferenceTracker.NotifyRefernceRead(this);
        return backingDict.ContainsKey(item);
    }

    public bool Contains(TValue item) => ContainsKey(keyFn(item));

    public void CopyTo(TValue[] array, int arrayIndex)
    {
        ReferenceTracker.NotifyRefernceRead(this);
        backingDict.Values.CopyTo(array, arrayIndex);
    }

    public IEnumerator<TValue> GetEnumerator()
    {
        ReferenceTracker.NotifyRefernceRead(this);
        return backingDict.Values.GetEnumerator();
    }

    public bool Remove(TKey item)
    {
        if (backingDict.Remove(item))
        {
            ValueChanged?.Invoke();
            return true;
        }
        return false;
    }

    public bool Remove(TKey item, [MaybeNullWhen(false)] out TValue value)
    {
        if (backingDict.Remove(item, out value))
        {
            ValueChanged?.Invoke();
            return true;
        }
        return false;
    }

    public bool TryGetValue(TKey item, [MaybeNullWhen(false)] out TValue value)
    {
        if (RerunReadFromKey)
            ReferenceTracker.NotifyRefernceRead(this);
        if (backingDict.TryGetValue(item, out value))
        {
            return true;
        }
        return false;
    }

    public bool RemoveValue(TValue item) => Remove(keyFn(item));

    bool ICollection<TValue>.Remove(TValue item) => RemoveValue(item);
    void ICollection<TValue>.Add(TValue item) => Add(item);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class ReactiveSet<T> : ISet<T>, IReference
{
    readonly HashSet<T> backingSet = [];

    public int Count
    {
        get
        {
            ReferenceTracker.NotifyRefernceRead(this);
            return backingSet.Count;
        }
    }

    public bool IsReadOnly => false;

    public event Action? ValueChanged;

    public bool Add(T item)
    {
        if (backingSet.Add(item))
        {
            ValueChanged?.Invoke();
            return true;
        }
        return false;
    }

    public void Clear()
    {
        if (Count is 0) return;
        backingSet.Clear();
        ValueChanged?.Invoke();
    }

    public bool Contains(T item)
    {
        ReferenceTracker.NotifyRefernceRead(this);
        return backingSet.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        ReferenceTracker.NotifyRefernceRead(this);
        backingSet.CopyTo(array, arrayIndex);
    }

    public void ExceptWith(IEnumerable<T> other)
    {
        int cnt = backingSet.Count;
        backingSet.ExceptWith(other);
        if (cnt != backingSet.Count)
            ValueChanged?.Invoke();
    }

    public IEnumerator<T> GetEnumerator()
    {
        ReferenceTracker.NotifyRefernceRead(this);
        return backingSet.GetEnumerator();
    }

    public void IntersectWith(IEnumerable<T> other)
    {
        int cnt = backingSet.Count;
        backingSet.IntersectWith(other);
        if (cnt != backingSet.Count)
            ValueChanged?.Invoke();
    }

    public bool IsProperSubsetOf(IEnumerable<T> other)
    {
        ReferenceTracker.NotifyRefernceRead(this);
        return backingSet.IsProperSubsetOf(other);
    }

    public bool IsProperSupersetOf(IEnumerable<T> other)
    {
        ReferenceTracker.NotifyRefernceRead(this);
        return backingSet.IsProperSupersetOf(other);
    }

    public bool IsSubsetOf(IEnumerable<T> other)
    {
        ReferenceTracker.NotifyRefernceRead(this);
        return backingSet.IsSubsetOf(other);
    }

    public bool IsSupersetOf(IEnumerable<T> other)
    {
        ReferenceTracker.NotifyRefernceRead(this);
        return backingSet.IsSupersetOf(other);
    }

    public bool Overlaps(IEnumerable<T> other)
    {
        ReferenceTracker.NotifyRefernceRead(this);
        return backingSet.Overlaps(other);
    }

    public bool Remove(T item)
    {
        if (backingSet.Remove(item))
        {
            ValueChanged?.Invoke();
            return true;
        }
        return false;
    }

    public bool SetEquals(IEnumerable<T> other)
    {
        ReferenceTracker.NotifyRefernceRead(this);
        return backingSet.SetEquals(other);
    }

    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        int cnt = backingSet.Count;
        backingSet.SymmetricExceptWith(other);
        if (cnt != backingSet.Count)
            ValueChanged?.Invoke();
    }

    public void UnionWith(IEnumerable<T> other)
    {
        int cnt = backingSet.Count;
        backingSet.UnionWith(other);
        if (cnt != backingSet.Count)
            ValueChanged?.Invoke();
    }

    void ICollection<T>.Add(T item) => Add(item);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}